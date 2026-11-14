using System.IO;
using Editors.KitbasherEditor.ChildEditors.ReRiggingTool;
using Editors.KitbasherEditor.ChildEditors.SaveDialog;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Services.SceneSaving;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.Ui.Editors.BoneMapping;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.ReRigging
{
    // An Empire swordsman - the state troops' torso, head and legs, on humanoid01 - moved onto humanoid01c, the Chaos Warrior
    // skeleton, the way it is done in Kitbash: open the torso, import the head and legs and make them editable, pick
    // humanoid01c on the Editable Model node, select the meshes, Re-rig, Auto map, map the leftovers by hand, OK, save.
    // humanoid01c has 98 bones to humanoid01's 72; 12 of the shared names sit at other indices, so a mesh that only took the
    // new skeleton's name would hang its cape, back skirt and eyebrows on the wrong bones. Reads the game's own packs from
    // WH3_DATA and is skipped without it.
    public class ReRigging_SwordsmanOntoHumanoid01cTests
    {
        const string Folder = @"variantmeshes\wh_variantmodels\hu1\emp\emp_state_troops\";
        const string Torso = Folder + @"torso\emp_state_troops_torso_01.rigid_model_v2";
        static readonly string[] HeadAndLegs = [Folder + @"head\emp_state_troops_head_01.rigid_model_v2", Folder + @"legs\emp_state_troops_legs_01.rigid_model_v2"];
        const string SourceSkeleton = "humanoid01";
        const string TargetSkeleton = "humanoid01c";

        // The humanoid01 bones with no bone of the same name in humanoid01c, and the one each goes to. Chosen 2026-11-11:
        // the renamed pads and jaw by name, the front skirt to the side skirt on its own side, the eyes to the head.
        static readonly Dictionary<string, string> Equivalents = new()
        {
            ["jaw"] = "jaw_0",
            ["shoulder_pad_left"] = "shoulderpad_left_0",
            ["shoulder_pad_right"] = "shoulderpad_right_0",
            ["skirt_front_left_0"] = "skirt_left_0",
            ["skirt_front_left_1"] = "skirt_left_0",
            ["skirt_front_right_0"] = "skirt_right_0",
            ["skirt_front_right_1"] = "skirt_right_0",
            ["eye_left"] = "head",
            ["eye_right"] = "head",
        };

        [Test]
        public void Warhammer3_SwordsmanOntoHumanoid01c_EveryInfluenceLandsOnItsOwnBoneOrItsEquivalent()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(data != null && Directory.Exists(data), "set WH3_DATA to the game's data folder");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            foreach (var pack in new[] { "anim2.pack", "variants4.pack" })
                runner.PackFileService.AddContainer(loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data!, pack), true));
            var outputPack = runner.CreateOutputPack();

            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(Torso)!);
            var commands = runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>();
            var sceneManager = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>();
            var referenceGroup = sceneManager.GetNodeByName<GroupNode>(SpecialNodes.ReferenceMeshs)!;
            var mainNode = sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            foreach (var part in HeadAndLegs)
            {
                commands.Create<ImportReferenceMeshCommand>(x => x.Configure(part)).Execute();
                SceneNodeHelper.MakeNodeEditable(mainNode, referenceGroup.Children.Last());
            }

            var meshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(mainNode).ToList();
            Assert.That(meshes.Select(x => x.Geometry.SkeletonName), Is.All.EqualTo(SourceSkeleton));
            Assert.That(meshes.Select(x => x.Name).Distinct().Count(), Is.GreaterThanOrEqualTo(3), "torso, head and legs are not all in the editable model");

            // The Editable Model node's skeleton box, as picked in the GUI: it swaps the scene's skeleton, which the re-rig
            // maps onto, and rebuilds the attachment points from the new bone list.
            var rootScene = runner.GetRequiredServiceInCurrentEditorScope<KitbasherRootScene>();
            Assert.That(rootScene.Skeleton.SkeletonName, Is.EqualTo(SourceSkeleton));
            var nodeEditor = runner.GetRequiredServiceInCurrentEditorScope<MainEditableNodeViewModel>();
            nodeEditor.Initialize(mainNode);
            nodeEditor.SkeletonName = nodeEditor.SkeletonNameList.Single(x => Path.GetFileNameWithoutExtension(x) == TargetSkeleton);
            Assert.That(rootScene.Skeleton.SkeletonName, Is.EqualTo(TargetSkeleton));

            var skeletons = runner.GetRequiredServiceInCurrentEditorScope<ISkeletonAnimationLookUpHelper>();
            var source = skeletons.GetSkeletonFileFromName(SourceSkeleton)!;
            var target = skeletons.GetSkeletonFileFromName(TargetSkeleton)!;
            var sourceNames = source.Bones.ToDictionary(b => b.Id, b => b.Name);
            var targetNames = target.Bones.ToDictionary(b => b.Id, b => b.Name);
            var targetIndex = target.Bones.ToDictionary(b => b.Name, b => b.Id);
            Assert.That(Equivalents.Keys.Where(targetIndex.ContainsKey), Is.Empty, "a listed leftover is in humanoid01c after all");
            Assert.That(Equivalents.Values.Distinct(), Is.SubsetOf(targetIndex.Keys));

            var before = meshes.Select(Influences).ToList();

            // Without this the test proves nothing: if the two skeletons agreed on every index the swordsman uses, a re-rig
            // that did nothing would pass every check below.
            var wrongWithoutRerig = before.SelectMany(x => x).Count(i => targetNames.GetValueOrDefault(i.Bone) != Equivalents.GetValueOrDefault(sourceNames[i.Bone], sourceNames[i.Bone]));
            Assert.That(wrongWithoutRerig, Is.GreaterThan(0), "the skeletons agree on every index the swordsman uses");

            // Built the way OpenReriggingToolCommand.Execute builds it, which also opens the window.
            var usedBones = meshes.SelectMany(x => x.Geometry.GetUniqeBlendIndices()).Distinct().Select(x => (int)x).ToList();
            var config = new RemappedAnimatedBoneConfiguration
            {
                MeshSkeletonName = SourceSkeleton,
                MeshBones = AnimatedBoneHelper.CreateFromSkeleton(source, usedBones),
                ParnetModelSkeletonName = rootScene.Skeleton.SkeletonName,
                ParentModelBones = AnimatedBoneHelper.CreateFromSkeleton(target)
            };
            var rerig = runner.GetRequiredServiceInCurrentEditorScope<ReRiggingViewModel>();
            rerig.Initialize(meshes, config);
            rerig.AutoMapSelfAndChildrenByName();
            TestContext.Out.WriteLine($"left for the person after auto map: {rerig.UnmappedUsedBonesText.Value}");
            Assert.That(rerig.IsMappingComplete.Value, Is.False, "auto map placed every used bone, so the window's list was never tested here");

            // What the user does by hand in the window: every used bone the auto map could not place, and every bone on the
            // list whatever the auto map chose for it, goes to its listed equivalent. A used bone that is neither mapped nor on
            // the list fails here, by name, before OK can paper over it.
            var used = Flatten(config.MeshBones).Where(b => b.IsUsedByCurrentModel.Value).ToList();
            var handMapped = new List<string>();
            foreach (var bone in used.Where(b => Equivalents.ContainsKey(b.Name.Value)))
            {
                var name = Equivalents[bone.Name.Value];
                handMapped.Add($"{bone.Name.Value} -> {name} (auto map said {(bone.MappedBoneIndex.Value == -1 ? "nothing" : bone.MappedBoneName.Value)})");
                bone.MappedBoneIndex.Value = targetIndex[name];
                bone.MappedBoneName.Value = name;
            }
            Assert.That(used.Where(b => b.MappedBoneIndex.Value == -1).Select(b => b.Name.Value), Is.Empty, "used bones left unmapped");
            TestContext.Out.WriteLine($"{SourceSkeleton}: {source.Bones.Length} bones, {TargetSkeleton}: {target.Bones.Length}; the swordsman uses {used.Count}");
            TestContext.Out.WriteLine($"mapped by hand: {(handMapped.Count == 0 ? "none used" : string.Join(", ", handMapped))}");

            // OnOkButton opens a MessageBox on a validation failure, which would hang a test run.
            Assert.That(rerig.Validate(out var validationError), Is.True, validationError);
            Assert.That(rerig.OnOkButton(), Is.True);
            Assert.That(meshes.Select(x => x.Geometry.SkeletonName), Is.All.EqualTo(TargetSkeleton));

            // Every influence by NAME: a bone humanoid01c has lands on exactly that bone, a leftover on its equivalent.
            var after = meshes.Select(Influences).ToList();
            for (var m = 0; m < meshes.Count; m++)
            {
                Assert.That(after[m].Count, Is.EqualTo(before[m].Count), $"{meshes[m].Name}: influence count changed");
                for (var i = 0; i < before[m].Count; i++)
                {
                    var oldName = sourceNames[before[m][i].Bone];
                    var expected = Equivalents.GetValueOrDefault(oldName, oldName);
                    Assert.That(targetNames.GetValueOrDefault(after[m][i].Bone), Is.EqualTo(expected),
                        $"{meshes[m].Name}: vertex {before[m][i].Vertex} was on {oldName}");
                }
                foreach (var v in meshes[m].Geometry.VertexArray)
                {
                    var w = v.BlendWeights;
                    Assert.That(w.X + w.Y + w.Z + w.W, Is.EqualTo(1f).Within(1e-3f), $"{meshes[m].Name}: weights");
                }
            }
            TestContext.Out.WriteLine($"{before.Sum(x => x.Count)} influences in {meshes.Count} meshes, {wrongWithoutRerig} on the wrong bone without the re-rig");

            // Save and parse the bytes back: the file has to say humanoid01c and keep every index, and each attachment point
            // has to name the humanoid01c bone it hangs on.
            var saveSettings = runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>();
            saveSettings.IsUserInitialized = true;
            saveSettings.DisplayDialogOnNextSave = false;
            // The save check's window is modal and needs a UI thread, so what it finds is printed and answered with Save anyway.
            commands.Create<SaveCommand>(x => x.AskToSaveAnyway = (_, problems) =>
            {
                foreach (var problem in problems.Errors)
                    TestContext.Out.WriteLine($"Save check: {problem.ErrorType} - {problem.ItemName}: {problem.Description}");
                return true;
            }).Execute();

            var saved = runner.PackFileService.FindFile(Torso, outputPack);
            Assert.That(saved, Is.Not.Null, "nothing was saved into the output pack");
            var rmv = ModelFactory.Create().Load(saved!.DataSource.ReadData());
            Assert.That(rmv.Header.SkeletonName, Is.EqualTo(TargetSkeleton));

            var savedOrder = mainNode.GetMeshesInLod(0, saveSettings.OnlySaveVisible);
            Assert.That(rmv.ModelList[0].Length, Is.EqualTo(savedOrder.Count));
            foreach (var (mesh, index) in savedOrder.Select((x, i) => (x, i)))
            {
                var savedMesh = rmv.ModelList[0][index];
                Assert.That(savedMesh.Material.ModelName, Is.EqualTo(mesh.Name));
                var mismatched = 0;
                for (var v = 0; v < mesh.Geometry.VertexArray.Length; v++)
                {
                    var memory = mesh.Geometry.VertexArray[v];
                    float[] weights = [memory.BlendWeights.X, memory.BlendWeights.Y, memory.BlendWeights.Z, memory.BlendWeights.W];
                    float[] bones = [memory.BlendIndices.X, memory.BlendIndices.Y, memory.BlendIndices.Z, memory.BlendIndices.W];
                    for (var k = 0; k < mesh.Geometry.GetWeightCount(); k++)
                    {
                        if (weights[k] > 0 && savedMesh.Mesh.VertexList[v].BoneIndex[k] != (byte)bones[k])
                            mismatched++;
                    }
                }
                Assert.That(mismatched, Is.Zero, $"{mesh.Name}: saved bone indices differ from the re-rigged mesh");

                var points = ((WeightedMaterial)savedMesh.Material).AttachmentPointParams;
                Assert.That(points, Is.Not.Empty, $"{mesh.Name}: no attachment points");
                foreach (var point in points)
                    Assert.That(targetNames.GetValueOrDefault(point.BoneIndex)?.Replace("bn_", ""), Is.EqualTo(point.Name), $"{mesh.Name}: attachment point {point.Name}");
            }
        }

        static IEnumerable<AnimatedBone> Flatten(IEnumerable<AnimatedBone> bones) =>
            bones.SelectMany(b => Flatten(b.Children).Prepend(b));

        // Every (vertex, slot) whose weight is non-zero, in vertex order.
        static List<(int Vertex, int Bone)> Influences(Rmv2MeshNode mesh)
        {
            var output = new List<(int, int)>();
            var weightCount = mesh.Geometry.GetWeightCount();
            var vertices = mesh.Geometry.VertexArray;
            for (var v = 0; v < vertices.Length; v++)
            {
                float[] weights = [vertices[v].BlendWeights.X, vertices[v].BlendWeights.Y, vertices[v].BlendWeights.Z, vertices[v].BlendWeights.W];
                float[] bones = [vertices[v].BlendIndices.X, vertices[v].BlendIndices.Y, vertices[v].BlendIndices.Z, vertices[v].BlendIndices.W];
                for (var k = 0; k < weightCount; k++)
                {
                    if (weights[k] > 0)
                        output.Add((v, (int)bones[k]));
                }
            }
            return output;
        }
    }
}
