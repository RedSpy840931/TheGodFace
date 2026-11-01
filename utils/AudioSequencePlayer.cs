using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using TheGodFace.Utils;

namespace TheGodFace.Utils
{
    // Plays generated speech frame by frame and shows the face pose attached to the same frame
    public class AudioSequencePlayer : IDisposable
    {
        // Fallback hold time for a frame when Piper produced no audio file
        private const double SilentFrameSeconds = 1.0;

        private readonly PiperTtsService _ttsService;
        private readonly Action<int, string> _onFrameTextUpdate;
        private readonly Action<FaceFrame> _onFaceFrameUpdate;
        private WaveOutEvent _waveOut;
        private AudioFileReader _audioReader;
        private bool _disposed;

        // One spoken frame: its text, the wav Piper wrote for it and the pose to show meanwhile
        private class AudioFrame
        {
            public string Text { get; set; }
            public string AudioFilePath { get; set; }
            public bool IsGenerated { get; set; }
            public FaceFrame FaceFrame { get; set; }
        }

        // Stores the callbacks MainWindow uses to update the reply text and the face
        public AudioSequencePlayer(Action<int, string> onFrameTextUpdate = null, Action<FaceFrame> onFaceFrameUpdate = null)
        {
            _ttsService = PiperTtsService.Instance;
            _onFrameTextUpdate = onFrameTextUpdate;
            _onFaceFrameUpdate = onFaceFrameUpdate;
        }

        // Turns the model sequence into internal frames, keeping the pose next to its text
        private static List<AudioFrame> BuildFrames(List<FaceFrame> sequence)
        {
            var frames = new List<AudioFrame>(sequence.Count);

            foreach (var frame in sequence)
            {
                frames.Add(new AudioFrame
                {
                    Text = frame?.frame_text ?? "",
                    IsGenerated = false,
                    FaceFrame = frame
                });
            }

            return frames;
        }

        // True when Piper can actually speak the text, dots and spaces count as silence
        private static bool IsSpeakable(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                    return true;
            }

            return false;
        }

        // Synthesizes one frame and returns the wav path, null when the frame is silent or synthesis failed
        private async Task<string> GenerateFrameAudioAsync(AudioFrame frame)
        {
            // Mark as generated first, so a failed frame is not retried in a loop
            if (frame.IsGenerated)
                return frame.AudioFilePath;

            frame.IsGenerated = true;

            if (!IsSpeakable(frame.Text))
                return null;

            try
            {
                frame.AudioFilePath = await _ttsService.SynthesizeAsync(frame.Text);
            }
            catch (Exception ex)
            {
                // A failed frame must not break the sequence, the face still gets its time
                frame.AudioFilePath = null;
                System.Diagnostics.Debug.WriteLine($"TTS frame failed: {ex.Message}");
            }

            return frame.AudioFilePath;
        }

        // Plays every frame, applying its pose and reply text, and uses the real wav length as the frame clock
        public async Task PlaySequenceWithTtsAsync(List<FaceFrame> sequence, CancellationToken token = default)
        {
            if (sequence == null || sequence.Count == 0)
                return;

            // Own frame list per call, so a cancelled run cannot corrupt the next one
            var frames = BuildFrames(sequence);

            Stop();

            // The first frame is synthesized before playback, the rest are generated while the previous one sounds
            Task<string> nextAudioTask = GenerateFrameAudioAsync(frames[0]);

            for (int i = 0; i < frames.Count; i++)
            {
                if (token.IsCancellationRequested)
                    return;

                var frame = frames[i];
                bool speakable = IsSpeakable(frame.Text);

                // Pose first, so the face reacts even while its audio is still being synthesized
                if (_onFaceFrameUpdate != null && frame.FaceFrame != null)
                {
                    _onFaceFrameUpdate(frame.FaceFrame);
                }

                // Silent frames (the resting pose) keep the previous reply line untouched
                if (_onFrameTextUpdate != null && speakable)
                {
                    _onFrameTextUpdate(i, frame.Text);
                }

                // Wait for the audio of this frame, the task was started before the previous frame finished
                if (nextAudioTask != null)
                {
                    await nextAudioTask;
                    nextAudioTask = null;
                }

                if (token.IsCancellationRequested)
                    return;

                // Start the next frame synthesis so it overlaps with the current playback
                if (i + 1 < frames.Count && !frames[i + 1].IsGenerated)
                {
                    nextAudioTask = GenerateFrameAudioAsync(frames[i + 1]);
                }

                if (!speakable)
                    continue;

                bool played = await PlayAudioFileAsync(frame.AudioFilePath, token);

                if (token.IsCancellationRequested)
                    return;

                // No audio for this frame, hold the pose for a moment so the animation stays visible
                if (!played)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(SilentFrameSeconds), token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        // Plays one wav file and returns when it ends or the token cancels, false when there was nothing to play
        private async Task<bool> PlayAudioFileAsync(string filePath, CancellationToken token)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            var tcs = new TaskCompletionSource<bool>();
            WaveOutEvent waveOut = null;

            try
            {
                _audioReader = new AudioFileReader(filePath);
                waveOut = new WaveOutEvent();
                _waveOut = waveOut;
                waveOut.Init(_audioReader);

                // PlaybackStopped fires on a worker thread, so the awaiting loop continues on the UI context
                waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult(true);

                // Cancel only this device, never whichever wav a newer sequence has already started
                using (token.Register(() =>
                {
                    try { waveOut.Stop(); } catch { }
                    tcs.TrySetResult(true);
                }))
                {
                    waveOut.Play();
                    await tcs.Task;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio playback failed: {ex.Message}");
                return false;
            }
            finally
            {
                try { waveOut?.Dispose(); } catch { }
                try { _audioReader?.Dispose(); } catch { }

                if (ReferenceEquals(_waveOut, waveOut))
                    _waveOut = null;

                _audioReader = null;

                // Temp wav is not needed after playback
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch { }
            }
        }

        // Stops the current wav, used when a new answer replaces the running sequence
        public void Stop()
        {
            try
            {
                _waveOut?.Stop();
            }
            catch { }
        }

        // Releases the audio device and forgets the current frame
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            Stop();
            try { _waveOut?.Dispose(); } catch { }
            try { _audioReader?.Dispose(); } catch { }
            _waveOut = null;
            _audioReader = null;
        }
    }
}
