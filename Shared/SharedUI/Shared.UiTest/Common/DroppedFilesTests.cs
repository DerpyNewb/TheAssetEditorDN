using System.Windows;
using Shared.Core.PackFiles.Models;
using Shared.Ui.Common;

namespace Shared.UiTest.Common
{
    internal class DroppedFilesTests
    {
        [Test, Apartment(ApartmentState.STA)]
        public void AFileDraggedFromThePackTree_IsThatFile_AndNoPack()
        {
            var file = PackFile.CreateFromASCII("a.wsmodel", "a");
            var data = new DataObject(file);

            Assert.That(DroppedFiles.PackFile(data), Is.SameAs(file));
            Assert.That(DroppedFiles.Packs(data), Is.Empty);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void PathsDraggedFromWindows_GiveTheirPacks_AndNoFile()
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { @"c:\mods\a.pack", @"c:\mods\b.txt", @"c:\mods\C.PACK", @"c:\mods\d.pack_temp" });

            Assert.That(DroppedFiles.Packs(data), Is.EqualTo(new[] { @"c:\mods\a.pack", @"c:\mods\C.PACK" }));
            Assert.That(DroppedFiles.PackFile(data), Is.Null);
        }
    }
}
