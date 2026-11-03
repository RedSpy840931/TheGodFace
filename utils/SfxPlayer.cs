// Version: 3 (plays per-preset sound files from disk as well as the embedded cues)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TheGodFace.Utils
{
    // Plays the short interface cues that ship inside the assembly, one voice per cue.
    public class SfxPlayer : IDisposable
    {
        // Cue keys, also used to keep a cue from overlapping itself.
        private const string InitializingCue = "initializing";
        private const string BlinkCue = "blink";
        private const string EyeMoveCue = "eye move";
        private const string ExpressionCue = "expression change";
        // Prefix that keeps the preset files in their own cue namespace.
        private const string PresetFileCuePrefix = "file:";

        // Eye-move cue plays well below the rest of the cues.
        private const float EyeMoveVolume = 0.28f;
        // Expression cue sits under the default loudness, slightly above the eye cue.
        private const float ExpressionVolume = 0.5f;
        // Blink cue drops a bit less than the other two, so the eyes stay noticeable.
        private const float BlinkVolume = 0.7f;
        // Eye-move wobble: five percent either way, which shifts speed and pitch by the same amount.
        private const double EyeMoveSpeedSpread = 0.05;

        // Guards the cue tables and the cached samples.
        private readonly object _gate = new object();
        // Picks one of the two eye-move files and its speed wobble.
        private readonly Random _random = new Random();
        // Devices currently sounding, keyed by cue so a busy cue is simply skipped.
        private readonly Dictionary<string, WaveOutEvent> _playing = new Dictionary<string, WaveOutEvent>();
        // Decoded samples per resolved source, reused so a repeat cue needs no decoding work.
        private readonly Dictionary<string, DecodedCue> _cache = new Dictionary<string, DecodedCue>();
        // Names of the embedded sounds, read once at construction.
        private readonly string[] _resources;
        private bool _disposed;

        // One decoded cue: all samples in one buffer plus the format they belong to.
        private sealed class DecodedCue
        {
            public float[] Samples = Array.Empty<float>();
            public WaveFormat? Format;
        }

        // Reads a cue at a fractional step, which changes speed and pitch together.
        private sealed class VarispeedSampleProvider : ISampleProvider
        {
            private readonly float[] _samples;
            private readonly int _channels;
            // One step per output frame: 1.0 is the original speed, 1.05 is five percent faster.
            private readonly double _step;
            // Read position in source frames, the fraction selects the interpolation weight.
            private double _position;

            // Format handed to the output device, identical to the decoded source format.
            public WaveFormat WaveFormat { get; }

            // Wraps the decoded buffer and reports the source format upward.
            public VarispeedSampleProvider(float[] samples, WaveFormat format, double step)
            {
                _samples = samples;
                _channels = format.Channels;
                _step = step;
                WaveFormat = format;
            }

            // Fills the request with linearly interpolated frames and returns the samples written.
            public int Read(float[] buffer, int offset, int count)
            {
                int written = 0;
                while (written + _channels <= count)
                {
                    int frame = (int)_position;
                    int next = frame + 1;
                    // Past the last frame: report the end so the device raises PlaybackStopped.
                    if ((next * _channels) + _channels > _samples.Length) break;

                    double fraction = _position - frame;
                    for (int channel = 0; channel < _channels; channel++)
                    {
                        float current = _samples[(frame * _channels) + channel];
                        float following = _samples[(next * _channels) + channel];
                        buffer[offset + written + channel] = (float)(current + ((following - current) * fraction));
                    }

                    written += _channels;
                    _position += _step;
                }
                return written;
            }
        }

        // Collects the embedded resource names so file names can be matched loosely later.
        public SfxPlayer()
        {
            _resources = typeof(SfxPlayer).Assembly.GetManifestResourceNames();
        }

        // Startup cue, meant to run together with the splash animation.
        public void PlayInitialization() => PlayOnce(InitializingCue, "Initializing.mp3", 1.0, 1.0f);

        // Eye-close cue, fired at the start of every blink.
        public void PlayBlink() => PlayOnce(BlinkCue, "blink.mp3", 1.0, BlinkVolume);

        // Random pupil-motion cue, quieter than the rest and slightly detuned on every play.
        public void PlayEyeMove()
        {
            string file = _random.Next(2) == 0 ? "eye move 1.mp3" : "eye move 2.mp3";
            // Symmetric spread, so every play lands somewhere between 0.95x and 1.05x.
            double step = 1.0 + (((_random.NextDouble() * 2.0) - 1.0) * EyeMoveSpeedSpread);
            PlayOnce(EyeMoveCue, file, step, EyeMoveVolume);
        }

        // Face-change cue; a second request is dropped while the first is still audible.
        public void PlayExpressionChange() => PlayOnce(ExpressionCue, "expression change.mp3", 1.0, ExpressionVolume);

        // Sound attached to a preset: a loose file first, an embedded copy if the file is missing.
        public void PlayPresetSound(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            // The normalized path is the cue key, so the same file from two presets sounds once.
            PlayOnce(PresetFileCuePrefix + Normalize(path), path, 1.0, ExpressionVolume);
        }

        // Starts one cue on its own output device and returns at once.
        private void PlayOnce(string cue, string source, double speed, float volume)
        {
            lock (_gate)
            {
                if (_disposed) return;
                // Same cue still audible: skip instead of stacking two copies.
                if (_playing.ContainsKey(cue)) return;

                DecodedCue? decoded = GetCue(source);
                if (decoded?.Format == null) return;

                WaveOutEvent? output = null;
                try
                {
                    var provider = new VarispeedSampleProvider(decoded.Samples, decoded.Format, speed);
                    output = new WaveOutEvent { Volume = volume };
                    _playing[cue] = output;
                    output.PlaybackStopped += (_, __) => Release(cue, output);
                    output.Init(provider.ToWaveProvider());
                    output.Play();
                }
                catch (Exception ex)
                {
                    // A missing or busy audio device must not break the face behavior.
                    System.Diagnostics.Debug.WriteLine($"sfx {source} failed: {ex.Message}");
                    Release(cue, output);
                }
            }
        }

        // Frees the device of a finished cue, ignoring callbacks from an older playback.
        private void Release(string cue, WaveOutEvent? output)
        {
            WaveOutEvent? toDispose = null;

            lock (_gate)
            {
                // A newer playback already took this slot, so the callback owns nothing.
                if (output != null && _playing.TryGetValue(cue, out var current) && ReferenceEquals(current, output))
                {
                    toDispose = current;
                    _playing.Remove(cue);
                }
            }

            if (toDispose != null) { try { toDispose.Dispose(); } catch { } }
        }

        // Returns the decoded samples of a source, decoding it once per session.
        private DecodedCue? GetCue(string source)
        {
            Stream? raw = OpenSource(source, out string cacheKey);
            if (raw == null) return null;

            // Another cue already decoded the same file.
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                try { raw.Dispose(); } catch { }
                return cached;
            }

            try
            {
                using (raw)
                using (WaveStream reader = OpenReader(raw, source))
                {
                    ISampleProvider sampleSource = reader.ToSampleProvider();
                    int channels = sampleSource.WaveFormat.Channels;
                    var samples = new List<float>((int)(reader.TotalTime.TotalSeconds * sampleSource.WaveFormat.SampleRate * channels) + channels);
                    var buffer = new float[sampleSource.WaveFormat.SampleRate * channels];
                    int read;
                    while ((read = sampleSource.Read(buffer, 0, buffer.Length)) > 0)
                        for (int i = 0; i < read; i++) samples.Add(buffer[i]);

                    var cue = new DecodedCue { Samples = samples.ToArray(), Format = sampleSource.WaveFormat };
                    _cache[cacheKey] = cue;
                    return cue;
                }
            }
            catch (Exception ex)
            {
                // A broken file must not take the window down, the cue just stays silent.
                System.Diagnostics.Debug.WriteLine($"sfx {source} decode failed: {ex.Message}");
                return null;
            }
        }

        // Opens the audio data of a cue: a file on disk first, then the embedded copy.
        private Stream? OpenSource(string source, out string cacheKey)
        {
            string full = IsRooted(source) ? source : PathHelper.GetFullPath(source);
            if (!string.IsNullOrEmpty(full) && File.Exists(full))
            {
                cacheKey = full;
                return File.OpenRead(full);
            }

            // Packaged build without an assets folder: the sound travels inside the assembly.
            string fileName = Path.GetFileName(source);
            Stream? embedded = OpenResource(fileName);
            cacheKey = "embedded:" + fileName;
            return embedded;
        }

        // Builds the decoder that matches the file type, mp3 unless the name says wav.
        private static WaveStream OpenReader(Stream stream, string source) =>
            Path.GetExtension(source).Equals(".wav", StringComparison.OrdinalIgnoreCase)
                ? new WaveFileReader(stream)
                : new Mp3FileReader(stream);

        // Tells whether the stored path is already absolute.
        private static bool IsRooted(string path)
        {
            try { return Path.IsPathRooted(path); } catch { return false; }
        }

        // Opens the embedded resource of a cue, matching the file name with spaces and dots ignored.
        private Stream? OpenResource(string fileName)
        {
            string wanted = Normalize(fileName);
            foreach (string resource in _resources)
            {
                if (!Normalize(resource).EndsWith(wanted, StringComparison.Ordinal)) continue;
                Stream? stream = typeof(SfxPlayer).Assembly.GetManifestResourceStream(resource);
                if (stream != null) return stream;
            }
            return null;
        }

        // Strips everything but letters and digits, so "eye move 1.mp3" matches any resource name form.
        private static string Normalize(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            return builder.ToString();
        }

        // Stops every cue and drops the devices, called when the window closes.
        public void Dispose()
        {
            List<WaveOutEvent> devices;

            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                devices = new List<WaveOutEvent>(_playing.Values);
                _playing.Clear();
                _cache.Clear();
            }

            foreach (var device in devices)
            {
                try { device.Stop(); } catch { }
                try { device.Dispose(); } catch { }
            }
        }
    }
}
