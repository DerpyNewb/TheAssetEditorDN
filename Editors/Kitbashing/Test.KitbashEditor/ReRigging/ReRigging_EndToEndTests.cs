using Editors.KitbasherEditor.ChildEditors.ReRiggingTool;
using Editors.KitbasherEditor.ChildEditors.SaveDialog;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.UiCommands;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Services.SceneSaving;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.GameFormats.RigidModel;
using Shared.Ui.Editors.BoneMapping;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.ReRigging
{
    // The re-rigging tool as the GUI drives it - import as reference, Make Editable, auto map by
    // name, OK, save - minus the window. The window only builds the mapping; the work is static
    // helpers plus RemapBoneIndexesCommand, which is what an IPC action would call.
    public class ReRigging_EndToEndTests
    {
        const string GeneralBodyPath = @"variantmeshes\wh_variantmodels\hu1e\cth\cth_celestial_general\cth_celestial_general_body_02.rigid_model_v2";
        const string SourceSkeleton = "humanoid01e";
        const string TargetSkeleton = "humanoid01";

        [Test]
        public void Warhammer3_ReRigGeneralOntoKarl_AutoMapByName_EveryInfluenceKeepsItsBone()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPack = runner.LoadPackFile(TestFiles.KarlPackFile, true);

            var karl = runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(karl!);

            var commands = runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>();
            commands.Create<ImportReferenceMeshCommand>(x => x.Configure(GeneralBodyPath)).Execute();

            var sceneManager = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>();
            var referenceGroup = sceneManager.GetNodeByName<GroupNode>(SpecialNodes.ReferenceMeshs)!;
            var mainNode = sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            SceneNodeHelper.MakeNodeEditable(mainNode, referenceGroup.Children.Last());

            var meshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(mainNode)
                .Where(x => x.Geometry.SkeletonName == SourceSkeleton)
                .ToList();
            Assert.That(meshes, Is.Not.Empty, "the general's meshes never reached the editable model");

            var rootScene = runner.GetRequiredServiceInCurrentEditorScope<KitbasherRootScene>();
            Assert.That(rootScene.Skeleton.SkeletonName, Is.EqualTo(TargetSkeleton));

            var skeletons = runner.GetRequiredServiceInCurrentEditorScope<ISkeletonAnimationLookUpHelper>();
            var source = skeletons.GetSkeletonFileFromName(SourceSkeleton)!;
            var target = skeletons.GetSkeletonFileFromName(TargetSkeleton)!;
            var sourceNames = source.Bones.ToDictionary(b => b.Id, b => b.Name);
            var targetNames = target.Bones.ToDictionary(b => b.Id, b => b.Name);

            var before = meshes.Select(Influences).ToList();

            // Without this the test proves nothing: if the two skeletons agreed on every index the
            // mesh uses, a re-rig that did nothing at all would pass every check below.
            var wrongWithoutRerig = before.SelectMany(x => x)
                .Count(i => targetNames.GetValueOrDefault(i.Bone) != sourceNames[i.Bone]);
            Assert.That(wrongWithoutRerig, Is.GreaterThan(0), "the skeletons agree on every used index");

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

            // Auto map by name cannot place a bone the target lacks (humanoid01e's lower cape and
            // skirt), and the GUI leaves those to the user. Do what a modder does by hand: hand each
            // to its nearest ancestor that did get mapped, so the skirt follows the hips.
            var sourceParent = source.Bones.ToDictionary(b => b.Id, b => b.ParentId);
            var meshBoneById = Flatten(config.MeshBones).ToDictionary(b => b.BoneIndex.Value);
            var unmapped = meshBoneById.Values
                .Where(b => b.IsUsedByCurrentModel.Value && b.MappedBoneIndex.Value == -1)
                .ToList();
            foreach (var bone in unmapped)
            {
                var ancestor = sourceParent[bone.BoneIndex.Value];
                while (ancestor != -1 && meshBoneById[ancestor].MappedBoneIndex.Value == -1)
                    ancestor = sourceParent[ancestor];
                Assert.That(ancestor, Is.Not.EqualTo(-1), $"{bone.Name.Value} has no mapped ancestor");
                bone.MappedBoneIndex.Value = meshBoneById[ancestor].MappedBoneIndex.Value;
                bone.MappedBoneName.Value = meshBoneById[ancestor].MappedBoneName.Value;
            }
            TestContext.Out.WriteLine($"{SourceSkeleton}: {source.Bones.Length} bones, {TargetSkeleton}: {target.Bones.Length} bones");
            var unmappedIds = unmapped.Select(b => b.BoneIndex.Value).ToHashSet();
            TestContext.Out.WriteLine($"left unmapped by name:{string.Join(", ", unmapped.Select(b => $"{b.Name.Value}({b.BoneIndex.Value})"))}");

            // OnOkButton opens a MessageBox on a validation failure, which would hang a test run.
            Assert.That(rerig.Validate(out var validationError), Is.True, validationError);
            Assert.That(rerig.OnOkButton(), Is.True);

            Assert.That(meshes.Select(x => x.Geometry.SkeletonName), Is.All.EqualTo(TargetSkeleton));

            var after = meshes.Select(Influences).ToList();
            var targetHasName = targetNames.Values.ToHashSet();
            var renamed = new SortedSet<string>();
            for (var m = 0; m < meshes.Count; m++)
            {
                Assert.That(after[m].Count, Is.EqualTo(before[m].Count), $"{meshes[m].Name}: influence count changed");
                for (var i = 0; i < before[m].Count; i++)
                {
                    var oldName = sourceNames[before[m][i].Bone];
                    Assert.That(targetNames.TryGetValue(after[m][i].Bone, out var newName), Is.True,
                        $"{meshes[m].Name}: index {after[m][i].Bone} is not a {TargetSkeleton} bone");

                    // A bone the target has by its exact name must land on exactly that bone. One
                    // handed to an ancestor above must land on a bone up its own chain, never on an
                    // unrelated one. Anything else went through the automapper's rename rules.
                    if (targetHasName.Contains(oldName))
                        Assert.That(newName, Is.EqualTo(oldName), $"{meshes[m].Name}: vertex {before[m][i].Vertex}");
                    else if (unmappedIds.Contains(before[m][i].Bone))
                    {
                        Assert.That(AncestorNames(before[m][i].Bone, sourceParent, sourceNames), Does.Contain(newName),
                            $"{meshes[m].Name}: vertex {before[m][i].Vertex} moved from {oldName} to {newName}, which is not above it");
                        renamed.Add($"{oldName} -> {newName}");
                    }
                    else
                        renamed.Add($"{oldName} -> {newName} (rename rule)");
                }

                foreach (var v in meshes[m].Geometry.VertexArray)
                {
                    var w = v.BlendWeights;
                    Assert.That(w.X + w.Y + w.Z + w.W, Is.EqualTo(1f).Within(1e-3f), $"{meshes[m].Name}: weights");
                }
            }
            TestContext.Out.WriteLine($"{before.Sum(x => x.Count)} influences in {meshes.Count} meshes, {wrongWithoutRerig} wrong without the re-rig");
            TestContext.Out.WriteLine($"moved to another name: {string.Join(", ", renamed)}");

            // Save and parse the bytes back: the indices have to survive serialisation, not just
            // sit right in memory.
            var saveSettings = runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>();
            saveSettings.IsUserInitialized = true;
            saveSettings.DisplayDialogOnNextSave = false;
            commands.Create<SaveCommand>().Execute();

            var saved = runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl, outputPack);
            Assert.That(saved, Is.Not.Null);
            var rmv = ModelFactory.Create().Load(saved!.DataSource.ReadData());

            // Mesh names repeat, so find each one by position in the list the saver writes from.
            var savedOrder = mainNode.GetMeshesInLod(0, saveSettings.OnlySaveVisible);
            Assert.That(rmv.ModelList[0].Length, Is.EqualTo(savedOrder.Count));
            foreach (var mesh in meshes)
            {
                var index = savedOrder.IndexOf(mesh);
                Assert.That(index, Is.Not.EqualTo(-1), $"{mesh.Name} was not saved");
                var savedMesh = rmv.ModelList[0][index];
                Assert.That(savedMesh.Material.ModelName, Is.EqualTo(mesh.Name));
                Assert.That(savedMesh.Mesh.VertexList.Length, Is.EqualTo(mesh.Geometry.VertexArray.Length), $"{mesh.Name}: vertex count");

                // Slot by slot, keyed on the in-memory weight: a tiny weight can quantise to zero
                // on save, but the bone index beside it must not change.
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
            }
        }

        static HashSet<string> AncestorNames(int bone, Dictionary<int, int> parent, Dictionary<int, string> names)
        {
            var output = new HashSet<string>();
            for (var b = parent[bone]; b != -1; b = parent[b])
                output.Add(names[b]);
            return output;
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
