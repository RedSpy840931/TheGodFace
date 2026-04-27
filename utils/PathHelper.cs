// Version: 40 stable
using System;

namespace TheGodFace.Utils
{
    // ==========================================
    // ”“»À»“¿: ”ÃÕ€≈ œ”“» (œŒ–“¿“»¬ÕŒ—“‹)
    // ==========================================
    public static class PathHelper
    {
        public static string BaseAssetsDir { get; private set; }

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
                    return;
                }
                currentDir = System.IO.Directory.GetParent(currentDir)?.FullName;
            }
            BaseAssetsDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
        }

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

        public static string GetFullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                if (System.IO.Path.IsPathRooted(path)) return path;
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(BaseAssetsDir, path));
            }
            catch { return path; }
        }
    }
}