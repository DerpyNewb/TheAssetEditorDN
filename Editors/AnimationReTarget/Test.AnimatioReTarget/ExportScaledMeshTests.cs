using System.Globalization;
using Editors.AnimatioReTarget.Editor;
using Editors.AnimatioReTarget.Editor.Saving;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Moq;
using Shared.ByteParsing;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Vertex;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimatioReTarget
{
    // A retargeted animation saved at a skeleton scale keeps the scale only in its bone positions, so on the target's own
    // skeleton and models the parts drift apart. Export at scale writes a scaled skeleton and scaled models for it, and
    // animations saved after name that skeleton. The game draws a vertex as its weights times the saved animation's bone
    // transforms times the inverse bind pose, so the saved files must put every vertex where the editor drew it.
    public class ExportScaledMeshTests
    {
        const string Idle = @"animations\battle\humanoid01\2handed_hammer\stand\hu1_2hh_stand_idle_01.anim";
        const string Saved = @"animations\battle\humanoid01e\2handed_hammer\stand\prefix_hu1_2hh_stand_idle_01.anim";
        const string Skeleton = @"animations\skeletons\humanoid01e_s15.anim";
        const string Model = @"variantmeshes\wh_variantmodels\hu1e\cth\cth_celestial_general\cth_celestial_general_body_02.rigid_model_v2";

        const string Wsmodel = @"variantmeshes\wh_variantmodels\hu1e\cth\cth_celestial_general\cth_celestial_general_body_02.wsmodel";

        static (AssetEditorTestRunner, AnimationRetargetEditor, List<string> Messages) Open(bool withTarget = true, string target = Wsmodel)
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var messages = new List<string>();
            runner.Dialogs.Setup(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((m, _) => messages.Add(m));
            var editor = (AnimationRetargetEditor)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.AnimationRetarget_Editor);
            var scene = runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();
            var source = editor.GetSceneObjectFromId(AnimationRetargetIds.Source).Data;
            scene.SetMesh(source, runner.PackFileService.FindFile(@"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.wsmodel")!);
            scene.SetAnimation(source, Idle);
            if (withTarget)
            {
                scene.SetMesh(editor.GetSceneObjectFromId(AnimationRetargetIds.Target).Data, runner.PackFileService.FindFile(target)!);
                editor.BoneManager.ApplyDefaultMapping();
            }
            return (runner, editor, messages);
        }

        static Vector3 Skin(CommonVertex vertex, AnimationFrame frame)
        {
            var output = Vector3.Zero;
            for (var i = 0; i < vertex.WeightCount; i++)
                output += vertex.BoneWeight[i] * Vector3.Transform(vertex.GetPosistionAsVec3(), frame.BoneTransforms[vertex.BoneIndex[i]].WorldTransform);
            return output;
        }

        // A target picked as a wsmodel, and as the bare model, whose model is then the scene object's root.
        [TestCase(Wsmodel)]
        [TestCase(Model)]
        public void ExportedAtScale_TheSavedFilesDrawWhatTheEditorShows(string target)
        {
            var (runner, editor, messages) = Open(true, target);
            var pfs = runner.PackFileService;
            editor.Settings.SkeletonScale = 1.5f;
            editor.UpdateAnimation();

            var paths = editor.SaveManager.ExportScaledMesh();
            Assert.That(paths, Is.Not.Null, string.Join("\n", messages));
            Assert.That(paths!.Take(2), Is.EqualTo(new[] { Skeleton, @"animations\skeletons\humanoid01e_s15.bone_inv_trans_mats" }));
            Assert.That(paths.Skip(2).ToList(), Has.Count.GreaterThan(0).And.All.EndWith("_s15.rigid_model_v2"));
            Assert.That((editor.SaveManager.Settings.UseGeneratedSkeleton, editor.SaveManager.Settings.ScaledSkeletonName), Is.EqualTo((true, "humanoid01e_s15")));

            editor.SaveManager.SaveAnimation(false);
            var skeletonFile = AnimationFile.Create(pfs.FindFile(Skeleton)!);
            var scaledSkeleton = new GameSkeleton(skeletonFile, null);
            var animation = AnimationFile.Create(pfs.FindFile(Saved)!);
            Assert.That((skeletonFile.Header.SkeletonName, animation.Header.SkeletonName), Is.EqualTo(("humanoid01e_s15", "humanoid01e_s15")));
            Assert.That(pfs.FindFile(paths[1])!.DataSource.ReadData(), Is.EqualTo(scaledSkeleton.CreateInvMatrixFile().GetBytes()), "the inverse bind pose is the saved skeleton's");

            var generated = editor.GetSceneObjectFromId(AnimationRetargetIds.Generated).Data;
            var savedClip = new AnimationClip(animation, scaledSkeleton);
            var frameCount = generated.AnimationClip!.DynamicFrames.Count;
            var (worst, unscaled, vertices) = (0f, 0f, 0);
            foreach (var path in paths.Skip(2))
            {
                var scaled = ModelFactory.Create().Load(pfs.FindFile(path)!.DataSource.ReadData());
                var original = ModelFactory.Create().Load(pfs.FindFile(path.Replace("_s15.rigid_model_v2", ".rigid_model_v2"))!.DataSource.ReadData());
                Assert.That(scaled.Header.SkeletonName, Is.EqualTo("humanoid01e_s15"), path);
                var (box, originalBox) = (scaled.ModelList[0][0].CommonHeader.BoundingBox, original.ModelList[0][0].CommonHeader.BoundingBox);
                Assert.That((box.MinimumY, box.MaximumY), Is.EqualTo((originalBox.MinimumY * 1.5f, originalBox.MaximumY * 1.5f)), path);

                foreach (var frameIndex in new[] { 0, frameCount / 2, frameCount - 1 })
                {
                    var shown = AnimationSampler.Sample(frameIndex, 0, generated.Skeleton, generated.AnimationClip);
                    var inGame = AnimationSampler.Sample(frameIndex, 0, scaledSkeleton, savedClip);
                    var onOwnSkeleton = AnimationSampler.Sample(frameIndex, 0, generated.Skeleton, savedClip);
                    for (var mesh = 0; mesh < original.ModelList[0].Length; mesh++)
                    {
                        for (var v = 0; v < original.ModelList[0][mesh].Mesh.VertexList.Length; v++)
                        {
                            var originalVertex = original.ModelList[0][mesh].Mesh.VertexList[v];
                            var expected = Skin(originalVertex, shown);
                            worst = Math.Max(worst, Vector3.Distance(expected, Skin(scaled.ModelList[0][mesh].Mesh.VertexList[v], inGame)));
                            unscaled = Math.Max(unscaled, Vector3.Distance(expected, Skin(originalVertex, onOwnSkeleton)));
                            vertices += originalVertex.WeightCount == 0 ? 0 : 1;
                        }
                    }
                }

                foreach (var point in original.ModelList[0].Select(x => x.Material).OfType<Shared.GameFormats.RigidModel.MaterialHeaders.WeightedMaterial>().SelectMany(x => x.AttachmentPointParams))
                    TestContext.Out.WriteLine($"{path}: attachment point {point.Name} identity={point.Matrix.IsIdentity()}");
            }

            TestContext.Out.WriteLine($"{vertices} skinned vertices, worst {worst:F5}, the unscaled files {unscaled:F5}");
            Assert.That(vertices, Is.GreaterThan(1000));
            Assert.That(worst, Is.LessThan(0.005f), "the saved files draw a vertex away from where the editor drew it");
            Assert.That(unscaled, Is.GreaterThan(0.05f), "the unscaled files draw the same, so the check above proves nothing");

            // Unticked, saved animations name the target's own skeleton again.
            editor.SaveManager.Settings.UseGeneratedSkeleton = false;
            editor.SaveManager.SaveAnimation(false);
            Assert.That(AnimationFile.Create(pfs.FindFile(Saved)!).Header.SkeletonName, Is.EqualTo("humanoid01e"));
        }

        // Files already in the pack are overwritten only when the modder says so, and a cancel writes nothing.
        [Test]
        public void ExportingAgain_AsksBeforeOverwriting()
        {
            var (runner, editor, _) = Open();
            var pfs = runner.PackFileService;
            editor.Settings.SkeletonScale = 1.5f;
            editor.UpdateAnimation();
            var paths = editor.SaveManager.ExportScaledMesh()!;
            var exported = pfs.FindFile(Skeleton)!.DataSource.ReadData();
            pfs.SaveFile(pfs.FindFile(Skeleton)!, [1, 2, 3]);

            var asked = new List<string>();
            var answer = 1;
            runner.Dialogs.Setup(x => x.ShowChoiceBox(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns<string, string, IReadOnlyList<string>>((m, _, _) => { asked.Add(m); return answer; });
            Assert.That((editor.SaveManager.ExportScaledMesh(), pfs.FindFile(Skeleton)!.DataSource.ReadData()), Is.EqualTo(((List<string>?)null, new byte[] { 1, 2, 3 })));
            Assert.That(asked, Is.EqualTo(new[] { $"{paths.Count} of the {paths.Count} files are already in your pack." }));

            answer = 0;
            Assert.That(editor.SaveManager.ExportScaledMesh(), Is.EqualTo(paths));
            Assert.That(pfs.FindFile(Skeleton)!.DataSource.ReadData(), Is.EqualTo(exported));
        }

        // Every model is read and scaled before anything is written, so one that cannot be leaves the pack as it was.
        [Test]
        public void AModelThatCannotBeRead_SavesNothing()
        {
            var (runner, editor, messages) = Open();
            var pfs = runner.PackFileService;
            var pack = pfs.GetEditablePack()!;
            editor.Settings.SkeletonScale = 1.5f;
            pfs.DeleteFile(pfs.GetAllPackfileContainers().Single(x => x.Name.Contains("Karl")), pfs.FindFile(Model)!);
            var before = pack.GetFileCount();
            Assert.That((editor.SaveManager.ExportScaledMesh(), messages, pack.GetFileCount()), Is.EqualTo(((List<string>?)null, new[] { $"Nothing was saved. {Model} is in no loaded pack" }, before)));
        }

        // A pivot moves with the model. CA's skinned models leave it at zero, so one is set here.
        [Test]
        public void ScaleModel_MovesThePivot()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();   // without one, LoadPackFile never returns
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"));
            var file = ModelFactory.Create().Load(runner.PackFileService.FindFile(Model)!.DataSource.ReadData());
            file.ModelList[0][0].Material.PivotPoint = new Vector3(1, 2, 3);
            var scaled = ModelFactory.Create().Load(SaveManager.ScaleModel(ModelFactory.Create().Save(file), 1.5f, "humanoid01e_s15"));
            Assert.That(scaled.ModelList[0][0].Material.PivotPoint, Is.EqualTo(new Vector3(1.5f, 3, 4.5f)));
        }

        // The name is the same on a PC whose decimal mark is a comma.
        [Test]
        public void ScaleSuffix_IsTheSameInEveryCulture()
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(SaveManager.ScaleSuffix(1.5f), Is.EqualTo("_s15"));
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        // Nothing to scale, or a scale of 1, saves nothing and says why.
        [TestCase(false, 1.5f, "Pick a target model first.")]
        [TestCase(true, 1f, "Set a skeleton scale other than 1 first. At 1 the target's own skeleton and models already fit.")]
        public void WithNothingToScale_SavesNothing(bool withTarget, float scale, string message)
        {
            var (runner, editor, messages) = Open(withTarget);
            editor.Settings.SkeletonScale = scale;
            var before = runner.PackFileService.GetEditablePack()!.GetFileCount();
            Assert.That((editor.SaveManager.ExportScaledMesh(), messages, runner.PackFileService.GetEditablePack()!.GetFileCount()), Is.EqualTo(((List<string>?)null, new[] { message }, before)));
        }
    }
}
