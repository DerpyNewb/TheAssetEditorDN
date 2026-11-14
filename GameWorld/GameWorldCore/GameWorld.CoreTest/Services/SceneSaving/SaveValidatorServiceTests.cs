using System.IO;
using System.Text;
using GameWorld.Core.Animation;
using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Lod;
using GameWorld.Core.Test.TestUtility;
using Microsoft.Xna.Framework;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;

namespace GameWorld.Core.Test.Services.SceneSaving
{
    // The checks Kitbash runs before a save. Each test breaks one thing and looks for the line naming it.
    internal class SaveValidatorServiceTests
    {
        PackFileService _pfs = null!;
        IPackFileContainer _pack = null!;

        [SetUp]
        public void Setup()
        {
            _pfs = new PackFileService(null) { EnforceGameFilesMustBeLoaded = false };
            _pack = _pfs.CreateNewPackFileContainer("output", PackFileVersion.PFH5, PackFileCAType.MOD, true);
        }

        [Test]
        public void AMeshWithTwoBonesPerVertex_AddingUpTo1_PassesTheWeightChecks()
        {
            var problems = Validate(Model(Mesh("body")));

            Assert.That(problems.Errors, Is.Empty);
        }

        [Test]
        public void VerticesWithNoWeight_AreAnError_NamingTheMeshAndCount()
        {
            var mesh = Mesh("body");
            mesh.Geometry.VertexArray[1].BlendWeights = Vector4.Zero;

            var problems = Validate(Model(mesh));

            var problem = problems.Errors.Single();
            Assert.That(problem.IsError, Is.True);
            Assert.That(problem.ItemName, Is.EqualTo("body"));
            Assert.That(problem.Description, Does.StartWith("1 of 3 vertices have no bone weights"));
        }

        [Test]
        public void WeightsThatDoNotAddUpTo1_AreAWarning_CountingOnlyTheWeightsTheFormatKeeps()
        {
            var mesh = Mesh("body");
            mesh.Geometry.VertexArray[0].BlendWeights = new Vector4(0.25f, 0.25f, 0, 0);
            mesh.Geometry.VertexArray[1].BlendWeights = new Vector4(0.6f, 0, 0, 0);            // CA's own shape: the second weight is what the first leaves
            mesh.Geometry.VertexArray[2].BlendWeights = new Vector4(0.5f, 0.25f, 0.25f, 0);     // a weighted mesh keeps two; the third is dropped

            var problems = Validate(Model(mesh));

            var problem = problems.Errors.Single();
            Assert.That(problem.IsWarning, Is.True);
            Assert.That(problem.ItemName, Is.EqualTo("body"));
            Assert.That(problem.Description, Does.StartWith("2 of 3 vertices have bone weights that don't add up to 1"));
        }

        [Test]
        public void FourWeights_MayMissBy3_255ths_AsCasDo_ButNoMore()
        {
            var mesh = Mesh("body");
            mesh.Geometry.ChangeVertexType(UiVertexFormat.Cinematic, false);
            mesh.Geometry.VertexArray[0].BlendWeights = new Vector4(82, 79, 65, 26) / 255f;     // 252 of 255, from Karl Franz's body
            mesh.Geometry.VertexArray[1].BlendWeights = new Vector4(0.5f, 0.2f, 0.1f, 0.1f);
            mesh.Geometry.VertexArray[2].BlendWeights = new Vector4(0.25f, 0.25f, 0.25f, 0.25f);

            var problems = Validate(Model(mesh));

            Assert.That(problems.Errors.Single().Description, Does.StartWith("1 of 3 vertices have bone weights that don't add up to 1"));
        }

        [Test]
        public void ATextureNotInAnyPack_IsAWarning_NamingItAndItsMeshes()
        {
            var a = Mesh("body", baseColour: @"tex\missing_base_colour.dds");
            var b = Mesh("head", baseColour: @"tex\missing_base_colour.dds");

            var problems = Validate(Model(a, b));

            var problem = problems.Errors.Single(x => x.ItemName == @"tex\missing_base_colour.dds");
            Assert.That(problem.IsWarning, Is.True);
            Assert.That(problem.Description, Does.StartWith("This texture is not in any loaded pack").And.EndWith("Used by body, head."));
        }

        [TestCase("DXT1", 0u, "legacy D3DFMT_DXT1")]
        [TestCase("DX10", 71u, "BC1_UNORM")]              // BC1_UNORM
        [TestCase("DX10", 98u, "BC7_UNORM")]              // BC7_UNORM
        public void ABaseColourWithNoSrgbFlag_IsAWarning(string fourCc, uint dxgiFormat, string formatName)
        {
            AddTexture(@"tex\body_base_colour.dds", Dds(fourCc, dxgiFormat));

            var problems = Validate(Model(Mesh("body", baseColour: @"tex\body_base_colour.dds")));

            var problem = problems.Errors.Single();
            Assert.That(problem.ItemName, Is.EqualTo(@"tex\body_base_colour.dds"));
            Assert.That(problem.Description, Does.StartWith($"This base colour is {formatName}, which has no sRGB flag"));
        }

        [TestCase(72u)]     // BC1_UNORM_SRGB
        [TestCase(99u)]     // BC7_UNORM_SRGB
        public void ABaseColourWithAnSrgbFlag_PassesTheCheck(uint dxgiFormat)
        {
            AddTexture(@"tex\body_base_colour.dds", Dds("DX10", dxgiFormat));

            var problems = Validate(Model(Mesh("body", baseColour: @"tex\body_base_colour.dds")));

            Assert.That(problems.Errors, Is.Empty);
        }

