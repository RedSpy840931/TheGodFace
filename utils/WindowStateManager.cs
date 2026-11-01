// Runtime window state saved separately on close
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using TheGodFace.Models;

namespace TheGodFace.Utils
{
    public static class WindowStateManager
    {
        private const string StateFileName = "window_state.json";
        private const string ConfigFolder = "presets";

        private static readonly string StatePath = ResolveStatePath();

        public static string CurrentStatePath => StatePath;

        public static void Save(WindowStateConfig state)
        {
            try
            {
                string? folder = Path.GetDirectoryName(StatePath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(StatePath, JsonSerializer.Serialize(state, options));
            }
            catch { }
        }

        public static WindowStateConfig? Load()
        {
            try
            {
                if (File.Exists(StatePath))
                    return JsonSerializer.Deserialize<WindowStateConfig>(File.ReadAllText(StatePath));
            }
            catch { }
            return null;
        }

        private static string ResolveStatePath()
        {
            return Path.Combine(PathHelper.GetFullPath(ConfigFolder), StateFileName);
        }
    }
}
