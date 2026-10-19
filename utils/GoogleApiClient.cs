// Version: 5 (Gemma request branch removed, Gemini-only JSON requests)
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TheGodFace.Utils
{
    // Calls the Generative Language REST API and logs every exchange into ai_responses.txt.
    public class GoogleApiClient
    {
        // Reused HTTP client for all model calls, created once per service instance.
        private readonly HttpClient _http = new HttpClient();
        // API key resolved by GeminiService from the environment or api_key.txt.
        private readonly string _apiKey;

        // Stores the key, an empty string means no call can be made.
        public GoogleApiClient(string apiKey) => _apiKey = apiKey;

        // Sends one prompt to a model and returns the raw text answer, retrying once on a 429.
        public async Task<string> GenerateContentAsync(string model, string systemPrompt, string userMessage)
        {
            // Without a key the request cannot succeed, so fail with a message that names both sources.
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException(
                    "API-ключ не задан: задайте переменную окружения ICON_API_KEY или положите ключ в api_key.txt рядом с приложением.");

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey.Trim()}";

            // Gemini takes the system prompt as systemInstruction and answers with a JSON body.
            object requestBody = new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = userMessage } }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json"
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // Two attempts: the second one runs only after a rate limit pause.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var response = await _http.PostAsync(url, content);
                // Rate limited, wait out the quota window and retry the same payload.
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(2000);
                    continue;
                }
                // Any other failure is logged with the server body and surfaced to the cascade.
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    File.AppendAllText(PathHelper.GetFullPath("ai_responses.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{model}] [HTTP ERROR {response.StatusCode}]:\n{err}\n\n", Encoding.UTF8);
                    throw new HttpRequestException($"[{response.StatusCode}] {err}", null, response.StatusCode);
                }

                // The answer text sits in candidates[0].content.parts[0].text, missing parts mean an empty object.
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                string text = doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0
                    ? candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "{}"
                    : "{}";

                File.AppendAllText(PathHelper.GetFullPath("ai_responses.txt"), $"================================================================================\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MODEL: {model}\nUSER: {userMessage}\n--------------------------------- SERVER RESPONSE ------------------------------\n{text}\n\n", Encoding.UTF8);
                return text;
            }
            throw new HttpRequestException("Превышен лимит запросов к модели (429).");
        }
    }
}