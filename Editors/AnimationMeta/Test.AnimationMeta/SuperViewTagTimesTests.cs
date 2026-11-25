using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.Presentation.View;
using Editors.AnimationMeta.SuperView;
using Editors.AnimationMeta.SuperView.Editing;
using Editors.AnimationMeta.SuperView.Timeline;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Animation;
using Material.Icons;
using Material.Icons.WPF;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.Ui.Common.MenuSystem;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // The meta editor's "Set start to now" and "Set end to now", the player's keys, what the scrub bar's META markers do that
    // upstream's SuperViewTimelineTests leave to this fork (an undo, a marker outside the clip, Delete's rows), and the gizmo's
    // drags in the tab's own undo history, on Throt's ATTACK_5.
    public class SuperViewTagTimesTests
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
            Assert.That(Length, Is.GreaterThan(0.5f), "Throt ATTACK_5 did not load");
        }

        AnimationPlayer MainPlayer => _superView.SceneObjects[0].Data.Player;
        float Length => MainPlayer.GetAnimationLengthUs() / 1_000_000f;
        MetaDataEditorViewModel Meta => _superView.MetaEditor;

        // Added to the animation's meta the way the New tag dialog adds a tag, and selected.
        MetaDataEntry Add(string key, float start, float end)
        {
            var tag = (DecodedMetaEntryBase)_runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>().CreateDefault(key);
            (tag.StartTime, tag.EndTime) = (start, end);
            Meta.ParsedFile!.Attributes.Add(tag);
            Meta.UpdateView();
            Meta.SelectedTag = Meta.Tags.Last();
            return Meta.SelectedTag;
        }

        List<MetaDataTimelineItem> Markers => _superView.Player.TimelineItems.Cast<MetaDataTimelineItem>().ToList();
        MetaDataTimelineItem Marker(MetaDataEntry row) => Markers.Single(x => x.Item.Source == row._input);
        void Click(MetaDataTimelineItem marker) => _superView.Player.TimelineItemClickCommand.Execute(marker);
        double FrameSeconds => MainPlayer.AnimationClip!.MicrosecondsPerFrame / 1_000_000.0;

        static AttributeViewModel Field(MetaDataEntry row, string name) => row.Variables.Single(x => x.FieldName == name);

        // A tag's undo rebuilds the tab's rows from the file, which upstream's timeline hears as a change to the tags.
        [Test]
        public void TheMarkers_FollowAnEdit_AnAdd_ADelete_AndAnUndo()
        {
            var row = Add("FIRE_POS_10", Length * 0.1f, Length * 0.1f);
            var count = Markers.Count;

            Field(row, "Start Time").ValueAsString = (Length * 0.7f).ToString();
            Assert.That(Marker(row).Start, Is.EqualTo(Length * 0.7f).Within(1e-5), "an edit");

            Add("IMPACT_POS_10", 0.1f, 0.1f);
            Assert.That(Markers, Has.Count.EqualTo(count + 1), "an add");

            foreach (var tag in Meta.Tags)
                tag.IsSelected = tag._input == row._input;
            Meta.DeleteActionCommand.Execute(null);
            Assert.That(Markers.Select(x => x.Item.Source), Does.Not.Contain(row._input), "a delete");
            Assert.That(Markers, Has.Count.EqualTo(count));

            Meta.UndoCommand.Execute(null);
            Assert.That(Markers, Has.Count.EqualTo(count + 1), "an undo");
        }

        // A clicked marker picks its row as the only one Delete takes, which the list keeps in step with its own clicks only.
        [Test]
        public void ClickingAMarker_MakesItsRowTheOnlyOneDeleteTakes_AndPausesTheClip()
        {
            var row = Add("FIRE_POS_10", Length * 0.5f, Length * 0.5f);
            Meta.SelectedTag = Meta.Tags[0];
            Meta.Tags[0].IsSelected = true;
            _superView.SelectedTabControllerIndex = 0;
            MainPlayer.Play();

            Click(Marker(row));

            Assert.Multiple(() =>
            {
                Assert.That(MainPlayer.GetTimeUs() / 1_000_000.0, Is.EqualTo(Length * 0.5).Within(FrameSeconds), "the playhead");
                Assert.That(MainPlayer.IsPlaying, Is.False, "stopped there");
                Assert.That(_superView.SelectedTabControllerIndex, Is.EqualTo(1), "the animation tab");
                Assert.That(Meta.SelectedTag, Is.SameAs(row));
                Assert.That(Meta.Tags.Where(x => x.IsSelected), Is.EqualTo(new[] { row }), "the only row Delete would take");
            });
        }

        // Vanilla has tags before 0 (hu3_blunderbuss_aim_fire_stand_high_01 fires at -0.02 s); an edit can put one past the end.
        [Test]
        public void ClickingAMarkerOutsideTheClip_GoesToTheNearerEnd()
        {
            var after = Add("FIRE_POS_10", Length + 1, Length + 1);
            Click(Marker(after));
            Assert.That(MainPlayer.CurrentFrame, Is.EqualTo(MainPlayer.FrameCount() - 1), "the last frame");

            var before = Add("FIRE_POS_10", -0.5f, -0.5f);
            Click(Marker(before));
            Assert.That(MainPlayer.GetTimeUs(), Is.EqualTo(0));
        }

        // The gizmo's drag goes into the tab's own history, between the typed edits either side of it, so the toolbar's undo, Ctrl+Z
        // and the meta editor's undo take back the same steps in the same order. Upstream kept a second history for the gizmo alone.
        [Test]
        public void AGizmoDrag_IsOneStepInTheTabsUndo_BetweenTypedEdits()
        {
            _superView.SelectedTabControllerIndex = 1;
            var row = Add("FIRE_POS_10", 0.1f, 0.1f);
            var session = _runner.GetRequiredServiceInCurrentEditorScope<CombatMetaDataEditSession>();
            Assert.That(session.CurrentSource, Is.SameAs(row._input), "the gizmo is on the picked tag");
            Field(row, "Start Time").ValueAsString = 0.2f.ToString();
            var start = ((FirePos_v10)row._input).Position;

            Assert.That(session.BeginGesture(), Is.True);
            session.Translate(new Microsoft.Xna.Framework.Vector3(1, 0, 0));
            session.Translate(new Microsoft.Xna.Framework.Vector3(0, 1, 0));
            Assert.That(session.EndGesture(), Is.True);
            var moved = ((FirePos_v10)row._input).Position;
            Field(Meta.SelectedTag!, "End Time").ValueAsString = 0.3f.ToString();

            FirePos_v10 Last() => (FirePos_v10)Meta.ParsedFile!.Attributes.Last();
            Assert.Multiple(() =>
            {
                Assert.That(moved, Is.Not.EqualTo(start), "the drag moved the tag");
                Assert.That(_superView.HasUnsavedChanges, Is.True);
                Assert.That(_superView.CanUndoEditIn3D, Is.True);
            });

            _superView.UndoEditIn3DCommand.Execute(null);
            Assert.That((Last().Position, Last().EndTime), Is.EqualTo((moved, 0.1f)), "the typed edit after the drag");
            _superView.UndoEditIn3DCommand.Execute(null);
            Assert.That((Last().Position, Last().StartTime), Is.EqualTo((start, 0.2f)), "the whole drag, as one step");
            _superView.UndoEditIn3DCommand.Execute(null);
            Assert.That(Last().StartTime, Is.EqualTo(0.1f), "the typed edit before it");
            _superView.RedoEditIn3DCommand.Execute(null);
            _superView.RedoEditIn3DCommand.Execute(null);
            Assert.That((Last().Position, Last().StartTime), Is.EqualTo((moved, 0.2f)), "redone");
            Assert.That(session.CurrentSource, Is.SameAs(Last()), "the gizmo follows the pick to the tag the undo read back");
        }

        [Test]
        public void SetStartAndEndToNow_SetThePlayersTime()
        {
            var row = Add("SPLASH_ATTACK_10", 0.1f, 0.1f);
            MainPlayer.SetTime(250_000);
            Assert.That(Meta.SetStartToNowCommand.CanExecute(null), Is.True);

            Meta.SetStartToNowCommand.Execute(null);
            MainPlayer.SetTime(500_000);
            Meta.SetEndToNowCommand.Execute(null);

            var tag = (SplashAttack_v10)row._input;
            Assert.Multiple(() =>
            {
                Assert.That((tag.StartTime, tag.EndTime), Is.EqualTo((0.25f, 0.5f)));
                Assert.That(Field(row, "Start Time").ValueAsString, Is.EqualTo(0.25f.ToString()), "the box shows it");
                Assert.That(Field(row, "End Time").ValueAsString, Is.EqualTo(0.5f.ToString()));
                Assert.That(_superView.HasUnsavedChanges, Is.True);
                Assert.That((Marker(row).Start, Marker(row).End), Is.EqualTo((0.25, (double?)0.5)).Using<(double, double?)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 1e-6 && Math.Abs(a.Item2!.Value - b.Item2!.Value) < 1e-6), "the marker follows");
            });
        }

        // Each press is an undo step of its own, even between typed edits of the same tag a moment either side, which the
        // editor otherwise joins into one step.
        [Test]
        public void SetStartToNow_IsOneUndoStep_BetweenTypedEditsOfTheSameTag()
        {
            var row = Add("SPLASH_ATTACK_10", 0.1f, 0.1f);
            static SplashAttack_v10 Last(MetaDataEditorViewModel editor) => (SplashAttack_v10)editor.ParsedFile!.Attributes.Last();

            Field(row, "End Time").ValueAsString = 0.3f.ToString();
            MainPlayer.SetTime(250_000);
            Meta.SetStartToNowCommand.Execute(null);
            Field(Meta.SelectedTag!, "End Time").ValueAsString = 0.4f.ToString();

            Meta.UndoCommand.Execute(null);
            Assert.That((Last(Meta).StartTime, Last(Meta).EndTime), Is.EqualTo((0.25f, 0.3f)), "the typed edit after the press");
            Meta.UndoCommand.Execute(null);
            Assert.That((Last(Meta).StartTime, Last(Meta).EndTime), Is.EqualTo((0.1f, 0.3f)), "the press");
            Assert.That(Field(Meta.SelectedTag!, "Start Time").ValueAsString, Is.EqualTo(0.1f.ToString()), "the box shows the undo");
            Meta.UndoCommand.Execute(null);
            Assert.That((Last(Meta).StartTime, Last(Meta).EndTime), Is.EqualTo((0.1f, 0.1f)), "the typed edit before the press");
        }

        [Test]
        public void TheNowButtons_AreOff_ForATagWithNoTimes()
        {
            Meta.ParsedFile!.Attributes.Add(_runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>().CreateDefault("FIRE_POS_0"));
            Meta.UpdateView();
            Meta.SelectedTag = Meta.Tags.Last();

            Assert.That((Meta.SetStartToNowCommand.CanExecute(null), Meta.SetEndToNowCommand.CanExecute(null)), Is.EqualTo((false, false)));
            Meta.SelectedTag = Meta.Tags.First(x => x._input is DecodedMetaEntryBase);
            Assert.That((Meta.SetStartToNowCommand.CanExecute(null), Meta.SetEndToNowCommand.CanExecute(null)), Is.EqualTo((true, true)));

            Meta.ParsedFile!.Attributes.Add(_runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>().CreateDefault("FIRE_POS_2"));
            Meta.UpdateView();
            Meta.SelectedTag = Meta.Tags.Last();
            Assert.That(Meta.SetStartToNowCommand.CanExecute(null), Is.True, "FIRE_POS_2 has times on the version-2 base");
        }

        // The standalone meta editor has no player to read the time from.
        [Test]
        public void TheNowButtons_AreOff_InTheStandaloneEditor()
        {
            var file = _runner.PackFileService.FindFile(@"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta")!;
            var editor = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<MetaDataEditorViewModel>(file, EditorEnums.Meta_Editor);
            editor.SelectedTag = editor.Tags.First(x => x._input is DecodedMetaEntryBase);

            Assert.That((editor.HasPlayer, editor.SetStartToNowCommand.CanExecute(null)), Is.EqualTo((false, false)));
            Assert.That(Meta.HasPlayer, Is.True, "SuperView's editor");
        }

        [Test]
        public void Space_PlaysAndPausesTheClip_AndARebindMovesIt()
        {
            MainPlayer.Pause();
            Assert.That(_superView.OnKeyPressed(Key.Space, ModifierKeys.None), Is.True);
            Assert.That(MainPlayer.IsPlaying, Is.True, "played");
            _superView.OnKeyPressed(Key.Space, ModifierKeys.None);
            Assert.That(MainPlayer.IsPlaying, Is.False, "paused");

            _runner.ServiceProvider.GetRequiredService<HotkeyService>().Set(new Dictionary<string, Hotkey?> { [EditorHostBase.PlayPauseHotkey] = new Hotkey(Key.P, ModifierKeys.Control) });
            Assert.That(_superView.OnKeyPressed(Key.Space, ModifierKeys.None), Is.False, "Space is left to whatever has focus");
            Assert.That(MainPlayer.IsPlaying, Is.False);
            Assert.That(_superView.OnKeyPressed(Key.P, ModifierKeys.Control), Is.True);
            Assert.That(MainPlayer.IsPlaying, Is.True, "Ctrl+P plays it");
        }

        // Play after a pause carries on from the paused time; it used to go back to frame 0 every time.
        [Test]
        public void Play_CarriesOnFromWhereTheClipWasPaused_AndStartsAgainOnlyFromTheEnd()
        {
            var length = MainPlayer.GetAnimationLengthUs();
            // The scrub bar's own call, which pauses on the nearest frame.
            _superView.Player.SetAnimationTime(length / 2 / 1_000_000.0);
            Assert.That(MainPlayer.IsPlaying, Is.False, "paused");
            _superView.OnKeyPressed(Key.Space, ModifierKeys.None);
            Assert.That(MainPlayer.IsPlaying, Is.True);
            Assert.That(MainPlayer.GetTimeUs(), Is.EqualTo(length / 2.0).Within(MainPlayer.AnimationClip!.MicrosecondsPerFrame), "where it was paused");

            _superView.OnKeyPressed(Key.Space, ModifierKeys.None);
            MainPlayer.SetTime(length);
            _superView.OnKeyPressed(Key.Space, ModifierKeys.None);
            Assert.That(MainPlayer.GetTimeUs(), Is.Zero, "from the end it starts again");
        }

        // Left and Right step a frame, a held key steps on, and the scrub bar's playhead follows. Stepping onto the last frame with
        // Loop on stays there, paused; it used to start the clip again from frame 0.
        [Test]
        public void TheArrowKeys_StepAFrame_AndHeldStepOn_ToTheLastFrame_WithoutLoopingBack()
        {
            MainPlayer.Pause();
            Assert.That(_superView.Player.LoopAnimation.Value, Is.True, "Loop is on, as it opens");
            Assert.That(_superView.OnKeyPressed(Key.Right, ModifierKeys.None), Is.True);
            Assert.That(MainPlayer.CurrentFrame, Is.EqualTo(1));
            Assert.That(_superView.Player.ScrubTime, Is.GreaterThan(0), "the playhead followed");
            Assert.That(_superView.OnKeyPressed(Key.Left, ModifierKeys.None), Is.True);
            Assert.That(MainPlayer.CurrentFrame, Is.Zero);

            var last = MainPlayer.FrameCount() - 1;
            for (var i = 0; i < last + 5; i++)
                Assert.That(_superView.OnKeyPressed(Key.Right, ModifierKeys.None, isRepeat: true), Is.True, "a held key steps on");
            Assert.That(MainPlayer.CurrentFrame, Is.EqualTo(last), "at the end");
            Assert.That(MainPlayer.IsPlaying, Is.False, "still paused there");

            Assert.That(_superView.OnKeyPressed(Key.Space, ModifierKeys.None, isRepeat: true), Is.False, "a held Space does not toggle again");
            Assert.That(MainPlayer.IsPlaying, Is.False);
        }

        // A list, tree or slider with focus keeps the arrows for itself; a rebind moves the steps elsewhere.
        [Test]
        public void TheStepKeys_AreLeftToAControlThatMovesWithKeys_AndARebindMovesThem()
        {
            MainPlayer.Pause();
            Assert.That(_superView.OnKeyPressed(Key.Right, ModifierKeys.None, focusUsesKeys: true), Is.False);
            Assert.That(MainPlayer.CurrentFrame, Is.Zero);

            _runner.ServiceProvider.GetRequiredService<HotkeyService>().Set(new Dictionary<string, Hotkey?> { [EditorHostBase.NextFrameHotkey] = new Hotkey(Key.D, ModifierKeys.None), [EditorHostBase.PreviousFrameHotkey] = null });
            Assert.That(_superView.OnKeyPressed(Key.Right, ModifierKeys.None), Is.False, "Right is free again");
            Assert.That(_superView.OnKeyPressed(Key.D, ModifierKeys.None), Is.True);
            Assert.That(MainPlayer.CurrentFrame, Is.EqualTo(1));
            Assert.That(_superView.OnKeyPressed(Key.Left, ModifierKeys.None), Is.False, "the cleared step does nothing");
            Assert.That(MainPlayer.CurrentFrame, Is.EqualTo(1));
        }


        [Test, Apartment(ApartmentState.STA)]
        public void TheTagList_MarksEachRow_AndShowsTheNowButtonsOnlyWithAPlayer()
        {
            var file = _runner.PackFileService.FindFile(@"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta")!;
            var standalone = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<MetaDataEditorViewModel>(file, EditorEnums.Meta_Editor);

            foreach (var (editor, hasPlayer) in new[] { (Meta, true), (standalone, false) })
            {
                var view = new MetaDataEntryView { DataContext = editor };
                var host = new Window { Content = view, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 300, Height = 900 };
                try
                {
                    host.Show();
                    Pump();
                    var rows = FindAll<ListViewItem>(view).ToList();
                    Assert.That(rows, Has.Count.EqualTo(editor.Tags.Count), "every row is drawn");
                    foreach (var row in rows)
                    {
                        var entry = (MetaDataEntry)row.DataContext;
                        var icon = FindAll<MaterialIcon>(row).Single(x => AutomationProperties.GetAutomationId(x) == "SuperViewMark");
                        var kind = entry.SuperViewMark.Drawn switch
                        {
                            Editors.AnimationMeta.SuperView.Visualisation.ShownInSuperView.Drawn.Yes => MaterialIconKind.Eye,
                            Editors.AnimationMeta.SuperView.Visualisation.ShownInSuperView.Drawn.Partly => MaterialIconKind.EyeOutline,
                            _ => MaterialIconKind.EyeOffOutline,
                        };
                        Assert.That(icon.Kind, Is.EqualTo(kind), entry.DisplayName);
                        Assert.That(icon.ToolTip, Is.EqualTo(entry.SuperViewMark.Tooltip));
                    }
                    Assert.That(rows.Select(x => FindAll<MaterialIcon>(x).Single().Kind), Does.Contain(MaterialIconKind.Eye), "Throt's animated prop is drawn");

                    var buttons = FindAll<Button>(view).Where(x => AutomationProperties.GetAutomationId(x) is "MetaSetStartToNow" or "MetaSetEndToNow").ToList();
                    Assert.That(buttons, Has.Count.EqualTo(2));
                    Assert.That(buttons.Select(x => x.IsVisible), Is.All.EqualTo(hasPlayer), hasPlayer ? "SuperView" : "the standalone editor");
                    if (!hasPlayer)
                        continue;

                    // A button asks again whether it can run only when told to, so picking a row without times has to tell it.
                    editor.SelectedTag = editor.Tags.First(x => x._input is DecodedMetaEntryBase);
                    Pump();
                    Assert.That(buttons.Select(x => x.IsEnabled), Is.All.True, "a tag with times");
                    editor.ParsedFile!.Attributes.Add(_runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>().CreateDefault("FIRE_POS_0"));
                    editor.UpdateView();
                    editor.SelectedTag = editor.Tags.Last();
                    Pump();
                    Assert.That(buttons.Select(x => x.IsEnabled), Is.All.False, "FIRE_POS_0 has no times");
                }
                finally
                {
                    host.Close();
                }
            }
        }

        static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
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
    }
}
