// Version: 41 (falls back to pack URIs when the assets folder is not shipped with the exe)
using System;

namespace TheGodFace.Utils
{
    // Resolves asset paths between absolute disk paths and paths relative to the assets root.
    public static class PathHelper
    {
        // Folder that contains the assets directory, always ending with a separator.
        public static string BaseAssetsDir { get; private set; }

        // True when the assets folder sits on disk, so loose files win over the embedded copies.
        public static bool AssetsOnDisk { get; private set; }

        // Walks up from the executable until a folder with assets is found and caches it.
        static PathHelper()
        {
            string? currentDir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(currentDir)) currentDir = Environment.CurrentDirectory;

            while (!string.IsNullOrEmpty(currentDir))
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(currentDir, "assets")))
                {
                    BaseAssetsDir = currentDir;
                    if (!BaseAssetsDir.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                        BaseAssetsDir += System.IO.Path.DirectorySeparatorChar;
                    // Loose assets found, so image paths stay ordinary disk paths.
                    AssetsOnDisk = true;
                    return;
                }
                currentDir = System.IO.Directory.GetParent(currentDir)?.FullName;
            }
            BaseAssetsDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            // Nothing on disk: every asset path is served from the assembly resources.
            AssetsOnDisk = false;
        }

        // Converts an absolute asset path into the relative form stored in FaceConfig.
        public static string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";
            try
            {
                Uri baseUri = new Uri(BaseAssetsDir);
                Uri fullUri = new Uri(fullPath);
                string rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString());
                return rel.Replace('/', System.IO.Path.DirectorySeparatorChar);
            }
            catch { return fullPath; }
        }

        // Expands a stored relative asset path into an absolute one for WPF image bindings.
        public static string GetFullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                if (System.IO.Path.IsPathRooted(path)) return path;

                // Packaged exe without the assets folder: the image comes straight out of the assembly.
                if (!AssetsOnDisk && IsAssetPath(path)) return ToPackUri(path);

                return System.IO.Path.GetFullPath(System.IO.Path.Combine(BaseAssetsDir, path));
            }
            catch { return path; }
        }

        // Tells whether a stored relative path points into the assets folder.
        private static bool IsAssetPath(string path) =>
            path.Replace('/', System.IO.Path.DirectorySeparatorChar)
                .StartsWith("assets" + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        // Builds the pack URI of a file compiled into the assembly as a WPF resource.
        private static string ToPackUri(string path) => "pack://application:,,,/" + path.Replace('\\', '/');
    }
}