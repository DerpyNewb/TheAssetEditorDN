using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.ReferenceModel;
using Editors.Shared.Core.Services.RiderBake;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.UnitSimulator.Bake
{
    /// <summary>The Bake pane: the rider's table moved from the mount it was made on (Made on) to the simulator's mount, seated on a
    /// bone or a picked vertex, with an offset. Preview plays the bake on the rider; Bake all writes every rider slot's clip and a new
    /// table into the editable pack. docs/superpowers/specs/2026-11-18-asseteditor-rider-bake-design.md.</summary>
    public partial class RiderBakePanel : ObservableObject
    {
        public const string OnFoot = "(made on foot)";

        readonly IPackFileService _pfs;
        readonly IFileSaveService _saver;
        readonly SceneObjectEditor _sceneObjectEditor;
        readonly SelectionManager? _selection;
        readonly SceneObjectViewModel _mount;
        readonly SceneObjectViewModel _rider;
        Dictionary<string, IAnimationBinGenericFormat>? _tables;
        GameSkeleton? _sourceSkeleton;
        (Rmv2MeshNode Mesh, int Vertex)? _vertex;

        [ObservableProperty] List<string> _sourceTables = [OnFoot];
        [ObservableProperty] string _source = OnFoot;
        [ObservableProperty] List<string> _sourceBones = [];
        [ObservableProperty] string? _sourceBone;
        [ObservableProperty] List<string> _targetBones = [];
        [ObservableProperty] string? _targetBone;
        [ObservableProperty] bool _seatOnVertex;
        [ObservableProperty] string _vertexText = "No vertex picked";
        [ObservableProperty] float _offsetX;
        [ObservableProperty] float _offsetY;
        [ObservableProperty] float _offsetZ;
        [ObservableProperty] float _yaw;
        [ObservableProperty] float _pitch;
        [ObservableProperty] float _roll;
        [ObservableProperty] bool _preview;
        [ObservableProperty] string _tableName = "";
        [ObservableProperty] string _status = "";
        [ObservableProperty] ObservableCollection<string> _report = [];

        public RiderBakePanel(IPackFileService pfs, IFileSaveService saver, SceneObjectEditor sceneObjectEditor, SelectionManager? selection,
            SceneObjectViewModel mount, SceneObjectViewModel rider)
        {
            _pfs = pfs;
            _saver = saver;
            _sceneObjectEditor = sceneObjectEditor;
            _selection = selection;
            _mount = mount;
            _rider = rider;
            rider.FragAndSlotSelection.FragmentList.SelectedItemChanged += _ => OnRiderTableChanged();
            mount.FragAndSlotSelection.FragmentList.SelectedItemChanged += _ => OnMountTableChanged();
            // The parts load their slot's clip first (they subscribed first), so the preview replaces it afterwards.
            rider.FragAndSlotSelection.FragmentSlotList.SelectedItemChanged += _ => Refresh();
            mount.FragAndSlotSelection.FragmentSlotList.SelectedItemChanged += _ => Refresh();
        }

        AnimationBinWh3? RiderTable => _rider.FragAndSlotSelection.FragmentList.SelectedItem as AnimationBinWh3;
        AnimationBinWh3? MountTable => _mount.FragAndSlotSelection.FragmentList.SelectedItem as AnimationBinWh3;
        AnimationBinWh3? SourceTable => Source == OnFoot ? null : Tables().GetValueOrDefault(Source) as AnimationBinWh3;

        Dictionary<string, IAnimationBinGenericFormat> Tables() => _tables ??= AnimTableEdits.AllByName(_pfs);

        // The rider's table names the mount its clips were made on in MountBin. The game does not read it, but it is what Made on starts at.
        void OnRiderTableChanged()
        {
            if (RiderTable is not { } rider)
                return;
            SourceTables = [OnFoot, .. Tables().Values.OfType<AnimationBinWh3>().Where(x => !string.IsNullOrEmpty(x.SkeletonName)).Select(x => x.Name).Order()];
            Source = SourceTables.Contains(rider.MountBin) ? rider.MountBin : OnFoot;
            TableName = MountTable == null ? rider.Name : $"{rider.Name}_on_{MountTable.Name}";
            OnSourceChanged(Source);
        }

        void OnMountTableChanged()
        {
            TargetBones = _mount.Data.Skeleton?.BoneNames.ToList() ?? [];
            if (RiderTable != null && MountTable != null)
                TableName = $"{RiderTable.Name}_on_{MountTable.Name}";
            TargetBone = SourceBone != null && TargetBones.Contains(SourceBone) ? SourceBone : AutoBone(_mount.Data.Skeleton, MountTable, out _);
            Refresh();
        }

        partial void OnSourceChanged(string value)
        {
            _sourceSkeleton = SourceTable is { } table ? Skeleton(table.SkeletonName) : null;
            SourceBones = _sourceSkeleton?.BoneNames.ToList() ?? [];
            SourceBone = AutoBone(_sourceSkeleton, SourceTable, out _);
            if (TargetBone == null && SourceBone != null && TargetBones.Contains(SourceBone))
                TargetBone = SourceBone;
            Refresh();
        }

        partial void OnSourceBoneChanged(string? value) => Refresh();
        partial void OnTargetBoneChanged(string? value) => Refresh();
        partial void OnSeatOnVertexChanged(bool value) => Refresh();
        partial void OnOffsetXChanged(float value) => Refresh();
        partial void OnOffsetYChanged(float value) => Refresh();
        partial void OnOffsetZChanged(float value) => Refresh();
        partial void OnYawChanged(float value) => Refresh();
        partial void OnPitchChanged(float value) => Refresh();
        partial void OnRollChanged(float value) => Refresh();

        partial void OnPreviewChanged(bool value)
        {
            if (!value && _rider.FragAndSlotSelection.FragmentSlotList.SelectedItem is { } slot)
                _sceneObjectEditor.SetAnimation(_rider.Data, slot.AnimationFile);
            Refresh();
        }

        // The mount bone a rider's hip sits over, found on the first slot both tables pair; null when there is none to look at.
        string? AutoBone(GameSkeleton? mountSkeleton, AnimationBinWh3? mountTable, out int bone)
        {
            bone = -1;
            if (mountSkeleton == null || mountTable == null || RiderTable is not { } rider || _rider.Data.Skeleton is not { } riderSkeleton)
                return null;
            foreach (var row in rider.AnimationTableEntries)
            {
                var slot = AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)row.AnimationId)?.Value;
                if (slot == null || !slot.StartsWith("RIDER_", StringComparison.Ordinal) || RiderBaker.IsCopyOnly(slot))
                    continue;
                var riderClip = Clip(row.AnimationRefs.FirstOrDefault()?.AnimationFile, riderSkeleton);
                var mountClip = Clip(RiderBakeBatch.FirstAnim(mountTable, slot["RIDER_".Length..]), mountSkeleton);
                if (riderClip == null || mountClip == null)
                    continue;
                var hip = AnimationSampler.Sample(0, 0, riderSkeleton, riderClip).GetSkeletonAnimatedWorld(riderSkeleton, Hip(riderSkeleton)).Translation;
                bone = RiderSeat.NearestBoneBelow(mountSkeleton, mountClip, hip);
                return mountSkeleton.BoneNames[bone];
            }
            return null;
        }

        /// <summary>The rider's hip: root on a humanoid, else the first bone under the animroot that is not a weapon bone.</summary>
        public static int Hip(GameSkeleton skeleton)
        {
            var root = skeleton.GetBoneIndexByName("root");
            return root > 0 ? root : skeleton.GetDirectChildBones(0).FirstOrDefault(b => !skeleton.BoneNames[b].StartsWith("be_prop", StringComparison.Ordinal));
        }

        /// <summary>Puts the mount's largest mesh into vertex picking: click a vertex in the 3D view, then Use picked vertex.</summary>
        [RelayCommand]
        void PickVertex()
        {
            if (_selection == null || LargestMesh() is not { } mesh)
            {
                Status = "Load the mount's model first.";
                return;
            }
            _mount.Data.IsSelectable = true;
            _selection.CreateSelectionSate(GeometrySelectionMode.Vertex, mesh);
            Status = $"Click a vertex of {mesh.Name} in the 3D view, then Use picked vertex.";
        }

        [RelayCommand]
        void UsePickedVertex()
        {
            if (_selection?.GetState<VertexSelectionState>() is not { } state || state.CurrentSelection().Count == 0 || state.RenderObject is not Rmv2MeshNode mesh)
            {
                Status = "No vertex is picked.";
                return;
            }
            UseVertex(mesh, state.CurrentSelection().First());
        }

        /// <summary>Seats the rider on a vertex of the mount's mesh.</summary>
        public void UseVertex(Rmv2MeshNode mesh, int vertex)
        {
            _vertex = (mesh, vertex);
            VertexText = $"{mesh.Name}, vertex {vertex}";
            if (SeatOnVertex)
                Refresh();
            else
                SeatOnVertex = true;
        }

        Rmv2MeshNode? LargestMesh() => _mount.Data.ModelNode == null ? null
            : SceneNodeHelper.GetChildrenOfType<Rmv2LodNode>(_mount.Data.ModelNode).FirstOrDefault()?.GetAllModels(false).MaxBy(x => x.Geometry.VertexCount());

        Matrix Offset => RiderBaker.Offset(new Vector3(OffsetX, OffsetY, OffsetZ), new Vector3(Yaw, Pitch, Roll));

        RiderBakeMount? Target()
        {
            if (_mount.Data.Skeleton is not { } skeleton)
                return null;
            if (SeatOnVertex)
                return _vertex is { } v ? new RiderBakeMount(skeleton, RiderSeat.Vertex(v.Mesh, v.Vertex)) : null;
            var bone = TargetBone == null ? -1 : skeleton.GetBoneIndexByName(TargetBone);
            return bone < 0 ? null : new RiderBakeMount(skeleton, RiderSeat.Bone(skeleton, bone));
        }

        RiderBakeMount? SourceMount()
        {
            if (_sourceSkeleton == null || SourceBone == null)
                return null;
            var bone = _sourceSkeleton.GetBoneIndexByName(SourceBone);
            return bone < 0 ? null : new RiderBakeMount(_sourceSkeleton, RiderSeat.Bone(_sourceSkeleton, bone));
        }

        /// <summary>With Preview on, the rider plays the bake of the pair on show.</summary>
        public void Refresh()
        {
            if (!Preview)
                return;
            var riderSlot = _rider.FragAndSlotSelection.FragmentSlotList.SelectedItem;
            var mountSlot = _mount.FragAndSlotSelection.FragmentSlotList.SelectedItem;
            if (riderSlot == null || mountSlot == null || _rider.Data.Skeleton is not { } riderSkeleton || _mount.Data.AnimationClip is not { } targetClip || Target() is not { } target)
            {
                Status = "Preview needs a rider slot, a mount slot and a seat.";
                return;
            }
            if (RiderBaker.IsCopyOnly(riderSlot.SlotName))
            {
                Status = $"{riderSlot.SlotName} is copy-only: a bake keeps it as it is.";
                return;
            }
            var riderClip = Clip(riderSlot.AnimationFile, riderSkeleton);
            var source = SourceMount();
            var sourceClip = SourceTable is { } table && source != null ? Clip(RiderBakeBatch.FirstAnim(table, mountSlot.SlotName), source.Skeleton) : null;
            if (riderClip == null || (SourceTable != null && sourceClip == null))
            {
                Status = SourceTable != null && sourceClip == null ? $"{Source} has no {mountSlot.SlotName} to take the rider off." : $"{riderSlot.AnimationFile} is in no loaded pack.";
                return;
            }
            var baked = RiderBaker.Bake(riderClip, riderSkeleton, source, sourceClip, target, targetClip, Offset);
            _sceneObjectEditor.SetAnimationClip(_rider.Data, baked, $"baked {riderSlot.SlotName}");
            Status = $"Previewing {riderSlot.SlotName} baked onto {mountSlot.SlotName}, seated on {target.Seat.Name}.";
        }

        /// <summary>Bakes every rider slot onto the mount's table and saves the clips and the table into the editable pack.</summary>
        [RelayCommand]
        public void BakeAll()
        {
            if (RiderTable is not { } rider || MountTable is not { } mount || _rider.Data.Skeleton is not { } riderSkeleton || Target() is not { } target)
            {
                Status = "Pick the rider's table, the mount's table and a seat first.";
                return;
            }
            if (_pfs.GetEditablePack() == null)
            {
                Status = "No editable pack is selected.";
                return;
            }
            if (string.IsNullOrWhiteSpace(TableName))
            {
                Status = "Name the new table.";
                return;
            }
            var source = SourceTable is { } table && SourceMount() is { } sourceMount ? new RiderBakeTable(table, sourceMount) : null;
            var output = RiderBakeBatch.Bake(rider, riderSkeleton, source, new RiderBakeTable(mount, target), Offset, TableName.Trim(), Clip);
            Report = new(output.Report);
            // The table first: a table that will not save leaves no clips behind in the pack.
            if (AnimTableEdits.SaveNew(_pfs, _saver, output.Table) is { } problem)
            {
                Status = problem;
                return;
            }
            foreach (var (path, bytes) in output.Anims)
                _saver.Save(path, bytes, false);
            Status = $"{output.Anims.Count} clips and {output.Table.FileName} saved into {AnimTableEdits.OwnAnimPackPath(_pfs.GetEditablePack()!)}.";
        }

        AnimationClip? Clip(string? path, GameSkeleton skeleton) =>
            string.IsNullOrWhiteSpace(path) || _pfs.FindFile(path) is not { } file ? null : new AnimationClip(AnimationFile.Create(file), skeleton);

        GameSkeleton? Skeleton(string name) => _pfs.FindFile($"animations/skeletons/{name}.anim") is { } file ? new GameSkeleton(AnimationFile.Create(file), null) : null;
    }
}
