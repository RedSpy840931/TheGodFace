// Version: 6 (GIF splash: real per-frame delays, centered layout, collapse-to-line transition)
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TheGodFace.Utils
{
    // Plays the embedded initialization GIFs on the splash canvas and hands the face back on completion.
    public sealed class InitializerAnimator
    {
        // Splash size in 3D mode as a fraction of the smaller canvas side, 1.0 = full width.
        public const double Scale3D = 1.0;
        // Horizontal shift from the cube face center, in canvas pixels.
        public const double OffsetX3D = -150.0;
        // Vertical shift from the cube face center, in canvas pixels.
        public const double OffsetY3D = -350.0;

        // Splash size in 2D (maximized) mode, where the canvas equals the window size.
        public const double Scale2D = 2.0;
        // Horizontal shift from the window center in 2D mode.
        public const double OffsetX2D = 0.0;
        // Vertical shift from the window center in 2D mode.
        public const double OffsetY2D = 30.0;

        // Pause after the start GIF, so a finished initialization never flashes the loop.
        private const int LoopGraceMs = 300;
        // Frame delay used when a GIF frame carries no delay tag.
        private const int DefaultFrameDelayMs = 100;

        // Squash-to-line duration in milliseconds for the TV-off handover, 0 makes the splash vanish at once.
        public const int CollapseMs = 50;

        // Canvas that hosts the splash frames, normally FaceCanvas inside the face layout.
        private readonly Canvas _host;
        // Image element that draws the current GIF frame on top of the face.
        private readonly Image _image;
        // Vertical squash of the splash, driven manually because 3D hosting ignores RenderTransformOrigin.
        private readonly ScaleTransform _squash = new(1.0, 1.0);
        // Compensating shift that keeps the squash axis in the middle of the GIF.
        private readonly TranslateTransform _squashShift = new(0.0, 0.0);
        // Timer that advances GIF frames and squashes the splash, runs on the dispatcher.
        private readonly DispatcherTimer _frameTimer;
        // Decoder of the one-shot start GIF, null when the resource is missing.
        private readonly BitmapDecoder? _startDecoder;
        // Decoder of the looping GIF shown when initialization outlives the start animation.
        private readonly BitmapDecoder? _loopDecoder;
        // Per-frame delays of the start GIF in milliseconds, read from GIF metadata.
        private readonly int[] _startDelays;
        // Per-frame delays of the loop GIF in milliseconds, read from GIF metadata.
        private readonly int[] _loopDelays;

        // Playback states: start GIF, grace pause, loop GIF, or nothing.
        private enum Phase { Idle, Start, Gap, Loop }

        // Current playback state, Idle also means the splash is removed.
        private Phase _phase = Phase.Idle;
        // Index of the frame currently shown by either decoder.
        private int _frameIndex;
        // True when the splash is centered in the window (2D mode) instead of the cube face.
        private bool _centered;
        // Completed once the last start frame is drawn, awaited by MainWindow_Loaded.
        private TaskCompletionSource<bool> _startTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Awaitable signal that the start GIF reached its last frame.
        public Task StartCompleted => _startTcs.Task;

        // True while any splash phase is on screen, including the grace pause.
        public bool IsRunning => _phase != Phase.Idle;

        // Switches between window-centered (2D) and face-centered (3D) layout and repositions the splash.

        public bool Centered
        {
            get => _centered;
            set
            {
                _centered = value;
                if (IsRunning) Relayout();
            }
        }

        // Builds the splash image, loads both GIF decoders and primes the frame timer.
        public InitializerAnimator(Canvas host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));

            _image = new Image
            {
                Stretch = Stretch.Uniform,
                Visibility = Visibility.Visible,
                RenderTransform = new TransformGroup { Children = { _squash, _squashShift } }
            };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            Panel.SetZIndex(_image, 999);

            _frameTimer = new DispatcherTimer(DispatcherPriority.Render);
            _frameTimer.Tick += OnFrameTick;

            _startDecoder = LoadDecoder("TheGodFace.assets.decals.initializing_start.gif");
            _loopDecoder = LoadDecoder("TheGodFace.assets.decals.initializing_loop.gif");
            _startDelays = ReadDelays(_startDecoder);
            _loopDelays = ReadDelays(_loopDecoder);
        }

        // Adds the splash to the host canvas and starts the one-shot GIF from frame zero.
        public void Start()
        {
            if (IsRunning) return;

            _startTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_host.Children.Contains(_image)) _host.Children.Add(_image);
            _image.Opacity = 1.0;

            _phase = Phase.Start;
            _frameIndex = 0;

            if (_startDecoder == null || _startDecoder.Frames.Count == 0)
            {
                // Missing resource must not block startup, so the wait completes immediately.
                _phase = Phase.Gap;
                _startTcs.TrySetResult(true);
                return;
            }

            ShowFrame(_startDecoder, 0);
            _frameTimer.Interval = TimeSpan.FromMilliseconds(_startDelays[0]);
            _frameTimer.Start();
            Relayout();
        }

        // Immediate removal path used when the splash is cancelled instead of animated away.
        public Task StopAsync()
        {
            if (_phase == Phase.Idle) return Task.CompletedTask;

            _phase = Phase.Idle;
            _frameTimer.Stop();
            _squash.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _squashShift.BeginAnimation(TranslateTransform.YProperty, null);
            _squash.ScaleY = 1.0;
            _squashShift.Y = 0.0;
            try
            {
                if (_host.Children.Contains(_image)) _host.Children.Remove(_image);
            }
            catch { }
            _image.Source = null;

            _startTcs.TrySetResult(true);
            return Task.CompletedTask;
        }

        // Fire-and-forget stop used by the window Closed handler.
        public void Cancel() => _ = StopAsync();

        // Ends the splash like an old TV: squashes it into a line, then removes it from the canvas.
        public async Task CollapseAsync()
        {
            if (_phase == Phase.Idle) return;

            // Freeze the frame sequence but keep the last picture visible while it squashes.
            _phase = Phase.Idle;
            _frameTimer.Stop();
            _startTcs.TrySetResult(true);

            try
            {
                if (_image.Source != null && _host.Children.Contains(_image))
                    await SquashToLineAsync();
            }
            finally
            {
                // The picture must leave either way, and the transforms must return to neutral.
                try
                {
                    if (_host.Children.Contains(_image)) _host.Children.Remove(_image);
                }
                catch { }
                _image.Source = null;

                _squash.ScaleY = 1.0;
                _squashShift.Y = 0.0;
            }
        }

        // Squashes the GIF around its own vertical center with p' = p*S + center*(1-S) and awaits the last tick.
        private Task SquashToLineAsync()
        {
            // Layout height of the image element, which also holds the centered GIF content.
            double frameHeight = _image.Height > 0 ? _image.Height : _image.ActualHeight;
            // Axis of the squash, the middle of the GIF content.
            double center = frameHeight / 2.25;
            // Clamped duration so a zero setting still finishes within one timer tick.
            double durationMs = Math.Max(CollapseMs, 1);

            var done = new TaskCompletionSource<bool>();
            var clock = Stopwatch.StartNew();
            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            timer.Tick += (_, __) =>
            {
                // Normalized progress 0..1 driven by the wall clock, so dropped ticks do not stretch the animation.
                double t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / durationMs);
                _squash.ScaleY = 1.0 - t;
                _squashShift.Y = center * t;

                if (t >= 1.0)
                {
                    timer.Stop();
                    done.TrySetResult(true);
                }
            };

            timer.Start();
            return done.Task;
        }

        // Recomputes size and position of the splash after a mode switch or window resize.
        public void Relayout()
        {
            if (!IsRunning) return;

            // Explicit Width/Height come from RefreshAll and are fresher than ActualWidth, which lags a mode switch.
            double hostW = _host.Width;
            double hostH = _host.Height;
            if (double.IsNaN(hostW) || hostW <= 0) hostW = _host.ActualWidth;
            if (double.IsNaN(hostH) || hostH <= 0) hostH = _host.ActualHeight;
            if (double.IsNaN(hostW) || hostW <= 0) hostW = 1000;
            if (double.IsNaN(hostH) || hostH <= 0) hostH = 1000;

            double scale = _centered ? Scale2D : Scale3D;
            double offsetX = _centered ? OffsetX2D : OffsetX3D;
            double offsetY = _centered ? OffsetY2D : OffsetY3D;

            double size = Math.Min(hostW, hostH) * scale;
            _image.Width = size;
            _image.Height = size;

            Canvas.SetLeft(_image, (hostW - size) / 2.0 + offsetX);
            Canvas.SetTop(_image, (hostH - size) / 2.0 + offsetY);
        }

        // Advances the start or loop GIF by one frame using the per-frame delay from metadata.
        private void OnFrameTick(object? sender, EventArgs e)
        {
            if (_phase == Phase.Start)
            {
                if (_startDecoder == null) return;

                _frameIndex++;
                if (_frameIndex < _startDecoder.Frames.Count)
                {
                    ShowFrame(_startDecoder, _frameIndex);
                    _frameTimer.Interval = TimeSpan.FromMilliseconds(_startDelays[_frameIndex]);
                }
                else
                {
                    // The last frame stays on screen while the grace pause runs.
                    _frameTimer.Stop();
                    _phase = Phase.Gap;
                    _startTcs.TrySetResult(true);
                    ScheduleLoop();
                }
                return;
            }

            if (_phase == Phase.Loop && _loopDecoder != null && _loopDecoder.Frames.Count > 0)
            {
                _frameIndex++;
                if (_frameIndex >= _loopDecoder.Frames.Count) _frameIndex = 0;
                ShowFrame(_loopDecoder, _frameIndex);
                _frameTimer.Interval = TimeSpan.FromMilliseconds(_loopDelays[_frameIndex]);
            }
        }

        // Waits out the grace pause on a background task before switching to the loop GIF.
        private void ScheduleLoop()
        {
            _ = Task.Delay(LoopGraceMs).ContinueWith(_ =>
            {
                _host.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_phase != Phase.Gap) return;
                    if (_loopDecoder == null || _loopDecoder.Frames.Count == 0) return;

                    _phase = Phase.Loop;
                    _frameIndex = 0;
                    ShowFrame(_loopDecoder, 0);
                    _frameTimer.Interval = TimeSpan.FromMilliseconds(_loopDelays[0]);
                    _frameTimer.Start();
                }));
            });
        }

        // Pushes one decoder frame into the image element, ignoring out-of-range indexes.
        private void ShowFrame(BitmapDecoder decoder, int index)
        {
            if (index < 0 || index >= decoder.Frames.Count) return;
            _image.Source = decoder.Frames[index];
        }

        // Reads one GIF from the assembly manifest, returning null when the resource is absent.
        private static BitmapDecoder? LoadDecoder(string resourceName)
        {
            try
            {
                var stream = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(resourceName);
                if (stream == null) return null;

                return BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
            }
            catch { return null; }
        }

        // Converts the GIF frame delays from hundredths of a second into milliseconds.
        private static int[] ReadDelays(BitmapDecoder? decoder)
        {
            if (decoder == null || decoder.Frames.Count == 0) return Array.Empty<int>();

            var delays = new int[decoder.Frames.Count];
            for (int i = 0; i < delays.Length; i++)
            {
                int delay = DefaultFrameDelayMs;
                try
                {
                    if (decoder.Frames[i].Metadata is BitmapMetadata meta)
                    {
                        object? raw = meta.GetQuery("/grctlext/Delay");
                        int hundredths = raw switch
                        {
                            ushort us => us,
                            int i32 => i32,
                            _ => 0
                        };
                        if (hundredths > 0) delay = hundredths * 10;
                    }
                }
                catch { }
                delays[i] = delay;
            }
            return delays;
        }
    }
}
