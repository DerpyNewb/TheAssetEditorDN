using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.ReferenceModel;
using Editors.UnitSimulator.Tables;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.DB;

namespace Editors.UnitSimulator.Personalities
{
    /// <summary>A battle_personalities row as the Add list shows it.</summary>
    public record PersonalityChoice(DbRow Row)
    {
        public string Text => $"{Row.Text("key")}   {Row.Text("man_animations_table")}   {Row.Text("variant")}   ({Row.Pack}: {Row.File})";
    }

    /// <summary>The Personalities tab: the mount's attach bones and the personalities on them (spec sections 3 and 4).</summary>
    public partial class PersonalityList : ObservableObject
    {
        public const string NoBone = "";
        readonly IPackFileService _pfs;
        readonly SceneObjectEditor _editor;
        readonly IStandardDialogs _dialogs;
        readonly SceneObjectViewModel _mount;
        readonly Func<string, string, SceneObjectViewModel> _createPart;
        readonly Action<SceneObjectViewModel> _removePart;
        PersonalityRows? _rows;
        Dictionary<string, IAnimationBinGenericFormat>? _tables;
        List<AttachBone> _bones = [];
        string _techNote = "";
        int _created;

        public PersonalityRows Rows => _rows ?? LoadRows();
        Dictionary<string, IAnimationBinGenericFormat> Tables => _tables ??= AnimTableEdits.AllByName(_pfs);
        /// <summary>The man_animations_table picker's list: every loaded battle table.</summary>
        public IEnumerable<string> TableNames => Tables.Keys.Order(StringComparer.Ordinal);
        public IEnumerable<string> VariantNames => Rows.VariantNames;

        public ObservableCollection<PersonalityPart> Parts { get; } = [];
        static readonly string[] Attaches = ["autonomous_rider", "officer", "additional_officer"];
        [ObservableProperty] ObservableCollection<string> _attachTypes = new(Attaches);
        [ObservableProperty] ObservableCollection<string> _boneNames = [NoBone];
        [ObservableProperty] ObservableCollection<string> _techSkeletons = [];
        [ObservableProperty] string? _selectedTechSkeleton;
        [ObservableProperty] string _techStatus = "";
        [ObservableProperty] string _dbStatus = "";
        [ObservableProperty] string _status = "";
        [ObservableProperty] string _search = "";
        [ObservableProperty] ObservableCollection<PersonalityChoice> _choices = [];
        [ObservableProperty] PersonalityChoice? _selectedChoice;
        [ObservableProperty] string _setLandUnit = "";
        [ObservableProperty] PersonalityPart? _selected;
        // ponytail: the folder is remembered for the app's run, not across runs.
        static string _lastExportFolder = "";
        [ObservableProperty] string _exportFolder = _lastExportFolder;
        [ObservableProperty] string _exportName = "";
        [ObservableProperty] string _exportStatus = "";

        /// <param name="createPart">id and header in, a new part in the host's scene out.</param>
        public PersonalityList(IPackFileService pfs, SceneObjectEditor editor, IStandardDialogs dialogs, SceneObjectViewModel mount,
            Func<string, string, SceneObjectViewModel> createPart, Action<SceneObjectViewModel> removePart)
        {
            _pfs = pfs;
            _editor = editor;
            _dialogs = dialogs;
            _mount = mount;
            _createPart = createPart;
            _removePart = removePart;
            mount.Data.SkeletonChanged += _ => RefreshBones();
            mount.Data.MeshChanged += _ => FindTechSkeletons();
        }

        PersonalityRows LoadRows()
        {
            _rows = new PersonalityRows(_pfs);
            DbStatus = string.Join(" ", _rows.Problems.Select(x => $"Skipped {x}"));
            return _rows;
        }

        // ponytail: the first 200 matches are shown; a longer search narrows it. Page the list if 200 is ever too few.
        partial void OnSearchChanged(string value) =>
            Choices = new(Rows.All.Where(x => x.Text("key").Contains(value.Trim(), StringComparison.OrdinalIgnoreCase)).Take(200).Select(x => new PersonalityChoice(x)));

        partial void OnSelectedTechSkeletonChanged(string? value) => RefreshBones();

        [RelayCommand]
        void Add()
        {
            if (SelectedChoice != null)
                Add(SelectedChoice.Row);
        }

        [RelayCommand]
        void AddSet() => AddSet(SetLandUnit);

        [RelayCommand]
        void Remove()
        {
            if (Selected != null)
                Remove(Selected);
        }

        [RelayCommand]
        void BrowseTechSkeleton()
        {
            var picked = _dialogs.DisplayBrowseDialog([".anim"]);
            if (picked.Result && picked.File != null)
                UseTechSkeleton(_pfs.GetFullPath(picked.File));
        }

        [RelayCommand]
        void Export()
        {
            var problem = PersonalityExport.Write(ExportFolder.Trim(), ExportName.Trim(), Parts, Rows.ExportVersion(PersonalityRows.Personalities), Rows.ExportVersion(PersonalityRows.Junctions));
            if (problem == null)
                _lastExportFolder = ExportFolder.Trim();
            ExportStatus = problem ?? $"Written: {PersonalityRows.Personalities}/{ExportName.Trim()}.tsv and {PersonalityRows.Junctions}/{ExportName.Trim()}.tsv under {ExportFolder.Trim()}. Import them in RPFM.";
        }

