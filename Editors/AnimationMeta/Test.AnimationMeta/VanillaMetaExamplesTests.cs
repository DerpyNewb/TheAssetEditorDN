using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Editors.AnimationMeta.MetaEditor.Commands;
using Editors.AnimationMeta.MetaEditor.Examples;
using Microsoft.Extensions.DependencyInjection;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;
using static Test.AnimationMeta.MetaTestFiles;

namespace Test.AnimationMeta
{
    // Vanilla examples come from the game's packs as loaded, so these tests load small packs made in memory. The scan of the real
    // ones is VanillaMetaExamplesSurvey.
    public class VanillaMetaExamplesTests
    {
        // The runner's WPF objects give the test thread a dispatcher, which nothing pumps here, so an await that resumed on it would
        // never resume. The app's UI thread pumps its own.
        [SetUp]
        public void NoDispatcher() => SynchronizationContext.SetSynchronizationContext(null);

        static Effect_v12 Effect(string vfx, float start = 0) => Tag<Effect_v12>("EFFECT_12", x => { x.VfxName = vfx; x.StartTime = start; x.EndTime = start; });

        static string VfxName(VanillaExample example) => ((IEffectMeta)Parser.DeSerialize(new ParsedUnknownMetadataAttribute { Name = example.Name, Version = example.Version, Data = example.Data }, out _)!).VfxName;

        static (AssetEditorTestRunner Runner, VanillaMetaExamples Examples) Game(params ParsedMetadataAttribute[] tags)
        {
            var runner = new AssetEditorTestRunner();
            Pack(runner, true, ("animations/battle/a.anm.meta", Bytes(tags)));
            return (runner, new VanillaMetaExamples(runner.PackFileService, Parser));
        }

        static List<VanillaExample> Scan(VanillaMetaExamples examples, string key) => examples.Scan()!.GetAwaiter().GetResult()[key];

        [Test]
        public void AScan_KeepsFivePerTag_PreferringOnesThatDifferInMoreThanTheirTimes()
        {
            var (_, examples) = Game(Effect("a", 0.1f), Effect("a", 0.2f), Effect("b"), Effect("c"), Effect("d"), Effect("e"), Effect("f"));

            var found = Scan(examples, "EFFECT_12");

            Assert.That(found.Select(VfxName), Is.EqualTo(new[] { "a", "b", "c", "d", "e" }));
            Assert.That(found[0].Path, Is.EqualTo(@"animations\battle\a.anm.meta").Or.EqualTo("animations/battle/a.anm.meta"));
            Assert.That(found[0].Fields, Has.Some.EqualTo(new VanillaExampleField("Vfx Name", "a")));
            Assert.That(found[0].Fields, Has.Some.EqualTo(new VanillaExampleField("Start Time", "0.10000000")));
        }

        // Five ways one animation uses a tag show one unit; a second file's comes before the first file's other four.
        [Test]
        public void AScan_PrefersAnotherFile_OverMoreValuesFromOneAlreadyShown()
        {
            var runner = new AssetEditorTestRunner();
            Pack(runner, true,
                ("animations/battle/a.anm.meta", Bytes(Effect("b"), Effect("c"), Effect("d"), Effect("e"), Effect("f"))),
                ("animations/battle/z.anm.meta", Bytes(Effect("g"))));
            var examples = new VanillaMetaExamples(runner.PackFileService, Parser);

            var found = Scan(examples, "EFFECT_12");

            Assert.That(found.Select(VfxName), Is.EqualTo(new[] { "b", "g", "c", "d", "e" }));
        }

        [Test]
        public void AScan_FillsUpWithTimeOnlyVariants_ButNotWithCopies()
        {
            var (_, examples) = Game(Effect("a", 0.1f), Effect("a", 0.2f), Effect("a", 0.2f), Effect("b", 0.3f));

            var found = Scan(examples, "EFFECT_12");

            Assert.That(found.Select(x => (VfxName(x), x.Fields.Single(f => f.Name == "Start Time").Value)),
                Is.EqualTo(new[] { ("a", "0.10000000"), ("b", "0.30000000"), ("a", "0.20000000") }));
        }

