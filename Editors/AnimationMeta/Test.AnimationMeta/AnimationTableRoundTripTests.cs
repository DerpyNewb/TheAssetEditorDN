using System.IO;
using System.Xml.Serialization;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Microsoft.Extensions.DependencyInjection;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Test.TestingUtility.Shared;
using Wh3Entry = Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3.AnimationBinEntry;

namespace Test.AnimationMeta
{
    // Every battle animation table CA ships, through the two paths the AnimPack editor gives them: the binary one (open the
    // animpack, save it) and the text one (the XML a .bin is shown and edited as). Anything either path changes is a table the
    // editor cannot hand back to the game as it was. Reads anim.pack from WH3_DATA; skipped without it.
    public class AnimationTableRoundTripTests
    {
        static readonly string[] AnimPacks =
        [
            @"animations\database\battle\bin\animation_tables.animpack",
            @"animations\database\battle\bin\animation_tables__.animpack",
            @"animations\database\battle\bin\animation_tables__3.animpack",
        ];

        // The converter's two halves without Validate, which looks every file up and opens a dialog on any finding.
        sealed class TextPath() : AnimationBinWh3FileToXmlConverter(null!, null!, null!)
        {
            public byte[] FromText(string text) => ConvertXmlToBinary((XmlFormat)new XmlSerializer(typeof(XmlFormat)).Deserialize(new StringReader(text))!, "");
        }

        [Test]
        public void Warhammer3_EveryCaAnimationTable_ComesBackByteForByte()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(data != null && Directory.Exists(data), "set WH3_DATA to the game's data folder");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            runner.PackFileService.AddContainer(loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data!, "anim.pack"), true));

            var text = new TextPath();
            var losses = new Dictionary<string, List<string>>();
            void Lost(string kind, string what) { if (!losses.TryGetValue(kind, out var l)) losses[kind] = l = []; l.Add(what); }
            var tables = 0;

            foreach (var path in AnimPacks)
            {
                var packFile = runner.PackFileService.FindFile(path);
                Assert.That(packFile, Is.Not.Null, path);
                var original = packFile!.DataSource.ReadData();
                var originalEntries = Entries(original);
                var animPack = AnimationPackSerializer.Load(packFile, runner.PackFileService);
                var files = animPack.Files.ToList();
                Assert.That(files.Select(x => x.FileName), Is.EqualTo(originalEntries.Select(x => x.Name)), $"{path}: entries out of order or missing");

                for (var i = 0; i < files.Count; i++)
                {
                    var (name, bytes) = originalEntries[i];
                    var file = files[i];
                    if (file is not AnimationBinWh3 table)
                    {
                        if (!file.ToByteArray().AsSpan().SequenceEqual(bytes))
                            Lost($"{file.GetType().Name} saved differently", name);
                        if (name.EndsWith(".bin") && !name.Contains("matched_combat") && name.Contains("/battle/bin/"))
                            Lost("battle .bin not read as a table", name);
                        continue;
                    }

                    tables++;
                    if (!table.ToByteArray().AsSpan().SequenceEqual(bytes))
                        Lost("binary round trip", name);
                    if (!text.FromText(text.GetText(bytes)).AsSpan().SequenceEqual(bytes))
                        Lost("text round trip", name);
                }

                if (!AnimationPackSerializer.ConvertToBytes(animPack).AsSpan().SequenceEqual(original))
                    Lost("animpack rewrite", path);
            }

            TestContext.Out.WriteLine($"{tables} tables in {AnimPacks.Length} animpacks");
            foreach (var (kind, names) in losses)
                TestContext.Out.WriteLine($"{kind}: {names.Count}, e.g. {string.Join(", ", names.Take(5))}");
            Assert.That(tables, Is.GreaterThan(2500), "anim.pack did not load the tables"); // 2830 in 9.0
            Assert.That(losses.Keys, Is.Empty);
        }

        // A slot id past the end of 9.0's list, as a table from a later patch would hold, shows as "Not found [id=N]" and saves back as N.
        [Test]
        public void ASlotTheListDoesNotName_SavesBackAsItsId()
        {
            var table = new AnimationBinWh3("x.bin") { Name = "x", SkeletonName = "humanoid01" };
            table.AnimationTableEntries.Add(new Wh3Entry { AnimationId = 9000, AnimationRefs = [new() { AnimationFile = "a.anim", AnimationMetaFile = "", AnimationSoundMetaFile = "" }] });
            table.AnimationTableEntries.Add(new Wh3Entry { AnimationId = 1, AnimationRefs = [new() { AnimationFile = "b.anim", AnimationMetaFile = "", AnimationSoundMetaFile = "" }] });
            var bytes = table.ToByteArray();

            var text = new TextPath();
            var xml = text.GetText(bytes);
            Assert.That(xml, Does.Contain("Not found [id=9000]"));
            Assert.That(text.FromText(xml), Is.EqualTo(bytes));
        }

        // An entry the serializer cannot read is kept as its own bytes, so saving the animpack gives back what was opened.
        [Test]
        public void AnEntryThatDoesNotParse_SavesBackUnchanged()
        {
            static byte[] Entry(string name, byte[] data) => [.. ByteParsers.String.WriteCaString(name), .. BitConverter.GetBytes(data.Length), .. data];
            var good = new AnimationBinWh3("animations/database/battle/bin/good.bin") { Name = "good", SkeletonName = "humanoid01" }.ToByteArray();
            byte[] original = [.. BitConverter.GetBytes(2), .. Entry("animations/database/battle/bin/broken.bin", [4, 0, 0, 0, 9]), .. Entry("animations/database/battle/bin/good.bin", good)];

            var runner = new AssetEditorTestRunner();
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry(@"animations\database\battle\bin", PackFile.CreateFromBytes("test.animpack", original))]);
            var animPack = AnimationPackSerializer.Load(runner.PackFileService.FindFile(@"animations\database\battle\bin\test.animpack", pack)!, runner.PackFileService);

            Assert.That(animPack.Files.First().IsUnknownFile, Is.True, "the broken entry should load as unknown");
            Assert.That(AnimationPackSerializer.ConvertToBytes(animPack), Is.EqualTo(original));
        }

        // Each entry's own bytes, cut from the animpack by its header, independent of the serializer under test.
        static List<(string Name, byte[] Bytes)> Entries(byte[] animPack)
        {
            var chunk = new ByteChunk(animPack);
            var count = chunk.ReadInt32();
            var output = new List<(string, byte[])>(count);
            for (var i = 0; i < count; i++)
            {
                var name = chunk.ReadString();
                var size = chunk.ReadInt32();
                output.Add((name, chunk.ReadBytes(size)));
            }
            return output;
        }
    }
}
