using Editors.AnimationMeta.MetaEditor.Check;
using Editors.Audio.SoundTriggers;
using Editors.AnimationMeta.SuperView;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Events;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.DB;
using Editors.Shared.Core.Common.BaseControl;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;
using static Test.AnimationMeta.MetaTestFiles;

namespace Test.AnimationMeta
{
    // Check meta: each check finds what it is for, and leaves alone what vanilla WH3 does.
    public class MetaCheckTests
    {
        static readonly MetaDataDatabase s_database = new();

        static MetaCheckResult Check(MetaCheckScene? scene, params ParsedMetadataAttribute[] tags) => MetaChecker.Check(File(tags), s_database, scene, null);

        static Effect_v12 Effect(float start, float end, string vfx = "") => Tag<Effect_v12>("EFFECT_12", x => { x.StartTime = start; x.EndTime = end; x.VfxName = vfx; });

        [Test]
        public void ATagEndingBeforeItStarts_IsAWarning_ButFloatNoiseIsNot()
        {
            // The second is vanilla's hu1b_dlc27_def_rp3_cold_one_sword_persistent_metadata_alive_1 SPLICE_12, which ends 0.0000000037 s
            // before it starts.
            var result = Check(null, Effect(0.5f, 0.2f), Effect(-3.7252903E-09f, -7.450581E-09f), Effect(0.2f, 0.5f));

            Assert.That(result.Problems, Has.Count.EqualTo(1));
            var problem = result.Problems.Single();
            Assert.Multiple(() =>
            {
                Assert.That(problem.Index, Is.EqualTo(0));
                Assert.That(problem.TagName, Is.EqualTo("EFFECT_12"));
                Assert.That(problem.IsWarning, Is.True);
                Assert.That(problem.Message, Does.StartWith("It ends at 0.2 s, before it starts at 0.5 s."));
            });
        }

        [Test]
        public void ATagStartingPastTheClip_IsAnError_OnlyWhenTheClipIsKnown()
        {
            var tags = new ParsedMetadataAttribute[] { Effect(1.5f, 1.5f), Effect(1.0005f, 1.0005f), Effect(0.5f, 0.5f) };

            var known = Check(new MetaCheckScene(1.0f, null), tags);
            var unknown = Check(null, tags);

            Assert.Multiple(() =>
            {
                Assert.That(known.Problems.Select(x => (x.Index, x.IsWarning)), Is.EqualTo(new[] { (0, false) }), "only 1.5 s is past a 1 s clip; 1.0005 s is the clip's own end");
                Assert.That(known.Problems.Single().Message, Is.EqualTo("It starts at 1.5 s, but the clip is only 1 s long, so the animation ends before the tag starts."));
                Assert.That(unknown.Problems, Is.Empty);
                Assert.That(unknown.Skipped, Has.Some.Contains("start times weren't checked"));
                Assert.That(known.Skipped, Has.None.Contains("start times weren't checked"));
            });
        }

        [Test]
        public void AVersionVanillaNeverUses_IsAnError_ThatNamesTheVersionVanillaUses()
        {
            var result = Check(null,
                Tag<Effect_v7>("EFFECT_7"),
                Effect(0, 0),
                Tag<SoundImpact_v2>("SOUND_IMPACT_2"));

            Assert.That(result.Problems.Select(x => x.Index), Is.EqualTo(new[] { 0, 2 }));
            Assert.Multiple(() =>
            {
                Assert.That(result.Problems[0].IsWarning, Is.False);
                Assert.That(result.Problems[0].Message, Does.StartWith("Vanilla WH3 never uses version 7 of EFFECT."));
                Assert.That(result.Problems[0].Message, Does.EndWith("Vanilla uses EFFECT_12 instead, 19,012 times."));
                Assert.That(result.Problems[1].Message, Does.EndWith("Vanilla doesn't use SOUND_IMPACT in any version."));
            });
        }

        static SoundTrigger_v11 Sound(string name) => Tag<SoundTrigger_v11>("SOUND_TRIGGER_11", x => x.SoundEvent = name);

        static AudioMetadataTags SoundTable(params string[] keys) =>
            AudioMetadataTags.From(keys.Select(key => new DbRow(new Dictionary<string, object> { ["key"] = key, ["sound_event_battle_start"] = "Play_" + key }, 22, "db.pack", "db/audio_metadata_tags_tables/data__")));

