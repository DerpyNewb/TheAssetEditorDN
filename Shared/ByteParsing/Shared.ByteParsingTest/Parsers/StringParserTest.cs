using NUnit.Framework;
using Shared.ByteParsing.Parsers;

namespace Shared.ByteParsingTest.Parsers
{
    [TestFixture]
    public class StringParserTest
    {
        [Test]
        public void EncodeObject_RoundTrips()
        {
            var parser = new StringParser();
            var input = "hello";
            var bytes = parser.Encode((object)input);

            Assert.That(bytes, Is.Not.Null);

            var ok = parser.TryDecodeValue(bytes, 0, out var value, out var bytesRead, out var error);

            Assert.That(ok, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(value, Is.EqualTo(input));
        }

        // Whitespace used to be written as "", and a non-ASCII string with a character count where
        // the reader expects a byte count, which cut the string short and misaligned the rest of the file.
        [TestCase("  ")]
        [TestCase("wh3_smoke_ö")]
        public void WriteCaString_ReadsBackWhole(string input)
        {
            foreach (var parser in new StringParser[] { new(), new StringAsciiParser(), new OptionalStringParser(), new OptionalStringAsciiParser() })
            {
                var bytes = parser.WriteCaString(input);
                var ok = parser.TryDecodeValue(bytes, 0, out var value, out var bytesRead, out var error);

                Assert.That(ok, Is.True, $"{parser.TypeName}: {error}");
                Assert.That(value, Is.EqualTo(input), parser.TypeName);
                Assert.That(bytesRead, Is.EqualTo(bytes.Length), parser.TypeName);
            }
        }
    }
}
