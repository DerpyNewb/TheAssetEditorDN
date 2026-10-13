using System.IO;
using System.Text;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.DB;
using Test.TestingUtility.Shared;

namespace Test.UnitSimulator
{
    // A DB table file encoded in the test as the game lays one out: the version marker, one byte, the row count, then each row's
    // cells in the schema extract's binary order. For the cases no vanilla file has.
    static class MadeUpDb
    {
        public static byte[] File(string table, int version, params object[][] rows)
        {
            var fields = DbSchema.Versions(table)[version];
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(new byte[] { 0xFC, 0xFD, 0xFE, 0xFF });
            writer.Write(version);
            writer.Write((byte)1);
            writer.Write(rows.Length);
            foreach (var row in rows)
                for (var i = 0; i < fields.Count; i++)
                    Cell(writer, fields[i].Type, row[i]);
            writer.Flush();
            return stream.ToArray();
        }

        static void Cell(BinaryWriter writer, string type, object value)
        {
            switch (type)
            {
                case "OptionalStringU8":
                    var text = (string)value;
                    writer.Write((byte)(text.Length == 0 ? 0 : 1));
                    if (text.Length != 0)
                        Text(writer, text);
                    break;
                case "StringU8": Text(writer, (string)value); break;
                case "Boolean": writer.Write((bool)value); break;
                case "I32": writer.Write((int)value); break;
                case "F32": writer.Write((float)value); break;
                default: throw new NotSupportedException(type);
            }
        }

        static void Text(BinaryWriter writer, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        // The file in a pack of its own, loaded after the packs already there, as a mod ships one.
        public static IPackFileContainer AddPack(AssetEditorTestRunner runner, string packName, string table, string fileName, byte[] data)
        {
            var pack = runner.CreateEmptyPackFile(packName, false);
            runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry($@"db\{table}", PackFile.CreateFromBytes(fileName, data))]);
            return pack;
        }
    }
}
