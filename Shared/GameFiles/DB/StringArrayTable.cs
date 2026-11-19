using Shared.ByteParsing;

namespace Shared.GameFormats.DB
{
    public class StringArrayTable
    {
        public List<string> Values { get; set; } = new List<string>();

        public StringArrayTable(params string[] items)
        {
            if (items != null)
            {
                foreach (var item in items)
                    Values.Add(item);
            }
        }

        public StringArrayTable() { }

        public StringArrayTable(ByteChunk data)
        {
            var count = Count(data, 2);
            Values = new List<string>(count);
            for (var i = 0; i < count; i++)
                Values.Add(data.ReadString());
        }

        // An item count, refused when the bytes left could not hold that many items of at least minItemSize bytes: a count
        // read out of the wrong kind of file is any number, and the list was sized to it before the first item failed.
        internal static int Count(ByteChunk data, int minItemSize)
        {
            var count = data.ReadInt32();
            if (count < 0 || (long)count * minItemSize > data.BytesLeft)
                throw new InvalidDataException($"A count of {count} at byte {data.Index - 4}, with {data.BytesLeft} bytes left");
            return count;
        }

        public byte[] ToByteArray()
        {
            using var memStream = new MemoryStream();
            memStream.Write(ByteParsers.Int32.EncodeValue(Values.Count, out _));
            foreach (var item in Values)
                memStream.Write(ByteParsers.String.WriteCaString(item.ToLower()));

            return memStream.ToArray();
        }
    }

    public class IntArrayTable
    {
        public List<int> Data { get; set; }
        public IntArrayTable(ByteChunk data)
        {
            var count = StringArrayTable.Count(data, 4);
            Data = new List<int>(count);
            for (var i = 0; i < count; i++)
                Data.Add(data.ReadInt32());
        }
    }
}