        [Test]
        public void AScan_ReadsOnlyTheGamesPacks_AndOnlyOnce()
        {
            var (runner, examples) = Game(Effect("vanilla"));
            Pack(runner, false, ("animations/battle/mod.anm.meta", Bytes(Effect("modded"))));

            var first = examples.Scan();

            Assert.That(Scan(examples, "EFFECT_12").Select(VfxName), Is.EqualTo(new[] { "vanilla" }));
            Assert.That(examples.Scan(), Is.SameAs(first), "the scan is kept for the session");
        }

        [Test]
        public void WithNoGamePacks_TheExamplesSaySo()
        {
            var runner = new AssetEditorTestRunner();
            Pack(runner, false, ("animations/battle/mod.anm.meta", Bytes(Effect("modded"))));
            var examples = new VanillaMetaExamples(runner.PackFileService, Parser);
            var view = new VanillaExamplesViewModel(examples);

            Assert.That(examples.Scan(), Is.Null);
            Assert.That(view.Show("EFFECT_12").IsCompleted, Is.True);
            Assert.That(view.Status, Does.StartWith("No game packs are loaded"));
        }

        /// <summary>A file whose read waits for the test, so the scan is still running when the examples are first asked for.</summary>
        sealed class HeldSource(byte[] bytes) : IDataSource
        {
            public readonly ManualResetEventSlim Release = new();
            public readonly ManualResetEventSlim Read = new();
            public long Size => bytes.Length;
            public CompressionFormat CompressionFormat => CompressionFormat.None;
            public byte[] ReadData() { Read.Set(); Release.Wait(TimeSpan.FromSeconds(30)); return bytes; }
            public byte[] PeekData(int size) => bytes[..size];
            public ByteChunk ReadDataAsChunk() => new(ReadData());
        }