        // The game plays nothing for a SOUND_TRIGGER whose name has no audio_metadata_tags_tables row (measured 2026-11-19), and the
        // key has to match exactly: Drop_Weapon_Rest is CA's spelling, drop_weapon_rest is a miss.
        [Test]
        public void ASoundTrigger_WithNoTagsTableRow_IsAnError_AndAnEmptyOneAWarning()
        {
            var file = File(Sound("Drop_Weapon_Rest"), Sound("drop_weapon_rest"), Sound("Derpy_Test_Tone_F"), Sound(""), Effect(0, 0));

            var result = MetaChecker.Check(file, s_database, null, null, SoundTable("Drop_Weapon_Rest", "Vocalisation_Humanoid_Male_Pain"));

            Assert.That(result.Problems.Select(x => (x.Index, x.IsWarning)), Is.EqualTo(new[] { (1, false), (2, false), (3, true) }));
            Assert.That(result.Problems[0].Message, Does.StartWith("No row of audio_metadata_tags_tables has the key drop_weapon_rest, so the game plays nothing for it."));
            Assert.That(result.Skipped, Has.None.Contains("audio_metadata_tags_tables"));
        }

        [Test]
        public void SoundTriggers_AreSkipped_WithoutTheTagsTable_AndSaySo()
        {
            var withTable = MetaChecker.Check(File(Sound("Derpy_Test_Tone_F")), s_database, null, null, SoundTable());
            var noSounds = MetaChecker.Check(File(Effect(0, 0)), s_database, null, null);

            Assert.That(withTable.Problems, Is.Empty, "an empty table means the table wasn't found, not that every tag is missing");
            Assert.That(withTable.Skipped, Has.One.Contains("audio_metadata_tags_tables isn't loaded"));
            Assert.That(noSounds.Skipped, Has.None.Contains("audio_metadata_tags_tables"));
        }

