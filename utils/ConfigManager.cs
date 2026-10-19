// Version: 41 stable (config path bound to the exe directory, migration from legacy build folders)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TheGodFace.Models;

namespace TheGodFace.Utils
{
    // Loads and saves FaceConfig as face_config.json next to the exe and migrates legacy copies.
    public static class ConfigManager
    {
        // Name of the JSON file shared between the editor UI and the loader.
        private const string ConfigFileName = "face_config.json";

        // Resolved once at startup so the path never depends on the process working directory.
        private static readonly string ConfigPath = ResolveConfigPath();

        // Full path of the config file the app really uses, exposed for diagnostics.
        public static string CurrentConfigPath => ConfigPath;

        // Serializes the whole face state into the config file, swallowing I/O errors.
        public static void Save(FaceConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, options));
            }
            catch (Exception ex) { Console.WriteLine($"������ ����������: {ex.Message}"); }
        }

        // Reads the config file into a FaceConfig, returning null when it is missing or broken.
        public static FaceConfig? Load()
        {
            if (!File.Exists(ConfigPath)) return null;
            try { return JsonSerializer.Deserialize<FaceConfig>(File.ReadAllText(ConfigPath)); }
            catch (Exception) { return null; }
        }

        // Prefers the config next to the exe and copies it from a legacy build folder when missing.
        private static string ResolveConfigPath()
        {
            string exeDir = AppContext.BaseDirectory;
            string primary = Path.Combine(exeDir, ConfigFileName);

            if (File.Exists(primary)) return primary;

            string? legacy = EnumerateLegacyConfigs(exeDir)
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (legacy != null)
            {
                try
                {
                    File.Copy(legacy, primary, overwrite: false);
                    if (File.Exists(primary)) return primary;
                }
                catch { }
                return legacy;
            }

            return primary;
        }

        // Yields candidate config files: everything under bin plus the assets folder used when running from sources.
        private static IEnumerable<string> EnumerateLegacyConfigs(string exeDir)
        {
            string exeDirTrimmed = exeDir.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Walks up until the bin folder is found, so Debug, Release and any TFM are covered.
            string? binRoot = null;
            string? current = exeDirTrimmed;
            for (int depth = 0; depth < 4 && current != null; depth++)
            {
                if (string.Equals(Path.GetFileName(current), "bin", StringComparison.OrdinalIgnoreCase))
                {
                    binRoot = current;
                    break;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            if (binRoot != null)
            {
                string[] configs;
                try { configs = Directory.GetFiles(binRoot, ConfigFileName, SearchOption.AllDirectories); }
                catch { configs = Array.Empty<string>(); }

                foreach (string config in configs)
                    yield return config;
            }

            // Config next to assets, which is where a source-run build keeps its files.
            string assetsDir = PathHelper.BaseAssetsDir;
            if (!string.IsNullOrEmpty(assetsDir))
                yield return Path.Combine(assetsDir, ConfigFileName);
        }
    }
}