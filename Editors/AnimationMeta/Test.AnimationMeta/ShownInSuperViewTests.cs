using Editors.AnimationMeta.Presentation.View;
using Editors.AnimationMeta.SuperView;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common.BaseControl;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;
using static Editors.AnimationMeta.SuperView.Visualisation.ShownInSuperView;

namespace Test.AnimationMeta
{
    // The tag list and the New tag dialog mark each tag with whether SuperView draws it, from ShownInSuperView's one list.
    // These hold that list to what MetaDataBuilder really draws.
    public class ShownInSuperViewTests
    {
        class Fragment(List<AnimationBinEntryGenericFormat> entries) : IAnimationBinGenericFormat
        {
            public AnimationPackFileDatabase? PackFileReference => null;
            public string Name => "test";
            public string FullPath => "test";
            public string SkeletonName => "test_skeleton";
            public string MountBin => "";
            public List<AnimationBinEntryGenericFormat> Entries => entries;
        }

        // Every slot a tag can draw from: the five dock slots, a hand pose, and two face poses, the fewest that pose a face.
        static readonly string[] s_slots =
        [
            "DOCK_EQUIPMENT_LEFT_HAND", "DOCK_EQUIPMENT_RIGHT_HAND", "DOCK_EQUIPMENT_LEFT_WAIST", "DOCK_EQUIPMENT_RIGHT_WAIST", "DOCK_EQUIPMENT_BACK",
            "HAND_POSE_RELAX", "FACE_POSE_RELAX", "FACE_POSE_RELAX_BLINK",
        ];

