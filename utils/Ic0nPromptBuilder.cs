using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TheGodFace.Utils
{
    // Fills the prompt template from the presets folder with the presets available in the face editor.
    public static class Ic0nPromptBuilder
    {
        // Manifest name prefix of the prompt templates that the project file embeds into the assembly.
        private const string EmbeddedPromptPrefix = "TheGodFace.presets.prompts.";

        // Reads the template next to the versioned face preset or from the copy embedded into the assembly.
        private static string ReadTemplate(string fileName)
        {
            string diskPath = PathHelper.GetFullPath(Path.Combine("presets", "prompts", fileName));
            if (File.Exists(diskPath)) return File.ReadAllText(diskPath, Encoding.UTF8);

            // A packaged exe without the presets folder still gets the shipped prompt template.
            using Stream? embedded = typeof(Ic0nPromptBuilder).Assembly.GetManifestResourceStream(EmbeddedPromptPrefix + fileName);
            if (embedded == null) throw new FileNotFoundException($"Шаблон промпта {fileName} не найден ни на диске, ни в сборке.");

            using var reader = new StreamReader(embedded, Encoding.UTF8);
            return reader.ReadToEnd();
        }

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