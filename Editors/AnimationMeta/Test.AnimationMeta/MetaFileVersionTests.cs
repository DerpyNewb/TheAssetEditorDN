using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta
{
    // CA ships nine metas that are nothing but zero bytes; opening one used to throw "Unknown version - 0".
    public class MetaFileVersionTests
    {
        readonly MetaDataFileParser _parser = new(new MetaDataDatabase());

        [Test]
        public void AZeroFilledFile_HasNoTags()
        {
            var parsed = _parser.ParseFile(new byte[420]);

            Assert.That(parsed.Version, Is.EqualTo(0));
            Assert.That(parsed.Attributes, Is.Empty);
            Assert.That(_parser.ParseFile(_parser.GenerateBytes(parsed.Version, parsed)).Attributes, Is.Empty);
        }

        [Test]
        public void AnyOtherVersion_StillThrows()
        {
            var version0WithData = new byte[16];
            version0WithData[4] = 1;
            var version3 = new byte[8];
            version3[0] = 3;

            Assert.Throws<Exception>(() => _parser.ParseFile(version0WithData));
            Assert.Throws<Exception>(() => _parser.ParseFile(version3));
        }
    }
}
