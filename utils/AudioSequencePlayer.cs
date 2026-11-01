using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Controls;
using NAudio.Wave;
using TheGodFace.Utils;

namespace TheGodFace.Utils
{
    // Plays audio sequences frame by frame in sync with animation
    public class AudioSequencePlayer : IDisposable
    {
        private readonly List<AudioFrame> _frames = new List<AudioFrame>();
        private int _currentFrameIndex = -1;
        private WaveOutEvent _waveOut;
        private AudioFileReader _audioReader;
        private bool _disposed;
        private static PiperTtsService _sharedTtsService;
        private readonly PiperTtsService _ttsService;
        private readonly Action<int, string> _onFrameTextUpdate;
        private Task<string> _nextAudioTask;
        private int _nextFrameIndex = 0;

        public class AudioFrame
        {
            public string Text { get; set; }
            public string AudioFilePath { get; set; }
            public bool IsGenerated { get; set; }
        }

        public AudioSequencePlayer(Action<int, string> onFrameTextUpdate = null)
        {
            if (_sharedTtsService == null)
            {
                _sharedTtsService = PiperTtsService.Instance;
            }
            _ttsService = _sharedTtsService;
            _onFrameTextUpdate = onFrameTextUpdate;
        }

        // Add frame with text to be synthesized
        public void AddFrame(string text)
        {
            _frames.Add(new AudioFrame
            {
                Text = text,
                IsGenerated = false
            });
        }

        // Pre-generate all audio files
        public async Task PreGenerateAllAsync()
        {
            // Start generating first frame
            if (_frames.Count > 0 && !_frames[0].IsGenerated)
            {
                _nextAudioTask = GenerateFrameAudioAsync(0);
                await _nextAudioTask;
                _nextFrameIndex = 1;
            }

            // Pre-generate remaining in pipeline
            for (int i = 1; i < _frames.Count; i++)
            {
                if (!_frames[i].IsGenerated)
                {
                    var currentTask = _nextAudioTask;
                    _nextAudioTask = GenerateFrameAudioAsync(i);
                    await currentTask;
                    _nextFrameIndex = i + 1;
                }
            }
            
            if (_nextAudioTask != null)
            {
                await _nextAudioTask;
            }
        }

        private async Task<string> GenerateFrameAudioAsync(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= _frames.Count)
                return null;

            var frame = _frames[frameIndex];
            if (frame.IsGenerated && !string.IsNullOrEmpty(frame.AudioFilePath))
                return frame.AudioFilePath;

            frame.AudioFilePath = await _ttsService.SynthesizeAsync(frame.Text);
            frame.IsGenerated = true;
            return frame.AudioFilePath;
        }

        // Play sequence starting from beginning
        public async Task PlayAsync()
        {
            if (_frames.Count == 0)
                return;

            // Ensure first frame is ready
            if (!_frames[0].IsGenerated)
            {
                await GenerateFrameAudioAsync(0);
                _nextFrameIndex = 1;
                if (_nextFrameIndex < _frames.Count)
                {
                    _nextAudioTask = GenerateFrameAudioAsync(_nextFrameIndex);
                    _nextFrameIndex++;
                }
            }
            else if (_nextFrameIndex <= 1 && _frames.Count > 1)
            {
                // Start pipeline if not started
                if (_nextAudioTask == null)
                {
                    _nextAudioTask = GenerateFrameAudioAsync(1);
                    _nextFrameIndex = 2;
                }
            }

            for (int i = 0; i < _frames.Count; i++)
            {
                _currentFrameIndex = i;
                
                // Update text for current frame
                if (_onFrameTextUpdate != null)
                {
                    _onFrameTextUpdate(i, _frames[i].Text);
                }

                // Ensure current frame audio is ready
                if (!_frames[i].IsGenerated)
                {
                    await GenerateFrameAudioAsync(i);
                }

                // Start generating next frame while current plays (pipeline)
                Task nextGenTask = null;
                if (i + 1 < _frames.Count && !_frames[i + 1].IsGenerated)
                {
                    nextGenTask = GenerateFrameAudioAsync(i + 1);
                }

                // Play current frame
                await PlayAudioFileAsync(_frames[i].AudioFilePath);

                // Wait for next generation if it was started
                if (nextGenTask != null)
                {
                    await nextGenTask;
                }
            }
        }

        private async Task PlayAudioFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            var tcs = new TaskCompletionSource<bool>();

            try
            {
                _audioReader = new AudioFileReader(filePath);
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioReader);
                _waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult(true);
                _waveOut.Play();

                await tcs.Task;
            }
            finally
            {
                _waveOut?.Dispose();
                _audioReader?.Dispose();
                _waveOut = null;
                _audioReader = null;

                // Clean up temp file
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch { }
            }
        }

        public void Stop()
        {
            try
            {
                _waveOut?.Stop();
            }
            catch { }
        }

        // Compatibility method for old API
        public async Task PlaySequenceWithTtsAsync(List<FaceFrame> sequence, System.Threading.CancellationToken token = default)
        {
            if (sequence == null || sequence.Count == 0)
                return;

            // Clear existing frames
            _frames.Clear();
            
            foreach (var frame in sequence)
            {
                string text = frame.frame_text ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    AddFrame(text);
                }
                else
                {
                    AddFrame(" ");
                }
            }

            // Play with cancellation support
            if (token.IsCancellationRequested)
                return;

            await PlayAsync();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            Stop();
            _waveOut?.Dispose();
            _audioReader?.Dispose();
            _frames.Clear();
        }
    }
}
