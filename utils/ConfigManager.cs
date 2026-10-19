// Version: 42 (config lives in the versioned presets folder next to the prompts)
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
        // Folder that holds the versioned face preset together with the prompt templates.
        private const string ConfigFolder = "presets";

        // Resolved once at startup so the path never depends on the process working directory.
        private static readonly string ConfigPath = ResolveConfigPath();

        // Full path of the config file the app really uses, exposed for diagnostics.
        public static string CurrentConfigPath => ConfigPath;

        // Serializes the whole face state into the config file, swallowing I/O errors.
        public static void Save(FaceConfig config)
        {
            try
            {
                // A fresh checkout may lack the presets folder, so it is created before writing.
                string? folder = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

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

        // Uses the presets folder next to the sources and migrates an old copy out of the build output.
        private static string ResolveConfigPath()
        {
            string primary = Path.Combine(PathHelper.GetFullPath(ConfigFolder), ConfigFileName);

            if (File.Exists(primary)) return primary;

            string? legacy = EnumerateLegacyConfigs()
                .Where(File.Exists)
                .Where(path => !string.Equals(path, primary, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (legacy != null)
            {
                try
                {
                    // Migration copies the newest old config into presets, so the file becomes versioned.
                    string? folder = Path.GetDirectoryName(primary);
                    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                    File.Copy(legacy, primary, overwrite: false);
                    if (File.Exists(primary)) return primary;
                }
                catch { }
                // Copy failed: keep working on the old file instead of silently losing the pose.
                return legacy;
            }

            return primary;
        }

        // Yields candidate config files: everything under bin plus the folder next to the assets.
        private static IEnumerable<string> EnumerateLegacyConfigs()
        {
            string exeDirTrimmed = AppContext.BaseDirectory.TrimEnd(
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