        // A folder on disk, not in a pack: DisplayBrowseFolderDialog browses the packs' folders.
        [RelayCommand]
        void BrowseExportFolder()
        {
            var picked = _dialogs.ShowSystemFolderBrowserDialog();
            if (picked.Result && !string.IsNullOrWhiteSpace(picked.FolderPath))
                ExportFolder = picked.FolderPath;
        }

        /// <summary>Adds the personality from one file's row, with the first junction row naming it if there is one.</summary>
        public void Add(DbRow copy) => Add(copy, Rows.JunctionOf(copy.Text("key")));

        void Add(DbRow copy, DbRow? junction)
        {
            var key = copy.Text("key");
            if (Parts.Any(x => x.Key == key))
            {
                Status = $"{key} is on already: a personality is attached once, as the export writes one junction row per personality and land unit.";
                return;
            }
            var part = new PersonalityPart(_createPart($"Personality{++_created}", key), copy, junction, _bones.FirstOrDefault()?.Name ?? NoBone);
            Parts.Add(part);
            part.PropertyChanged += (_, e) => OnPartChanged(part, e.PropertyName);
            Load(part);
            Check(part);
            MarkCopies(part);
            ListPartValues();
            Status = "";
            Selected = part;
        }

        void OnPartChanged(PersonalityPart part, string? property)
        {
            switch (property)
            {
                case nameof(PersonalityPart.Bone):
                    Check(part);
                    if (!BoneNames.Contains(part.Bone))
                        ListPartValues();
                    break;
                case nameof(PersonalityPart.Attach):
                    if (!AttachTypes.Contains(part.Attach))
                        ListPartValues();
                    break;
                case nameof(PersonalityPart.Table):
                    LoadTable(part);
                    break;
                case nameof(PersonalityPart.Variant):
                    LoadModel(part);
                    break;
                case nameof(PersonalityPart.Key):
                    MarkCopies(part);
                    break;
            }
        }

        // Every loaded file with the part's key, or "new key": an export with a key a loaded file has replaces or duplicates that row.
        void MarkCopies(PersonalityPart part)
        {
            var copies = Rows.Copies(part.Key);
            part.Copies = copies.Count == 0 ? "new key" : "in " + string.Join(", ", copies.Select(x => $"{x.Pack}: {x.File}"));
        }

        /// <summary>Every junction row naming the land unit, each personality on its row's bone, with its row's values.</summary>
        public void AddSet(string landUnit)
        {
            var unit = landUnit.Trim();
            var rows = Rows.JunctionsOfUnit(unit);
            if (rows.Count == 0)
            {
                Status = $"No loaded junction row names the land unit {unit}.";
                return;
            }
            var problems = new List<string>();
            var first = Parts.Count;
            foreach (var row in rows)
            {
                var key = row.Text("battle_personality");
                if (Rows.Copies(key).LastOrDefault() is not { } copy)
                    problems.Add($"{unit}'s junction row names {key}, which no loaded battle_personalities table has.");
                else if (Parts.Any(x => x.Key == key))
                    problems.Add($"{key} is on already.");
                else
                    Add(copy, row);
            }
            if (Parts.Count > first)
                Selected = Parts[first];
            Status = string.Join(" ", problems);
        }

        public void Remove(PersonalityPart part)
        {
            Parts.Remove(part);
            _removePart(part.Part);
            Status = "";
        }

        /// <summary>Gives the part its variant's model and its table, on the table's own skeleton, playing STAND.</summary>
        public void Load(PersonalityPart part)
        {
            LoadModel(part);
            LoadTable(part);
        }

        // Each half on its own: the pickers set Table and Variant on every keystroke.
        void LoadModel(PersonalityPart part)
        {
            var variant = Rows.Variant(part.Variant);
            var modelPath = variant == null ? "" : $@"variantmeshes\variantmeshdefinitions\{variant.Text("variant_filename")}.variantmeshdefinition";
            if (variant == null)
                part.ModelStatus = $"No loaded variants table has the variant {part.Variant}.";
            else if (_pfs.FindFile(modelPath) is not { } model)
                part.ModelStatus = $"The variant {part.Variant} names {modelPath.Replace('\\', '/')}, which no loaded pack has.";
            else
            {
                _editor.SetMesh(part.Part.Data, model, false);
                part.ModelStatus = "";
            }
        }

        void LoadTable(PersonalityPart part)
        {
            if (!Tables.TryGetValue(part.Table, out var table))
                part.TableStatus = $"No loaded animpack has the table {part.Table}.";
            else if (_pfs.FindFile($@"animations\skeletons\{table.SkeletonName}.anim") is not { } skeleton)
                part.TableStatus = $"The table {part.Table} uses the skeleton {table.SkeletonName}, which no loaded pack has.";
            else
            {
                // The skeleton first: a part's table list is read again for each new skeleton.
                _editor.SetSkeleton(part.Part.Data, skeleton);
                var tables = part.Part.FragAndSlotSelection.FragmentList;
                tables.SelectedItem = tables.PossibleValues.FirstOrDefault(x => x.FullPath == table.FullPath);
                var slots = part.Part.FragAndSlotSelection.FragmentSlotList;
                slots.SelectedItem = slots.PossibleValues.FirstOrDefault(x => x.SlotName == "STAND") ?? slots.PossibleValues.FirstOrDefault();
                part.TableStatus = "";
            }
        }

