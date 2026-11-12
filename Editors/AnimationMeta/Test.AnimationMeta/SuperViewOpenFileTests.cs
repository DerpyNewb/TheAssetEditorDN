using System.IO;
using Editors.AnimationMeta.SuperView;
using Editors.Shared.Core.Common;
using GameWorld.Core.SceneNodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationPack;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // A .variantmeshdefinition offers SuperView in the tool selector, beside the XML and Kitbash editors.
    public class SuperViewOpenFileTests
    {
        const string ThrotVmd = @"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition";
        const string ThrotTable = @"animations/database/battle/bin/hu17_dlc16_throt.bin";

        AssetEditorTestRunner _runner = null!;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
        }

        [Test]
        public void VariantMesh_OpensInSuperView_WithItsSkeletonAndAnimations()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);

            Assert.That(superView.CurrentFile, Is.SameAs(vmd));
            var asset = superView.SceneObjects[0];
            Assert.That(asset.Data.MeshName.Value, Is.EqualTo(vmd.Name));
            Assert.That(asset.Data.Skeleton, Is.Not.Null, "the mesh's skeleton did not load");

            // Picking an animation set and slot loads that animation's meta, as when SuperView is opened empty.
            var selection = asset.FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
            Assert.That(superView.MetaEditor.ParsedFile, Is.Not.Null);
            Assert.That(superView.MetaEditor.Tags, Is.Not.Empty);
        }

        // SuperView's tab goes red with its meta editors: an edited field says SuperView has unsaved changes, so its tab heard.
        [Test]
        public void VariantMesh_AnEditedTagMarksTheTabUnsaved()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
            var said = new List<string?>();
            superView.PropertyChanged += (_, e) => said.Add(e.PropertyName);
            Assert.That(superView.HasUnsavedChanges, Is.False, "as loaded");

            var field = superView.MetaEditor.Tags.SelectMany(x => x.Variables).First(x => !x.IsReadOnly && x.Scrub == Shared.Ui.Common.Behaviors.ScrubMode.Integer);
            field.ValueAsString = (int.Parse(field.ValueAsString) + 1).ToString();
            Assert.That((superView.HasUnsavedChanges, said.Contains(nameof(superView.HasUnsavedChanges))), Is.EqualTo((true, true)));
        }

        // The meta props always play; the main model has to as well, or it stands in its bind pose
        // under them until the "Animation" toggle is found.
        [Test]
        public void VariantMesh_MainModelPlaysThePickedSlot()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");

            var data = superView.SceneObjects[0].Data;
            var meshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(data.ModelNode);
            Assert.That(meshes, Is.Not.Empty);
            Assert.That(meshes.All(x => x.AnimationPlayer == data.Player), "a mesh is not driven by the model's player");

            // One step of what AnimationsContainerComponent does every frame.
            Matrix[] Pose(double ms)
            {
                data.Player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(ms)));
                return Enumerable.Range(0, data.Skeleton.BoneCount).Select(data.Skeleton.GetAnimatedWorldTranform).ToArray();
            }
            var start = Pose(0);
            var later = Pose(400);

            Assert.That(data.Player.IsEnabled, Is.True, "the main model's player is off");
            Assert.That(later, Is.Not.EqualTo(start), "no bone moved in 400ms of the slot's animation");
        }

        // A slot of the animation set says which weapon bones it shows (bit N for be_prop_N), and the game hides the weapons on
        // the rest: STAND_NO_WEAPON shows none, and a Doomseeker's STAND_IDLE_1 only his left flail, the right one replaced by
        // the flail its animated prop swings. SuperView drew every weapon in every slot.
        [Test]
        public void AnimationSlot_ShowsOnlyTheWeaponBonesItFlags()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            var weapons = SceneNodeHelper.GetChildrenOfType<SlotNode>(superView.SceneObjects[0].Data.ModelNode)
                .Where(x => x.AttachmentBoneName?.StartsWith("be_prop_") == true && x.Children.Count > 0).ToList();
            var attack = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");

            // Throt's set shows every weapon in the slots the test pack carries, so ATTACK_5 is given the flags of a slot
            // that shows only his first weapon bone, then of one that shows all six.
            var kept = weapons[0].AttachmentBoneName;
            attack.WeaponBools = 1 << int.Parse(kept["be_prop_".Length..]);
            selection.FragmentSlotList.SelectedItem = attack;
            var onlyFirst = weapons.Where(x => x.IsVisible).Select(x => x.AttachmentBoneName).ToList();

            selection.FragmentSlotList.SelectedItem = null;
            attack.WeaponBools = 0b111111;
            selection.FragmentSlotList.SelectedItem = attack;
            Assert.That((weapons.Count > 1, onlyFirst, weapons.All(x => x.IsVisible)), Is.EqualTo((true, new List<string> { kept }, true)));
        }

        // Save meta as writes the open tab's meta, edits and all, to a new file and points the slot that tab shows at it, in the
        // table saved back into its animpack in the editable pack. The meta it was read from, and every other row, stay as they were.
        // The path is typed as a person might, and lands lower-cased with its extension, where the table says it is.
        [TestCase(1)]
        [TestCase(0)]
        public void SaveMetaAs_WritesANewMeta_AndPointsTheSlotAtIt(int tab)
        {
            var pfs = _runner.PackFileService;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(pfs.FindFile(ThrotVmd)!, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == ThrotTable);
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
            superView.SelectedTabControllerIndex = tab;
            var editor = tab == 0 ? superView.PersistentMetaEditor : superView.MetaEditor;
            var (original, originalBytes) = (editor.CurrentFile, editor.CurrentFile.DataSource.ReadData());
            var animPackPath = selection.FragmentList.SelectedItem.PackFileReference!.FileName;
            static (string, string, string, string) Row(AnimationBinEntryGenericFormat x) => (x.SlotName, x.AnimationFile, x.MetaFile, x.SoundFile);
            var rows = selection.FragmentList.SelectedItem.Entries.Select(Row).ToList();
            var assigned = rows.FindIndex(x => x.Item3 == (tab == 0 ? selection.MetaDataPersistName : selection.MetaDataName) && x.Item1 == (tab == 0 ? "PERSISTENT_METADATA_ALIVE" : "ATTACK_5"));

            var field = editor.Tags.SelectMany(x => x.Variables).First(x => !x.IsReadOnly && x.Scrub == Shared.Ui.Common.Behaviors.ScrubMode.Integer);
            var fieldAt = editor.Tags.SelectMany(x => x.Variables).ToList().IndexOf(field);
            field.ValueAsString = (int.Parse(field.ValueAsString) + 1).ToString();
            var edited = field.ValueAsString;

            const string Saved = "animations/derpy/throt_assigned.anm.meta";
            var said = new List<string>();
            _runner.Dialogs.Setup(x => x.DisplaySaveDialog(It.IsAny<IPackFileService>(), It.IsAny<List<string>>())).Returns(new SaveDialogResult(true, null, @"Animations\Derpy\Throt_Assigned"));
            _runner.Dialogs.Setup(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((m, _) => said.Add(m));
            superView.SaveMetaAsCommand.Execute(null);
            Assert.That(said, Is.Empty);

            var output = pfs.GetEditablePack()!;
            var table = AnimationPackSerializer.Load(pfs.FindFile(animPackPath, output)!, pfs).GetGenericAnimationSets().First(x => x.FullPath == ThrotTable);
            var expected = rows.Select((x, i) => i == assigned ? (x.Item1, x.Item2, Saved, x.Item4) : x).ToList();
            Assert.That((assigned >= 0, table.Entries.Select(Row).ToList()), Is.EqualTo((true, expected)));

            // The tab now shows the new file, read back from the pack with the edit in it; the slot names it; the original is untouched.
            Assert.That((pfs.GetFullPath(editor.CurrentFile).Replace('\\', '/'), editor.HasUnsavedChanges, tab == 0 ? selection.MetaDataPersistName : selection.MetaDataName),
                Is.EqualTo((Saved, false, Saved)));
            Assert.That(editor.Tags.SelectMany(x => x.Variables).ElementAt(fieldAt).ValueAsString, Is.EqualTo(edited));
            Assert.That((original.DataSource.ReadData(), pfs.FindFile(pfs.GetFullPath(original), output)), Is.EqualTo((originalBytes, (PackFile?)null)));
        }

        // A table list read before a save (as one is again on a skeleton change, a copy per pack) still offers the table as it was.
        // An assignment made from that stale copy edits the copy the pack now holds, so the earlier assignment survives it.
        [Test]
        public void SaveMetaAs_FromAStaleTable_KeepsTheEarlierAssignment()
        {
            var pfs = _runner.PackFileService;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(pfs.FindFile(ThrotVmd)!, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            // Read on its own, as a list read again would read it; the first save edits the listed copy, not this one.
            var stale = PackFileServiceUtility.GetAllAnimPacks(pfs).SelectMany(x => AnimationPackSerializer.Load(x, pfs).GetGenericAnimationSets()).First(x => x.FullPath == ThrotTable);
            void Pick(IAnimationBinGenericFormat table, int tab)
            {
                selection.FragmentList.SelectedItem = table;
                selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
                superView.SelectedTabControllerIndex = tab;
            }
            _runner.Dialogs.SetupSequence(x => x.DisplaySaveDialog(It.IsAny<IPackFileService>(), It.IsAny<List<string>>()))
                .Returns(new SaveDialogResult(true, null, "animations/derpy/first")).Returns(new SaveDialogResult(true, null, "animations/derpy/second"));

            Pick(selection.FragmentList.PossibleValues.First(x => x.FullPath == ThrotTable), 1);
            superView.SaveMetaAsCommand.Execute(null);
            selection.FragmentList.UpdatePossibleValues(selection.FragmentList.PossibleValues.Append(stale).ToList());
            Pick(stale, 0);
            superView.SaveMetaAsCommand.Execute(null);

            var table = AnimationPackSerializer.Load(pfs.FindFile(stale.PackFileReference!.FileName, pfs.GetEditablePack())!, pfs).GetGenericAnimationSets().First(x => x.FullPath == ThrotTable);
            Assert.That(table.Entries.Where(x => x.SlotName is "ATTACK_5" or "PERSISTENT_METADATA_ALIVE").Select(x => (x.SlotName, x.MetaFile)),
                Is.SupersetOf(new[] { ("ATTACK_5", "animations/derpy/first.anm.meta"), ("PERSISTENT_METADATA_ALIVE", "animations/derpy/second.anm.meta") }));
        }

        // A table in a CA pack is not refused: it goes alone into an animpack of the editable pack's own, under its CA name, as
        // the Workshop's mods that edit CA tables ship them, rather than the whole CA animpack being copied over CA's. A second
        // save finds that copy by name, so the first assignment stays, and SuperView opened again lists the table once, as saved.
        [Test]
        public void SaveMetaAs_OnACaTable_SavesTheTableAloneIntoAnAnimpackOfTheEditablePack()
        {
            var pfs = _runner.PackFileService;
            pfs.GetAllPackfileContainers().Single(x => x.Name.Contains("Throt")).IsCaPackFile = true;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(pfs.FindFile(ThrotVmd)!, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            var caAnimPack = selection.FragmentList.PossibleValues.First(x => x.FullPath == ThrotTable).PackFileReference!.FileName;
            var said = new List<string>();
            _runner.Dialogs.Setup(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((m, _) => said.Add(m));
            _runner.Dialogs.SetupSequence(x => x.DisplaySaveDialog(It.IsAny<IPackFileService>(), It.IsAny<List<string>>()))
                .Returns(new SaveDialogResult(true, null, "animations/derpy/first")).Returns(new SaveDialogResult(true, null, "animations/derpy/second"));
            foreach (var tab in new[] { 1, 0 })
            {
                selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == ThrotTable);
                selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
                superView.SelectedTabControllerIndex = tab;
                superView.SaveMetaAsCommand.Execute(null);
            }

            var output = pfs.GetEditablePack()!;
            var own = pfs.FindFile("animations/database/battle/bin/!!testoutput_animation_tables.animpack", output);
            Assert.That((said, own != null, pfs.FindFile(caAnimPack, output)), Is.EqualTo((new List<string>(), true, (PackFile?)null)));
            Assert.That(AnimTableEdits.ReadEntries(own!.DataSource.ReadData()).Select(x => x.Name), Is.EqualTo(new[] { ThrotTable }));
            var expected = new[] { ("ATTACK_5", "animations/derpy/first.anm.meta"), ("PERSISTENT_METADATA_ALIVE", "animations/derpy/second.anm.meta") };
            static IEnumerable<(string, string)> Assigned(IAnimationBinGenericFormat table) =>
                table.Entries.Where(x => x.SlotName is "ATTACK_5" or "PERSISTENT_METADATA_ALIVE").Select(x => (x.SlotName, x.MetaFile));
            Assert.That(Assigned(AnimationPackSerializer.Load(own, pfs).GetGenericAnimationSets().Single()), Is.SupersetOf(expected));

            // The list read from the packs again, as SuperView opened anew reads it.
            superView.SceneObjects[0].Data.TriggerSkeletonChanged();
            var listed = selection.FragmentList.PossibleValues.Where(x => x.FullPath == ThrotTable).ToList();
            Assert.That(listed, Has.Count.EqualTo(1));
            Assert.That(Assigned(listed[0]), Is.SupersetOf(expected));
        }

        // A skeleton with no battle animation table (Glottkin's only set is cam_lord_glt01_dlc29_ghurk.bin) still lists its
        // campaign animation set, and a slot from it plays with the set's persistent meta. Its hand poses and docks take the
        // battle slot names the meta's HandPose and DockEquipment tags look up.
        const string CamThrot = @"animations/campaign/database/bin/cam_throt.bin";

        // A campaign set for Throt's skeleton, made from his battle table, in a new editable pack: a global status with his
        // persistent meta, a hand pose and a dock, and a status_battle with one fatality.
        (IAnimationBinGenericFormat Battle, AnimationBinEntryGenericFormat Attack, AnimationBinEntryGenericFormat Persist) AddThrotCampaignSet()
        {
            var battle = PackFileServiceUtility.GetAllAnimPacks(_runner.PackFileService)
                .SelectMany(x => AnimationPackSerializer.Load(x, _runner.PackFileService).GetGenericAnimationSets())
                .First(x => x.FullPath == ThrotTable);
            var attack = battle.Entries.First(x => x.SlotName == "ATTACK_5");
            var persist = battle.Entries.First(x => x.SlotName == "PERSISTENT_METADATA_ALIVE");
            var bin = new CampaignAnimationBin
            {
                Version = 3,
                SkeletonName = battle.SkeletonName,
                Status =
                [
                    new()
                    {
                        Name = "global",
                        PersitantMetaData = [new() { Animation = "", Type = "global", AnimationMeta = persist.MetaFile, SoundMeta = "" }],
                        Poses = [new() { Animation = attack.AnimationFile, Type = "global", AnimationMeta = "", SoundMeta = "", PoseId = 3 }],
                        Docks = [new() { Animation = attack.AnimationFile, Type = "global", AnimationMeta = "", SoundMeta = "", Dock = "DOCK_EQPT_RHAND_2" }],
                    },
                    new() { Name = "status_battle", Action = [new() { Animation = attack.AnimationFile, Type = "global", Meta = attack.MetaFile, SoundMeta = "", ActionType = "battle_fatality", ActionId = 1 }] },
                ],
            };
            var pack = _runner.CreateOutputPack();
            _runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry(@"animations\campaign\database\bin", PackFile.CreateFromBytes("cam_throt.bin", CampaignAnimationBinLoader.Write(bin, "cam_throt")))]);
            return (battle, attack, persist);
        }

        // A campaign set is edited from SuperView as a battle table is. The slot panel follows the pick; its browse offers the
        // skeleton's own animations; Apply points the slot at other files and Save meta as at a new meta; the set is written whole
        // into the editable pack, paths lower-cased with forward slashes, and the other rows are left as they were.
        [Test]
        public void CampaignSet_SlotFilesAndSaveMetaAs_WriteTheSet()
        {
            var pfs = _runner.PackFileService;
            var (battle, attack, persist) = AddThrotCampaignSet();
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(pfs.FindFile(ThrotVmd)!, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == CamThrot);
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "status_battle/battle_fatality");
            Assert.That((superView.SlotAnim, superView.SlotMeta), Is.EqualTo((attack.AnimationFile, attack.MetaFile)), "the panel shows the picked slot");

            Func<string, bool>? offered = null;
            _runner.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>(), It.IsAny<Func<string, bool>?>()))
                .Callback<List<string>, Func<string, bool>?>((_, filter) => offered = filter)
                .Returns(new BrowseDialogResultFile(true, pfs.FindFile(attack.AnimationFile)!));
            superView.SlotAnim = "";
            superView.BrowseSlotAnimCommand.Execute(null);
            Assert.That((offered!(attack.AnimationFile.Replace('/', '\\')), offered("animations/battle/humanoid01/not_his.anim"), superView.SlotAnim),
                Is.EqualTo((true, false, attack.AnimationFile)));

            var other = battle.Entries.Select(x => x.AnimationFile).First(x => x != attack.AnimationFile && pfs.FindFile(x) != null);
            var said = new List<string>();
            _runner.Dialogs.Setup(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((m, _) => said.Add(m));
            superView.SlotAnim = other.ToUpperInvariant().Replace('/', '\\');
            superView.ApplySlotFilesCommand.Execute(null);
            _runner.Dialogs.Setup(x => x.DisplaySaveDialog(It.IsAny<IPackFileService>(), It.IsAny<List<string>>())).Returns(new SaveDialogResult(true, null, @"Animations\Derpy\Cam_Fatality"));
            superView.SelectedTabControllerIndex = 1;
            superView.SaveMetaAsCommand.Execute(null);
            Assert.That(said, Is.Empty);

            const string NewMeta = "animations/derpy/cam_fatality.anm.meta";
            var saved = CampaignAnimationBinLoader.Load(pfs.FindFile(CamThrot, pfs.GetEditablePack())!.DataSource.ReadDataAsChunk());
            var row = saved.Status.Single(x => x.Name == "status_battle").Action.Single();
            Assert.That((row.Animation, row.Meta, saved.Status.Single(x => x.Name == "global").PersitantMetaData.Single().AnimationMeta),
                Is.EqualTo((other, NewMeta, persist.MetaFile)));
            Assert.That((selection.MetaDataName, superView.SlotAnim, superView.SlotMeta, pfs.FindFile(NewMeta) != null), Is.EqualTo((NewMeta, other, NewMeta, true)));
        }

        // The campaign writer lower-cases every string, so a set with capitals is not what it writes back. One edit and then another
        // to a row naming a path with capitals both save, as the set in hand after a save is the one read back from the file.
        [Test]
        public void CampaignSet_WithCapitals_TakesASecondEdit()
        {
            var pfs = _runner.PackFileService;
            var (_, attack, _) = AddThrotCampaignSet();
            // Capitals in the path the fatality, the hand pose and the dock all name, as a set written by hand might have them.
            var file = pfs.FindFile(CamThrot, pfs.GetEditablePack())!;
            var bytes = file.DataSource.ReadData();
            var at = bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(attack.AnimationFile));
            System.Text.Encoding.UTF8.GetBytes(attack.AnimationFile.ToUpperInvariant()).CopyTo(bytes, at);
            pfs.SaveFile(file, bytes);

            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(pfs.FindFile(ThrotVmd)!, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == CamThrot);
            var said = new List<string>();
            _runner.Dialogs.Setup(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((m, _) => said.Add(m));
            foreach (var slot in new[] { "status_battle/battle_fatality", "DOCK_EQUIPMENT_RIGHT_HAND_2" })
            {
                selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == slot);
                superView.SlotMeta = $"animations/derpy/{slot.Replace('/', '_')}.anm.meta";
                superView.ApplySlotFilesCommand.Execute(null);
            }

            var saved = CampaignAnimationBinLoader.Load(pfs.FindFile(CamThrot, pfs.GetEditablePack())!.DataSource.ReadDataAsChunk());
            Assert.That((at >= 0, said, saved.Status.Single(x => x.Name == "status_battle").Action.Single().Meta, saved.Status.Single(x => x.Name == "global").Docks.Single().AnimationMeta),
                Is.EqualTo((true, new List<string>(), "animations/derpy/status_battle_battle_fatality.anm.meta", "animations/derpy/dock_equipment_right_hand_2.anm.meta")));
        }

        [Test]
        public void VariantMesh_ListsItsCampaignAnimationSet()
        {
            var (_, attack, persist) = AddThrotCampaignSet();

            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/campaign/database/bin/cam_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "status_battle/battle_fatality");
            Assert.That(superView.MetaEditor.ParsedFile, Is.Not.Null);
            Assert.That((selection.MetaDataName, selection.MetaDataPersistName), Is.EqualTo((attack.MetaFile, persist.MetaFile)));
            Assert.That(selection.FragmentSlotList.PossibleValues.Select(x => x.SlotName), Is.SupersetOf(new[] { "HAND_POSE_GRIP", "DOCK_EQUIPMENT_RIGHT_HAND_2" }));
            // A campaign set has no weapon flags, so its slots hide no weapon.
            var weapons = SceneNodeHelper.GetChildrenOfType<SlotNode>(superView.SceneObjects[0].Data.ModelNode).Where(x => x.AttachmentBoneName?.StartsWith("be_prop_") == true).ToList();
            Assert.That((weapons.Count > 0, weapons.All(x => x.IsVisible)), Is.EqualTo((true, true)), "a campaign slot hid a weapon");
        }

        [Test]
        public void VariantMesh_AlreadyOpenInKitbash_StillOpensInSuperView()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var open = _runner.CommandFactory.Create<OpenEditorCommand>();
            open.Execute(vmd, EditorEnums.Kitbash_Editor);

            Assert.That(open.Execute(vmd, EditorEnums.SuperView_Editor), Is.InstanceOf<SuperViewViewModel>());
        }

        // A vfx file added or saved in any pack (the VFX editor's save, a replaced texture) rebuilds the scene's effects, so
        // SuperView shows it without the tab being reopened, and so does a model, material or animation a rigid particle may
        // draw, wherever it lives; any other file leaves the scene alone.
        [TestCase(@"vfx\test_smoke.xml", true)]
        [TestCase(@"variantmeshes\wh_variantmodels\x\x.rigid_model_v2", true)]
        [TestCase(@"variantmeshes\wh_variantmodels\x\x.wsmodel", true)]
        [TestCase(@"variantmeshes\wh_variantmodels\x\x.xml.material", true)]
        [TestCase(@"animations\battle\x\x.anim", true)]
        [TestCase(@"text\notes.txt", false)]
        public void AChangedVfxFile_RebuildsTheScene(string path, bool rebuilds)
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var data = superView.SceneObjects[0].Data;
            var pack = _runner.CreateOutputPack();

            var before = data.MetaDataItems;
            _runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry(Path.GetDirectoryName(path)!, PackFile.CreateFromBytes(Path.GetFileName(path), [1]))]);
            Assert.That(data.MetaDataItems, rebuilds ? Is.Not.SameAs(before) : Is.SameAs(before), "on add");

            before = data.MetaDataItems;
            _runner.PackFileService.SaveFile(_runner.PackFileService.FindFile(path, pack)!, [2]);
            Assert.That(data.MetaDataItems, rebuilds ? Is.Not.SameAs(before) : Is.SameAs(before), "on save");
        }

        // The bar above the 3D view, beside Camera, opens the Kitbash editor's render settings, the light that lights the models
        // and lit smoke, and takes a screenshot. The side panel, under Show VFX, no longer carries them.
        static void Lay(System.Windows.FrameworkElement element)
        {
            element.Measure(new System.Windows.Size(1400, 900));
            element.Arrange(new System.Windows.Rect(0, 0, 1400, 900));
            element.UpdateLayout();
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TheViewBarOffersTheRenderSettingsAndAScreenshot()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            Test.TestingUtility.TestUtility.AppResources.Load();
            var view = new AnimationEditor.Common.BaseControl.EditorHostView { DataContext = superView, Layouts = new(Directory.CreateTempSubdirectory("docklayout").FullName) };
            Lay(view);
            // The side panel on its own: the dock builds its panes only once loaded in a window, and loading SuperView's real 3D
            // view there starts a device that outlives the test and holds the test host open after the run.
            var panel = new EditorView { DataContext = superView };
            Lay(panel);
            var bar = Descendants(view).OfType<System.Windows.Controls.MenuItem>().Select(x => x.Command).ToList();
            var side = Descendants(panel).OfType<System.Windows.Controls.Button>().Select(x => x.Command).ToList();
            System.Windows.Input.ICommand[] both =[superView.OpenRenderSettingsCommand, superView.TakeScreenshotCommand];
            Assert.That((both.All(bar.Contains), both.Any(side.Contains)), Is.EqualTo((true, false)));
        }

        // The render settings' numbers drag like the meta editor's: press on Light intensity, drag a hundred pixels right, and the
        // light the scene draws with doubles while the mouse is still down. The rotations drag in whole degrees.
        [Test, Apartment(ApartmentState.STA)]
        public void RenderSettings_NumbersDragLikeTheMetaEditors()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var app = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            app.Resources["CustomWindowStyle"] = new System.Windows.Style(typeof(System.Windows.Window));
            var store = _runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Rendering.SceneRenderParametersStore>();
            store.LightIntensityMult = 1;
            var window = new GameWorld.Core.Utility.RenderSettingsDialog.RenderSettingsWindow(
                _runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Rendering.RenderEngineComponent>(), store,
                _runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Grid.GridComponent>());

            (window.ShowActivated, window.ShowInTaskbar, window.Left) = (false, false, -10000);
            window.Show();
            try
            {
                var scrubs = Descendants(window).OfType<System.Windows.Controls.TextBox>()
                    .Select(x => Microsoft.Xaml.Behaviors.Interaction.GetBehaviors(x).OfType<Shared.Ui.Common.Behaviors.NumberScrubBehavior>().SingleOrDefault()).ToList();
                var light = scrubs.FirstOrDefault();
                light?.Begin(0);
                light?.Move(100, System.Windows.Input.ModifierKeys.None);
                Assert.That((store.LightIntensityMult, scrubs.Select(x => x?.Mode)), Is.EqualTo((2f, new Shared.Ui.Common.Behaviors.ScrubMode?[]
                    { Shared.Ui.Common.Behaviors.ScrubMode.Decimal, Shared.Ui.Common.Behaviors.ScrubMode.Integer, Shared.Ui.Common.Behaviors.ScrubMode.Integer, Shared.Ui.Common.Behaviors.ScrubMode.Integer })));
            }
            finally
            {
                window.Close();
            }
        }

        // A screenshot is the next frame at twice the view's size, named after the file, saved where PhotoStudio saves its own.
        [Test]
        public void AScreenshotSavesTheNextFrameAtTwiceItsSize()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var folder = Path.Combine(Path.GetTempPath(), "superview_shots_" + Guid.NewGuid().ToString("N"));
            (superView.ScreenshotFolder, superView.OpenScreenshotFolder) = (folder, false);
            try
            {
                superView.TakeScreenshotCommand.Execute(null);
                var engine = _runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Rendering.RenderEngineComponent>();
                var device = _runner.ServiceProvider.GetRequiredService<GameWorld.Core.WpfWindow.IWpfGame>().GraphicsDevice;
                using var target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 64, 48, false,
                    Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color, Microsoft.Xna.Framework.Graphics.DepthFormat.Depth24);
                device.SetRenderTarget(target);
                engine.Draw(new GameTime());
                device.SetRenderTarget(null);
                var shot = Directory.GetFiles(folder, "*.png").Single();
                var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(shot), System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
                Assert.That((Path.GetFileName(shot).StartsWith("skv_throt_"), frame.PixelWidth, frame.PixelHeight), Is.EqualTo((true, 128, 96)));
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        // A screenshot is the picture alone: no labels, no axis widget, no FPS or stats text, no wireframe or selected faces. Each
        // overlay bucket gets an item that records when it is drawn; the screenshot frame draws only the model's, and the next
        // frame, an ordinary one, draws them all again.
        [Test]
        public void AScreenshotDrawsNoGuides()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var folder = Path.Combine(Path.GetTempPath(), "superview_shots_" + Guid.NewGuid().ToString("N"));
            (superView.ScreenshotFolder, superView.OpenScreenshotFolder) = (folder, false);
            try
            {
                var engine = _runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Rendering.RenderEngineComponent>();
                var device = _runner.ServiceProvider.GetRequiredService<GameWorld.Core.WpfWindow.IWpfGame>().GraphicsDevice;
                using var target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 64, 48, false,
                    Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color, Microsoft.Xna.Framework.Graphics.DepthFormat.Depth24);
                var drawn = new List<GameWorld.Core.Components.Rendering.RenderBuckedId>();
                List<GameWorld.Core.Components.Rendering.RenderBuckedId> Frame()
                {
                    drawn.Clear();
                    engine.Update(new GameTime());
                    foreach (var bucket in Enum.GetValues<GameWorld.Core.Components.Rendering.RenderBuckedId>())
                        engine.AddRenderItem(bucket, new RecordDraw(() => drawn.Add(bucket)));
                    device.SetRenderTarget(target);
                    engine.Draw(new GameTime());
                    device.SetRenderTarget(null);
                    return drawn.Distinct().Order().ToList();
                }

                superView.TakeScreenshotCommand.Execute(null);
                var shot = Frame();
                var next = Frame();
                Assert.That((shot, next), Is.EqualTo((
                    new[] { GameWorld.Core.Components.Rendering.RenderBuckedId.Normal, GameWorld.Core.Components.Rendering.RenderBuckedId.Transparent },
                    Enum.GetValues<GameWorld.Core.Components.Rendering.RenderBuckedId>().Order().ToArray())));
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        sealed class RecordDraw(Action drawn) : GameWorld.Core.Components.Rendering.IRenderItem
        {
            public void Draw(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, GameWorld.Core.Rendering.CommonShaderParameters parameters,
                GameWorld.Core.Components.Rendering.RenderingTechnique renderingTechnique) => drawn();
        }

        // Faction colours tint the parts the mask texture marks, as in game, so picking them in the render settings shows on the
        // model. The material that WH3 models load with had no tint, and the colours reached nothing.
        [Test]
        public void FactionColoursTintTheMaskedParts()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var store = _runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Rendering.SceneRenderParametersStore>();

            // Throt has no faction-coloured parts, as legendary characters do not: his masks' colour channels are empty. Every
            // mesh takes a white mask instead, so all of it is faction coloured.
            var white = System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
                Enumerable.Repeat((byte)255, 4 * 4 * 4).ToArray(), 16);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(white));
            using var png = new MemoryStream();
            encoder.Save(png);
            _runner.PackFileService.AddFilesToPack(_runner.CreateOutputPack(), [new NewPackFileEntry("test", PackFile.CreateFromBytes("white_mask.png", png.ToArray()))]);
            static IEnumerable<GameWorld.Core.SceneNodes.ISceneNode> Every(GameWorld.Core.SceneNodes.ISceneNode node) => node.Children.SelectMany(x => Every(x).Prepend(x));
            var meshes = Every(_runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.SceneManager>().RootNode).OfType<GameWorld.Core.SceneNodes.Rmv2MeshNode>().ToList();
            foreach (var mask in meshes.Select(x => x.Material.GetCapability<GameWorld.Core.Rendering.Materials.Capabilities.MetalRoughCapability>().Mask))
                (mask.TexturePath, mask.UseTexture) = (@"test\white_mask.png", true);

            // Each material WH3 models load with, alone: Throt's head takes the default one and his body the emissive one.
            var tinted = new Dictionary<GameWorld.Core.Rendering.Materials.Shaders.CapabilityMaterialsEnum, int>();
            foreach (var type in meshes.Select(x => x.Material.Type).Distinct())
            {
                foreach (var mesh in meshes)
                    mesh.IsVisible = mesh.Material.Type == type;
                (store.FactionColour0, store.FactionColour1, store.FactionColour2) = (Vector3.UnitY, Vector3.UnitY, Vector3.UnitY);
                var green = RenderFrame();
                (store.FactionColour0, store.FactionColour1, store.FactionColour2) = (Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ);
                var blue = RenderFrame();
                tinted[type] = green.Zip(blue).Count(x => Math.Abs(x.First.G - x.Second.G) + Math.Abs(x.First.B - x.Second.B) > 16);
            }
            Assert.That(tinted.Keys, Is.EquivalentTo(new[] { GameWorld.Core.Rendering.Materials.Shaders.CapabilityMaterialsEnum.MetalRoughPbr_Default, GameWorld.Core.Rendering.Materials.Shaders.CapabilityMaterialsEnum.MetalRoughPbr_Emissive }));
            Assert.That(tinted.Values, Is.All.GreaterThan(100), string.Join(", ", tinted));
        }

        // One frame of the editor's scene, the model framed from the front, as the app draws it.
        Microsoft.Xna.Framework.Color[] RenderFrame()
        {
            var device = _runner.ServiceProvider.GetRequiredService<GameWorld.Core.WpfWindow.IWpfGame>().GraphicsDevice;
            _runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Rendering.ArcBallCamera>().ViewMatrixOverride =
                Matrix.CreateLookAt(new Vector3(0, 1.2f, 4), new Vector3(0, 1, 0), Vector3.Up);
            var components = _runner.GetRequiredServiceInCurrentEditorScope<IEnumerable<IGameComponent>>().ToList();
            using var target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 256, 192, false,
                Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color, Microsoft.Xna.Framework.Graphics.DepthFormat.Depth24);
            var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
            device.SetRenderTarget(target);
            foreach (var component in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                component.Update(time);
            foreach (var component in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                component.Draw(time);
            device.SetRenderTarget(null);
            var pixels = new Microsoft.Xna.Framework.Color[target.Width * target.Height];
            target.GetData(pixels);
            return pixels;
        }

        static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
}
