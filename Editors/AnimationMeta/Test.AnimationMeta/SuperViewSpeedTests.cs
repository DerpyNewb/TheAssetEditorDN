using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AnimationEditor.Common.AnimationPlayer;
using Editors.AnimationMeta.SuperView;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationPack;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // The animation bar's speed choice, on Throt's ATTACK_5 in SuperView.
    public class SuperViewSpeedTests
    {
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
            Assert.That(Main.Player.GetAnimationLengthUs(), Is.GreaterThan(500_000), "Throt ATTACK_5 did not load");
        }

        SceneObject Main => _superView.SceneObjects[0].Data;

        // Ten 20 ms frames of what AnimationsContainerComponent does every frame, from the clip's start; returns how far the clip
        // moved, in microseconds.
        static long Run(SceneObject asset)
        {
            asset.Player.SetTime(0);
            asset.Player.Play();
            for (var i = 0; i < 10; i++)
                asset.Player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(20)));
            return asset.Player.GetTimeUs();
        }

        class TimeRecorder : IMetaDataInstance
        {
            public float LastTime = -1;
            public AnimationPlayer Player => null!;
            public void CleanUp() { }
            public void Update(float currentTime) => LastTime = currentTime;
        }

        [Test]
        public void HalfSpeed_MovesTheClipHalfAsFar_EvenAfterTheAnimationIsSetAgain_AndTheMetaTagsFollow()
        {
            var full = Run(Main);
            Assert.That(full, Is.EqualTo(200_000).Within(1), "1x moves 200 ms in 200 ms");

            _superView.Player.Speed.Value = 0.5;
            Main.Player.SetAnimation(Main.Player.AnimationClip, Main.Skeleton);   // a slot change keeps the player
            var recorder = new TimeRecorder();
            Main.MetaDataItems.Add(recorder);
            var half = Run(Main);
            Main.Update(new GameTime());   // the scene's own update, which hands the meta tags their time

            Assert.That(half, Is.EqualTo(full / 2).Within(1), "0.5x moves half as far");
            Assert.That(recorder.LastTime, Is.EqualTo(half / 1_000_000f).Within(1e-6), "the meta tags are given the slowed time");
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TheBar_OffersThreeSpeeds_StartsAt1x_AndAPickReachesThePlayer()
        {
            _superView.Player.IsEnabled.Value = true;
            var view = new AnimationPlayerView { DataContext = _superView.Player };
            var host = new Window { Content = view, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 800, Height = 300 };
            try
            {
                host.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var combo = FindAll<ComboBox>(view).Single(x => AutomationProperties.GetAutomationId(x) == "AnimationSpeed");

                Assert.That(combo.Items.Cast<double>(), Is.EqualTo(new[] { 1, 0.5, 0.25 }));
                Assert.That(combo.SelectedItem, Is.EqualTo(1.0), "a new editor plays at 1x");
                combo.SelectedIndex = 2;
                Assert.That(Main.Player.Speed, Is.EqualTo(0.25), "the pick reaches the model's player");
            }
            finally
            {
                host.Close();
            }
        }

        static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (var found in FindAll<T>(child))
                    yield return found;
            }
        }

        [Test]
        public void AFrameStep_IsOneFrame_AtAnySpeed()
        {
            _superView.Player.Speed.Value = 0.25;
            Main.Player.CurrentFrame = 3;
            _superView.Player.SetAnimationNextFrame();
            Assert.That(Main.Player.CurrentFrame, Is.EqualTo(4));
        }

        [Test]
        public void AnAssetAddedAfterTheChoice_PlaysAtThatSpeed()
        {
            _superView.Player.Speed.Value = 0.25;
            var added = new SceneObject("added") { Player = new AnimationPlayer() };
            added.Player.SetAnimation(Main.Player.AnimationClip, Main.Skeleton);
            _superView.Player.RegisterAsset(added);

            Assert.That(Run(added), Is.EqualTo(50_000).Within(1), "0.25x moves 50 ms in 200 ms");
        }
    }
}
