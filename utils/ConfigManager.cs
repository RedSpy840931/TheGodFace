// Version: 40 stable
using System;
using System.IO;
using System.Text.Json;
using TheGodFace.Models;

namespace TheGodFace.Utils
{
    // ==========================================
    // ”“»À»“¿: —Œ’–¿Õ≈Õ»≈ » «¿√–”« ¿
    // ==========================================
    public static class ConfigManager
    {
        private const string ConfigPath = "face_config.json";

        public static void Save(FaceConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, options));
            }
            catch (Exception ex) { Console.WriteLine($"Œ¯Ë·Í‡ ÒÓı‡ÌÂÌËˇ: {ex.Message}"); }
        }

        public static FaceConfig? Load()
        {
            if (!File.Exists(ConfigPath)) return null;
            try { return JsonSerializer.Deserialize<FaceConfig>(File.ReadAllText(ConfigPath)); }
            catch (Exception) { return null; }
        }
    }
}