using System.IO;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.GameFormats.RigidModel.Types;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.ReRigging
{
    // The Editable Model's skeleton box rebuilds the attachment points when the skeleton changes. Its filter box sends null
    // whenever typing refilters the list, and the rebuild was keyed on the previous name being non-null, so a skeleton
    // picked after typing a filter - the normal way to find one among 729 - kept the old skeleton's points and saved them.
    public class MainNodeSkeletonSwapTests
    {
        [Test]
        public void PickingASkeletonAfterTypingAFilter_RebuildsTheAttachmentPoints_AndReopeningKeepsThem()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(TestFiles.KarlPackFile, true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            var mainNode = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            var rootScene = runner.GetRequiredServiceInCurrentEditorScope<KitbasherRootScene>();

            var nodeEditor = runner.GetRequiredServiceInCurrentEditorScope<MainEditableNodeViewModel>();
            nodeEditor.Initialize(mainNode);
            Assert.That(Path.GetFileNameWithoutExtension(nodeEditor.SkeletonName), Is.EqualTo("humanoid01"));
            var before = Points(mainNode.AttachmentPoints);

            nodeEditor.SkeletonName = null;  // typing in the filter box
            Assert.That(rootScene.Skeleton.SkeletonName, Is.EqualTo("humanoid01"), "a cleared box is not a skeleton");
            nodeEditor.SkeletonName = nodeEditor.SkeletonNameList.Single(x => Path.GetFileNameWithoutExtension(x) == "humanoid01e");
            Assert.That(rootScene.Skeleton.SkeletonName, Is.EqualTo("humanoid01e"));

            var expected = Points(AttachmentPointHelper.CreateFromBoneList(rootScene.Skeleton.BoneNames));
            Assert.That(expected, Is.Not.EqualTo(before), "the two skeletons give the same points, so this proves nothing");
            Assert.That(Points(mainNode.AttachmentPoints), Is.EqualTo(expected));

            // Selecting the node again sets the box to the skeleton it already has: a model's own points must survive that.
            var own = AttachmentPointHelper.CreateFromBoneList(["bn_own_point"]);
            mainNode.SetAttachmentPoints(own, false);
            runner.GetRequiredServiceInCurrentEditorScope<MainEditableNodeViewModel>().Initialize(mainNode);
            Assert.That(Points(mainNode.AttachmentPoints), Is.EqualTo(Points(own)));
        }

        static List<string> Points(List<RmvAttachmentPoint> points) => points.Select(p => $"{p.BoneIndex}:{p.Name}").ToList();
    }
}
