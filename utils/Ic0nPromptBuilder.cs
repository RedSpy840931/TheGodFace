using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TheGodFace.Utils
{
    // Fills the prompt template from the presets folder with the presets available in the face editor.
    public static class Ic0nPromptBuilder
    {
        // Reads one template from presets/prompts as UTF-8 text, next to the versioned face preset.
        private static string ReadTemplate(string fileName) =>
            File.ReadAllText(PathHelper.GetFullPath(Path.Combine("presets", "prompts", fileName)), Encoding.UTF8);

        // Builds the model prompt from the sitting pose and the allowed preset names.
        public static string BuildFlashLitePrompt(
            IEnumerable<string> availableMouths,
            IEnumerable<string> availableBrows,
            IEnumerable<string> availableCheeks,
            string currentMouth,
            string currentBrow,
            string currentCheek,
            int currentStress) =>
            ReadTemplate("flash_lite_prompt.txt")
                .Replace("{currentMouth}", currentMouth)
                .Replace("{currentBrow}", currentBrow)
                .Replace("{currentCheek}", currentCheek)
                .Replace("{currentStress}", currentStress.ToString())
                .Replace("{mouths}", string.Join(", ", availableMouths.Select(m => $"\"{m.Trim()}\"")))
                .Replace("{brows}", string.Join(", ", availableBrows.Select(b => $"\"{b.Trim()}\"")))
                .Replace("{cheeks}", string.Join(", ", availableCheeks.Select(c => $"\"{c.Trim()}\"")));
    }
}