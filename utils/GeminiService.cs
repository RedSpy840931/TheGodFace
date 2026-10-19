// Version: 16 (Gemma stage removed: single Flash Lite cascade)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TheGodFace.Utils
{
    // One pose in an animation sequence, mirrored by the JSON schema the model answers with.
    public class FaceFrame
    {
        public double? duration { get; set; }
        public string? target_mouth { get; set; }
        public string? target_eyebrow { get; set; }
        public string? target_cheek { get; set; }
        public int stress_level { get; set; } = 1;
        public bool sweat_drop { get; set; } = false;
        public string squint { get; set; } = "none";
        public string gaze { get; set; } = "free";
    }

    // Parsed model answer: chat text plus the face sequence the UI should play.
    public class FaceStateResponse
    {
        // Chat line shown to the user, with a default for empty answers.
        public string reply_text { get; set; } = "Упс, я потеряла дар речи.";
        // Model that produced the answer, filled in by GeminiService after the cascade returns.
        public string? UsedModel { get; set; }
        public List<FaceFrame> sequence { get; set; } = new List<FaceFrame>();

        // Exposes the last frame as a flat state, so MainWindow can bind one value per property.
        public int stress_level => sequence.LastOrDefault()?.stress_level ?? 1;
        public bool sweat_drop => sequence.LastOrDefault()?.sweat_drop ?? false;
        public string target_mouth => sequence.LastOrDefault()?.target_mouth ?? "Small closed neutral";
        public string target_eyebrow => sequence.LastOrDefault()?.target_eyebrow ?? "Neutral (None)";
        public string target_cheek => sequence.LastOrDefault()?.target_cheek ?? "Neutral (None)";
        public string squint => sequence.LastOrDefault()?.squint ?? "none";
        public string gaze => sequence.LastOrDefault()?.gaze ?? "free";
    }

    // Sends chat text through the Flash Lite cascade and turns raw model text into a FaceStateResponse.
    public class GeminiService
    {
        // Shared HTTP client for every model call, created once with the key resolved at startup.
        private readonly GoogleApiClient _client = new GoogleApiClient(LoadApiKey());

        // Reads the key from ICON_API_KEY or api_key.txt and returns empty when neither exists.
        private static string LoadApiKey()
        {
            string? fromEnv = Environment.GetEnvironmentVariable("ICON_API_KEY");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            try
            {
                string keyFile = PathHelper.GetFullPath("api_key.txt");
                if (File.Exists(keyFile))
                {
                    string key = File.ReadAllText(keyFile).Trim();
                    if (key.Length > 0) return key;
                }
            }
            catch { /* a broken key file must not crash the app */ }

            return string.Empty;
        }
        // Models tried in order, the next one takes over when the previous one is unavailable.
        private readonly string[] _flashLiteCascade = new[] { "gemini-3.5-flash-lite", "gemini-3.1-flash-lite" };

        // Builds the prompt from the current pose and sends the user text through the cascade.
        public async Task<FaceStateResponse> SendMessageAsync(
            string userMessage,
            IEnumerable<string> availableMouths,
            IEnumerable<string> availableBrows,
            IEnumerable<string> availableCheeks,
            string currentMouth,
            string currentBrow,
            string currentCheek,
            int currentStress,
            Action<FaceStateResponse>? onIntermediateResponse = null)
        {
            // Prompt template filled with the current presets so the model picks names that exist.
            string prompt = Ic0nPromptBuilder.BuildFlashLitePrompt(availableMouths, availableBrows, availableCheeks, currentMouth, currentBrow, currentCheek, currentStress);
            try
            {
                var (raw, model) = await ExecuteCascadeAsync(_flashLiteCascade, prompt, userMessage, fallbackModel =>
                {
                    // Interim pose shown while the next model of the cascade is being called.
                    onIntermediateResponse?.Invoke(new FaceStateResponse
                    {
                        reply_text = $"Переключаюсь на резерв {fallbackModel}...",
                        sequence = new List<FaceFrame> { new FaceFrame { duration = 1, target_mouth = "Small displeased", target_eyebrow = "Asymmetric Doubt", stress_level = 2, gaze = "straight" } }
                    });
                });

                var response = FaceResponseParser.Parse(raw);
                response.UsedModel = model;
                return response;
            }
            catch (Exception ex)
            {
                return new FaceStateResponse
                {
                    reply_text = $"Отказ нейросети ({ex.Message}). Дай мне передохнуть минуту!",
                    UsedModel = "Ошибка",
                    sequence = new List<FaceFrame>
                    {
                        new FaceFrame { duration = 2, target_mouth = "Pog", target_eyebrow = "Shock / Surprise", stress_level = 3, sweat_drop = true, gaze = "straight" },
                        new FaceFrame { duration = null, target_mouth = "Small frown", target_eyebrow = "Concerned", stress_level = 2, sweat_drop = true, gaze = "free" }
                    }
                };
            }
        }

        // Tries every model of the cascade in order and reports the model that finally answered.
        private async Task<(string raw, string model)> ExecuteCascadeAsync(string[] models, string prompt, string message, Action<string>? onModelSwitched = null)
        {
            Exception? lastEx = null;
            for (int i = 0; i < models.Length; i++)
            {
                try { return (await _client.GenerateContentAsync(models[i], prompt, message), models[i]); }
                catch (Exception ex)
                {
                    lastEx = ex;
                    if (i < models.Length - 1) onModelSwitched?.Invoke(models[i + 1]);
                }
            }
            throw lastEx ?? new Exception("Все модели в каскаде недоступны.");
        }
    }
}