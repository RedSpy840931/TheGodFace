// Version: 17 (direct routing to the active model, cascade removed)
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
        public string? frame_text { get; set; }
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
        // Model that produced the answer, filled in by GeminiService after the request returns.
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

    // Sends chat text straight to the currently available model and parses the answer.
    public class GeminiService
    {
        // Models in preference order, the monitor reports which one can take a request right now.
        private readonly string[] _priorityModels = new[] { "gemini-3.5-flash-lite", "gemini-3.1-flash-lite" };

        // Shared HTTP client for every model call, created once with the key resolved at startup.
        private readonly GoogleApiClient _client;

        // Background zero-token probe that keeps a status per model for the routing decision.
        private readonly ModelAvailabilityMonitor _monitor;

        // Resolves the key once and wires the client to the availability monitor.
        public GeminiService()
        {
            _client = new GoogleApiClient(LoadApiKey());
            _monitor = new ModelAvailabilityMonitor(_client, _priorityModels);
        }

        // Availability state, the window reads it to wake the face up after the splash.
        public ModelAvailabilityMonitor Monitor => _monitor;

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

        // Starts the background health checks, safe to call more than once.
        public void StartMonitoring() => _monitor.Start();

        // Stops the health checks, called when the window closes.
        public void StopMonitoring() => _monitor.Stop();

        // Builds the prompt from the current pose and sends it to the active model in one request.
        public async Task<FaceStateResponse> SendMessageAsync(
            string userMessage,
            IEnumerable<string> availableMouths,
            IEnumerable<string> availableBrows,
            IEnumerable<string> availableCheeks,
            string currentMouth,
            string currentBrow,
            string currentCheek,
            int currentStress)
        {
            try
            {
                // Prompt template filled with the current presets so the model picks names that exist.
                string prompt = Ic0nPromptBuilder.BuildFlashLitePrompt(availableMouths, availableBrows, availableCheeks, currentMouth, currentBrow, currentCheek, currentStress);

                // The monitor already knows which model is free, so no sequential cascade is needed.
                string model = _monitor.GetActiveModel();
                string raw = await _client.GenerateContentAsync(model, prompt, userMessage);

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
                        new FaceFrame { target_mouth = "Pog", target_eyebrow = "Shock / Surprise", stress_level = 3, sweat_drop = true, gaze = "straight" },
                        new FaceFrame { target_mouth = "Small frown", target_eyebrow = "Concerned", stress_level = 2, sweat_drop = true, gaze = "free" }
                    }
                };
            }
        }
    }
}