        [Test]
        public void ATextureThatIsNotABaseColour_NeedsNoSrgbFlag()
        {
            AddTexture(@"tex\body_mask.dds", Dds("DXT1"));
            var mesh = Mesh("body");
            mesh.Material.GetCapability<MetalRoughCapability>().Mask.TexturePath = @"tex\body_mask.dds";

            var problems = Validate(Model(mesh));

            Assert.That(problems.Errors, Is.Empty);
        }

        [Test]
        public void ProblemsInLodsPast0_AreChecked_OnlyWhenTheSaveKeepsThoseLods()
        {
            var lod1 = Mesh("body");
            lod1.Geometry.VertexArray[0].BlendWeights = Vector4.Zero;
            var model = Model(Mesh("body"));
            model.AddObject(new Rmv2LodNode("lod1", 1)).AddObject(lod1);

            var kept = Validate(model, LodStrategy.NoLodGeneration);
            var rebuilt = Validate(model, LodStrategy.MeshOptimizer);

            Assert.That(kept.Errors.Single().ItemName, Is.EqualTo("body (LOD 1)"));
            Assert.That(rebuilt.Errors, Is.Empty, "the save rebuilds LOD 1 from LOD 0");
        }

        [Test]
        public void AHiddenMesh_IsChecked_OnlyWhenHiddenMeshesAreSaved()
        {
            var hidden = Mesh("cape");
            hidden.Geometry.VertexArray[0].BlendWeights = Vector4.Zero;
            hidden.IsVisible = false;
            var model = Model(Mesh("body"), hidden);

            Assert.That(Validate(model, onlySaveVisible: true).Errors, Is.Empty);
            Assert.That(Validate(model, onlySaveVisible: false).Errors.Where(x => x.IsError).Single().ItemName, Is.EqualTo("cape"));
        }

        [Test]
        public void MeshesWithDifferentPivots_AreAWarning()
        {
            var a = Mesh("body");
            var b = Mesh("head");
            b.PivotPoint = new Vector3(0, 1, 0);

            var problems = Validate(Model(a, b));

            Assert.That(problems.Errors.Select(x => x.Description), Has.One.StartWith("Model contains several different pivot points"));
        }

        ErrorList Validate(MainEditableNode model, LodStrategy lods = LodStrategy.MeshOptimizer, bool onlySaveVisible = true)
        {
            var settings = new GeometrySaveSettings(new ApplicationSettingsService(GameTypeEnum.Warhammer3)) { LodGenerationMethod = lods, OnlySaveVisible = onlySaveVisible };
            return new SaveValidatorService(_pfs).Validate(model, settings);
        }

        MainEditableNode Model(params Rmv2MeshNode[] meshes)
        {
            // The skeleton the meshes name, so the skeleton checks pass.
            var skeleton = new AnimationFile();
            skeleton.Header.SkeletonName = "test_skeleton";
            skeleton.Bones = [new AnimationFile.BoneInfo { Id = 0, Name = "root", ParentId = -1 }, new AnimationFile.BoneInfo { Id = 1, Name = "spine", ParentId = 0 }];
            var frame = new AnimationFile.Frame();
            for (var i = 0; i < 2; i++)
            {
                frame.Transforms.Add(new RmvVector3(0, i, 0));
                frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            skeleton.AnimationParts.Add(part);

            var model = new MainEditableNode("model", new SkeletonNode(new GameSkeleton(skeleton, null!)), _pfs);
            var lod0 = model.AddObject(new Rmv2LodNode("lod0", 0));
            foreach (var mesh in meshes)
                lod0.AddObject(mesh);
            return model;
        }

        static Rmv2MeshNode Mesh(string name, string baseColour = "")
        {
            var geometry = MeshTestHelper.CreateTriangleMesh();
            geometry.ChangeVertexType(UiVertexFormat.Weighted, false);
            for (var i = 0; i < geometry.VertexArray.Length; i++)
            {
                geometry.VertexArray[i].BlendIndices = new Vector4(0, 1, 0, 0);
                geometry.VertexArray[i].BlendWeights = new Vector4(0.5f, 0.5f, 0, 0);
            }

            var material = new CapabilityMaterialFactory(new ApplicationSettingsService(GameTypeEnum.Warhammer3), null).CreateMaterial(CapabilityMaterialsEnum.MetalRoughPbr_Default);
            material.GetCapability<MetalRoughCapability>().BaseColour.TexturePath = baseColour;
            return new Rmv2MeshNode(geometry, new WeightedMaterial { ModelName = name }, material, null!);
        }

        void AddTexture(string path, byte[] bytes) =>
            _pfs.AddFilesToPack(_pack, [new NewPackFileEntry(Path.GetDirectoryName(path)!, PackFile.CreateFromBytes(Path.GetFileName(path), bytes))]);

        // A 4x4 DDS: the legacy header names a FourCC, a DX10 one a DXGI format.
        static byte[] Dds(string fourCc, uint dxgiFormat = 0)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("DDS "));
            foreach (var value in new uint[] { 124, 0x81007, 4, 4, 8, 0, 1 })    // size, flags, height, width, linear size, depth, mips
                writer.Write(value);
            writer.Write(new byte[44]);
            writer.Write(32u);
            writer.Write(4u);                                                     // DDPF_FOURCC
            writer.Write(Encoding.ASCII.GetBytes(fourCc));
            writer.Write(new byte[20]);
            writer.Write(0x1000u);                                                // DDSCAPS_TEXTURE
            writer.Write(new byte[16]);
            if (fourCc == "DX10")
            {
                foreach (var value in new uint[] { dxgiFormat, 3, 0, 1, 0 })     // format, TEXTURE2D, misc, array size, misc2
                    writer.Write(value);
            }
            writer.Write(new byte[16]);
            return stream.ToArray();
        }
    }
}