        /// <summary>A tech skeleton browsed to, offered beside the ones the variants rows give, and picked.</summary>
        public void UseTechSkeleton(string path)
        {
            if (!TechSkeletons.Contains(path, StringComparer.OrdinalIgnoreCase))
                TechSkeletons.Add(path);
            SelectedTechSkeleton = path;
        }

        /// <summary>Puts each part on its bone as the mount stands this frame. Run after the players have posed the skeletons and before
        /// the parts copy their Offset to their nodes (PersonalityPlacement).</summary>
        public void PlaceAll()
        {
            var skeleton = _mount.Data.Skeleton;
            foreach (var part in Parts)
            {
                var bone = skeleton == null ? null : _bones.FirstOrDefault(x => x.Name == part.Bone);
                part.Part.Data.Offset = bone == null ? Matrix.Identity : AttachBones.World(bone, skeleton!);
            }
        }

        /// <summary>Each bone a part sits on, once, where PlaceAll puts it: what the 3D view marks. A part at the origin, or on a bone
        /// the mount lacks, has none.</summary>
        public List<(string Bone, Matrix World)> Markers()
        {
            if (_mount.Data.Skeleton is not { } skeleton)
                return [];
            return Parts.Select(x => x.Bone).Distinct().Select(x => _bones.FirstOrDefault(b => b.Name == x)).OfType<AttachBone>()
                .Select(x => (x.Name, AttachBones.World(x, skeleton))).ToList();
        }

        // The mount model's tech skeletons: every .anim in the tech folder of each variants row naming the model's file that is built on
        // the mount's skeleton. The rest (destruction animations, proxies, turret parts) are named, not offered.
        void FindTechSkeletons()
        {
            var model = Path.GetFileNameWithoutExtension(_mount.Data.MeshName.Value);
            var found = Rows.TechFoldersOf(model).SelectMany(x => TechFolders.Skeletons(_pfs, x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var (offered, refused) = (new List<string>(), new List<string>());
            foreach (var path in found)
            {
                if (TechProblem(path) is { } why)
                    refused.Add(why);
                else
                    offered.Add(path);
            }
            _techNote = found.Count == 0
                ? $"No variants row with a tech folder names {model}: only the mount skeleton's own ap_ bones are offered."
                : refused.Count == 0 ? "" : "Not offered: " + string.Join(" ", refused);
            TechSkeletons = new(offered);
            SelectedTechSkeleton = offered.FirstOrDefault();
            RefreshBones();
        }

        // Why the file gives no attach bones for the mount's skeleton, or null.
        string? TechProblem(string path)
        {
            if (_mount.Data.Skeleton is not { } skeleton)
                return $"{path}: the mount has no skeleton.";
            return AttachBones.FromTech(skeleton, AnimationFile.Create(_pfs.FindFile(path)!), path).Problem;
        }

        void RefreshBones()
        {
            var skeleton = _mount.Data.Skeleton;
            var bones = new List<AttachBone>();
            string? problem = null;
            if (skeleton != null)
            {
                bones.AddRange(AttachBones.FromAnimation(skeleton));
                if (SelectedTechSkeleton != null && _pfs.FindFile(SelectedTechSkeleton) is { } file)
                {
                    var (fromTech, why) = AttachBones.FromTech(skeleton, AnimationFile.Create(file), SelectedTechSkeleton);
                    bones.AddRange(fromTech);
                    problem = why;
                }
            }
            _bones = bones;
            ListPartValues();
            TechStatus = string.Join(" ", new[] { problem ?? "", _techNote }.Where(x => x != ""));
            foreach (var part in Parts)
                Check(part);
        }

        // The pickers' lists: the mount's bones and the three attach types, each with every part's own value too. A WPF picker whose list
        // loses the shown part's value writes null into it, so a mount swapped under a selected part would erase its bone.
        void ListPartValues()
        {
            var own = Parts.Select(x => x.Bone).Where(x => !string.IsNullOrEmpty(x));
            string[] bones = [NoBone, .. _bones.Select(x => x.Name).Concat(own).Distinct()];
            if (!bones.SequenceEqual(BoneNames))
                BoneNames = new(bones);
            string[] attaches = [.. Attaches.Concat(Parts.Select(x => x.Attach).Where(x => !string.IsNullOrEmpty(x))).Distinct()];
            if (!attaches.SequenceEqual(AttachTypes))
                AttachTypes = new(attaches);
        }

        void Check(PersonalityPart part) =>
            part.BoneStatus = part.Bone == NoBone || _bones.Any(x => x.Name == part.Bone) ? "" : $"The mount has no bone {part.Bone}: it sits at the origin.";
    }
}
