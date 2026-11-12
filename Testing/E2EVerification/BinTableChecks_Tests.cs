using Editors.AnimationFragmentEditor.AnimationPack.BinGrid;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using GameWorld.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.E2EVerification
{
    // The checks a WH3 table gets: once in the XML view's Apply, which shows them in a window, and now in a class the bin grid shares.
    // TodaysFindings pins what the XML route reported before the checks moved, one fixture per check; ChecksFor holds the class to
    // the same lists, less the two exceptions the design allows (an empty meta or sound path is not a missing file, and a slot
    // string that names no slot is reported alone).
    internal class BinTableChecks_Tests
    {
        const string Bin = "animations/database/battle/bin/";
        const string Graph = "animations/locomotion_graphs/derpy_a.xml";
        const string KarlAnim = "animations/battle/humanoid01/2handed_hammer/stand/hu1_2hh_stand_idle_01.anim";
        const string GoodMeta = "animations/derpy/good.anm.meta";
        const string GoodSound = "animations/derpy/good.snd.meta";

        sealed class Capture(ISkeletonAnimationLookUpHelper skeletons, MetaDataFileParser metas, PackFile animPack) : AnimationBinWh3FileToXmlConverter(skeletons, metas, animPack)
        {
            public List<ErrorListDataItem> Shown { get; } = [];
            protected override void ShowFindings(ErrorList errors) => Shown.AddRange(errors.Errors);
        }

        sealed record Setup(AssetEditorTestRunner Runner, PackFile AnimPack)
        {
            public IPackFileService Pfs => Runner.PackFileService;
            public ISkeletonAnimationLookUpHelper Skeletons => Runner.ServiceProvider.GetRequiredService<ISkeletonAnimationLookUpHelper>();
            public MetaDataFileParser Metas => Runner.ServiceProvider.GetRequiredService<MetaDataFileParser>();
            public Capture Converter() => new(Skeletons, Metas, AnimPack);
            public BinTableChecks Checks() => new(Pfs, Skeletons, Metas, AnimPack);
        }

        // Karl's pack for humanoid01 and one real animation, plus a mount table to ride, a graph, and metas that are and are not.
        static Setup Load()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var pack = runner.CreateOutputPack();
            var mount = Table("derpy_mount");
            mount.AnimationTableEntries.Add(Row("ATTACK_1", ("animations/derpy/mount_missing.anim", "", "")));
            var animPack = new AnimationPackFileDatabase("derpy_test.animpack");
            animPack.AddFile(mount);
            byte[] meta = [2, 0, 0, 0, 0, 0, 0, 0];
            byte[] notMeta = [9, 0, 0, 0];
            runner.PackFileService.AddFilesToPack(pack, [
                new NewPackFileEntry(@"animations\database\battle\bin", PackFile.CreateFromBytes("derpy_test.animpack", AnimationPackSerializer.ConvertToBytes(animPack))),
                new NewPackFileEntry(@"animations\locomotion_graphs", PackFile.CreateFromBytes("derpy_a.xml", [0])),
                new NewPackFileEntry(@"animations\derpy", PackFile.CreateFromBytes("good.anm.meta", meta)),
                new NewPackFileEntry(@"animations\derpy", PackFile.CreateFromBytes("good.snd.meta", meta)),
                new NewPackFileEntry(@"animations\derpy", PackFile.CreateFromBytes("bad.xml", notMeta)),
                new NewPackFileEntry(@"animations\derpy", PackFile.CreateFromBytes("bad.anm.meta", notMeta)),
                new NewPackFileEntry(@"animations\derpy", PackFile.CreateFromBytes("bad.snd.meta", notMeta))]);
            return new(runner, runner.PackFileService.FindFile(Bin + "derpy_test.animpack")!);
        }

        static AnimationBinWh3 Table(string name) => new(Bin + name + ".bin") { Name = name, SkeletonName = "humanoid01", LocomotionGraph = Graph };

        static AnimationBinEntry Row(string slot, params (string Anim, string Meta, string Sound)[] refs) => new()
        {
            AnimationId = (uint)AnimationSlotTypeHelperWh3.GetfromValue(slot).Id,
            BlendIn = 0.3f,
            SelectionWeight = 1f,
            WeaponBools = 63,
            AnimationRefs = refs.Select(x => new AnimationBinEntry.AnimationRef { AnimationFile = x.Anim, AnimationMetaFile = x.Meta, AnimationSoundMetaFile = x.Sound }).ToList(),
        };

        static AnimationBinWh3 With(AnimationBinWh3 table, Action<AnimationBinWh3> change)
        {
            change(table);
            return table;
        }

        static readonly Dictionary<string, Func<AnimationBinWh3>> Fixtures = new()
        {
            ["name_mismatch"] = () => With(Table("fx_name"), x => x.Name = "other"),
            ["bad_skeleton"] = () => With(Table("fx_skeleton"), x => x.SkeletonName = "nope01"),
            ["missing_graph"] = () => With(Table("fx_graph"), x => x.LocomotionGraph = "animations/locomotion_graphs/derpy_missing.xml"),
            ["no_graph"] = () => With(Table("fx_no_graph"), x => x.LocomotionGraph = ""),
            ["missing_files"] = () => With(Table("fx_missing"), x => x.AnimationTableEntries.Add(Row("STAND", ("animations/nope.anim", "animations/nope.anm.meta", "animations/nope.snd.meta")))),
            ["empty_paths"] = () => With(Table("fx_empty_paths"), x => x.AnimationTableEntries.Add(Row("STAND", (KarlAnim, "", "")))),
            ["empty_slot"] = () => With(Table("fx_empty_slot"), x => x.AnimationTableEntries.Add(Row("STAND"))),
            ["bad_headers"] = () => With(Table("fx_bad"), x => x.AnimationTableEntries.Add(Row("STAND", ("animations/derpy/bad.xml", "animations/derpy/bad.anm.meta", "animations/derpy/bad.snd.meta")))),
            ["good_files"] = () => With(Table("fx_good"), x => x.AnimationTableEntries.Add(Row("STAND", (KarlAnim, GoodMeta, GoodSound)))),
            ["rider"] = () => With(Table("fx_rider"), x => { x.MountBin = "derpy_mount"; x.AnimationTableEntries.Add(Row("RIDER_ATTACK_1", (KarlAnim, GoodMeta, GoodSound))); }),
        };

        static IEnumerable<string> Names => Fixtures.Keys;

        // What the XML route reported for each fixture before the checks moved, read off a run against 2853f125.
        static readonly Dictionary<string, (string Where, string Message, bool IsError)[]> Today = new()
        {
            ["name_mismatch"] = [("Name", "The name of the bin file has to be the same as the provided name. fx_name vs other", true)],
            ["bad_skeleton"] = [("Skeleton", "Skeleton nope01 is not found", true)],
            ["missing_graph"] = [("LocomotionGraph", "LocomotionGraph animations/locomotion_graphs/derpy_missing.xml is not found", true)],
            ["no_graph"] = [("LocomotionGraph", "LocomotionGraph not provided", false)],
            ["missing_files"] =
            [
                ("STAND", "Animation file animations/nope.anim is not found", false),
                ("STAND", "Meta file animations/nope.anm.meta is not found", false),
                ("STAND", "Sound file animations/nope.snd.meta is not found", false),
            ],
            ["empty_paths"] = [("STAND", "Meta file  is not found", false), ("STAND", "Sound file  is not found", false)],
            ["empty_slot"] = [("STAND", "Slot does not have any animations", false)],
            ["bad_headers"] =
            [
                ("STAND", "Animation file animations/derpy/bad.xml does not appears to be a valid animation file", false),
                ("STAND", "Meta file animations/derpy/bad.anm.meta does not appear to be a valid meta animation", false),
                ("STAND", "Sound file animations/derpy/bad.snd.meta does not appear to be a valid meta sound", false),
            ],
            ["good_files"] = [],
            ["rider"] = [("RIDER_ATTACK_1", "Could not locate animations/derpy/mount_missing.anim while trying to validate CheckForRiderAndHisMountAnimationsVersion", false)],
        };

        // Today's list less the design's one change to it: an empty meta or sound path is not a missing file.
        static IEnumerable<(string, string, bool)> Expected(string name) =>
            Today[name].Where(x => x.Message is not ("Meta file  is not found" or "Sound file  is not found")).Select(x => (x.Where, x.Message, x.IsError));

        static List<(string, string, bool)> Sorted(IEnumerable<(string, string, bool)> findings) => findings.OrderBy(x => x.Item1).ThenBy(x => x.Item2).ToList();

        [TestCaseSource(nameof(Names))]
        public void XmlRouteFindings_ForEachFixture(string name)
        {
            var setup = Load();
            var table = Fixtures[name]();
            var converter = setup.Converter();
            converter.ToBytes(converter.GetText(table.ToByteArray()), table.FileName, setup.Pfs, out _);
            Assert.That(Sorted(converter.Shown.Select(x => (x.ItemName, x.Description, x.IsError))), Is.EqualTo(Sorted(Expected(name))));
        }

        [TestCaseSource(nameof(Names))]
        public void ChecksFor_EachFixture(string name)
        {
            var setup = Load();
            var table = Fixtures[name]();
            var checks = setup.Checks();
            var found = checks.Header(table, table.FileName).Concat(checks.Files(table)).Concat(checks.Deep(table)).Select(x => (x.Where, x.Message, x.IsError));
            Assert.That(Sorted(found), Is.EqualTo(Sorted(Expected(name))));
        }

        [Test]
        public void AnEmptySkeleton_IsAnError()
        {
            var setup = Load();
            var table = With(Table("fx_no_skeleton"), x => x.SkeletonName = "");
            Assert.That(setup.Checks().Header(table, table.FileName).Select(x => (x.Where, x.Message, x.IsError)),
                Is.EqualTo(new[] { ("Skeleton", "Missing skeleton item on root", true) }));
        }

        [Test]
        public void AnInvalidSlot_ReportsOnlyThatError()
        {
            var setup = Load();
            var table = Fixtures["missing_files"]();
            var converter = setup.Converter();
            var text = converter.GetText(table.ToByteArray()).Replace("Slot=\"STAND\"", "Slot=\"NOT_A_SLOT\"");
            var bytes = converter.ToBytes(text, table.FileName, setup.Pfs, out var error);
            Assert.That((bytes, error != null), Is.EqualTo(((byte[]?)null, true)));
            Assert.That(converter.Shown.Select(x => (x.ItemName, x.Description, x.IsError)), Is.EqualTo(new[] { ("NOT_A_SLOT", "Not a valid animation slot for game", true) }));
        }
    }
}
