using Editors.AnimatioReTarget.Editor;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimatioReTarget
{
    // The rewrite built the bone tree from the SOURCE skeleton, while the remapper reads each node as a TARGET bone. Karl's humanoid01
    // and the Cathay humanoid01e disagree from index 1 on (be_prop_0 against root; their props are weapon_1..6 at 2..7), so a setting
    // on "be_prop_0" moved the target's root, the "Is relative to" list was empty, and target bones past the source's 72 never moved.
    public class BoneTreeTests
    {
        const int SourceProp = 1;      // humanoid01 be_prop_0
        const int SourceHand = 30;     // humanoid01 hand_right

        AnimationRetargetEditor _editor = null!;
        SceneObject _source = null!, _target = null!, _generated = null!;
        AssetEditorTestRunner _runner = null!;
        Shared.Core.PackFiles.Models.IPackFileContainer _output = null!;
        SceneObjectEditor _scene = null!;

        [SetUp]
        public void Open()
        {
            var runner = _runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            _output = runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true)!;
            _editor = (AnimationRetargetEditor)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationRetarget_Editor);
            var scene = _scene = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();

            _source = _editor.GetSceneObjectFromId(AnimationRetargetIds.Source).Data;
            scene.SetMesh(_source, runner.PackFileService.FindFile(@"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.wsmodel")!);
            scene.SetAnimation(_source, "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim");
            _target = _editor.GetSceneObjectFromId(AnimationRetargetIds.Target).Data;
            scene.SetMesh(_target, runner.PackFileService.FindFile(@"variantmeshes\wh_variantmodels\hu1e\cth\cth_celestial_general\cth_celestial_general_body_02.wsmodel")!);
            _generated = _editor.GetSceneObjectFromId(AnimationRetargetIds.Generated).Data;

            Assert.That(_source.Skeleton.BoneNames[SourceProp], Is.EqualTo("be_prop_0"));
            Assert.That(_source.Skeleton.BoneNames[SourceHand], Is.EqualTo("hand_right"));
            _editor.BoneManager.ApplyDefaultMapping();
        }

        SkeletonBoneNode_new Node(string name) => SkeletonBoneNodeHelper.GetNodeFromName(name, _editor.BoneManager.Bones)
            ?? throw new AssertionException($"no node named {name}");

        static void MapTo(SkeletonBoneNode_new node, int sourceBone)
        {
            node.MappedIndex = sourceBone;
            node.HasMapping = true;
        }

        IEnumerable<(Vector3 Source, Vector3 Generated)> Frames(int sourceBone, int targetBone, int sourceRef = -1, int targetRef = -1)
        {
            var clip = _generated.AnimationClip;
            for (var f = 0; f < clip.DynamicFrames.Count; f += 5)
            {
                var src = AnimationSampler.Sample(f, 0, _source.Skeleton, _source.AnimationClip);
                var gen = AnimationSampler.Sample(f, 0, _target.Skeleton, clip);
                var s = src.GetSkeletonAnimatedWorld(_source.Skeleton, sourceBone).Translation;
                var g = gen.GetSkeletonAnimatedWorld(_target.Skeleton, targetBone).Translation;
                if (sourceRef != -1)
                {
                    s -= src.GetSkeletonAnimatedWorld(_source.Skeleton, sourceRef).Translation;
                    g -= gen.GetSkeletonAnimatedWorld(_target.Skeleton, targetRef).Translation;
                }
                yield return (s, g);
            }
        }

        [Test]
        public void TheTree_IsTheTargetSkeleton_AndTheRelativeListHoldsEveryBone()
        {
            var skeleton = _target.Skeleton;
            for (var i = 0; i < skeleton.BoneCount; i++)
                Assert.That(SkeletonBoneNodeHelper.GetNodeFromId(i, _editor.BoneManager.Bones)?.BoneName, Is.EqualTo(skeleton.BoneNames[i]), $"bone {i}");

            var list = _editor.BoneManager.FlatBoneList;
            Assert.That(list, Has.Count.EqualTo(skeleton.BoneCount + 1));
            Assert.That(list[0], Is.Null, "the first entry clears the relative bone");
            Assert.That(list.Skip(1).Select(x => x!.BoneName), Is.EqualTo(skeleton.BoneNames));
        }

        [Test]
        public void ATargetBonePastTheSourceCount_IsTransferred()
        {
            // feathers_3 is bone 95 of 96; the source has 72. Map it onto the source's hand and it must follow the hand.
            var feather = Node("feathers_3");
            MapTo(feather, SourceHand);
            _editor.Settings.ApplyRelativeScale = false;
            _editor.UpdateAnimation();

            foreach (var (s, g) in Frames(SourceHand, feather.BoneIndex))
                Assert.That(Vector3.Distance(s, g), Is.LessThan(1e-3f));
        }

        [Test]
        public void ARelativeBone_KeepsThePropWhereTheSourceHeldIt()
        {
            var prop = Node("weapon_1");
            MapTo(prop, SourceProp);
            prop.SelectedRelativeBone = _editor.BoneManager.FlatBoneList.Single(x => x?.BoneName == "hand_right");
            _editor.UpdateAnimation();

            var hand = Node("hand_right").BoneIndex;
            foreach (var (s, g) in Frames(SourceProp, prop.BoneIndex, SourceHand, hand))
                Assert.That(Vector3.Distance(s, g), Is.LessThan(1e-3f), "the prop's offset from the hand, in world space");
        }

        [Test]
        public void AnUnmappedRelativeBone_IsSkipped_NotAnException()
        {
            var prop = Node("weapon_1");
            MapTo(prop, SourceProp);
            prop.SelectedRelativeBone = Node("feathers_0");   // no namesake in humanoid01, so no mapping
            Assert.That(Node("feathers_0").HasMapping, Is.False);
            Assert.DoesNotThrow(() => _editor.UpdateAnimation());
            Assert.That(_generated.AnimationClip, Is.Not.Null);
        }

        [Test]
        public void ClearRelativeBone_EmptiesIt()
        {
            var prop = Node("weapon_1");
            _editor.BoneManager.SelectedBone = prop;
            prop.SelectedRelativeBone = Node("hand_right");
            _editor.BoneManager.ClearRelativeBoneCommand.Execute(null);
            Assert.That(prop.SelectedRelativeBone, Is.Null);
        }

        [Test]
        public void TheMappingWindow_StartsFromWhatTheTreeHasApplied()
        {
            // The window edits a config in place. It was built once and kept, so edits made before Cancel came back on the next open
            // and went in with the next OK. Built from the tree each time, a hand mapping the name automap cannot make survives it.
            // (Not weapon_1: the automap pairs weapon_1..6 with be_prop_0..5 by itself.)
            var feather = Node("feathers_0");
            MapTo(feather, SourceHand);
            _editor.BoneManager.ApplyDefaultMapping();
            Assert.That(feather.HasMapping, Is.True);
            Assert.That(feather.MappedIndex, Is.EqualTo(SourceHand));
        }

        [Test]
        public void TheTree_SaysWhichSourceBoneDrivesEachBone()
        {
            Assert.That(Node("hand_right").MappedFrom, Is.EqualTo("hand_right"));
            Assert.That(Node("feathers_0").MappedFrom, Is.Empty);
        }

        [Test]
        public void ResetSelectedBone_ClearsItsSettings_AndKeepsItsMapping()
        {
            var hand = Node("hand_right");
            _editor.BoneManager.SelectedBone = hand;
            hand.TranslationOffset.X.Value = 3;
            hand.RotationOffset.Y.Value = 45;
            hand.BoneLengthMult = 2;
            hand.FreezeRotation = true;
            hand.ApplyTranslation = false;
            hand.SelectedRelativeBone = Node("hand_left");

            _editor.BoneManager.ResetSelectedBoneCommand.Execute(null);

            Assert.That(hand.TranslationOffset.X.Value, Is.Zero);
            Assert.That(hand.RotationOffset.Y.Value, Is.Zero);
            Assert.That(hand.BoneLengthMult, Is.EqualTo(1));
            Assert.That(hand.FreezeRotation, Is.False);
            Assert.That(hand.ApplyTranslation, Is.True);
            Assert.That(hand.SelectedRelativeBone, Is.Null);
            Assert.That(hand.HasMapping, Is.True);
        }

        [Test]
        public void ApplyingAMapping_RegeneratesTheAnimation()
        {
            _editor.UpdateAnimation();
            var before = _generated.AnimationClip;
            _editor.BoneManager.ApplyDefaultMapping();
            Assert.That(_generated.AnimationClip, Is.Not.SameAs(before));
        }

        [Test]
        public void TheSaveWindow_ShowsWhereTheFileGoes()
        {
            _editor.SaveManager.Settings.SavePrefix = "x_";
            Assert.That(_editor.SaveManager.Settings.OutputPath, Is.EqualTo(@"animations\battle\humanoid01e\2handed_hammer\stand\x_hu1_2hh_stand_idle_01.anim"));
        }

        [Test, Apartment(ApartmentState.STA)]
        public void TheDropdowns_HaveTheirItems()
        {
            // Both were bound to things WPF cannot read: a property that did not exist, and a field.
            var view = new Editors.AnimatioReTarget.Editor.BoneHandling.Presentation.SelectedBoneView { DataContext = _editor };
            var relative = FindAll<System.Windows.Controls.ComboBox>(view).Single();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);   // runs WPF's deferred binding pass
            var expr = System.Windows.Data.BindingOperations.GetBindingExpression(relative, System.Windows.Controls.ItemsControl.ItemsSourceProperty);
            Assert.That(expr?.Status, Is.EqualTo(System.Windows.Data.BindingStatus.Active), "the binding reached its source");
            Assert.That(relative.Items, Has.Count.EqualTo(_target.Skeleton.BoneCount + 1));

            var formats = new System.Windows.Controls.ComboBox { DataContext = _editor.SaveManager.Settings };
            formats.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, "PossibleAnimationFormats");
            Assert.That(formats.Items, Has.Count.EqualTo(3));
        }

        static IEnumerable<T> FindAll<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
        {
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
            {
                if (child is T match)
                    yield return match;
                foreach (var inner in FindAll<T>(child))
                    yield return inner;
            }
        }

        // Batch export: upstream left it disabled with its code commented out.

        List<string> TwoSources()
        {
            // The test pack has one humanoid01 animation; a second comes from another, read-only pack, as a mod's would.
            const string Idle = "animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim";
            var bytes = _runner.PackFileService.FindFile(Idle)!.DataSource.ReadData();
            var extra = _runner.CreateEmptyPackFile("ExtraAnims", false);
            _runner.PackFileService.AddFilesToPack(extra, [new Shared.Core.PackFiles.NewPackFileEntry("animations\\battle\\humanoid01\\2handed_hammer\\stand", Shared.Core.PackFiles.Models.PackFile.CreateFromBytes("hu1_2hh_stand_idle_02.anim", bytes))]);

            // The animation index takes a new pack in on a background task.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            var candidates = _editor.SaveManager.BatchCandidates();
            while (candidates.Count < 2 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
                candidates = _editor.SaveManager.BatchCandidates();
            }
            Assert.That(candidates, Does.Contain("animations\\battle\\humanoid01\\2handed_hammer\\stand\\hu1_2hh_stand_idle_01.anim"));
            Assert.That(candidates, Has.None.StartsWith("animations\\skeletons"), "a skeleton is not an animation to retarget");
            Assert.That(candidates, Has.Count.GreaterThanOrEqualTo(2));
            return candidates.Take(2).ToList();
        }

        byte[]? Saved(string source) =>
            _runner.PackFileService.FindFile(_editor.SaveManager.OutputPath(source)!, _output)?.DataSource.ReadData();

        void AnswerExisting(int? choice) => _runner.Dialogs
            .Setup(x => x.ShowChoiceBox(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(choice);

        [Test]
        public void BatchExport_SavesEachAnimation_AsTheSingleSaveWould()
        {
            var sources = TwoSources();
            var result = _editor.SaveManager.BatchExport(sources)!;
            Assert.That(result.Saved, Is.EqualTo(2));

            foreach (var source in sources)
            {
                var batch = Saved(source);
                Assert.That(batch, Is.Not.Null, source);
                Assert.That(Shared.GameFormats.Animation.AnimationFile.Create(new Shared.Core.PackFiles.Models.PackFile("x", new Shared.Core.PackFiles.Models.FileSources.MemorySource(batch!))).Header.SkeletonName, Is.EqualTo("humanoid01e"));

                _scene.SetAnimation(_source, source);
                _editor.UpdateAnimation();
                _editor.SaveManager.SaveAnimation(false);
                Assert.That(Saved(source), Is.EqualTo(batch), $"{source}: the batch wrote what Save writes for it");
            }
        }

        [Test]
        public void BatchExport_AsksOnceAboutAnimationsAlreadyInThePack()
        {
            var sources = TwoSources();
            _editor.SaveManager.BatchExport([sources[0]]);
            var first = Saved(sources[0]);
            _editor.Settings.SkeletonScale = 2;   // so a rewrite is told apart from the file left alone

            AnswerExisting(1);   // Skip them
            var skipped = _editor.SaveManager.BatchExport(sources)!;
            _runner.Dialogs.Verify(x => x.ShowChoiceBox(It.Is<string>(m => m.Contains("1 of the 2")), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Once);
            Assert.That((skipped.Saved, skipped.Skipped), Is.EqualTo((1, 1)));
            Assert.That(Saved(sources[0]), Is.EqualTo(first));

            AnswerExisting(0);   // Overwrite them
            var overwritten = _editor.SaveManager.BatchExport(sources)!;
            Assert.That((overwritten.Saved, overwritten.Skipped), Is.EqualTo((2, 0)));
            Assert.That(Saved(sources[0]), Is.Not.EqualTo(first));
        }

        [TestCase(2)]
        [TestCase(null)]
        public void BatchExport_Cancelled_WritesNothing(int? choice)
        {
            var sources = TwoSources();
            _editor.SaveManager.BatchExport([sources[0]]);
            var first = Saved(sources[0]);
            _editor.Settings.SkeletonScale = 2;

            AnswerExisting(choice);
            Assert.That(_editor.SaveManager.BatchExport(sources), Is.Null);
            Assert.That(Saved(sources[0]), Is.EqualTo(first));
            Assert.That(Saved(sources[1]), Is.Null);
        }

        [Test]
        public void BatchExport_OneBadAnimation_DoesNotStopTheRest()
        {
            var good = TwoSources()[0];
            var result = _editor.SaveManager.BatchExport(["animations\\battle\\humanoid01\\no_such_file.anim", good])!;
            Assert.That(result.Saved, Is.EqualTo(1));
            Assert.That(result.Failed, Has.Count.EqualTo(1));
            Assert.That(result.Failed[0], Does.Contain("no_such_file"));
            Assert.That(Saved(good), Is.Not.Null);
        }

        [Test]
        public void SelectingABone_MarksItOnTheGeneratedModel_AndItsSourceOnTheSource()
        {
            var prop = Node("weapon_1");
            MapTo(prop, SourceProp);
            _editor.BoneManager.SelectedBone = prop;

            Assert.That(_generated.SkeletonSceneNode.SelectedBoneIndex, Is.EqualTo(prop.BoneIndex));
            Assert.That(_source.SkeletonSceneNode.SelectedBoneIndex, Is.EqualTo(SourceProp));
        }
    }
}
