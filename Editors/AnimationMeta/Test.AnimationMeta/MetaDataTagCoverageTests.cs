using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.Shared.Core.Common.BaseControl;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // Every tag the "Create MetaData entry" list offers, added the way its OK button adds it.
    public class MetaDataTagCoverageTests
    {
        static readonly string[] s_placeholderDescriptions = ["Missing", "missing", "TODO"];

        AssetEditorTestRunner _runner = null!;
        SuperViewViewModel _superView = null!;
        MetaDataFileParser _parser = null!;

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
            _parser = _runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
        }

        static IEnumerable<string> AllTags() => new MetaDataDatabase().GetSupportedTypes().Order();

        void AddAndSelect(ParsedMetadataAttribute entry)
        {
            var editor = _superView.MetaEditor;
            editor.ParsedFile!.Attributes.Add(entry);
            editor.UpdateView();
            editor.SelectedTag = editor.Tags.Last();    // selecting redraws SuperView
        }

        [TestCaseSource(nameof(AllTags))]
        public void NewTag_AddsShowsAndRoundTrips(string tag)
        {
            Assert.That(_superView.MetaEditor.ParsedFile, Is.Not.Null, "Throt ATTACK_5 has no meta file");

            var entry = _parser.CreateDefault(tag);
            Assert.DoesNotThrow(() => AddAndSelect(entry), "SuperView failed to draw the new tag");

            var row = _superView.MetaEditor.Tags.Last();
            Assert.That(row.IsDecodedCorrectly, Is.True);
            foreach (var variable in row.Variables)
                Assert.That(variable.IsValid, Is.True, $"{variable.FieldName} shows as invalid");

            // Every Vector4 is a rotation; a new tag must start at "no rotation", not the zero quaternion.
            foreach (var rotation in entry.GetType().GetProperties().Where(x => x.PropertyType == typeof(Vector4)))
                Assert.That(((Vector4)rotation.GetValue(entry)!).Length(), Is.EqualTo(1).Within(1e-5), $"{rotation.Name} default");

            var bytes = _parser.GenerateBytes(2, new ParsedMetadataFile() { Version = 2, Attributes = [entry] });
            var reparsed = _parser.ParseFile(bytes);
            Assert.That(reparsed.Attributes, Has.Count.EqualTo(1));
            Assert.That(reparsed.Attributes[0], Is.InstanceOf(entry.GetType()), "did not read back as itself");
            Assert.That(_parser.GenerateBytes(2, reparsed), Is.EqualTo(bytes));

            Assert.That(s_placeholderDescriptions, Does.Not.Contain(row.Description), "no description");
        }

        // A tag no definition matches (a newer game version, another game's format) loads as a red
        // row; saving must write its bytes back as they were, not just its version number.
        [Test]
        public void UnknownTag_IsSavedBackUnchanged()
        {
            var payload = new byte[] { 10, 0, 0, 0, 0, 0, 0x80, 0x3F, 0, 0, 0x80, 0x3F, 0, 0, 7, 0, 0, 0, 1, 2, 3 };
            var file = new List<byte>();
            file.AddRange(BitConverter.GetBytes(2));
            file.AddRange(BitConverter.GetBytes(1));
            file.AddRange(BitConverter.GetBytes((short)"MADE_UP_TAG".Length));
            file.AddRange(System.Text.Encoding.ASCII.GetBytes("MADE_UP_TAG"));
            file.AddRange(payload);

            var parsed = _parser.ParseFile(file.ToArray());
            Assert.That(parsed.Attributes[0], Is.InstanceOf<ParsedUnknownMetadataAttribute>());
            Assert.That(_parser.GenerateBytes(2, parsed), Is.EqualTo(file.ToArray()));
        }

        // PROP is drawn the way ANIMATED_PROP is. Throt's attack meta already has an animated prop
        // whose model is in the test pack; a static PROP on the same model must add one more item.
        [TestCase("PROP_14")]
        [TestCase("PROP_15")]
        public void Prop_IsDrawnInSuperView(string tag)
        {
            var animatedProp = _superView.MetaEditor.ParsedFile!.GetItemsOfType<IAnimatedPropMeta>().First();
            var drawnBefore = _superView.SceneObjects[0].Data.MetaDataItems.Count;
            Assert.That(drawnBefore, Is.GreaterThan(0), "the test pack's animated prop is not drawn");

            var prop = _parser.CreateDefault(tag);
            ((IAnimatedPropMeta)prop).ModelName = animatedProp.ModelName;
            AddAndSelect(prop);

            Assert.That(_superView.SceneObjects[0].Data.MetaDataItems, Has.Count.EqualTo(drawnBefore + 1));
        }

        // A slider step on one tag rebuilds SuperView: the animated prop, whose tag did not change, stays as it was built, and
        // in the scene once.
        [Test]
        public void AnAttributeChange_KeepsWhatTheOtherTagsBuilt()
        {
            var data = _superView.SceneObjects[0].Data;
            var props = data.MetaDataItems.OfType<AnimatedPropInstance>().ToList();
            Assert.That(props, Is.Not.Empty, "the test pack's animated prop is not drawn");

            _runner.GetRequiredServiceInCurrentEditorScope<Shared.Core.Events.IEventHub>().Publish(new MetaDataAttributeChangedEvent());

            Assert.Multiple(() =>
            {
                Assert.That(data.MetaDataItems, Is.SupersetOf(props), "the unchanged prop was built again");
                Assert.That(data.MainNode.Children.Count(x => x.Name == "Animated_prop"), Is.EqualTo(props.Count), "props in the scene");
            });
        }
    }
}
