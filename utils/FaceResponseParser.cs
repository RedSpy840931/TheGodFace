// Version: 16 (Anchor-based forward bracket balancing with string escape handling)
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheGodFace.Utils
{
    // Extracts the JSON object from model text that may wrap it in prose or code fences.
    public static class FaceResponseParser
    {
        // Deserialization settings matching the casing and numeric quirks of model output.
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        // Turns raw model text into a FaceStateResponse and never throws to the caller.
        public static FaceStateResponse Parse(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return CreateFallback("Пустой ответ от сервера.");

            string jsonCandidate = ExtractJsonByAnchor(rawText);

            FaceStateResponse? result = null;
            try
            {
                result = JsonSerializer.Deserialize<FaceStateResponse>(jsonCandidate, Options);
            }
            catch
            {
                // Second attempt on the untouched text, in case the extracted slice was wrong.
                try { result = JsonSerializer.Deserialize<FaceStateResponse>(rawText, Options); }
                catch { }
            }

            result ??= CreateFallback(rawText);

            if (result.sequence == null || result.sequence.Count == 0)
            {
                result.sequence = new List<FaceFrame>
                {
                    new FaceFrame
                    {
                        duration = null,
                        target_mouth = "Small closed neutral",
                        target_eyebrow = "Neutral (None)",
                        target_cheek = "Neutral (None)",
                        stress_level = 1,
                        sweat_drop = false,
                        squint = "none",
                        gaze = "free"
                    }
                };
            }

            return result;
        }

        // Cuts out the JSON object that holds reply_text by balancing braces from an anchor.
        private static string ExtractJsonByAnchor(string text)
        {
            // Anchor on the last reply_text key and open the brace before it, otherwise use the first brace.
            int keyIndex = text.LastIndexOf("\"reply_text\"", StringComparison.OrdinalIgnoreCase);
            int startIndex = keyIndex != -1 ? text.LastIndexOf('{', keyIndex) : text.IndexOf('{');

            if (startIndex == -1) return text;

            // Walk forward balancing braces while skipping string bodies and escaped characters.
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = startIndex; i < text.Length; i++)
            {
                char c = text[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return text.Substring(startIndex, i - startIndex + 1);
                        }
                    }
                }
            }

            return text.Substring(startIndex);
        }

        // Builds a neutral single-frame answer so the face keeps working on unparsable input.
        private static FaceStateResponse CreateFallback(string text) => new FaceStateResponse
        {
            reply_text = text,
            sequence = new List<FaceFrame>
            {
                new FaceFrame
                {
                    duration = null,
                    target_mouth = "Small closed neutral",
                    target_eyebrow = "Neutral (None)",
                    target_cheek = "Neutral (None)",
                    stress_level = 1,
                    sweat_drop = false,
                    squint = "none",
                    gaze = "free"
                }
            }
        };
    }
}