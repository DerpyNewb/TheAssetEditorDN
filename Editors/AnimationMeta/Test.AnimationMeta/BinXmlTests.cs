using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Wh3Entry = Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3.AnimationBinEntry;

namespace Test.AnimationMeta
{
    // One mapping between a table row and the <Animation> element the XML view shows. The converter and the bin grid's copy and
    // paste both go through it, so a row copied from one pastes into the other.
    public class BinXmlTests
    {
        static readonly BaseAnimationSlotHelper Slots = AnimationSlotTypeHelperWh3.GetInstance();

        static Wh3Entry Row(uint id, int weapons, bool unk, params string[] anims) => new()
        {
            AnimationId = id,
            BlendIn = 0.25f,
            SelectionWeight = 2f,
            WeaponBools = weapons,
            Unk = unk,
            AnimationRefs = anims.Select(x => new Wh3Entry.AnimationRef { AnimationFile = x, AnimationMetaFile = x + ".anm.meta", AnimationSoundMetaFile = "" }).ToList(),
        };

        // Two variants on one slot, a weapon set that is not all or nothing, and a slot id past the end of 9.0's list.
        static AnimationBinWh3 Table()
        {
            var table = new AnimationBinWh3("animations/database/battle/bin/x.bin") { Name = "x", SkeletonName = "humanoid01" };
            table.AnimationTableEntries.AddRange([Row(1, 63, true, "a.anim", "b.anim"), Row(42, 40, false, "c.anim"), Row(9000, 0, false, "d.anim")]);
            return table;
        }

        [Test]
        public void FromEntryThenToEntry_GivesTheSameBytes()
        {
            var table = Table();
            var before = table.ToByteArray();
            table.AnimationTableEntries = table.AnimationTableEntries.Select(x => BinXml.ToEntry(BinXml.FromEntry(x, Slots), Slots)).ToList();
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [Test]
        public void ToTextThenFromText_GivesTheSameRows()
        {
            var table = Table();
            var before = table.ToByteArray();
            table.AnimationTableEntries = BinXml.FromText(BinXml.ToText(table.AnimationTableEntries))!;
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [Test]
        public void FromText_OfTheXmlViewsOwnText_FindsItsAnimations()
        {
            var table = Table();
            var before = table.ToByteArray();
            var text = new AnimationBinWh3FileToXmlConverter(null!, null!, null!).GetText(before);
            table.AnimationTableEntries = BinXml.FromText(text)!;
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [TestCase("hello")]
        [TestCase("")]
        [TestCase("<Bin/>")]
        [TestCase("<Animation Slot=\"NOT_A_SLOT\" BlendId=\"0.3\" SelectionWeight=\"1\" WeaponBone=\"True, True, True, True, True, True\" Unk=\"false\" />")]
        public void FromText_OfAnythingElse_IsNull(string text) => Assert.That(BinXml.FromText(text), Is.Null);
    }
}
