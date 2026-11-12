using System.IO;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using GameWorld.Core.Services;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.AnimationFragmentEditor.AnimationPack.BinGrid
{
    /// <summary>One thing a check found. Entry is the row's index in AnimationTableEntries, null for the header; Where is the
    /// row's slot name, or for the header "Name", "Skeleton" or "LocomotionGraph".</summary>
    public record BinFinding(int? Entry, string Where, string Message, bool IsError);

    /// <summary>The checks a battle animation table gets, shared by the XML view's Apply and the bin grid. Header and Files only
    /// look paths up; Deep opens the files the rows name, so the grid runs it in the background.</summary>
    public class BinTableChecks
    {
        readonly IPackFileService _pfs;
        readonly ISkeletonAnimationLookUpHelper _skeletons;
        readonly MetaDataFileParser _metaParser;
        readonly PackFile? _animPack;

        string _animationPersistanceMetaFileName = "";
        readonly Dictionary<string, uint> _animationsVersionFoundInPersistenceMeta = [];
        AnimationPackFileDatabase? _loadedAnimPack;

        public BinTableChecks(IPackFileService pfs, ISkeletonAnimationLookUpHelper skeletons, MetaDataFileParser metaParser, PackFile? animPack)
        {
            _pfs = pfs;
            _skeletons = skeletons;
            _metaParser = metaParser;
            _animPack = animPack;
        }

        static BaseAnimationSlotHelper Slots(AnimationBinWh3 table) => table.TableVersion == 4 ? AnimationSlotTypeHelperWh3.GetInstance() : AnimationSlotTypeHelper3k.GetInstance();

        public List<BinFinding> Header(AnimationBinWh3 table, string filePath)
        {
            var found = new List<BinFinding>();
            void Error(string where, string message) => found.Add(new BinFinding(null, where, message, true));

            if (string.IsNullOrWhiteSpace(table.SkeletonName))
                Error("Skeleton", "Missing skeleton item on root");
            else if (_skeletons.GetSkeletonFileFromName(table.SkeletonName) == null)
                Error("Skeleton", $"Skeleton {table.SkeletonName} is not found");

            if (table.TableVersion == 4)
            {
                if (string.IsNullOrWhiteSpace(table.LocomotionGraph))
                    found.Add(new BinFinding(null, "LocomotionGraph", "LocomotionGraph not provided", false));
                else if (_pfs.FindFile(table.LocomotionGraph) == null)
                    Error("LocomotionGraph", $"LocomotionGraph {table.LocomotionGraph} is not found");
            }

            if (string.IsNullOrWhiteSpace(table.Name))
            {
                Error("Name", "Name can not be empty");
            }
            else
            {
                var filename = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
                if (filename != table.Name.ToLowerInvariant())
                    Error("Name", $"The name of the bin file has to be the same as the provided name. {filename} vs {table.Name}");
            }
            return found;
        }

        /// <summary>A slot with no variants, and a path no loaded pack holds. An empty meta or sound path is not a missing file:
        /// vanilla leaves thousands empty.</summary>
        public List<BinFinding> Files(AnimationBinWh3 table)
        {
            var found = new List<BinFinding>();
            var slots = Slots(table);
            for (var i = 0; i < table.AnimationTableEntries.Count; i++)
            {
                var row = table.AnimationTableEntries[i];
                var slot = BinXml.SlotName(slots, row.AnimationId);
                var entry = i;
                void Warn(string message) => found.Add(new BinFinding(entry, slot, message, false));
                if (row.AnimationRefs.Count == 0)
                    Warn("Slot does not have any animations");
                foreach (var reference in row.AnimationRefs)
                {
                    if (_pfs.FindFile(reference.AnimationFile) == null)
                        Warn($"Animation file {reference.AnimationFile} is not found");
                    if (!string.IsNullOrEmpty(reference.AnimationMetaFile) && _pfs.FindFile(reference.AnimationMetaFile) == null)
                        Warn($"Meta file {reference.AnimationMetaFile} is not found");
                    if (!string.IsNullOrEmpty(reference.AnimationSoundMetaFile) && _pfs.FindFile(reference.AnimationSoundMetaFile) == null)
                        Warn($"Sound file {reference.AnimationSoundMetaFile} is not found");
                }
            }
            return found;
        }

        /// <summary>The checks that open files: each file's header, the meta splices' animation versions against the row's and the
        /// persistent meta's, and a rider's animations against its mount's. Rows are walked in order, since the persistent meta's
        /// versions are gathered as PERSISTENT_METADATA_ALIVE is passed.</summary>
        public List<BinFinding> Deep(AnimationBinWh3 table)
        {
            var found = new List<BinFinding>();
            var slots = Slots(table);
            var pfs = _pfs;
            _animationsVersionFoundInPersistenceMeta.Clear();
            _loadedAnimPack = null;
            for (var i = 0; i < table.AnimationTableEntries.Count; i++)
            {
                var row = table.AnimationTableEntries[i];
                var slot = BinXml.SlotName(slots, row.AnimationId);
                var errorList = new ErrorList();
                foreach (var animationRef in row.AnimationRefs)
                {
                    if (pfs.FindFile(animationRef.AnimationFile) != null)
                    {
                        if (!IsAnimFile(animationRef.AnimationFile, pfs, errorList, slot))
                            errorList.Warning(slot, $"Animation file {animationRef.AnimationFile} does not appears to be a valid animation file");
                        else
                            ValidateAnimationVersionAgainstPersistenceMeta(animationRef.AnimationFile, slot, table.SkeletonName, pfs, errorList);
                    }

                    if (pfs.FindFile(animationRef.AnimationMetaFile) != null)
                    {
                        if (!IsAnimMetaFile(animationRef.AnimationMetaFile, pfs, errorList, slot))
                            errorList.Warning(slot, $"Meta file {animationRef.AnimationMetaFile} does not appear to be a valid meta animation");
                        else
                            CheckForAnimationVersionsInMeta(animationRef.AnimationFile, animationRef.AnimationMetaFile, slot, table.SkeletonName, pfs, errorList);
                    }

                    if (_animPack != null)
                        CheckForRiderAndHisMountAnimationsVersion(table.MountBin, _animPack, slot, animationRef.AnimationFile, pfs, errorList);

                    if (pfs.FindFile(animationRef.AnimationSoundMetaFile) != null && !IsSndMetaFile(animationRef.AnimationSoundMetaFile, pfs, errorList, slot))
                        errorList.Warning(slot, $"Sound file {animationRef.AnimationSoundMetaFile} does not appear to be a valid meta sound");
                }
                found.AddRange(errorList.Errors.Select(x => new BinFinding(i, x.ItemName, x.Description, x.IsError)));
            }
            return found;
        }

        private bool IsAnimFile(string file, IPackFileService pfs, ErrorList errorList, string animationSlot)
        {
            var endsWithAnim = file.EndsWith(".anim");

            var theFile = pfs.FindFile(file);
            if (theFile == null)
            {
                errorList.Warning(animationSlot, $"Inable to locate {file} for {animationSlot}");
                return false;
            }

            var data = theFile.DataSource.ReadData();
            var headerIsReallyAnimFile = data[0] == 0x06 || data[0] == 0x07 || data[0] == 0x08; //check if version is not 6 7 8 (or just check if it's 2)
            return endsWithAnim && headerIsReallyAnimFile;
        }

        private bool IsAnimMetaFile(string file, IPackFileService pfs, ErrorList errorList, string animationSlot)
        {
            var endsWithDotMeta = file.EndsWith(".anm.meta") || file.EndsWith(".meta");

            var theFile = pfs.FindFile(file);
            if (theFile == null)
            {
                errorList.Warning(animationSlot, $"Inable to locate {file} for {animationSlot}");
                return false;
            }

            var data = theFile.DataSource.ReadData();
            var headerIsReallyAnimMetaFile = data[0] == 0x02; //check if version is not 6 7 8 (or just check if it's 2)
            return endsWithDotMeta && headerIsReallyAnimMetaFile;
        }

        private bool IsSndMetaFile(string file, IPackFileService pfs, ErrorList errorList, string animationSlot)
        {
            var endsWithDotMeta = file.EndsWith(".snd.meta");

            var theFile = pfs.FindFile(file);
            if (theFile == null)
            {
                errorList.Warning(animationSlot, $"Inable to locate {file} for {animationSlot}");
                return false;
            }

            var data = theFile.DataSource.ReadData();
            var headerIsReallyAnimMetaFile = data[0] == 0x02; //check if version is not 6 7 8 (or just check if it's 2)
            return endsWithDotMeta && headerIsReallyAnimMetaFile;
        }

        private bool CheckForAnimationVersionsInMeta(string mainAnimationFile, string metaFile, string animationSlot, string skeleton, IPackFileService pfs, ErrorList errorList)
        {
            var result = true;

            var theFile = pfs.FindFile(metaFile);
            if (theFile == null)
            {
                errorList.Warning(animationSlot, $"Inable to locate {metaFile} for {animationSlot}");
                return false;
            }
            var data = theFile.DataSource.ReadData();
            var parsed = _metaParser.ParseFile(data);

            var mainAnimationHeader = GetAnimationHeader(mainAnimationFile, pfs);
            if (mainAnimationHeader == null)
            {
                errorList.Warning(animationSlot, $"Cannot locate animation {mainAnimationFile} while trying to parse meta");
                return false;
            }

            var mainAnimationVersion = mainAnimationHeader.Version;

            var metaItems = parsed.Attributes;

            foreach (var item in metaItems)
            {
                if (item.DisplayName.Contains("SPLICE") && item is Splice_v11 splice)
                {
                    var animPath = splice.Animation;
                    if (animPath == null || animPath == "")
                    {
                        errorList.Warning(animationSlot, $"Animation Meta Splice {metaFile} has no animation defined");
                        result = false;
                        continue;
                    }

                    var parsedHeader = GetAnimationHeader(animPath, pfs);
                    if (parsedHeader == null)
                    {
                        errorList.Warning(animationSlot, $"Unable to determine header for {animPath} in slot {animationSlot}");
                        result = false;
                        continue;
                    }

                    var animationVersion = parsedHeader.Version;
                    if (animationSlot == "PERSISTENT_METADATA_ALIVE") //check for this too, cus this plays throughtout char animation (all of them)
                    {
                        _animationsVersionFoundInPersistenceMeta[animPath] = animationVersion;
                        _animationPersistanceMetaFileName = metaFile;
                    }
                    else
                    {
                        if (mainAnimationVersion == 5 && animationVersion == 8) continue; //no idea why this isn't problem in vanilla wh3
                        if (mainAnimationVersion == 8 && animationVersion == 5) continue; //no idea why this isn't problem in vanilla wh3

                        var isTheVersionMatch = animationVersion == mainAnimationVersion;
                        var isMatchedCombat = animationSlot.StartsWith("COMBAT_");
                        var isMountedAnim = animationSlot.StartsWith("RIDER_") && skeleton.Contains("humanoid");

                        if (isMatchedCombat) continue;
                        if (!isMountedAnim) continue;
                        if (isTheVersionMatch) continue;

                        {
                            errorList.Warning(animationSlot, $"Animation Meta Splice {metaFile} has different version than in the main animation. File referenced in meta: {animPath} with version {animationVersion} vs in the main animation {mainAnimationFile} with version {mainAnimationVersion}");
                            result = false;
                        }
                    }
                }
                else if (item.DisplayName.Contains("DISABLE_PER") && animationSlot.StartsWith("RIDER_"))
                {
                    errorList.Warning(animationSlot, $"Contains DISABLE_PERSISTENCE which could cause sync rider animation. In metafile: {metaFile}");
                }


            }

            return result;
        }

        private bool ValidateAnimationVersionAgainstPersistenceMeta(string mainAnimationFile, string animationSlot, string skeleton, IPackFileService pfs, ErrorList errorList)
        {
            var versions = _animationsVersionFoundInPersistenceMeta;
            var result = true;

            var mainAnimation = pfs.FindFile(mainAnimationFile);
            if (mainAnimation == null)
            {
                errorList.Warning(animationSlot, $"Inable to locate {mainAnimationFile} for {animationSlot}");
                return false;
            }
            var mainAnimationParsed = AnimationFile.Create(mainAnimation);
            var mainAnimationVersion = mainAnimationParsed.Header.Version;


            foreach (var (animPath, animationVersion) in versions)
            {
                if (mainAnimationVersion == 5 && animationVersion == 8) continue; //no idea why this isn't problem in vanilla wh3
                if (mainAnimationVersion == 8 && animationVersion == 5) continue; //no idea why this isn't problem in vanilla wh3

                var isTheVersionMatch = animationVersion == mainAnimationVersion;
                var isMatchedCombat = animationSlot.StartsWith("COMBAT_");
                var isMountedAnim = animationSlot.StartsWith("RIDER_") && skeleton.Contains("humanoid");

                if (isMatchedCombat) continue;
                if (!isMountedAnim) continue;
                if (isTheVersionMatch) continue;
                {
                    errorList.Warning(animationSlot, $"Animation Meta Splice {_animationPersistanceMetaFileName} has different version than in the main animation. File referenced in meta: {animPath} with version {animationVersion} vs in the main animation {mainAnimationFile} with version {mainAnimationVersion}");
                    result = false;
                }
            }

            return result;
        }

        private bool CheckForRiderAndHisMountAnimationsVersion(string mountBinReference, PackFile animpack, string animationSlot, string animationFile, IPackFileService pfs, ErrorList errorList)
        {
            if (!animationSlot.Contains("RIDER_")) return true;

            var result = true;

            // Read once per Deep pass, not once per rider variant.
            var animPack = _loadedAnimPack ??= AnimationPackSerializer.Load(animpack, pfs);
            var itemNames = animPack.Files.ToList();

            var findMountBinReference = itemNames.Find(x => x.FileName.Contains(mountBinReference));
            if (findMountBinReference == null)
            {
                errorList.Warning(animationSlot, $"Cannot validate referenced {mountBinReference} of this rider, perhaps it's located in outside the current animpak files? or it is defined in another animpack or mod?");
                return false;
            }

            var mountBinBytes = findMountBinReference.ToByteArray();

            var mountBin = new AnimationBinWh3(findMountBinReference.FileName, mountBinBytes);
            var mountSlots = mountBin.TableVersion == 4 ? AnimationSlotTypeHelperWh3.GetInstance() : AnimationSlotTypeHelper3k.GetInstance();
            var animations = mountBin.AnimationTableEntries.Select(x => BinXml.FromEntry(x, mountSlots)).ToList();

            var riderAnimationSlotWithoutPrefix = animationSlot.Substring(6);
            var mainAnimationToCompareHeader = GetAnimationHeader(animationFile, pfs);
            if (mainAnimationToCompareHeader == null)
            {
                errorList.Warning(animationSlot, $"Cannot validate referenced {mountBinReference} of this rider, perhaps it's located in outside the current animpak files? or it is defined in another animpack or mod?");
                return false;
            }
            var mainAnimationToCompareVersion = mainAnimationToCompareHeader.Version;
            var mainAnimationToData = GetAnimationData(animationFile, pfs);
            if (mainAnimationToData == null)
            {
                errorList.Warning(animationSlot, $"Failed to find {animationFile} for {animationSlot}");
                return false;
            }

            var mainAnimationLength = mainAnimationToData.AnimationParts[0].DynamicFrames.Count;
            var mainAnimationTime = mainAnimationToCompareHeader.AnimationTotalPlayTimeInSec;


            foreach (var anim in animations)
            {
                if (anim.Slot != riderAnimationSlotWithoutPrefix) continue;

                var animationInstances = anim.Ref;
                foreach (var animationInstance in animationInstances)
                {
                    var header = GetAnimationHeader(animationInstance.File, pfs);

                    if (header == null)
                    {
                        errorList.Warning(animationSlot, $"Could not locate {animationInstance.File} while trying to validate CheckForRiderAndHisMountAnimationsVersion");
                        continue;
                    }

                    var version = header.Version;
                    var isVersionMatch = version == mainAnimationToCompareVersion;
                    if (!isVersionMatch)
                    {
                        errorList.Warning(animationSlot, $"Rider animation version mismatch with mount animation. Mount animation {animationInstance.File} with version {version} vs in the main animation {animationFile} with version {mainAnimationToCompareVersion}");
                        result = false;
                    }

                    var timing = header.AnimationTotalPlayTimeInSec;
                    var data = GetAnimationData(animationInstance.File, pfs);
                    if (data == null)
                    {
                        errorList.Warning(animationSlot, $"Failed to find {animationInstance.File} for {animationSlot}");
                        return false;
                    }
                    else
                    {
                        var length = data.AnimationParts[0].DynamicFrames.Count;
                        var isTImingMatch = mainAnimationTime == timing;
                        if (!isTImingMatch)
                        {
                            errorList.Warning(animationSlot, $"Rider animation mismatch with mount timing  animation. Mount animation {animationInstance.File} with timing {timing} vs in the main animation {animationFile} with timing {mainAnimationTime}");
                            result = false;
                        }

                        var isLenMatch = mainAnimationLength == length;
                        if (!isLenMatch)
                        {
                            errorList.Warning(animationSlot, $"Rider animation mismatch with mount frames animation. Mount animation {animationInstance.File} with length {length} vs in the main animation {animationFile} with timing {mainAnimationLength}");
                            result = false;
                        }
                    }
                }
            }

            return result;
        }


        private AnimationFile.AnimationHeader? GetAnimationHeader(string path, IPackFileService pfs)
        {
            var mainAnimation = pfs.FindFile(path);
            if (mainAnimation == null) return null;
            var mainAnimationParsed = AnimationFile.Create(mainAnimation);
            return mainAnimationParsed.Header;
        }

        private AnimationFile? GetAnimationData(string path, IPackFileService pfs)
        {
            var mainAnimation = pfs.FindFile(path);
            if (mainAnimation == null) return null;
            var mainAnimationParsed = AnimationFile.Create(mainAnimation);
            return mainAnimationParsed;
        }
    }
}
