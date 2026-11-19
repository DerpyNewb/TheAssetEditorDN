using Editors.AnimationMeta.SuperView;
using Editors.Shared.Core.Common;
using Moq;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Test.AnimationMeta
{
    // SuperView's slot grid: every slot of the picked set as a row, edited in place, played at once and saved with the set.
    public partial class SuperViewOpenFileTests
    {
        static string Key(string? path) => path?.Replace('\\', '/').ToLowerInvariant() ?? "";

        (SuperViewViewModel SuperView, IAnimationBinGenericFormat Set) OpenThrotTable()
        {
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(_runner.PackFileService.FindFile(ThrotVmd)!, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == ThrotTable);
            return (superView, selection.FragmentList.SelectedItem);
        }

        // The set as the editable pack now holds it.
        IAnimationBinGenericFormat SavedCopy(IAnimationBinGenericFormat set) => AnimTableEdits.Latest(_runner.PackFileService, (AnimationBinWh3)set);

        Dictionary<string, byte[]> AllFiles() => _runner.PackFileService.GetAllPackfileContainers()
            .SelectMany(c => c.GetAllFiles().Select(f => ($"{c.Name}|{f.Key}", f.Value.DataSource.ReadData()))).ToDictionary(x => x.Item1, x => x.Item2);

        static (string, int, string, string, string, float, float, int) Whole(AnimationBinEntryGenericFormat x) =>
            (x.SlotName, x.SlotIndex, x.AnimationFile, x.MetaFile, x.SoundFile, x.BlendInTime, x.SelectionWeight, x.WeaponBools);

        // The grid's rows are the set's slots. Picking a slot in the Scene panel picks its row, and picking a row picks its slot,
        // which plays and shows in the slot panel.
        [Test]
        public void SlotGrid_ListsTheSetsSlots_AndFollowsTheScenePanelBothWays()
        {
            var (superView, _) = OpenThrotTable();
            var (grid, selection, data) = (superView.SlotGrid, superView.SceneObjects[0].FragAndSlotSelection, superView.SceneObjects[0].Data);
            Assert.That((grid.Rows.Select(x => x.Slot).ToList(), grid.IsBattleTable, grid.HasUnsavedChanges),
                Is.EqualTo((selection.FragmentSlotList.PossibleValues.ToList(), true, false)));

            var attack = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
            selection.FragmentSlotList.SelectedItem = attack;
            Assert.That(grid.SelectedRow?.Slot, Is.SameAs(attack));

            var other = grid.Rows.First(x => x.Slot != attack && x.Animation != attack.AnimationFile && _runner.PackFileService.FindFile(x.Animation) != null);
            grid.SelectedRow = other;
            // Picked, not edited: the row keeps its own files rather than taking the previous row's from the slot panel.
            Assert.That((selection.FragmentSlotList.SelectedItem, Key(data.AnimationName.Value), superView.SlotAnim, superView.SlotMeta, other.IsEdited, grid.HasUnsavedChanges),
                Is.EqualTo((other.Slot, other.Animation, other.Saved.Animation, other.Saved.Meta, false, false)));
        }

        // Throt's table names one file per slot. ATTACK_5 is given a second variant, another slot's animation, as CA's idles have.
        string AddAttackVariant()
        {
            var pfs = _runner.PackFileService;
            var animPack = PackFileServiceUtility.GetAllAnimPacks(pfs).First(x => AnimationPackSerializer.Load(x, pfs).GetGenericAnimationSets().Any(t => t.FullPath == ThrotTable));
            var table = AnimationPackSerializer.Load(animPack, pfs).GetGenericAnimationSets().OfType<AnimationBinWh3>().Single(x => x.FileName == ThrotTable);
            var slots = ((IAnimationBinGenericFormat)table).Entries;
            var attack = slots.Single(x => x.SlotName == "ATTACK_5");
            var other = slots.Select(x => x.AnimationFile).First(x => x != attack.AnimationFile && pfs.FindFile(x) != null);
            AnimTableEdits.Row(table, attack.SlotIndex).AnimationRefs.Add(new() { AnimationFile = other, AnimationMetaFile = "", AnimationSoundMetaFile = "" });
            pfs.SaveFile(animPack, AnimTableEdits.Splice(animPack.DataSource.ReadData(), table.FileName, table.ToByteArray()));
            return other;
        }

        // An edit plays at once and writes nothing. Blend, weight and weapons are the slot's, so its other variants take them too.
        // Undo takes every edit back, playing the slot as saved; redo puts them back. Save writes them into the set in the editable
        // pack, every other row as it was, and leaves nothing to undo.
        [Test]
        public void SlotGrid_EditPlaysAtOnce_UndoesAndRedoes_AndSaveWritesItIntoTheSet()
        {
            var pfs = _runner.PackFileService;
            var second = AddAttackVariant();
            var (superView, set) = OpenThrotTable();
            var (grid, data) = (superView.SlotGrid, superView.SceneObjects[0].Data);
            var variants = grid.Rows.Where(x => x.Slot.SlotName == "ATTACK_5").ToList();
            Assert.That(variants.Select(x => x.Animation).Last(), Is.EqualTo(second), "ATTACK_5's second variant");
            var (row, sibling) = (variants[0], variants[1]);
            var (asSaved, siblingAsSaved) = (row.Values, sibling.Values);
            var donor = grid.Rows.Select(x => x.Animation).First(x => x != row.Animation && x != sibling.Animation && pfs.FindFile(x) != null);
            grid.SelectedRow = row;
            var files = AllFiles();

            row.Animation = donor;
            Assert.That(Key(data.AnimationName.Value), Is.EqualTo(donor), "the edit plays at once");
            row.BlendIn = asSaved.BlendIn + 0.25f;
            row.Weight = asSaved.Weight + 1.5f;
            row.Weapon3 = !row.Weapon3;
            var edited = row.Values;
            Assert.That((sibling.Animation, sibling.BlendIn, sibling.Weight, sibling.Values.Weapons),
                Is.EqualTo((siblingAsSaved.Animation, edited.BlendIn, edited.Weight, edited.Weapons)), "the slot's other variant");
            Assert.That((AllFiles(), row.IsEdited, sibling.IsEdited, superView.HasUnsavedChanges, grid.CanUndo), Is.EqualTo((files, true, true, true, true)));

            var undos = 0;
            for (; grid.CanUndo; undos++)
                grid.UndoCommand.Execute(null);
            Assert.That((undos, row.Values, sibling.Values, superView.HasUnsavedChanges, Key(data.AnimationName.Value)),
                Is.EqualTo((4, asSaved, siblingAsSaved, false, asSaved.Animation)));
            while (grid.CanRedo)
                grid.RedoCommand.Execute(null);
            Assert.That((row.Values, Key(data.AnimationName.Value), superView.SlotAnim), Is.EqualTo((edited, donor, donor)));

            var others = grid.Rows.Select(x => x.Slot).Where(x => x.SlotIndex != row.Slot.SlotIndex).Select(Whole).ToList();
            var asked = Answer(1);    // a save that left the grid thinking it still had edits would ask to save or drop them
            Assert.That(superView.Save(), Is.True);
            Assert.That(asked, Is.Empty, "the save asked about unsaved slot edits");
            var saved = SavedCopy(set).Entries;
            Assert.That(saved.Where(x => x.SlotIndex == row.Slot.SlotIndex).Select(x => (x.AnimationFile, x.BlendInTime, x.SelectionWeight, x.WeaponBools)),
                Is.EqualTo(new[] { (donor, edited.BlendIn, edited.Weight, edited.Weapons), (second, edited.BlendIn, edited.Weight, edited.Weapons) }));
            Assert.That(saved.Where(x => x.SlotIndex != row.Slot.SlotIndex).Select(Whole), Is.EqualTo(others));
            Assert.That((grid.HasUnsavedChanges, grid.CanUndo, superView.HasUnsavedChanges, Key(grid.SelectedRow?.Animation)), Is.EqualTo((false, false, false, donor)));
        }

        // Picking another set while the grid has edits asks first. Drop them writes nothing; Save them, or the box closed, writes them
        // into the set left, and the list offers it as saved. Asked once, with the picker on screen: the saved copy swapped into
        // its list makes WPF pick null, which must not find the edits just written still waiting.
        [TestCase(1, false), Apartment(ApartmentState.STA)]
        [TestCase(0, true)]
        [TestCase(null, true)]
        public void SlotGrid_LeavingASetWithEdits_AsksToSaveOrDropThem(int? choice, bool written)
        {
            AddThrotCampaignSet();
            var pfs = _runner.PackFileService;
            // Throt's own pack editable, so the save writes a new copy of the set rather than into the listed one.
            pfs.SetEditablePack(pfs.GetAllPackfileContainers().Single(x => x.Name.Contains("Throt")));
            var (superView, set) = OpenThrotTable();
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            BindPicker(selection.FragmentList);
            var row = superView.SlotGrid.Rows.First(x => x.Slot.SlotName == "ATTACK_5");
            var weight = row.Weight + 2f;
            row.Weight = weight;
            var asked = new List<IReadOnlyList<string>>();
            _runner.Dialogs.Setup(x => x.ShowChoiceBox(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Callback<string, string, IReadOnlyList<string>>((_, _, choices) => asked.Add(choices)).Returns(choice);
            var said = new List<string>();
            _runner.Dialogs.Setup(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((m, _) => said.Add(m));

            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == CamThrot);
            Assert.That((asked.Count, said, superView.SlotGrid.IsBattleTable, superView.SlotGrid.HasUnsavedChanges), Is.EqualTo((1, new List<string>(), false, false)));
            Assert.That(SavedCopy(set).Entries.First(x => x.SlotName == "ATTACK_5").SelectionWeight == weight, Is.EqualTo(written));
            var listed = selection.FragmentList.PossibleValues.Single(x => x.FullPath == ThrotTable);
            Assert.That(listed.Entries.First(x => x.SlotName == "ATTACK_5").SelectionWeight == weight, Is.EqualTo(written), "the list offers the set as saved");
        }

        // Full grid opens what the grid leaves out in the set's own editor: the animpack holding a battle table, the editable pack's
        // copy once a save has put one there, or a campaign set's file.
        [Test]
        public void SlotGrid_FullGrid_OpensTheSetsOwnFile()
        {
            AddThrotCampaignSet();
            var pfs = _runner.PackFileService;
            var (superView, set) = OpenThrotTable();
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            var animPack = set.PackFileReference!.FileName;
            var original = pfs.FindFile(animPack)!.DataSource.ReadData();
            superView.SlotGrid.Rows.First(x => x.Slot.SlotName == "ATTACK_5").Weight += 1f;
            Assert.That(superView.Save(), Is.True);
            var copy = pfs.FindFile(animPack, pfs.GetEditablePack()!);
            // A mod loaded after the editable pack with the same animpack, which a plain lookup by path would find instead.
            var later = _runner.CreateEmptyPackFile("later_mod", false);
            pfs.AddFilesToPack(later, [new NewPackFileEntry(System.IO.Path.GetDirectoryName(animPack)!, PackFile.CreateFromBytes(System.IO.Path.GetFileName(animPack), original))]);
            List<PackFile> Opened() => _runner.ScopeRepository.GetEditorHandles().OfType<IFileEditor>().Where(x => x != superView && x.CurrentFile != null)
                .Select(x => x.CurrentFile).ToList();

            superView.SlotGrid.OpenFullGridCommand.Execute(null);
            Assert.That((copy != null, copy != pfs.FindFile(animPack), Opened()), Is.EqualTo((true, true, new[] { copy })), "the editable pack's copy");
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == CamThrot);
            superView.SlotGrid.OpenFullGridCommand.Execute(null);
            Assert.That(Opened().Select(x => Key(pfs.GetFullPath(x))), Is.EqualTo(new[] { Key(animPack), CamThrot }));
        }

        // A campaign set's rows hold files only: an edit to blend, weight or weapons says so and changes nothing.
        [Test]
        public void SlotGrid_CampaignSet_RefusesBlendWeightAndWeapons()
        {
            AddThrotCampaignSet();
            var (superView, _) = OpenThrotTable();
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == CamThrot);
            var grid = superView.SlotGrid;
            var row = grid.Rows.First(x => x.Slot.SlotName == "status_battle/battle_fatality");
            var before = row.Values;
            foreach (var edit in new Action[] { () => row.BlendIn += 0.5f, () => row.Weight += 1f, () => row.Weapon2 = !row.Weapon2 })
            {
                grid.Status = "";
                edit();
                Assert.That((grid.Status, row.Values, grid.HasUnsavedChanges, grid.CanUndo),
                    Is.EqualTo(("A campaign set's rows hold files only: no blend, weight or weapons.", before, false, false)));
            }
        }
    }
}