        static (VanillaExamplesViewModel View, HeldSource Held) HeldGame()
        {
            var runner = new AssetEditorTestRunner();
            var held = new HeldSource(Bytes(Effect("a"), Effect("b")));
            var pack = runner.CreateEmptyPackFile("game", false);
            runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry("animations/battle", new PackFile("held.anm.meta", held))]);
            pack.IsCaPackFile = true;
            return (new VanillaExamplesViewModel(new VanillaMetaExamples(runner.PackFileService, Parser)), held);
        }

        // The New tag dialog asks with no tag picked as it opens, so the scan is under way while one is being picked.
        [Test]
        public void OpeningTheExamples_StartsTheScanBeforeATagIsPicked()
        {
            var (view, held) = HeldGame();

            Assert.That(view.Show(null).IsCompleted, Is.True);

            Assert.That(held.Read.Wait(TimeSpan.FromSeconds(30)), Is.True, "the game's metas are being read");
            Assert.That(view.Status, Is.EqualTo("Pick a tag to see how vanilla WH3 uses it."));
            held.Release.Set();
        }

        [Test]
        public void WhileTheScanRuns_TheExamplesSaySo_ThenShowWhenItEnds()
        {
            var (view, held) = HeldGame();

            var shown = view.Show("EFFECT_12");

            Assert.That(shown.IsCompleted, Is.False);
            Assert.That(view.Status, Does.StartWith("Reading the game's animation metas for examples."));
            Assert.That(view.Items, Is.Empty);

            held.Release.Set();
            Assert.That(shown.Wait(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(view.Items.Select(VfxName), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(view.Selected, Is.SameAs(view.Items[0]));
            Assert.That(view.Status, Does.StartWith("How the game's own animations use EFFECT_12."));
        }

        // "Insert like this one" in the New tag dialog: the button hands the picked example to the command, which adds a tag
        // with its values. Add with no example still gives the defaults.
        [Test]
        public void InsertLikeThisOne_AddsATagWithTheExamplesValues()
        {
            var (runner, examples) = Game(Effect("a", 0.25f), Effect("b", 0.75f));
            var editor = Open(runner, Tag<SplashAttack_v10>("SPLASH_ATTACK_10"));
            var command = runner.CommandFactory.Create<NewEntryCommand>(x => x.Configure(editor));
            var view = new VanillaExamplesViewModel(examples) { Used = x => command.Add("EFFECT_12", x) };
            view.Show("EFFECT_12").Wait();

            view.Selected = view.Items[1];
            view.UseCommand.Execute(null);
            command.Add("EFFECT_12", null);

            var (added, plain) = ((Effect_v12)editor.ParsedFile!.Attributes[1], (Effect_v12)editor.ParsedFile.Attributes[2]);
            Assert.Multiple(() =>
            {
                Assert.That((added.VfxName, added.StartTime), Is.EqualTo(("b", 0.75f)));
                Assert.That((plain.VfxName, plain.StartTime), Is.EqualTo(("", 0f)));
                Assert.That(editor.SelectedTag?._input, Is.SameAs(plain));
            });
        }

        [Test]
        public void TheUseButton_IsOffUntilAnExampleIsPicked()
        {
            var (_, examples) = Game(Effect("a"));
            var view = new VanillaExamplesViewModel(examples);
            view.Show(null).Wait();

            Assert.That(view.UseCommand.CanExecute(null), Is.False);
            view.Show("EFFECT_12").Wait();
            Assert.That(view.UseCommand.CanExecute(null), Is.True);
        }

        // The editor's Vanilla examples window follows the selected tag, and its button puts the picked example's values in that
        // tag's place; undo puts the old ones back.
        [Test]
        public void TheEditorsExamples_FollowTheSelectedTag_AndCopyIntoIt()
        {
            var (runner, _) = Game(Effect("a"), Effect("b"), Tag<SplashAttack_v10>("SPLASH_ATTACK_10"));
            var editor = Open(runner, Tag<SplashAttack_v10>("SPLASH_ATTACK_10"), Effect("mine", 0.5f));
            var command = runner.CommandFactory.Create<OpenVanillaExamplesCommand>(x => x.Configure(editor));
            // The command's own, the session's one.
            runner.ServiceProvider.GetRequiredService<VanillaMetaExamples>().Scan()!.Wait();
            var view = command.Follow(editor, out var stop);
            Assert.That(view.Items, Is.Empty, "nothing is selected yet");

            editor.SelectedTag = editor.Tags[1];
            Assert.That(view.Items.Select(VfxName), Is.EqualTo(new[] { "a", "b" }), view.Status);

            view.Selected = view.Items[1];
            view.UseCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(((Effect_v12)editor.ParsedFile!.Attributes[1]).VfxName, Is.EqualTo("b"));
                Assert.That(editor.SelectedTag, Is.SameAs(editor.Tags[1]));
                Assert.That(editor.ParsedFile.Attributes, Has.Count.EqualTo(2));
            });

            editor.UndoCommand.Execute(null);
            Assert.That(((Effect_v12)editor.ParsedFile!.Attributes[1]).VfxName, Is.EqualTo("mine"));

            stop();
            editor.SelectedTag = editor.Tags[0];
            Assert.That(view.Items.Select(x => x.Key), Is.EqualTo(new[] { "EFFECT_12", "EFFECT_12" }), "a closed window no longer follows");
        }

        // The view's text and button come from the bindings: the status, the host's label, and the picked example's fields.
        [Test, Apartment(ApartmentState.STA)]
        public void TheView_ShowsTheStatusTheExamplesAndTheHostsLabel()
        {
            var (_, examples) = Game(Effect("a"));
            var model = new VanillaExamplesViewModel(examples);
            model.Show("EFFECT_12").Wait();
            // The theme's ListViewItem style, which the view's lists are based on; a test has no App.xaml to give it.
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources[typeof(ListViewItem)] = new Style(typeof(ListViewItem));

            var view = new VanillaExamplesView { DataContext = model, UseLabel = "Insert like this one" };
            view.Measure(new Size(600, 300));
            view.Arrange(new Rect(0, 0, 600, 300));
            view.UpdateLayout();

            var texts = Descendants(view).OfType<TextBlock>().Select(x => x.Text).ToList();
            var button = Descendants(view).OfType<Button>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(texts, Does.Contain(model.Status));
                Assert.That(texts, Does.Contain("a"), "the picked example's Vfx Name");
                Assert.That(button.Content, Is.EqualTo("Insert like this one"));
                Assert.That(button.IsEnabled, Is.True);
            });
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var below in Descendants(child))
                    yield return below;
            }
        }
    }
}
