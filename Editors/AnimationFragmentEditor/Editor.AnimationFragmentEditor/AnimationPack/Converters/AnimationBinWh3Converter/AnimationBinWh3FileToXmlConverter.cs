using CommonControls.BaseDialogs.ErrorListDialog;
using Editors.Shared.Core.Editors.TextEditor;
using Editors.AnimationFragmentEditor.AnimationPack.BinGrid;
using GameWorld.Core.Services;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Ui.Editors.TextEditor;

namespace Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter
{
    public class AnimationBinWh3FileToXmlConverter : XmlToBinaryConverter<XmlFormat, AnimationBinWh3>
    {
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly MetaDataFileParser _metaDataTagDeSerializer;


        private readonly PackFile _animPackToValidate;

        public AnimationBinWh3FileToXmlConverter(ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper, MetaDataFileParser metaDataTagDeSerializer, PackFile animPackToValidate)
        {
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _metaDataTagDeSerializer = metaDataTagDeSerializer;
            _animPackToValidate = animPackToValidate;
        }

        protected override string CleanUpXml(string xmlText)
        {
            xmlText = xmlText.Replace("</BinEntry>", "</BinEntry>\n");
            xmlText = xmlText.Replace("<Bin>", "<Bin>\n");
            xmlText = xmlText.Replace("</GeneralBinData>", "</GeneralBinData>\n");
            return xmlText;
        }

        protected override XmlFormat ConvertBinaryToXml(byte[] bytes)
        {
            var binFile = new AnimationBinWh3("", bytes);
            var outputBin = new XmlFormat();

            var slotHelper = binFile.TableVersion == 4 ? AnimationSlotTypeHelperWh3.GetInstance() : AnimationSlotTypeHelper3k.GetInstance();
            outputBin.Version = binFile.TableVersion == 4 ? "Wh3" : "ThreeKingdom";

            outputBin.Data = new GeneralBinData()
            {
                TableVersion = binFile.TableVersion,
                TableSubVersion = binFile.TableSubVersion,
                Name = binFile.Name,
                MountBin = binFile.MountBin,
                SkeletonName = binFile.SkeletonName,
                LocomotionGraph = binFile.LocomotionGraph,
                UnknownValue1_RelatedToFlight = binFile.UnknownValue1
            };

            foreach (var animation in binFile.AnimationTableEntries)
                outputBin.Animations.Add(BinXml.FromEntry(animation, slotHelper));

            return outputBin;
        }

        protected override byte[] ConvertXmlToBinary(XmlFormat xmlBin, string fileName)
        {
            var binFile = new AnimationBinWh3("", null);

            binFile.TableVersion = xmlBin.Data.TableVersion;
            binFile.TableSubVersion = xmlBin.Data.TableSubVersion;
            binFile.Name = xmlBin.Data.Name;
            binFile.MountBin = xmlBin.Data.MountBin;
            binFile.SkeletonName = xmlBin.Data.SkeletonName;
            binFile.LocomotionGraph = xmlBin.Data.LocomotionGraph;
            binFile.UnknownValue1 = xmlBin.Data.UnknownValue1_RelatedToFlight;
            binFile.Unknown = "";

            var slotHelper = binFile.TableVersion == 4 ? AnimationSlotTypeHelperWh3.GetInstance() : AnimationSlotTypeHelper3k.GetInstance();

            foreach (var animationEntry in xmlBin.Animations)
                binFile.AnimationTableEntries.Add(BinXml.ToEntry(animationEntry, slotHelper));

            return binFile.ToByteArray();
        }

        /// <summary>Shows what the checks found. A test overrides it to read the list instead of opening a window.</summary>
        protected virtual void ShowFindings(ErrorList errors) => ErrorListWindow.ShowDialog("Errors", errors, false);

        protected override ITextConverter.SaveError Validate(XmlFormat type, string s, IPackFileService pfs, string filepath)
        {
            if (type.Data == null)
                return new ITextConverter.SaveError() { ErrorLength = 0, ErrorLineNumber = 1, ErrorPosition = 0, Text = "Data section of xml missing" };

            if (type.Animations == null)
                return new ITextConverter.SaveError() { ErrorLength = 0, ErrorLineNumber = 1, ErrorPosition = 0, Text = "Animation section of xml missing" };

            if (!(type.Data.TableVersion == 2 || type.Data.TableVersion == 4))
                return new ITextConverter.SaveError() { ErrorLength = 0, ErrorLineNumber = 1, ErrorPosition = 0, Text = "Incorrect TableVersion - must be 4 (wh3) or 2 (3k)" };

            if (type.Data.TableVersion == 4 && type.Data.TableSubVersion != 3)
                return new ITextConverter.SaveError() { ErrorLength = 0, ErrorLineNumber = 1, ErrorPosition = 0, Text = "Incorrect TableSubVersion - must be 3 for wh3" };

            if (string.IsNullOrWhiteSpace(type.Data.SkeletonName))
                return new ITextConverter.SaveError() { ErrorLength = 0, ErrorLineNumber = 1, ErrorPosition = 0, Text = "Missing skeleton item on root" };

            // A slot string that names no slot cannot become a table to check, so it is reported alone until it is fixed.
            var slotHelper = type.Data.TableVersion == 4 ? AnimationSlotTypeHelperWh3.GetInstance() : AnimationSlotTypeHelper3k.GetInstance();
            var errorList = new ErrorList();
            foreach (var animation in type.Animations.Where(x => BinXml.SlotId(slotHelper, x.Slot) == null))
                errorList.Error(animation.Slot, $"Not a valid animation slot for game");

            if (errorList.Errors.Count == 0)
            {
                var table = new AnimationBinWh3(filepath, ConvertXmlToBinary(type, filepath));
                var checks = new BinTableChecks(pfs, _skeletonAnimationLookUpHelper, _metaDataTagDeSerializer, _animPackToValidate);
                foreach (var finding in checks.Header(table, filepath).Concat(checks.Files(table)).Concat(checks.Deep(table)))
                {
                    if (finding.IsError)
                        errorList.Error(finding.Where, finding.Message);
                    else
                        errorList.Warning(finding.Where, finding.Message);
                }
            }

            if (errorList.Errors.Count != 0)
                ShowFindings(errorList);

            var hasCriticalError = errorList.Errors.Where(x => x.IsError).Any();
            if (hasCriticalError)
                return new ITextConverter.SaveError() { ErrorLength = 0, ErrorLineNumber = 1, ErrorPosition = 0, Text = "Critical Error found, unable to save" };
            return null;
        }
    }
}
