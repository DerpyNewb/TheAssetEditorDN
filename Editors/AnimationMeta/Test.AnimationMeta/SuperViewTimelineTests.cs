using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.Presentation.View;
using Editors.AnimationMeta.SuperView;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Animation;
using Material.Icons;
using Material.Icons.WPF;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // SuperView's tag timeline under the slot panel, and the meta editor's "Set start to now" and "Set end to now", on Throt's
    // ATTACK_5.
    public class SuperViewTimelineTests
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

        TimelineTick Tick(MetaDataEntry row) => _superView.TimelineTicks.Single(x => x.Tag == row._input);

        static AttributeViewModel Field(MetaDataEntry row, string name) => row.Variables.Single(x => x.FieldName == name);

        [Test]
        public void TheStrip_HasATickAtEveryTimedTagsStart_FromBothMetas()
        {
            var untimed = _runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>().CreateDefault("FIRE_POS_0");
            var version2 = (FirePos_v2)_runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>().CreateDefault("FIRE_POS_2");
            version2.StartTime = Length / 2;
            Meta.ParsedFile!.Attributes.Add(untimed);
            Meta.ParsedFile!.Attributes.Add(version2);
            Meta.UpdateView();
            var quarter = Add("FIRE_POS_10", Length / 4, Length / 4);

            static IEnumerable<ParsedMetadataAttribute> Timed(MetaDataEditorViewModel editor) =>
                editor.ParsedFile?.Attributes.Where(x => x is DecodedMetaEntryBase or DecodedMetaEntryBase_v2) ?? [];
            var timed = Timed(_superView.PersistentMetaEditor).Concat(Timed(Meta)).ToList();

            Assert.That(_superView.HasTimeline, Is.True);
            Assert.That(_superView.TimelineTicks.Select(x => x.Tag), Is.EqualTo(timed), "one tick per timed tag, persistent first");
            Assert.That(_superView.TimelineTicks.Select(x => x.Tag), Does.Not.Contain(untimed), "FIRE_POS_0 has no times");
            Assert.That(_superView.TimelineTicks.Count(x => x.Persistent), Is.EqualTo(Timed(_superView.PersistentMetaEditor).Count()));
            Assert.That(Tick(quarter).From, Is.EqualTo(0.25).Within(1e-4), "a quarter of the way along");
            Assert.That(_superView.TimelineTicks.Single(x => x.Tag == version2).From, Is.EqualTo(0.5).Within(1e-4), "FIRE_POS_2, a version-2 tag");
        }

        [Test]
        public void AWindow_GetsABarToItsEnd_AndAnInstantDoesNot()
        {
            var window = Add("SPLASH_ATTACK_10", Length * 0.2f, Length * 0.6f);
            var instant = Add("FIRE_POS_10", Length * 0.5f, Length * 0.5f);

            Assert.Multiple(() =>
            {
                Assert.That((Tick(window).IsWindow, Tick(window).From, Tick(window).To), Is.EqualTo((true, 0.2, 0.6)).Using<(bool, double, double)>(Near));
                Assert.That((Tick(instant).IsWindow, Tick(instant).From, Tick(instant).To), Is.EqualTo((false, 0.5, 0.5)).Using<(bool, double, double)>(Near));
                Assert.That(Tick(window).Tooltip, Is.EqualTo(FormattableString.Invariant($"SPLASH_ATTACK_10 runs from {Length * 0.2f:0.00#} s to {Length * 0.6f:0.00#} s.")));
                Assert.That(Tick(instant).Tooltip, Is.EqualTo(FormattableString.Invariant($"FIRE_POS_10 is at {Length * 0.5f:0.00#} s.")));
            });
        }

        static bool Near((bool, double, double) a, (bool, double, double) b) => a.Item1 == b.Item1 && Math.Abs(a.Item2 - b.Item2) < 1e-4 && Math.Abs(a.Item3 - b.Item3) < 1e-4;

        // Vanilla has tags before 0 (hu3_blunderbuss_aim_fire_stand_high_01 fires at -0.02 s); an edit can put one past the end.
        [Test]
        public void ATagOutsideTheClip_SitsAtTheNearerEdge_AndSaysSo()
        {
            var before = Add("FIRE_POS_10", -0.02f, -0.02f);
            var after = Add("IMPACT_POS_10", Length + 1, Length + 1);
            var atEnd = Add("TARGET_POS_10", Length, Length);

            Assert.Multiple(() =>
            {
                Assert.That((Tick(before).From, Tick(before).Outside), Is.EqualTo((0d, true)));
                Assert.That(Tick(before).Tooltip, Does.EndWith("That's before the clip starts, so its tick sits at the left edge."));
                Assert.That((Tick(after).From, Tick(after).Outside), Is.EqualTo((1d, true)));
                Assert.That(Tick(after).Tooltip, Does.Contain(FormattableString.Invariant($"That's after the clip ends at {Length:0.00#} s")));
                Assert.That((Tick(atEnd).From, Tick(atEnd).Outside), Is.EqualTo((1d, false)), "the last moment is in the clip");
            });
        }

        [Test]
        public void TheStrip_FollowsAnEdit_AnAdd_ADelete_AndAnUndo()
        {
            var row = Add("FIRE_POS_10", Length * 0.1f, Length * 0.1f);
            var count = _superView.TimelineTicks.Count;

            Field(row, "Start Time").ValueAsString = (Length * 0.7f).ToString();
            Assert.That(Tick(row).From, Is.EqualTo(0.7).Within(1e-4), "an edit");

            Add("IMPACT_POS_10", 0, 0);
            Assert.That(_superView.TimelineTicks, Has.Count.EqualTo(count + 1), "an add");

            foreach (var tag in Meta.Tags)
                tag.IsSelected = tag._input == row._input;
            Meta.DeleteActionCommand.Execute(null);
            Assert.That(_superView.TimelineTicks.Select(x => x.Tag), Does.Not.Contain(row._input), "a delete");
            Assert.That(_superView.TimelineTicks, Has.Count.EqualTo(count));

            Meta.UndoCommand.Execute(null);
            Assert.That(_superView.TimelineTicks, Has.Count.EqualTo(count + 1), "an undo");
        }

        [Test]
        public void TheSelectedTag_IsHighlighted_InTheTabThatShowsIt()
        {
            _superView.SelectedTabControllerIndex = 1;
            var row = Add("FIRE_POS_10", 0.1f, 0.1f);

            Assert.That(_superView.TimelineTicks.Where(x => x.IsSelected).Select(x => x.Tag), Is.EqualTo(new[] { row._input }));

            _superView.SelectedTabControllerIndex = 0;
            Assert.That(_superView.TimelineTicks.Where(x => x.IsSelected && !x.Persistent), Is.Empty, "the animation tab is not shown");
        }

        [Test]
        public void ClickingATick_MovesThePlayheadThere_AndSelectsItsTag()
        {
            var row = Add("FIRE_POS_10", Length * 0.5f, Length * 0.5f);
            Meta.SelectedTag = Meta.Tags[0];
            Meta.Tags[0].IsSelected = true;
            _superView.SelectedTabControllerIndex = 0;
            MainPlayer.Play();

            _superView.SelectTickCommand.Execute(Tick(row));

            Assert.Multiple(() =>
            {
                Assert.That(MainPlayer.GetTimeUs(), Is.EqualTo((long)Math.Round(Length * 0.5f * 1_000_000d)), "the playhead");
                Assert.That(MainPlayer.IsPlaying, Is.False, "stopped there");
                Assert.That(_superView.Player.SelectedAnimationCurrentTime.Value, Is.EqualTo(Length * 0.5f).Within(1e-5), "the playback bar's readout");
                Assert.That(_superView.SelectedTabControllerIndex, Is.EqualTo(1), "the animation tab");
                Assert.That(Meta.SelectedTag, Is.SameAs(row));
                Assert.That(Meta.Tags.Where(x => x.IsSelected), Is.EqualTo(new[] { row }), "the only row Delete would take");
                Assert.That(Tick(row).IsSelected, Is.True, "its tick is highlighted");
            });
        }

        [Test]
        public void ClickingATickOutsideTheClip_GoesToTheNearerEnd()
        {
            var after = Add("FIRE_POS_10", Length + 1, Length + 1);
            _superView.SelectTickCommand.Execute(Tick(after));
            Assert.That(MainPlayer.GetTimeUs(), Is.EqualTo(MainPlayer.GetAnimationLengthUs()));

            var before = Add("FIRE_POS_10", -0.5f, -0.5f);
            _superView.SelectTickCommand.Execute(Tick(before));
            Assert.That(MainPlayer.GetTimeUs(), Is.EqualTo(0));
        }

        [Test]
        public void ClickingAPersistentTick_ShowsThePersistentTab()
        {
            var persistent = _superView.TimelineTicks.FirstOrDefault(x => x.Persistent);
            Assert.That(persistent, Is.Not.Null, "Throt's persistent meta has no tag with times");
            _superView.SelectedTabControllerIndex = 1;

            _superView.SelectTickCommand.Execute(persistent);

            Assert.That(_superView.SelectedTabControllerIndex, Is.EqualTo(0));
            Assert.That(_superView.PersistentMetaEditor.SelectedTag!._input, Is.SameAs(persistent!.Tag));
            Assert.That(persistent.Tooltip, Does.EndWith(" It's in the persistent meta."));
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
                Assert.That((Tick(row).From, Tick(row).To), Is.EqualTo((0.25 / Length, 0.5 / Length)).Using<(double, double)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 1e-4 && Math.Abs(a.Item2 - b.Item2) < 1e-4), "the strip follows");
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

        // Hosted in a plain window off screen, as Kitbash's view tests are. WPF ignores a synthetic mouse, so the ticks are
        // pressed through their automation peers.
        [Test, Apartment(ApartmentState.STA)]
        public void TheStripView_DrawsEachTickAtItsTime_AndPressingOneSelectsItsTag()
        {
            var outside = Add("IMPACT_POS_10", Length + 1, Length + 1);
            var window = Add("SPLASH_ATTACK_10", Length * 0.2f, Length * 0.6f);
            var target = Add("FIRE_POS_10", Length * 0.5f, Length * 0.5f);
            Meta.SelectedTag = Meta.Tags[0];

            var view = new MetaTimelineView { DataContext = _superView };
            var host = new Window { Content = view, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 400, Height = 120 };
            try
            {
                host.Show();
                Pump();
                var panel = FindAll<MetaTimelinePanel>(view).Single();
                var width = panel.ActualWidth;
                var buttons = FindAll<Button>(view).Where(x => AutomationProperties.GetAutomationId(x) == "MetaTimelineTick").ToList();
                Assert.That(width, Is.GreaterThan(300));
                Assert.That(buttons, Has.Count.EqualTo(_superView.TimelineTicks.Count));
                foreach (var button in buttons)
                {
                    var tick = (TimelineTick)button.DataContext;
                    var line = button.TranslatePoint(new Point(MetaTimelinePanel.Pad, 0), panel).X;
                    Assert.That(line, Is.EqualTo(Math.Clamp(tick.From * width - 1, 0, width - 2)).Within(0.5), tick.Tooltip);
                    Assert.That(button.ToolTip, Is.EqualTo(tick.Tooltip));
                }
                Assert.That(Part(TickButton(view, outside), "Tick").OpacityMask, Is.Not.Null, "an outside tick is dashed");
                Assert.That(Part(TickButton(view, target), "Tick").OpacityMask, Is.Null, "an inside one is not");
                Assert.That(TickButton(view, window).ActualWidth - 2 * MetaTimelinePanel.Pad, Is.EqualTo(0.4 * width + 1).Within(0.5), "the bar runs to the end");

                ((IInvokeProvider)new ButtonAutomationPeer(TickButton(view, target)).GetPattern(PatternInterface.Invoke)).Invoke();
                Pump();

                Assert.That(Meta.SelectedTag, Is.SameAs(target));
                Assert.That(((SolidColorBrush)Part(TickButton(view, target), "Tick").Fill).Color, Is.EqualTo(Colors.Red), "the selected tick is red");
                Assert.That((Part(TickButton(view, outside), "Tick").Fill as SolidColorBrush)?.Color, Is.Not.EqualTo(Colors.Red), "the others are not");
            }
            finally
            {
                host.Close();
            }
        }

        static Button TickButton(DependencyObject view, MetaDataEntry row) => FindAll<Button>(view).Single(x => x.DataContext is TimelineTick tick && tick.Tag == row._input);
        static Rectangle Part(Button button, string part) => (Rectangle)button.Template.FindName(part, button);

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
