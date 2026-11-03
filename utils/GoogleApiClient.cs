// Version: 6 (metadata probe for the availability monitor, per-request timeouts)
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TheGodFace.Utils
{
    // Calls the Generative Language REST API and logs every dialogue exchange into ai_responses.txt.
    public class GoogleApiClient
    {
        // Longest wait for a generation answer, so a hung model cannot freeze the chat.
        private static readonly TimeSpan GenerationTimeout = TimeSpan.FromSeconds(90);

        // Reused HTTP client for all model calls, created once per service instance.
        private readonly HttpClient _http = new HttpClient();
        // API key resolved by GeminiService from the environment or api_key.txt.
        private readonly string _apiKey;

        // Stores the key, an empty string means no call can be made.
        public GoogleApiClient(string apiKey) => _apiKey = apiKey;

        // Asks the metadata endpoint about one model; the request costs zero tokens and is never logged.
        public async Task<int> ProbeModelAsync(string model, TimeSpan timeout, CancellationToken token)
        {
            // Without a key no probe can succeed, 0 travels the same path as a network failure.
            if (string.IsNullOrWhiteSpace(_apiKey)) return 0;

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}?key={_apiKey.Trim()}";
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);

            try
            {
                // Headers only, the monitor needs the status code and nothing from the body.
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                return (int)response.StatusCode;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // A probe timeout counts as unreachable, the monitor decides what that means.
                return 0;
            }
            catch
            {
                // DNS, TLS and socket failures all mean the model cannot be reached.
                return 0;
            }
        }

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

            // Serialized once, then wrapped in a fresh content for every attempt.
            string payload = JsonSerializer.Serialize(requestBody);

            // Two attempts: the second one runs only after a rate limit pause.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var attemptCts = new CancellationTokenSource(GenerationTimeout);

                HttpResponseMessage response;
                try
                {
                    response = await _http.PostAsync(url, content, attemptCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // The model never answered in time, the routing layer reports this to the chat.
                    throw new TimeoutException($"Модель {model} не ответила за {GenerationTimeout.TotalSeconds:0} секунд.");
                }

                using (response)
                {
                    // Rate limited, wait out the quota window and retry the same payload.
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        await Task.Delay(2000);
                        continue;
                    }
                    // Any other failure is logged with the server body and surfaced to the caller.
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
            }
            throw new HttpRequestException("Превышен лимит запросов к модели (429).");
        }
    }
}
