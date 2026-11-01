using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TheGodFace.Utils
{
    // Service for synthesizing speech using Piper TTS with persistent json-input process
    public class PiperTtsService : IDisposable
    {
        private static PiperTtsService _instance;
        private static readonly object _lock = new object();
        
        private readonly string _piperExePath;
        private readonly string _voiceModelPath;
        private readonly string _piperDir;
        private Process _process;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _initialized;
        private bool _disposed;
        private readonly Task _initTask;

        public static PiperTtsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new PiperTtsService("ic0n_voice.onnx");
                        }
                    }
                }
                return _instance;
            }
        }

        public PiperTtsService(string voiceName = "ic0n_voice.onnx")
        {
            _piperExePath = PathHelper.GetPiperExePath();
            _voiceModelPath = PathHelper.GetPiperVoicePath(voiceName);
            _piperDir = PathHelper.GetPiperDir();
            _initTask = InitializeAsync();
        }

        // Initialize persistent Piper process
        public async Task InitializeAsync()
        {
            if (_initialized || _disposed)
                return;

            if (!File.Exists(_piperExePath))
                throw new FileNotFoundException($"piper.exe not found at {_piperExePath}");

            if (!File.Exists(_voiceModelPath))
                throw new FileNotFoundException($"Voice model not found at {_voiceModelPath}");

            var startInfo = new ProcessStartInfo
            {
                FileName = _piperExePath,
                Arguments = $"--model \"{_voiceModelPath}\" --json-input",
                WorkingDirectory = _piperDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo };
            _process.Start();
            _initialized = true;

            // Give process time to load model
            await Task.Delay(200);
        }

        // Ensure initialized
        public async Task EnsureInitializedAsync()
        {
            if (_initialized)
                return;
            await _initTask;
        }

        // Synthesize text to WAV file and return path to generated file
        public async Task<string> SynthesizeAsync(string text)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PiperTtsService));

            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be empty", nameof(text));

            await EnsureInitializedAsync();

            string tempFile = Path.Combine(Path.GetTempPath(), $"ic0n_tts_{Guid.NewGuid()}.wav");

            await _semaphore.WaitAsync();
            try
            {
                var request = new
                {
                    text = text,
                    output_file = tempFile
                };

                string jsonRequest = JsonSerializer.Serialize(request);
                await _process.StandardInput.WriteLineAsync(jsonRequest);
                await _process.StandardInput.FlushAsync();

                // Wait for Piper to confirm completion
                string responseLine = await _process.StandardOutput.ReadLineAsync();
                
                if (string.IsNullOrEmpty(responseLine))
                {
                    try
                    {
                        string error = await _process.StandardError.ReadToEndAsync();
                        throw new Exception($"Piper TTS failed: {error}");
                    }
                    catch
                    {
                        throw new Exception("Piper TTS failed: no response from process");
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }

            if (!File.Exists(tempFile))
                throw new Exception("Piper did not produce output file");

            return tempFile;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.StandardInput?.Close();
                    try { _process.Kill(); } catch { }
                    try { _process.WaitForExit(1000); } catch { }
                }
                _process?.Dispose();
            }
            catch { }

            _semaphore?.Dispose();
        }
    }
}
