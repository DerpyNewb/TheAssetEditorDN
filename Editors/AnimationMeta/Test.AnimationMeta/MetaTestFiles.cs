using Editors.AnimationMeta.Presentation;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    /// <summary>Meta files made in memory, so a test needs no game install: tags built with the editor's own defaults and
    /// written by the editor's own parser.</summary>
    static class MetaTestFiles
    {
        public static readonly MetaDataFileParser Parser = new(new MetaDataDatabase());

        public static T Tag<T>(string key, Action<T>? set = null) where T : ParsedMetadataAttribute
        {
            var tag = (T)Parser.CreateDefault(key);
            set?.Invoke(tag);
            return tag;
        }

        public static ParsedMetadataFile File(params ParsedMetadataAttribute[] tags) => new() { Version = 2, Attributes = [.. tags] };

        public static byte[] Bytes(params ParsedMetadataAttribute[] tags) => Parser.GenerateBytes(2, File(tags));

        /// <summary>A pack holding the files. isGame marks it as one of CA's, as the game's own packs are when loaded.</summary>
        public static IPackFileContainer Pack(AssetEditorTestRunner runner, bool isGame, params (string Path, byte[] Bytes)[] files)
        {
            var pack = runner.CreateEmptyPackFile(isGame ? "game" : "mod", false);
            runner.PackFileService.AddFilesToPack(pack, files
                .Select(x => new NewPackFileEntry(System.IO.Path.GetDirectoryName(x.Path)!, PackFile.CreateFromBytes(System.IO.Path.GetFileName(x.Path), x.Bytes)))
                .ToList());
            pack.IsCaPackFile = isGame;
            return pack;
        }

        /// <summary>The meta editor on a file of these tags, read back from bytes as it would be from a pack.</summary>
        public static MetaDataEditorViewModel Open(AssetEditorTestRunner runner, params ParsedMetadataAttribute[] tags)
        {
            var pack = runner.CreateEmptyPackFile("open", true);
            runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry("animations/test", PackFile.CreateFromBytes("test.anm.meta", Bytes(tags)))]);
            var file = runner.PackFileService.FindFile("animations/test/test.anm.meta", pack)!;
            return runner.CommandFactory.Create<OpenEditorCommand>().Execute<MetaDataEditorViewModel>(file, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);
        }
    }
}
