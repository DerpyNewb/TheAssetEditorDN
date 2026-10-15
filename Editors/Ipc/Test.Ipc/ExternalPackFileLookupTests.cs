using Editors.Ipc;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Test.Ipc
{
    public class ExternalPackFileLookupTests
    {
        static IPackFileContainer Pack(params string[] paths)
        {
            var pack = new Mock<IPackFileContainer>();
            pack.Setup(x => x.GetAllFiles()).Returns(paths.ToDictionary(x => x, x => PackFile.CreateFromBytes(Path.GetFileName(x), [])));
            return pack.Object;
        }

        // A file a mod overrides is in two packs; find lists it once, in path order, whatever the case of the query.
        [Test]
        public void Search_ListsEachPathOnce_InOrder_UpToTheLimit()
        {
            var service = new Mock<IPackFileService>();
            service.Setup(x => x.GetAllPackfileContainers()).Returns([
                Pack(@"variantmeshes\b\karl.wsmodel", @"variantmeshes\a\karl.rigid_model_v2", @"ui\other.png"),
                Pack(@"variantmeshes\a\karl.rigid_model_v2")]);
            var lookup = new ExternalPackFileLookup(service.Object);

            Assert.That(lookup.Search("KARL", 10), Is.EqualTo(new[] { @"variantmeshes\a\karl.rigid_model_v2", @"variantmeshes\b\karl.wsmodel" }));
            Assert.That(lookup.Search("karl", 1), Is.EqualTo(new[] { @"variantmeshes\a\karl.rigid_model_v2" }));
        }
    }
}