        [Test]
        public void APropBoneId_IsFlagged_WhenTheBoneItCountsFromOneIsMissing()
        {
            var bones = new MetaCheckScene(null, ["root", "hand_right", "be_prop_0", "BE_PROP_1", "be_prop_3"]);
            var tags = new ParsedMetadataAttribute[]
            {
                Tag<DockEquipmentRHand_v11>("DOCK_EQPT_RHAND_11", x => x.PropBoneId = 0),   // unknown in game, never flagged
                Tag<DockEquipmentRHand_v11>("DOCK_EQPT_RHAND_11", x => x.PropBoneId = 1),   // be_prop_0
                Tag<DockEquipmentRHand_v11>("DOCK_EQPT_RHAND_11", x => x.PropBoneId = 2),   // BE_PROP_1, any case
                Tag<DockEquipmentRHand_v11>("DOCK_EQPT_RHAND_11", x => x.PropBoneId = 3),   // be_prop_2 is missing, though be_prop_3 is there
                Tag<DockEquipmentRHand_v11>("DOCK_EQPT_RHAND_11", x => x.PropBoneId = 4),   // be_prop_3
                Tag<WeaponOn_v11>("WEAPON_ON_11", x => x.PropBoneId = 5),
                Tag<WeaponRHand_v2>("WEAPON_RHAND_2", x => x.PropBoneId = 9),
                Tag<WeaponRHand_v1>("WEAPON_RHAND_1", x => x.PropBoneId = 9),
                Tag<WeaponRHand_v0>("WEAPON_RHAND_0", x => x.PropBoneId = 9),
            };

            var flagged = Check(bones, tags).Problems.Where(x => x.Message.Contains("prop bone")).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(flagged.Select(x => x.Index), Is.EqualTo(new[] { 3, 5, 6, 7, 8 }));
                Assert.That(flagged[0].Message, Does.StartWith("Its prop bone id is 3, so it docks be_prop_2, but this skeleton has no be_prop_2."));
                Assert.That(flagged[0].Message, Does.EndWith("id 1 is be_prop_0."));
                Assert.That(Check(null, tags).Problems.Where(x => x.Message.Contains("prop bone")), Is.Empty, "no skeleton, no check");
            });
        }

        [Test]
        public void AVfxFileNoLoadedPackHas_IsAnError()
        {
            var runner = new AssetEditorTestRunner();
            Pack(runner, true, ("vfx/present.xml", [1]));
            var tags = new ParsedMetadataAttribute[]
            {
                Effect(0, 0, "present"),
                Effect(0, 0, "missing"),
                Tag<Blood_v12>("BLOOD_12", x => x.VfxName = "missing_blood"),
                Effect(0, 0, ""),
            };

            var problems = MetaChecker.Check(File(tags), s_database, null, runner.PackFileService).Problems;

            Assert.Multiple(() =>
            {
                Assert.That(problems.Select(x => (x.Index, x.IsWarning)), Is.EqualTo(new[] { (1, false), (2, false), (3, true) }));
                Assert.That(problems[0].Message, Is.EqualTo("Its vfx file, vfx/missing.xml, isn't in any loaded pack. Unless another mod adds it, the effect won't play."));
                Assert.That(problems[1].Message, Does.Contain("vfx/missing_blood.xml"));
                Assert.That(problems[2].Message, Does.StartWith("It names no vfx file"));
            });
        }

        // Without CA's packs every vanilla effect would look missing, so the check waits for them.
        [Test]
        public void WithNoGamePacks_VfxFilesAreNotChecked()
        {
            var runner = new AssetEditorTestRunner();
            Pack(runner, false, ("vfx/present.xml", [1]));

            var result = MetaChecker.Check(File(Effect(0, 0, "missing")), s_database, null, runner.PackFileService);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(result.Skipped, Has.Some.StartsWith("No game packs are loaded, so vfx files weren't checked"));
        }

        [Test]
        public void ClickingAProblem_SelectsItsTag()
        {
            var runner = new AssetEditorTestRunner();
            var editor = Open(runner, Effect(0, 0), Effect(0.5f, 0.2f), Effect(0, 0));
            var command = runner.CommandFactory.Create<CheckMetaCommand>(x => x.Configure(editor));
            var window = new MetaCheckViewModel(editor, () => command.Check(editor));
            Assert.That(window.Problems, Has.Count.EqualTo(1));
            Assert.That(window.Summary, Is.EqualTo("1 problem in 3 tags. Click one to select its tag."));

            window.Selected = window.Problems[0];

            Assert.That(editor.SelectedTag?._input, Is.SameAs(editor.ParsedFile!.Attributes[1]));
        }

        [Test]
        public void TheCheckButton_IsOnlyLiveWithAFileOpen()
        {
            var runner = new AssetEditorTestRunner();
            var editor = Open(runner, Effect(0, 0));
            Assert.That(editor.CheckActionCommand.CanExecute(null), Is.True);

            editor.LoadFile(null);

            Assert.That(editor.CheckActionCommand.CanExecute(null), Is.False);
        }

        // In SuperView the check knows the playing clip and the skeleton. A persistent meta plays under every clip of the set, so
        // its tab is not checked against one clip's length.
        [Test]
        public void InSuperView_TheAnimationTabIsCheckedAgainstTheClip_AndThePersistentTabIsNot()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var superView = (SuperViewViewModel)runner.ServiceProvider.GetRequiredService<IEditorManager>().Create(EditorEnums.SuperView_Editor)!;
            superView.Load(new AnimationToolInput
            {
                Mesh = runner.PackFileService.FindFile(@"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition")!,
                FragmentName = @"animations/database/battle/bin/hu17_dlc16_throt.bin",
                AnimationSlot = DefaultAnimationSlotTypeHelper.GetfromValue("ATTACK_5"),
            });
            var clip = superView.SceneObjects[0].Data.Player.AnimationClip!.PlayTimeInSec;
            Assert.That(clip, Is.GreaterThan(0));

            var animation = superView.MetaEditor.CheckScene!();
            var persistent = superView.PersistentMetaEditor.CheckScene!();
            Assert.Multiple(() =>
            {
                Assert.That(animation.ClipSeconds, Is.EqualTo(clip));
                Assert.That(persistent.ClipSeconds, Is.Null);
                Assert.That(animation.BoneNames, Does.Contain("be_prop_0"));
                Assert.That(persistent.BoneNames, Is.SameAs(animation.BoneNames));
            });

            var late = (DecodedMetaEntryBase)superView.MetaEditor.ParsedFile!.Attributes.First(x => x is DecodedMetaEntryBase);
            late.StartTime = clip + 1;
            var command = runner.CommandFactory.Create<CheckMetaCommand>(x => x.Configure(superView.MetaEditor));
            Assert.That(command.Check(superView.MetaEditor).Problems, Has.Some.Matches<MetaProblem>(x => x.Tag == late && x.Message.Contains("the clip is only")));
        }
    }
}
