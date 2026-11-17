
namespace Editors.ImportExport.Misc
{
    public static class FileExtensionHelper
    {
        public static bool IsGltfFile(string fileName)
        {            
            return fileName.EndsWith(".gltf", StringComparison.InvariantCultureIgnoreCase) ||
                   fileName.EndsWith(".glb", StringComparison.InvariantCultureIgnoreCase);
        }

        public static bool IsDdsFile(string fileName)
        {
            var isDdsFile = fileName.EndsWith(".dds", StringComparison.InvariantCultureIgnoreCase);
            return isDdsFile;
        }

        public static bool IsDdsMaterialFile(string fileName)
        {
            var isDdsFile = IsDdsFile(fileName);
            var isMaterialFile = fileName.EndsWith("material", StringComparison.InvariantCultureIgnoreCase);
            return isDdsFile && isMaterialFile;
        }

        public static bool IsRmvFile(string fileName)
        {
            var isRmv = fileName.EndsWith(".rigid_model_v2", StringComparison.InvariantCultureIgnoreCase);
            return isRmv;
        }

        /// <summary>.blend and .obj, which go through Blender.</summary>
        public static bool IsBlenderImportFile(string fileName) =>
            fileName.EndsWith(".blend", StringComparison.InvariantCultureIgnoreCase) ||
            fileName.EndsWith(".obj", StringComparison.InvariantCultureIgnoreCase);

        public static bool IsWsModelFile(string fileName)
        {
            var isRmv = fileName.EndsWith(".wsmodel", StringComparison.InvariantCultureIgnoreCase);
            return isRmv;
        }
    }
}