        AssetEditorTestRunner _runner = null!;
        SuperViewViewModel _superView = null!;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            _superView = (SuperViewViewModel)_runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.SuperView_Editor);
            _superView.Load(new AnimationToolInput()
            {
                Mesh = _runner.PackFileService.FindFile(@"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition")!,
                FragmentName = @"animations/database/battle/bin/hu17_dlc16_throt.bin",
                AnimationSlot = DefaultAnimationSlotTypeHelper.GetfromValue("ATTACK_5")
            });
        }

        // Every version of every tag the editor knows, built on its own through the builder with the Throt in the scene. A tag
        // counts as drawn when the builder returns, adds to the scene or adds to the player anything a meta without it does not.
        // A tag that needs a model, an animation or a weight to draw at all is given one first (Prime); the persistent meta
        // carries a locator and an effect, so a tag that hides the persistent meta changes what is drawn too.
        [Test]
        public void TheList_NamesExactlyTheTagsTheBuilderDraws()
        {
            var builder = (MetaDataBuilder)_runner.GetRequiredServiceInCurrentEditorScope<IMetaDataBuilder>();
            var scene = _superView.SceneObjects[0].Data;
            var anim = _superView.SceneObjects[0].FragAndSlotSelection.FragmentSlotList.SelectedItem!.AnimationFile;
            var model = _superView.MetaEditor.ParsedFile!.GetItemsOfType<IAnimatedPropMeta>().First().ModelName;
            var set = new Fragment([.. s_slots.Select(x => new AnimationBinEntryGenericFormat { SlotName = x, AnimationFile = anim })]);
            var persistent = new ParsedMetadataFile { Version = 2, Attributes = [Create("IMPACT_POS_10", typeof(ImpactPosition_v10)), Create("EFFECT_11", typeof(Effect_v11))] };

            (int Instances, int Nodes, int Rules) Build(ParsedMetadataAttribute? tag)
            {
                scene.Player.AnimationRules.Clear();
                var nodes = scene.MainNode.Children.Count;
                var instances = builder.Create(persistent, new ParsedMetadataFile { Version = 2, Attributes = tag == null ? [] : [tag] }, null, scene.MainNode, scene, scene.Player, set);
                var built = (instances.Count, scene.MainNode.Children.Count - nodes, scene.Player.AnimationRules.Count);
                foreach (var instance in instances)
                    instance.CleanUp();
                return built;
            }

            var without = Build(null);
            Assert.That(without.Instances, Is.EqualTo(2), "the persistent meta's locator and effect");
            var database = new MetaDataDatabase();
            var wrong = new List<string>();
            var checkedTypes = 0;
            foreach (var key in database.GetSupportedTypes().Order())
            {
                foreach (var type in database.GetDefinition(key))
                {
                    var tag = Create(key, type);
                    Prime(tag, anim, model);
                    var drawn = Build(tag) != without;
                    var listed = Of(type).Drawn != Drawn.No;
                    if (drawn != listed)
                        wrong.Add($"{key} ({type.Name}): the builder {(drawn ? "draws" : "does not draw")} it, the list says {(listed ? "it does" : "it does not")}");
                    checkedTypes++;
                }
            }

            Assert.That(checkedTypes, Is.GreaterThan(190), "tag versions checked");
            Assert.That(wrong, Is.Empty);
        }

        static ParsedMetadataAttribute Create(string key, Type type)
        {
            var tag = (ParsedMetadataAttribute)Activator.CreateInstance(type)!;
            tag.Name = key[..key.LastIndexOf('_')];
            tag.Version = int.Parse(key[(key.LastIndexOf('_') + 1)..]);
            return tag;
        }

        // What the defaults lack: a prop needs a model in the packs, a splice an animation for the skeleton and a weighted bone.
        static void Prime(ParsedMetadataAttribute tag, string animation, string model)
        {
            if (tag is IAnimatedPropMeta prop)
                prop.ModelName = model;
            if (tag is Splice_v11 splice)
                (splice.Animation, splice.GenericBoneIndex, splice.GenericWeight) = (animation, 1, 1);
        }

        [Test]
        public void ThePartialCases_SayWhatIsMissing()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Of(typeof(Splice_v11)).Tooltip, Does.Contain("generic-bone"));
                Assert.That(Of(typeof(RHandPose)).Tooltip, Does.Contain("Only version 10"));
                Assert.That(Of(typeof(DockEquipmentLHand_v10)).Tooltip, Does.Contain("DOCK_EQUIPMENT_"));
                Assert.That(Of(typeof(CrewLocation_v10)).Tooltip, Does.Contain("ignores the tag's times"));
                Assert.That(Of(typeof(SyncMarker_v10)).Tooltip, Does.Contain("doesn't draw"));
            });
        }

        // The New tag dialog marks each key with the type its Add button creates, the key's first.
        [Test]
        public void TheNewTagDialog_MarksEachKeyAsItsTypeIs()
        {
            var items = NewTagItem.All(new MetaDataDatabase());

            Assert.Multiple(() =>
            {
                Assert.That(items.Single(x => x.Key == "FIRE_POS_10").SuperViewMark.Drawn, Is.EqualTo(Drawn.Yes));
                Assert.That(items.Single(x => x.Key == "FIRE_POS_2").SuperViewMark.Drawn, Is.EqualTo(Drawn.No));
                Assert.That(items.Single(x => x.Key == "SPLICE_11").SuperViewMark.Drawn, Is.EqualTo(Drawn.Partly));
                Assert.That(items.Single(x => x.Key == "SPLICE_11").SuperViewMark.Tooltip, Is.EqualTo(Of(typeof(Splice_v11)).Tooltip));
            });
        }

        // The tag list's rows carry the mark for the tag each one decoded as.
        [Test]
        public void TheTagList_MarksEachRowAsItsTagIs()
        {
            var editor = _superView.MetaEditor;
            editor.ParsedFile!.Attributes.Add(Create("SYNC_MARKER_10", typeof(SyncMarker_v10)));
            editor.UpdateView();

            Assert.That(editor.Tags.First(x => x._input is IAnimatedPropMeta).SuperViewMark.Drawn, Is.EqualTo(Drawn.Yes), "Throt's animated prop");
            Assert.That(editor.Tags.Last().SuperViewMark.Drawn, Is.EqualTo(Drawn.No), "the sync marker");
        }
    }
}
