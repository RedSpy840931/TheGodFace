using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;
using TheGodFace.Models;

namespace TheGodFace.Utils
{
    public class AudioFramePair
    {
        public FaceFrame Frame { get; set; } = new FaceFrame();
        public string? AudioFilePath { get; set; }
        public double DurationSeconds { get; set; }
    }

    // Coordinates TTS generation and synchronized playback with animation frames
    public class AudioSequencePlayer
    {
        private static PiperTtsService? _sharedTtsService;
        private readonly PiperTtsService _ttsService;
        private readonly MainWindow _mainWindow;

        public AudioSequencePlayer(MainWindow mainWindow, string voiceName = "ic0n_voice.onnx")
        {
            _mainWindow = mainWindow;
            if (_sharedTtsService == null)
                _sharedTtsService = new PiperTtsService(voiceName);
            _ttsService = _sharedTtsService;
        }

        // Get duration of WAV file in seconds using NAudio
        private double GetWavDuration(string filePath)
        {
            try
            {
                using var reader = new WaveFileReader(filePath);
                return reader.TotalTime.TotalSeconds;
            }
            catch
            {
                return 0;
            }
        }

        // Play sequence with TTS - pipeline: generate next while playing current
        public async Task PlaySequenceWithTtsAsync(List<FaceFrame> sequence, CancellationToken token = default)
        {
            if (sequence == null || sequence.Count == 0) return;

            // Skip last frame if it's the idle frame with null duration and no text
            var framesToPlay = new List<FaceFrame>(sequence.Count);
            foreach (var frame in sequence)
            {
                if (frame.duration == null && string.IsNullOrWhiteSpace(frame.frame_text))
                    continue;
                framesToPlay.Add(frame);
            }

            if (framesToPlay.Count == 0) return;

            // Pre-generate first frame
            Task<AudioFramePair>? nextGeneration = null;
            
            for (int i = 0; i < framesToPlay.Count; i++)
            {
                if (token.IsCancellationRequested) break;
                
                AudioFramePair pair;
                if (i == 0 || nextGeneration == null)
                {
                    pair = await GenerateAudioForFrame(framesToPlay[i], token);
                }
                else
                {
                    pair = await nextGeneration;
                }
                
                // Start generating next frame while current plays
                if (i + 1 < framesToPlay.Count)
                {
                    nextGeneration = GenerateAudioForFrame(framesToPlay[i + 1], token);
                }
                else
                {
                    nextGeneration = null;
                }
                
                // Apply face frame on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() => 
                {
                    _mainWindow.ApplyFaceFrame(pair.Frame);
                });

                // Play audio if exists
                if (!string.IsNullOrEmpty(pair.AudioFilePath) && File.Exists(pair.AudioFilePath))
                {
                    try
                    {
                        // Show text only when audio playback starts
                        await Application.Current.Dispatcher.InvokeAsync(() => 
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(pair.Frame.frame_text))
                                {
                                    _mainWindow.SetAiReplyText(pair.Frame.frame_text);
                                }
                            }
                            catch { }
                        });

                        using var audioFile = new AudioFileReader(pair.AudioFilePath);
                        using var outputDevice = new WaveOutEvent();
                        outputDevice.Init(audioFile);
                        outputDevice.Play();
                        
                        double waitTime = pair.DurationSeconds;
                        if (waitTime < 0.2) waitTime = 0.2;
                        if (waitTime > 15.0) waitTime = 15.0;
                        
                        await Task.Delay(TimeSpan.FromSeconds(waitTime), token);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Audio playback error: {ex.Message}");
                        await Application.Current.Dispatcher.InvokeAsync(() => 
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(pair.Frame.frame_text))
                                {
                                    _mainWindow.SetAiReplyText(pair.Frame.frame_text);
                                }
                            }
                            catch { }
                        });
                        double waitTime = pair.DurationSeconds > 0 ? pair.DurationSeconds : 1.0;
                        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(waitTime, 0.2, 5.0)), token);
                    }
                    finally
                    {
                        try { if (File.Exists(pair.AudioFilePath)) File.Delete(pair.AudioFilePath); } catch { }
                    }
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => 
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(pair.Frame.frame_text))
                            {
                                _mainWindow.SetAiReplyText(pair.Frame.frame_text);
                            }
                        }
                        catch { }
                    });
                    double waitTime = pair.DurationSeconds > 0 ? pair.DurationSeconds : 1.0;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(waitTime, 0.2, 5.0)), token);
                }
            }
        }

        private async Task<AudioFramePair> GenerateAudioForFrame(FaceFrame frame, CancellationToken token)
        {
            string text = frame.frame_text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return new AudioFramePair
                {
                    Frame = frame,
                    AudioFilePath = null,
                    DurationSeconds = frame.duration ?? 1.0
                };
            }

            try
            {
                string wavPath = await _ttsService.SynthesizeAsync(text);
                if (token.IsCancellationRequested)
                {
                    try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
                    return new AudioFramePair
                    {
                        Frame = frame,
                        AudioFilePath = null,
                        DurationSeconds = frame.duration ?? 1.0
                    };
                }
                
                double duration = GetWavDuration(wavPath);
                if (duration < 0.1) duration = frame.duration ?? 1.0;
                
                frame.duration = duration;
                
                return new AudioFramePair
                {
                    Frame = frame,
                    AudioFilePath = wavPath,
                    DurationSeconds = duration
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TTS error: {ex.Message}");
                return new AudioFramePair
                {
                    Frame = frame,
                    AudioFilePath = null,
                    DurationSeconds = frame.duration ?? 1.0
                };
            }
        }
    }
}
