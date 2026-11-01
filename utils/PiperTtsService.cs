using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace TheGodFace.Utils
{
    // Service for synthesizing speech using Piper TTS
    public class PiperTtsService
    {
        private readonly string _piperExePath;
        private readonly string _voiceModelPath;
        private readonly string _piperDir;

        public PiperTtsService(string voiceName = "ic0n_voice.onnx")
        {
            _piperExePath = PathHelper.GetPiperExePath();
            _voiceModelPath = PathHelper.GetPiperVoicePath(voiceName);
            _piperDir = PathHelper.GetPiperDir();
        }

        // Synthesize text to WAV file and return path to generated file
        public async Task<string> SynthesizeAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be empty", nameof(text));

            if (!File.Exists(_piperExePath))
                throw new FileNotFoundException($"piper.exe not found at {_piperExePath}");

            if (!File.Exists(_voiceModelPath))
                throw new FileNotFoundException($"Voice model not found at {_voiceModelPath}");

            string tempFile = Path.Combine(Path.GetTempPath(), $"ic0n_tts_{Guid.NewGuid()}.wav");

            var startInfo = new ProcessStartInfo
            {
                FileName = _piperExePath,
                Arguments = $"--model \"{_voiceModelPath}\" --output_file \"{tempFile}\"",
                WorkingDirectory = _piperDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            await process.StandardInput.WriteAsync(text);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"Piper TTS failed with exit code {process.ExitCode}: {error}");
            }

            if (!File.Exists(tempFile))
                throw new Exception("Piper did not produce output file");

            return tempFile;
        }
    }
}
