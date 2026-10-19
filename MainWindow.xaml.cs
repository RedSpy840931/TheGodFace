// Version: 48 (TV-style transition: GIF collapses to a line, face stretches out of it)
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Win32;
using TheGodFace.Models;
using TheGodFace.Utils;

namespace TheGodFace
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out Win32Point pt);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point { public int X; public int Y; }

        private const double BaseEyeWidth = 120.0;
        private const double BaseEyeHeight = 200.0;
        private const double FixedLineThickness = 15.0;
        private const double MaxPupilScale = 0.93;
        private const double DiagonalThicknessRatio = 0.9;

        private const int ClicksToOpenMenu = 3;
        private const int ClickTimeoutSeconds = 3;
        private const int BlinkDurationMs = 100;
        // Face reveal duration in milliseconds: stretches the face from a line to full height, 0 disables it.
        // Independent from InitializerAnimator.CollapseMs, which only squashes the splash GIF.
        private const int FaceRevealMs = 150;

        private const double MaxTrackingOffsetRatio = 0.45;
        private const double TrackingSmoothness = 0.15;
        private const double DefaultFactor = 1.0;
        private const double HalfDivisor = 2.0;
        private const double Face3DResolution = 1000.0;
        private const double ZoomStep = 0.5;
        private const double MinZoom = 5.0;
        private const double MaxZoom = 10.0;
        private const double DefaultCameraZ = 5.0;
        private const double HeadTurnThresholdRatio = 0.3;
        private const double CubeRotationSensitivity = 0.05;
        private const double ManualRotationSensitivity = 0.5;
        private const double MaxCubeRotationAngle = 35.0;

        private const int StateCalm = 1;
        private const int StateExcited = 2;
        private const int StateShock = 3;
        private const double StressMultiplierCalm = 1.0;
        private const double StressMultiplierExcited = 0.8;
        private const double StressMultiplierShock = 0.7;
        private const double BagOffsetX = 10.0;
        private const double BagOffsetY = -60.0;
        private const double BagVerticalLen = 70.0;
        private const double BagHorizontalLen = 50.0;

        private double _blushScale = 0.1;
        public double BlushScale { get => _blushScale; set { _blushScale = value; OnPropertyChanged(nameof(BlushScale)); } }

        private double _blushCanvasTop = 100.0;
        public double BlushCanvasTop { get => _blushCanvasTop; set { _blushCanvasTop = value; OnPropertyChanged(nameof(BlushCanvasTop)); } }

        private double _blushBlurScale = 0.725;
        public double BlushBlurScale { get => _blushBlurScale; set { _blushBlurScale = value; OnPropertyChanged(nameof(BlushBlurScale)); } }

        private double _blushBlurOffsetY = 170.0;
        public double BlushBlurOffsetY { get => _blushBlurOffsetY; set { _blushBlurOffsetY = value; OnPropertyChanged(nameof(BlushBlurOffsetY)); } }

        public string BlushFullPath => PathHelper.GetFullPath("assets\\decals\\blush.png");
        public string BlushBlurFullPath => PathHelper.GetFullPath("assets\\decals\\blush_blur.png");

        private bool _hasSweatDrop = false;
        public bool HasSweatDrop { get => _hasSweatDrop; set { _hasSweatDrop = value; OnPropertyChanged(nameof(HasSweatDrop)); } }

        private double _sweatDropScale = 0.45;
        public double SweatDropScale { get => _sweatDropScale; set { _sweatDropScale = value; OnPropertyChanged(nameof(SweatDropScale)); } }

        private double _sweatDropOffsetX = 115.0;
        public double SweatDropOffsetX { get => _sweatDropOffsetX; set { _sweatDropOffsetX = value; OnPropertyChanged(nameof(SweatDropOffsetX)); } }

        private double _sweatDropOffsetY = -20.0;
        public double SweatDropOffsetY { get => _sweatDropOffsetY; set { _sweatDropOffsetY = value; OnPropertyChanged(nameof(SweatDropOffsetY)); } }

        public string SweatDropFullPath => PathHelper.GetFullPath("assets\\decals\\sweat_drop.png");

        private CancellationTokenSource _appLifetimeCts = new CancellationTokenSource();
        private CancellationTokenSource? _idleCts;
        private CancellationTokenSource? _sequencePlaybackCts;
        private bool _isIdleActionRunning = false;
        private DateTime _lastMouseMoveTime = DateTime.Now;
        private Point _lastGlobalMousePos;
        public Point SaccadeOffset { get; set; } = new Point(0, 0);
        private Point? _wanderTarget = null;
        private readonly Random _random = new Random();

        private GeminiService? _geminiService;

        // Splash animator that plays the initialization GIF on FaceCanvas.
        private InitializerAnimator? _initAnimator;
        // Y scale of the face reveal, created here because XAML gives no fields for named Freezables.
        private readonly ScaleTransform _faceRevealScale = new(1.0, 1.0);
        // Y shift that keeps the reveal axis fixed while _faceRevealScale changes.
        private readonly TranslateTransform _faceRevealShift = new(0.0, 0.0);
        private bool _isSplashActive = false;
        private int _leftClickCount = 0;
        private DateTime _lastClickTime = DateTime.MinValue;
        private double _leftTrackingX = 0;
        private double _leftTrackingY = 0;
        private double _rightTrackingX = 0;
        private double _rightTrackingY = 0;
        private double _cubeRotX = 0;
        private double _cubeRotY = 0;
        private double _currentScale = 1.0;
        private double _currentHeightOffset = 0.0;
        private bool _is3DMode = true;
        private bool _isSubmissiveMode = false;
        private bool _isDraggingRMB = false;
        private Point _lastMousePosRMB;
        private bool _isDraggingMouth = false;
        private MouthPreset? _draggedMouth = null;
        private Point _dragStartMousePos;
        private Point _dragStartMouthOffset;

        private bool _gazeFree = true;
        public bool GazeFree
        {
            get => _gazeFree;
            set
            {
                if (_gazeFree == value) return;
                _gazeFree = value;
                OnPropertyChanged(nameof(GazeFree));
                if (value) { GazeLeft = false; GazeRight = false; GazeDown = false; }
                else { UpdateGazeFreeState(); }
            }
        }

        private bool _gazeStraight = false;
        public bool GazeStraight { get => _gazeStraight; set { if (_gazeStraight == value) return; _gazeStraight = value; OnPropertyChanged(nameof(GazeStraight)); } }

        private bool _gazeLeft = false;
        public bool GazeLeft
        {
            get => _gazeLeft;
            set
            {
                if (_gazeLeft == value) return;
                _gazeLeft = value;
                OnPropertyChanged(nameof(GazeLeft));
                if (value) { GazeFree = false; GazeRight = false; }
                UpdateGazeFreeState();
            }
        }

        private bool _gazeRight = false;
        public bool GazeRight
        {
            get => _gazeRight;
            set
            {
                if (_gazeRight == value) return;
                _gazeRight = value;
                OnPropertyChanged(nameof(GazeRight));
                if (value) { GazeFree = false; GazeLeft = false; }
                UpdateGazeFreeState();
            }
        }

        private bool _gazeDown = false;
        public bool GazeDown
        {
            get => _gazeDown;
            set
            {
                if (_gazeDown == value) return;
                _gazeDown = value;
                OnPropertyChanged(nameof(GazeDown));
                if (value) { GazeFree = false; }
                UpdateGazeFreeState();
            }
        }

        // Falls back to a free gaze when no direction flag is left.
        private void UpdateGazeFreeState() { if (!GazeLeft && !GazeRight && !GazeDown && !GazeFree) GazeFree = true; }

        private string _blinkFilePath = "assets\\decals\\blink.png";
        public string BlinkFilePath { get => _blinkFilePath; set { _blinkFilePath = value; OnPropertyChanged(nameof(BlinkFilePath)); OnPropertyChanged(nameof(BlinkFullPath)); } }
        public string BlinkFullPath => PathHelper.GetFullPath(BlinkFilePath);

        private double _blinkScale = 0.1533391326353083;
        public double BlinkScale { get => _blinkScale; set { _blinkScale = value; OnPropertyChanged(nameof(BlinkScale)); } }

        private string _squintFilePath = "assets\\decals\\squint.png";
        public string SquintFilePath { get => _squintFilePath; set { _squintFilePath = value; OnPropertyChanged(nameof(SquintFilePath)); OnPropertyChanged(nameof(SquintFullPath)); } }
        public string SquintFullPath => PathHelper.GetFullPath(SquintFilePath);

        private double _squintScale = 0.17557492170942327;
        public double SquintScale { get => _squintScale; set { _squintScale = value; OnPropertyChanged(nameof(SquintScale)); OnPropertyChanged(nameof(SquintScaleRight)); } }
        public double SquintScaleRight => -SquintScale;

        private bool _isLeftSquinting = false;
        public bool IsLeftSquinting { get => _isLeftSquinting; set { _isLeftSquinting = value; UpdateEyeVisibility(); OnPropertyChanged(nameof(IsLeftSquinting)); } }

        private bool _isRightSquinting = false;
        public bool IsRightSquinting { get => _isRightSquinting; set { _isRightSquinting = value; UpdateEyeVisibility(); OnPropertyChanged(nameof(IsRightSquinting)); } }

        private bool _isLeftMouseHeldOnLeftEye = false;
        private bool _isLeftMouseHeldOnRightEye = false;
        private bool _isBlinking = false;

        public ObservableCollection<MouthPreset> Mouths { get; set; } = new ObservableCollection<MouthPreset>();
        public ObservableCollection<EyebrowPreset> Eyebrows { get; set; } = new ObservableCollection<EyebrowPreset>();
        public ObservableCollection<CheekPreset> Cheeks { get; set; } = new ObservableCollection<CheekPreset>();

        // Subscribes to window lifecycle events and the render loop, then binds the window to itself.
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            // Reveal transform sits on the canvas because RenderTransform of the 3D host visual is ignored.
            FaceCanvas.RenderTransform = new TransformGroup { Children = { _faceRevealScale, _faceRevealShift } };
            // Config and GeminiService load in the background during the splash, so the window renders at once.
            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;
            CompositionTarget.Rendering += OnRendering;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Toggle3DMode(true);
            GetCursorPos(out Win32Point pt);
            _lastGlobalMousePos = new Point(pt.X, pt.Y);

            // Splash phase: face elements and chat stay hidden while the GIF plays.
            HideFaceAndChat();

            // Splash draws on the cube face canvas, or in the window center when the app starts maximized.
            _initAnimator = new InitializerAnimator(FaceCanvas)
            {
                Centered = WindowState == WindowState.Maximized
            };
            _initAnimator.Start();

            // Wait for the start GIF to reach its last frame.
            await _initAnimator.StartCompleted;

            // Wait for config and service, otherwise ApplyLoadedConfig gets null and keeps the defaults.
            await InitializeInBackgroundAsync();

            // Splash is done: squash it into a line and drop it from the canvas.
            if (_initAnimator != null) await _initAnimator.CollapseAsync();

            // Face phase: config applied and elements restored while the canvas is still a line,
            // so the face becomes visible only through the stretch below.
            _initAnimator = null;
            double revealCenter = FaceRevealCenter();
            _faceRevealScale.ScaleY = 0.0;
            _faceRevealShift.Y = revealCenter;

            ApplyLoadedConfig(_loadedConfig);
            _geminiService ??= new GeminiService();
            ShowFaceAndChat();

            // Stretch the face from the line to full height, which ends the handover.
            await RevealFaceAsync(revealCenter);
            StartBehaviors();
        }

        private FaceConfig? _loadedConfig;

        // Loads face_config.json and GeminiService off the UI thread, with a ten second guard
        // so a hung disk keeps the splash from blocking startup forever.
        private async Task InitializeInBackgroundAsync()
        {
            try
            {
                Task initTask = Task.Run(() =>
                {
                    _loadedConfig = ConfigManager.Load();
                    _geminiService = new GeminiService();
                });

                Task winner = await Task.WhenAny(initTask, Task.Delay(TimeSpan.FromSeconds(10)));
                if (winner != initTask)
                {
                    AiReplyText.Text = "ic-0n: инициализация затянулась, запускаюсь без конфига...";
                }
                else
                {
                    await initTask;
                }
            }
            catch (Exception ex)
            {
                AiReplyText.Text = $"ic-0n: ошибка инициализации ({ex.Message}), запускаюсь с дефолтами...";
            }
        }

        // Hides eyes, mouth, blush and chat while the splash owns the canvas.
        private void HideFaceAndChat()
        {
            _isSplashActive = true;
            LeftEyeContainer.Visibility = Visibility.Collapsed;
            RightEyeContainer.Visibility = Visibility.Collapsed;
            MouthsControl.Visibility = Visibility.Collapsed;
            CenterBlushBlurImage.Visibility = Visibility.Collapsed;
            ChatPanel.Visibility = Visibility.Collapsed;
        }

        // Restores the face elements and the chat panel after the splash is gone.
        private void ShowFaceAndChat()
        {
            _isSplashActive = false;
            LeftEyeContainer.Visibility = Visibility.Visible;
            RightEyeContainer.Visibility = Visibility.Visible;
            MouthsControl.Visibility = Visibility.Visible;
            ChatPanel.Visibility = Visibility.Visible;
            UpdateEyeVisibility();
            RefreshAll();
        }

        // Re-centers and repositions the splash after a mode switch or window resize.
        private void UpdateSplashLayout()
        {
            var animator = _initAnimator;
            if (animator == null || !animator.IsRunning) return;

            animator.Centered = WindowState == WindowState.Maximized;
            animator.Relayout();

            // Layout may resize FaceCanvas after this call, so recompute once more on the next dispatcher pass.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (animator.IsRunning) animator.Relayout();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // Vertical center of the canvas that the reveal grows from.
        private double FaceRevealCenter()
        {
            double height = FaceCanvas.Height > 0 ? FaceCanvas.Height : FaceCanvas.ActualHeight;
            return height / 2.0;
        }

        // Stretches the face around the canvas center using p' = p*S + center*(1-S) and awaits the last tick.
        private async Task RevealFaceAsync(double center)
        {
            try
            {
                if (FaceRevealMs > 0)
                {
                    var done = new TaskCompletionSource<bool>();
                    var clock = Stopwatch.StartNew();
                    var timer = new DispatcherTimer(DispatcherPriority.Render)
                    {
                        Interval = TimeSpan.FromMilliseconds(16)
                    };

                    timer.Tick += (_, __) =>
                    {
                        double t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / FaceRevealMs);
                        _faceRevealScale.ScaleY = t;
                        _faceRevealShift.Y = center * (1.0 - t);

                        if (t >= 1.0)
                        {
                            timer.Stop();
                            done.TrySetResult(true);
                        }
                    };

                    timer.Start();
                    await done.Task;
                }
            }
            finally
            {
                // The face must end up at full size no matter how the loop above finished.
                _faceRevealScale.ScaleY = 1.0;
                _faceRevealShift.Y = 0.0;
            }
        }

        // Cancels the splash and every background loop when the window closes.
        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _initAnimator?.Cancel();
            _appLifetimeCts.Cancel();
            _idleCts?.Cancel();
            _sequencePlaybackCts?.Cancel();
        }

        // Sends the chat text to GeminiService and plays the returned sequence or keeps the idle gaze.
        private async void TestAiButton_Click(object sender, RoutedEventArgs e)
        {
            string userText = UserInputBox.Text;
            if (string.IsNullOrWhiteSpace(userText)) return;
            _geminiService ??= new GeminiService();
            AiReplyText.Text = "Думаю...";
            AiReplyText.Foreground = Brushes.Gray;
            _idleCts?.Cancel();
            _sequencePlaybackCts?.Cancel();

            var mouthNames = this.Mouths.Select(m => m.Name).ToList();
            var browNames = this.Eyebrows.Select(b => b.Name).ToList();
            var cheekNames = this.Cheeks.Select(c => c.Name).ToList();

            string currentMouth = this.Mouths.FirstOrDefault(m => m.IsVisible)?.Name ?? "Small closed neutral";
            string currentBrow = this.Eyebrows.FirstOrDefault(b => b.IsVisible)?.Name ?? "Neutral (None)";
            string currentCheek = this.Cheeks.FirstOrDefault(c => c.IsVisible)?.Name ?? "Neutral (None)";
            int currentStress = (int)this.StressSlider.Value;

            var response = await _geminiService.SendMessageAsync(
                userText,
                mouthNames,
                browNames,
                cheekNames,
                currentMouth,
                currentBrow,
                currentCheek,
                currentStress,
                onIntermediateResponse: inter => Dispatcher.Invoke(() =>
                {
                    AiReplyText.Text = $"ic-0n [{inter.UsedModel ?? "резерв"}]: {inter.reply_text}";
                    AiReplyText.Foreground = Brushes.Yellow;
                    if (inter.sequence != null && inter.sequence.Count > 0) _ = PlaySequenceAsync(inter.sequence);
                })
            );

            Dispatcher.Invoke(() =>
            {
                string modelTag = !string.IsNullOrEmpty(response.UsedModel) ? $"[{response.UsedModel}] " : "";
                AiReplyText.Text = $"ic-0n {modelTag}: {response.reply_text}";
                AiReplyText.Foreground = Brushes.LightGreen;
                if (response.sequence != null && response.sequence.Count > 0) _ = PlaySequenceAsync(response.sequence);
                else { this.GazeFree = true; UpdateEyeVisibility(); RefreshAll(); }
            });
        }

        // Plays a model sequence frame by frame and cancels any sequence still running.
        public async Task PlaySequenceAsync(List<FaceFrame> sequence)
        {
            _sequencePlaybackCts?.Cancel();
            _sequencePlaybackCts = new CancellationTokenSource();
            var token = _sequencePlaybackCts.Token;
            try
            {
                for (int i = 0; i < sequence.Count; i++)
                {
                    if (token.IsCancellationRequested) break;
                    var frame = sequence[i];
                    ApplyFaceFrame(frame);
                    if (frame.duration.HasValue && frame.duration.Value > 0)
                    {
                        double delaySec = Math.Clamp(frame.duration.Value, 0.2, 5.0);
                        await Task.Delay(TimeSpan.FromSeconds(delaySec), token);
                    }
                }
            }
            catch (TaskCanceledException) { }
        }

        // Maps one model frame onto the sliders and picks the closest named presets in Mouths, Eyebrows and Cheeks.
        private void ApplyFaceFrame(FaceFrame frame)
        {
            this.StressSlider.Value = Math.Clamp(frame.stress_level, 1, 3);
            this.HasSweatDrop = frame.sweat_drop;
            ApplySquint(frame.squint ?? "none");
            ApplyGaze(frame.gaze ?? "free");

            string targetMouth = (frame.target_mouth ?? "").Trim();
            var matchedMouth = Mouths.FirstOrDefault(m => string.Equals(m.Name.Trim(), targetMouth, StringComparison.OrdinalIgnoreCase))
                ?? Mouths.FirstOrDefault(m => m.Name.Contains(targetMouth, StringComparison.OrdinalIgnoreCase) || targetMouth.Contains(m.Name, StringComparison.OrdinalIgnoreCase))
                ?? Mouths.FirstOrDefault(m => m.IsVisible)
                ?? Mouths.FirstOrDefault();

            if (matchedMouth != null)
                foreach (var m in Mouths) m.IsVisible = (m == matchedMouth);

            string targetBrow = (frame.target_eyebrow ?? "").Trim();
            var matchedBrow = Eyebrows.FirstOrDefault(b => string.Equals(b.Name.Trim(), targetBrow, StringComparison.OrdinalIgnoreCase))
                ?? Eyebrows.FirstOrDefault(b => b.Name.Contains(targetBrow, StringComparison.OrdinalIgnoreCase) || targetBrow.Contains(b.Name, StringComparison.OrdinalIgnoreCase))
                ?? Eyebrows.FirstOrDefault(b => b.IsVisible);

            if (matchedBrow != null)
                foreach (var b in Eyebrows) b.IsVisible = (b == matchedBrow);

            string targetCheek = (frame.target_cheek ?? "").Trim();
            var matchedCheek = Cheeks.FirstOrDefault(c => string.Equals(c.Name.Trim(), targetCheek, StringComparison.OrdinalIgnoreCase))
                ?? Cheeks.FirstOrDefault(c => c.Name.Contains(targetCheek, StringComparison.OrdinalIgnoreCase) || targetCheek.Contains(c.Name, StringComparison.OrdinalIgnoreCase))
                ?? Cheeks.FirstOrDefault(c => c.IsVisible);

            if (matchedCheek != null)
                foreach (var c in Cheeks) c.IsVisible = (c == matchedCheek);

            UpdateEyeVisibility();
            RefreshAll();
        }

        // Applies the squint mode from the model: none, left, right or both.
        private void ApplySquint(string squint)
        {
            switch (squint.ToLowerInvariant())
            {
                case "left": IsLeftSquinting = true; IsRightSquinting = false; break;
                case "right": IsLeftSquinting = false; IsRightSquinting = true; break;
                case "both": IsLeftSquinting = true; IsRightSquinting = true; break;
                default: IsLeftSquinting = false; IsRightSquinting = false; break;
            }
        }

        // Applies the gaze mode and clears the other direction flags on the way.
        private void ApplyGaze(string gaze)
        {
            switch (gaze.ToLowerInvariant())
            {
                case "straight": GazeFree = GazeLeft = GazeRight = GazeDown = false; GazeStraight = true; break;
                case "left": GazeStraight = GazeRight = GazeDown = GazeFree = false; GazeLeft = true; break;
                case "right": GazeStraight = GazeLeft = GazeDown = GazeFree = false; GazeRight = true; break;
                case "down": GazeStraight = GazeLeft = GazeRight = GazeFree = false; GazeDown = true; break;
                default: GazeStraight = GazeLeft = GazeRight = GazeDown = false; GazeFree = true; break;
            }
        }

        // Starts the blink, saccade and idle loops for the rest of the app lifetime.
        private void StartBehaviors()
        {
            var token = _appLifetimeCts.Token;
            _ = ConstantBlinkLoop(token);
            _ = ConstantSaccadeLoop(token);
            _ = IdleMonitorLoop(token);
        }

        // Blinks every four to ten seconds while the gaze stays free.
        private async Task ConstantBlinkLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(_random.Next(4000, 10001), token);
                    if (!token.IsCancellationRequested && GazeFree) await TriggerBlinkAsync();
                }
            }
            catch (TaskCanceledException) { }
        }

        // Shifts the pupils in short random saccades so the eyes never look frozen.
        private async Task ConstantSaccadeLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(_random.Next(2000, 5001), token);
                    for (int i = 0; i < 3; i++)
                    {
                        if (token.IsCancellationRequested) return;
                        SaccadeOffset = new Point(_random.Next(-50, 51), _random.Next(-30, 31));
                        await Task.Delay(1000, token);
                    }
                    SaccadeOffset = new Point(0, 0);
                }
            }
            catch (TaskCanceledException) { }
        }

        // Turns user inactivity into a side glance after a minute and a wandering gaze after two.
        private async Task IdleMonitorLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token);
                    double idleSeconds = (DateTime.Now - _lastMouseMoveTime).TotalSeconds;
                    if (idleSeconds >= 120.0 && !_isIdleActionRunning) _ = RunWanderingTask();
                    else if (idleSeconds >= 60.0 && idleSeconds < 120.0 && !_isIdleActionRunning) _ = RunSideEyeTask();
                }
            }
            catch (TaskCanceledException) { }
        }

        // Idle reaction: looks left, right or down for a few seconds, then blinks back to free gaze.
        private async Task RunSideEyeTask()
        {
            _isIdleActionRunning = true;
            _idleCts = new CancellationTokenSource();
            var token = _idleCts.Token;
            try
            {
                if (GazeFree && !GazeStraight)
                {
                    int dir = _random.Next(3);
                    if (dir == 0) GazeLeft = true;
                    else if (dir == 1) GazeRight = true;
                    else GazeDown = true;
                    await Task.Delay(_random.Next(2000, 4001), token);
                    if (!token.IsCancellationRequested) { await TriggerBlinkAsync(); GazeFree = true; }
                }
            }
            catch (TaskCanceledException) { }
            finally { _isIdleActionRunning = false; }
        }

        // Idle reaction: stares at a random screen point for ten to forty seconds.
        private async Task RunWanderingTask()
        {
            _isIdleActionRunning = true;
            _idleCts = new CancellationTokenSource();
            var token = _idleCts.Token;
            try
            {
                GazeStraight = true;
                GazeFree = GazeLeft = GazeRight = GazeDown = false;
                double sw = SystemParameters.PrimaryScreenWidth;
                double sh = SystemParameters.PrimaryScreenHeight;
                _wanderTarget = new Point(_random.Next((int)sw), _random.Next((int)sh));
                await Task.Delay(_random.Next(10000, 40001), token);
                if (!token.IsCancellationRequested) { _wanderTarget = null; GazeStraight = false; GazeFree = true; }
            }
            catch (TaskCanceledException) { }
            finally { _isIdleActionRunning = false; }
        }

        // Shows the blink decals for BlinkDurationMs and hides them again.
        public async Task TriggerBlinkAsync()
        {
            _isBlinking = true;
            UpdateEyeVisibility();
            await Task.Delay(BlinkDurationMs);
            _isBlinking = false;
            UpdateEyeVisibility();
        }

        private async void ManualBlink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) btn.IsEnabled = false;
            await TriggerBlinkAsync();
            if (sender is Button btn2) btn2.IsEnabled = true;
        }

        // Per-frame loop: reads the global cursor, drives the pupils and tilts the cube toward it.
        private void OnRendering(object? sender, EventArgs e)
        {
            if (!IsVisible || PresentationSource.FromVisual(this) == null) return;
            GetCursorPos(out Win32Point mousePt);
            Point currentGlobalMousePos = new Point(mousePt.X, mousePt.Y);
            double distMoved = Math.Abs(currentGlobalMousePos.X - _lastGlobalMousePos.X) + Math.Abs(currentGlobalMousePos.Y - _lastGlobalMousePos.Y);
            if (distMoved > 5.0)
            {
                _lastGlobalMousePos = currentGlobalMousePos;
                _lastMouseMoveTime = DateTime.Now;
                if (_isIdleActionRunning)
                {
                    _idleCts?.Cancel();
                    _wanderTarget = null;
                    GazeStraight = GazeLeft = GazeRight = GazeDown = false;
                    GazeFree = true;
                    _ = TriggerBlinkAsync();
                }
            }

            Point targetGlobalPos = _wanderTarget ?? currentGlobalMousePos;
            Point mouseInGrid;
            try { mouseInGrid = MainGrid.PointFromScreen(targetGlobalPos); }
            catch { mouseInGrid = new Point(MainGrid.ActualWidth / 2, MainGrid.ActualHeight / 2); }

            double actualWidth = _is3DMode ? Face3DResolution : MainGrid.ActualWidth;
            double actualHeight = _is3DMode ? Face3DResolution : MainGrid.ActualHeight;
            if (actualWidth <= 0) actualWidth = 800;
            if (actualHeight <= 0) actualHeight = 600;

            double centerX = actualWidth / HalfDivisor;
            double centerY = actualHeight / HalfDivisor;
            double actualDx = mouseInGrid.X - (MainGrid.ActualWidth / HalfDivisor) + SaccadeOffset.X;
            double actualDy = mouseInGrid.Y - (MainGrid.ActualHeight / HalfDivisor + (_currentHeightOffset * _currentScale)) + SaccadeOffset.Y;

            if (!GazeFree)
            {
                actualDx = 0;
                actualDy = 0;
                if (GazeLeft) actualDx = -10000;
                else if (GazeRight) actualDx = 10000;
                if (GazeDown) actualDy = 10000;
            }

            double baseDx = _currentScale > 0 ? actualDx / _currentScale : actualDx;
            double baseDy = _currentScale > 0 ? actualDy / _currentScale : actualDy;
            double eyePhysicalOffset = DistanceSlider.Value / HalfDivisor;
            double leftEyePhysicalX = -eyePhysicalOffset;
            double rightEyePhysicalX = eyePhysicalOffset;
            double distL = Math.Abs(baseDx - leftEyePhysicalX);
            double distR = Math.Abs(baseDx - rightEyePhysicalX);
            double maxDist = MainGrid.ActualWidth / HalfDivisor;
            if (_currentScale > 0) maxDist /= _currentScale;

            double factorL = Math.Clamp(distL / maxDist, 0.0, DefaultFactor);
            double factorR = Math.Clamp(distR / maxDist, 0.0, DefaultFactor);
            double leftSegmentLen = GazeStraight ? eyePhysicalOffset : eyePhysicalOffset * factorL;
            double rightSegmentLen = GazeStraight ? eyePhysicalOffset : eyePhysicalOffset * factorR;

            double leftTargetDx = (baseDx - leftSegmentLen) - leftEyePhysicalX;
            double rightTargetDx = (baseDx + rightSegmentLen) - rightEyePhysicalX;

            CalculateTrackingOffset(leftTargetDx, baseDy, ref _leftTrackingX, ref _leftTrackingY);
            CalculateTrackingOffset(rightTargetDx, baseDy, ref _rightTrackingX, ref _rightTrackingY);

            double averageY = (_leftTrackingY + _rightTrackingY) / HalfDivisor;
            _leftTrackingY = averageY;
            _rightTrackingY = averageY;

            if (_is3DMode && !_isSubmissiveMode)
            {
                Point rotTargetGrid;
                try { rotTargetGrid = MainGrid.PointFromScreen(currentGlobalMousePos); }
                catch { rotTargetGrid = new Point(MainGrid.ActualWidth / 2, MainGrid.ActualHeight / 2); }
                double rotDx = rotTargetGrid.X - (MainGrid.ActualWidth / HalfDivisor);
                double rotDy = rotTargetGrid.Y - (MainGrid.ActualHeight / HalfDivisor + (_currentHeightOffset * _currentScale));
                UpdateCubeRotation(rotDx, rotDy);
            }

            UpdateEyeNarrowness();
            UpdateMasks();

            if (SweatDropImage != null)
            {
                double sweatWidth = 200, sweatHeight = 200;
                double projScale = _is3DMode ? CubeScaleSlider.Value : 1.0;
                double finalEyeScale = ScaleSlider.Value * projScale;
                double rightEyeCenterX = centerX + (eyePhysicalOffset * finalEyeScale);
                double eyeTopY = centerY - (BaseEyeHeight / HalfDivisor) + (_currentHeightOffset * finalEyeScale);
                double sweatX = rightEyeCenterX + (SweatDropOffsetX * finalEyeScale);
                double sweatY = eyeTopY + (SweatDropOffsetY * finalEyeScale);

                Canvas.SetLeft(SweatDropImage, sweatX - (sweatWidth / HalfDivisor));
                Canvas.SetTop(SweatDropImage, sweatY - (sweatHeight / HalfDivisor));
                SweatDropImage.RenderTransform = new ScaleTransform(SweatDropScale * projScale, SweatDropScale * projScale);
            }
        }

        // Rebuilds the clip geometry that carves eyebrows and cheeks around the eyes.
        private void UpdateMasks()
        {
            var activeBrow = Eyebrows.FirstOrDefault(e => e.IsVisible);
            var activeCheek = Cheeks.FirstOrDefault(c => c.IsVisible);
            bool isLeftSquint = IsLeftSquinting || _isLeftMouseHeldOnLeftEye;
            bool isRightSquint = IsRightSquinting || _isLeftMouseHeldOnRightEye;

            if (activeBrow == null && activeCheek == null) { LeftEyeGraphics.Clip = RightEyeGraphics.Clip = null; return; }

            Rect browRect = new Rect(-900, 106.408 - 2.0, 2000, 2000);
            double cheekWidth = activeCheek?.MaskWidth ?? 200.0;
            double cheekHeight = activeCheek?.MaskHeight ?? 29.53;
            Rect cheekRect = new Rect(-900, -2000, 2000, 2000 + (100 + cheekHeight / 2) - 2.0);

            Geometry? leftBrowGeo = (activeBrow != null && activeBrow.RenderLeft && !isLeftSquint)
                ? new RectangleGeometry(browRect) { Transform = new TransformGroup { Children = { new ScaleTransform(activeBrow.LeftScaleX, activeBrow.Scale, 100, 100), new RotateTransform(activeBrow.LeftRotation, 100, 100), new TranslateTransform(-40, activeBrow.LeftCanvasTop) } } } : null;

            Geometry? leftCheekGeo = (activeCheek != null && activeCheek.RenderLeft && !isLeftSquint)
                ? new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(cheekRect), new EllipseGeometry(new Point(100, 122.0), cheekWidth / 2, cheekHeight / 2)) { Transform = new TransformGroup { Children = { new ScaleTransform(activeCheek.Scale, activeCheek.Scale, 100, 100), new TranslateTransform(-40, activeCheek.LeftCanvasTop) } } } : null;

            LeftEyeGraphics.Clip = (leftBrowGeo != null && leftCheekGeo != null) ? new CombinedGeometry(GeometryCombineMode.Intersect, leftBrowGeo, leftCheekGeo) : (leftBrowGeo ?? leftCheekGeo);

            Geometry? rightBrowGeo = (activeBrow != null && activeBrow.RenderRight && !isRightSquint)
                ? new RectangleGeometry(browRect) { Transform = new TransformGroup { Children = { new ScaleTransform(activeBrow.RightScaleX, activeBrow.Scale, 100, 100), new RotateTransform(activeBrow.RightRotation, 100, 100), new TranslateTransform(-40, activeBrow.RightCanvasTop) } } } : null;

            Geometry? rightCheekGeo = (activeCheek != null && activeCheek.RenderRight && !isRightSquint)
                ? new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(cheekRect), new EllipseGeometry(new Point(100, 122.0), cheekWidth / 2, cheekHeight / 2)) { Transform = new TransformGroup { Children = { new ScaleTransform(activeCheek.Scale, activeCheek.Scale, 100, 100), new TranslateTransform(-40, activeCheek.RightCanvasTop) } } } : null;

            RightEyeGraphics.Clip = (rightBrowGeo != null && rightCheekGeo != null) ? new CombinedGeometry(GeometryCombineMode.Intersect, rightBrowGeo, rightCheekGeo) : (rightBrowGeo ?? rightCheekGeo);
        }

        // Smoothly limits the pupil offset to the eye bounds and eases it toward the target.
        private void CalculateTrackingOffset(double dx, double dy, ref double currentOffsetX, ref double currentOffsetY)
        {
            double boundX = BaseEyeWidth / HalfDivisor, boundY = BaseEyeHeight / HalfDivisor;
            double scaleToBorder = Math.Min(dx == 0 ? double.MaxValue : Math.Abs(boundX / dx), dy == 0 ? double.MaxValue : Math.Abs(boundY / dy));
            double limitX = dx * scaleToBorder * MaxTrackingOffsetRatio, limitY = dy * scaleToBorder * MaxTrackingOffsetRatio;
            double targetX = (dx * dx + dy * dy > limitX * limitX + limitY * limitY) ? limitX : dx;
            double targetY = (dx * dx + dy * dy > limitX * limitX + limitY * limitY) ? limitY : dy;
            currentOffsetX += (targetX - currentOffsetX) * TrackingSmoothness;
            currentOffsetY += (targetY - currentOffsetY) * TrackingSmoothness;
        }

        // Tilts the cube once the cursor offset passes the head turn threshold.
        private void UpdateCubeRotation(double dx, double dy)
        {
            double rotThresholdX = MainGrid.ActualWidth * HeadTurnThresholdRatio;
            double rotThresholdY = MainGrid.ActualHeight * HeadTurnThresholdRatio;
            double overflowX = dx > rotThresholdX ? dx - rotThresholdX : (dx < -rotThresholdX ? dx + rotThresholdX : 0);
            double overflowY = dy > rotThresholdY ? dy - rotThresholdY : (dy < -rotThresholdY ? dy + rotThresholdY : 0);
            _cubeRotX += (Math.Clamp(overflowY * CubeRotationSensitivity, -MaxCubeRotationAngle, MaxCubeRotationAngle) - _cubeRotX) * TrackingSmoothness;
            _cubeRotY += (Math.Clamp(overflowX * CubeRotationSensitivity, -MaxCubeRotationAngle, MaxCubeRotationAngle) - _cubeRotY) * TrackingSmoothness;
            CubeRotX.Angle = _cubeRotX;
            CubeRotY.Angle = _cubeRotY;
        }

        // Moves FaceWrapper between the cube front face and the 2D container, then re-runs the layout.
        private void Toggle3DMode(bool is3D)
        {
            _is3DMode = is3D;
            if (is3D)
            {
                if (Container2D.Children.Contains(FaceWrapper)) Container2D.Children.Remove(FaceWrapper);
                FaceWrapper.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                FrontFace3D.Visual = FaceWrapper;
                Container2D.Visibility = Visibility.Collapsed;
                Container3D.Visibility = Visibility.Visible;
                this.Background = Brushes.Transparent;
                MainCamera.Position = new Point3D(0, 0, DefaultCameraZ);
            }
            else
            {
                FrontFace3D.Visual = null;
                FaceWrapper.Background = Brushes.Black;
                if (!Container2D.Children.Contains(FaceWrapper)) Container2D.Children.Add(FaceWrapper);
                Container3D.Visibility = Visibility.Collapsed;
                Container2D.Visibility = Visibility.Visible;
                this.Background = Brushes.Black;
                _cubeRotX = _cubeRotY = 0;
            }
            RefreshAll();
        }

        // Maximizing switches to 2D mode, restoring returns to the cube unless the settings menu is open.
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized) Toggle3DMode(false);
            else if (this.WindowState == WindowState.Normal && SettingsMenu.Visibility == Visibility.Collapsed) Toggle3DMode(true);
            UpdateSplashLayout();
        }

        // Zooms the 3D camera within the configured limits.
        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_is3DMode) return;
            MainCamera.Position = new Point3D(0, 0, Math.Clamp(MainCamera.Position.Z - (e.Delta > 0 ? ZoomStep : -ZoomStep), MinZoom, MaxZoom));
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && _is3DMode) { _isSubmissiveMode = !_isSubmissiveMode; return; }
            if (e.ClickCount == 2) this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingRMB = true;
            _lastMousePosRMB = e.GetPosition(this);
            Mouse.Capture(this);
        }

        private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingRMB = false;
            Mouse.Capture(null);
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingRMB && _isSubmissiveMode && _is3DMode)
            {
                Point currentPos = e.GetPosition(this);
                _cubeRotY += (currentPos.X - _lastMousePosRMB.X) * ManualRotationSensitivity;
                _cubeRotX += (currentPos.Y - _lastMousePosRMB.Y) * ManualRotationSensitivity;
                CubeRotX.Angle = _cubeRotX;
                CubeRotY.Angle = _cubeRotY;
                _lastMousePosRMB = currentPos;
            }
        }

        private void LeftEyeHitBox_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _isLeftMouseHeldOnLeftEye = true;
            UpdateEyeVisibility();
            if ((DateTime.Now - _lastClickTime).TotalSeconds > ClickTimeoutSeconds) _leftClickCount = 0;
            _leftClickCount++;
            _lastClickTime = DateTime.Now;
            if (_leftClickCount >= ClicksToOpenMenu)
            {
                SettingsMenu.Visibility = Visibility.Visible;
                Toggle3DMode(false);
                this.WindowState = WindowState.Maximized;
                _leftClickCount = 0;
            }
        }

        private void LeftEyeHitBox_MouseUp(object sender, MouseButtonEventArgs e) { _isLeftMouseHeldOnLeftEye = false; UpdateEyeVisibility(); }
        private void LeftEyeHitBox_MouseLeave(object sender, MouseEventArgs e) { _isLeftMouseHeldOnLeftEye = false; UpdateEyeVisibility(); }
        private void RightEyeHitBox_MouseDown(object sender, MouseButtonEventArgs e) { e.Handled = true; _isLeftMouseHeldOnRightEye = true; UpdateEyeVisibility(); }
        private void RightEyeHitBox_MouseUp(object sender, MouseButtonEventArgs e) { _isLeftMouseHeldOnRightEye = false; UpdateEyeVisibility(); }
        private void RightEyeHitBox_MouseLeave(object sender, MouseEventArgs e) { _isLeftMouseHeldOnRightEye = false; UpdateEyeVisibility(); }

        // Applies squint, blink, eye bag and blush visibility to every eye element.
        private void UpdateEyeVisibility()
        {
            if (_isSplashActive) return; // hidden while the splash owns the canvas
            if (LeftSquintImage == null || RightSquintImage == null) return;
            var activeCheek = Cheeks.FirstOrDefault(c => c.IsVisible);
            bool showBlush = activeCheek != null && activeCheek.HasBlush;
            bool isLeftSquint = IsLeftSquinting || _isLeftMouseHeldOnLeftEye;
            bool isLeftBlink = _isBlinking && !isLeftSquint;
            bool isLeftEyeVisible = !isLeftSquint && !isLeftBlink;
            bool isLeftBagVisible = isLeftEyeVisible && StressSlider.Value == StateShock;

            LeftSquintImage.Visibility = isLeftSquint ? Visibility.Visible : Visibility.Hidden;
            LeftBlinkImage.Visibility = isLeftBlink ? Visibility.Visible : Visibility.Hidden;
            LeftInnerRect.Visibility = LeftEyeBorder.Visibility = isLeftEyeVisible ? Visibility.Visible : Visibility.Hidden;
            LeftEyeBag.Visibility = isLeftBagVisible ? Visibility.Visible : Visibility.Hidden;
            if (LeftEyebrowsControl != null) LeftEyebrowsControl.Visibility = isLeftSquint ? Visibility.Hidden : Visibility.Visible;
            if (LeftCheeksControl != null) LeftCheeksControl.Visibility = isLeftSquint ? Visibility.Hidden : Visibility.Visible;
            if (LeftBlushImage != null) LeftBlushImage.Visibility = showBlush ? Visibility.Visible : Visibility.Hidden;

            bool isRightSquint = IsRightSquinting || _isLeftMouseHeldOnRightEye;
            bool isRightBlink = _isBlinking && !isRightSquint;
            bool isRightEyeVisible = !isRightSquint && !isRightBlink;
            bool isRightBagVisible = isRightEyeVisible && StressSlider.Value == StateShock;

            RightSquintImage.Visibility = isRightSquint ? Visibility.Visible : Visibility.Hidden;
            RightBlinkImage.Visibility = isRightBlink ? Visibility.Visible : Visibility.Hidden;
            RightInnerRect.Visibility = RightEyeBorder.Visibility = isRightEyeVisible ? Visibility.Visible : Visibility.Hidden;
            RightEyeBag.Visibility = isRightBagVisible ? Visibility.Visible : Visibility.Hidden;
            if (RightEyebrowsControl != null) RightEyebrowsControl.Visibility = isRightSquint ? Visibility.Hidden : Visibility.Visible;
            if (RightCheeksControl != null) RightCheeksControl.Visibility = isRightSquint ? Visibility.Hidden : Visibility.Visible;
            if (RightBlushImage != null) RightBlushImage.Visibility = showBlush ? Visibility.Visible : Visibility.Hidden;
            if (CenterBlushBlurImage != null) CenterBlushBlurImage.Visibility = showBlush ? Visibility.Visible : Visibility.Hidden;
        }

        // Recomputes canvas size, eye scale, positions and decal geometry for the active mode.
        private void RefreshAll()
        {
            if (!IsLoaded) return;
            double actualWidth = _is3DMode ? Face3DResolution : MainGrid.ActualWidth;
            double actualHeight = _is3DMode ? Face3DResolution : MainGrid.ActualHeight;
            if (actualWidth <= 0) actualWidth = 800;
            if (actualHeight <= 0) actualHeight = 600;

            FaceWrapper.Width = FaceCanvas.Width = actualWidth;
            FaceWrapper.Height = FaceCanvas.Height = actualHeight;

            double projScale = _is3DMode ? CubeScaleSlider.Value : 1.0;
            double finalEyeScale = ScaleSlider.Value * projScale;
            _currentScale = finalEyeScale;
            _currentHeightOffset = HeightSlider.Value;

            LeftEyeContainer.RenderTransform = new ScaleTransform(finalEyeScale, finalEyeScale);
            RightEyeContainer.RenderTransform = new ScaleTransform(finalEyeScale, finalEyeScale);

            double centerX = actualWidth / HalfDivisor, centerY = actualHeight / HalfDivisor;
            double scaledDistance = DistanceSlider.Value * finalEyeScale;
            double scaledHeightOff = HeightSlider.Value * finalEyeScale;

            Canvas.SetLeft(LeftEyeContainer, centerX - (scaledDistance / HalfDivisor) - (BaseEyeWidth / HalfDivisor));
            Canvas.SetLeft(RightEyeContainer, centerX + (scaledDistance / HalfDivisor) - (BaseEyeWidth / HalfDivisor));
            Canvas.SetTop(LeftEyeContainer, centerY - (BaseEyeHeight / HalfDivisor) + scaledHeightOff);
            Canvas.SetTop(RightEyeContainer, centerY - (BaseEyeHeight / HalfDivisor) + scaledHeightOff);

            if (CenterBlushBlurImage != null)
            {
                Canvas.SetLeft(CenterBlushBlurImage, centerX - (1500.0 / HalfDivisor));
                Canvas.SetTop(CenterBlushBlurImage, centerY - (BaseEyeHeight / HalfDivisor) + scaledHeightOff + (BlushBlurOffsetY * finalEyeScale) - (800.0 / HalfDivisor));
                CenterBlushBlurImage.RenderTransform = new ScaleTransform(BlushBlurScale * projScale, BlushBlurScale * projScale);
            }

            if (MouthsControl != null)
            {
                MouthsControl.RenderTransform = new ScaleTransform(projScale, projScale);
                Canvas.SetLeft(MouthsControl, centerX);
                Canvas.SetTop(MouthsControl, centerY);
            }

            LeftEyeBorder.StrokeThickness = RightEyeBorder.StrokeThickness = FixedLineThickness / HalfDivisor;
            RefreshBagsGeometry();
            UpdateEyeNarrowness();
        }

        // Rebuilds the eye bag polylines that appear in the shock stress state.
        private void RefreshBagsGeometry()
        {
            LeftEyeBag.Points = new PointCollection { new Point(-BagOffsetX, BaseEyeHeight + BagOffsetY), new Point(-BagOffsetX, BaseEyeHeight + BagOffsetY + BagVerticalLen), new Point(-BagOffsetX + BagHorizontalLen, BaseEyeHeight + BagOffsetY + BagVerticalLen) };
            RightEyeBag.Points = new PointCollection { new Point(BaseEyeWidth + BagOffsetX, BaseEyeHeight + BagOffsetY), new Point(BaseEyeWidth + BagOffsetX, BaseEyeHeight + BagOffsetY + BagVerticalLen), new Point(BaseEyeWidth + BagOffsetX - BagHorizontalLen, BaseEyeHeight + BagOffsetY + BagVerticalLen) };
        }

        // Resizes both pupils from the stress slider and redraws the crossed pupil lines.
        private void UpdateEyeNarrowness()
        {
            if (LeftInnerRect == null) return;
            double mult = StressSlider.Value == StateExcited ? StressMultiplierExcited : (StressSlider.Value == StateShock ? StressMultiplierShock : StressMultiplierCalm);
            UpdateEyeVisibility();

            double nWidth = BaseEyeWidth * MaxPupilScale * mult;
            double nHeight = BaseEyeHeight * MaxPupilScale * mult;
            double diagThick = LeftEyeBorder.StrokeThickness * DiagonalThicknessRatio;

            DrawPupil(LeftInnerRect, LeftPath1, LeftPath2, LeftBezier1_Start, LeftBezier1_Seg, LeftBezier2_Start, LeftBezier2_Seg, nWidth, nHeight, diagThick, _leftTrackingX, _leftTrackingY);
            DrawPupil(RightInnerRect, RightPath1, RightPath2, RightBezier1_Start, RightBezier1_Seg, RightBezier2_Start, RightBezier2_Seg, nWidth, nHeight, diagThick, _rightTrackingX, _rightTrackingY);
        }

        private void DrawPupil(Canvas inner, System.Windows.Shapes.Path p1, System.Windows.Shapes.Path p2, PathFigure f1, QuadraticBezierSegment s1, PathFigure f2, QuadraticBezierSegment s2, double w, double h, double thick, double offX, double offY)
        {
            inner.Width = w; inner.Height = h;
            Canvas.SetLeft(inner, (BaseEyeWidth - w) / HalfDivisor);
            Canvas.SetTop(inner, (BaseEyeHeight - h) / HalfDivisor);
            p1.StrokeThickness = p2.StrokeThickness = thick;

            double midX = w / HalfDivisor, midY = h / HalfDivisor;
            double cx = midX + (offX * HalfDivisor), cy = midY + (offY * HalfDivisor);

            f1.StartPoint = new Point(0, 0); s1.Point1 = new Point(cx, cy); s1.Point2 = new Point(w, h);
            f2.StartPoint = new Point(w, 0); s2.Point1 = new Point(cx, cy); s2.Point2 = new Point(0, h);
        }

        // Adds a mouth preset, hiding every other one so the new entry is the visible mouth.
        private void CreateMouth_Click(object sender, RoutedEventArgs e)
        {
            foreach (var m in Mouths) { m.IsVisible = m.IsExpanded = false; }
            Mouths.Add(new MouthPreset { Name = "New Emotion", OffsetX = -50, OffsetY = 100, IsVisible = true, IsExpanded = true });
        }

        private void MouthFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MouthPreset preset)
            {
                var dialog = new OpenFileDialog { Filter = "Image Files|*.png;*.gif;*.jpg|All Files|*.*", InitialDirectory = System.IO.Path.Combine(PathHelper.BaseAssetsDir, "assets", "mouth") };
                if (dialog.ShowDialog() == true) preset.FilePath = PathHelper.GetRelativePath(dialog.FileName);
            }
        }

        private void DeleteMouth_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.DataContext is MouthPreset preset) Mouths.Remove(preset); }

        private void MouthName_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is TextBlock tb && VisualTreeHelper.GetParent(tb) is Grid grid && grid.Children.Count > 1 && grid.Children[1] is TextBox txt)
            {
                tb.Visibility = Visibility.Collapsed; txt.Visibility = Visibility.Visible; txt.Focus(); txt.SelectAll(); e.Handled = true;
            }
        }

        private void MouthName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txt && VisualTreeHelper.GetParent(txt) is Grid grid && grid.Children.Count > 0 && grid.Children[0] is TextBlock tb)
            {
                txt.Visibility = Visibility.Collapsed; tb.Visibility = Visibility.Visible;
            }
        }

        private void MouthName_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Keyboard.ClearFocus(); }

        private void MouthVisibility_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is MouthPreset sel)
                foreach (var m in Mouths) if (m != sel) m.IsVisible = false;
        }

        private void MouthExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is MouthPreset exp)
                foreach (var m in Mouths) if (m != exp) m.IsExpanded = false;
        }

        // Starts dragging the visible mouth image and remembers the offset it had at grab time.
        private void MouthImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is MouthPreset m && SettingsMenu.Visibility == Visibility.Visible && m.IsExpanded)
            {
                _isDraggingMouth = true; _draggedMouth = m;
                _dragStartMousePos = e.GetPosition(MouthsControl);
                _dragStartMouthOffset = new Point(m.OffsetX, m.OffsetY);
                fe.CaptureMouse(); e.Handled = true;
            }
        }

        // Moves the dragged mouth with the cursor, writing updated offsets into the preset.
        private void MouthImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingMouth && _draggedMouth != null)
            {
                Point pos = e.GetPosition(MouthsControl);
                _draggedMouth.OffsetX = _dragStartMouthOffset.X + (pos.X - _dragStartMousePos.X);
                _draggedMouth.OffsetY = _dragStartMouthOffset.Y + (pos.Y - _dragStartMousePos.Y);
            }
        }

        private void MouthImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingMouth) { _isDraggingMouth = false; _draggedMouth = null; (sender as FrameworkElement)?.ReleaseMouseCapture(); }
        }

        private void CreateEyebrow_Click(object sender, RoutedEventArgs e)
        {
            foreach (var eb in Eyebrows) { eb.IsVisible = eb.IsExpanded = false; }
            Eyebrows.Add(new EyebrowPreset { Name = "New Eyebrows", IsVisible = true, IsExpanded = true });
        }

        private void DeleteEyebrow_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.DataContext is EyebrowPreset preset) Eyebrows.Remove(preset); }

        private void EyebrowName_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is TextBlock tb && VisualTreeHelper.GetParent(tb) is Grid grid && grid.Children.Count > 1 && grid.Children[1] is TextBox txt)
            {
                tb.Visibility = Visibility.Collapsed; txt.Visibility = Visibility.Visible; txt.Focus(); txt.SelectAll(); e.Handled = true;
            }
        }

        private void EyebrowName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txt && VisualTreeHelper.GetParent(txt) is Grid grid && grid.Children.Count > 0 && grid.Children[0] is TextBlock tb)
            {
                txt.Visibility = Visibility.Collapsed; tb.Visibility = Visibility.Visible;
            }
        }

        private void EyebrowName_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Keyboard.ClearFocus(); }

        private void EyebrowVisibility_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is EyebrowPreset sel)
                foreach (var eb in Eyebrows) if (eb != sel) eb.IsVisible = false;
        }

        private void EyebrowExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is EyebrowPreset exp)
                foreach (var eb in Eyebrows) if (eb != exp) eb.IsExpanded = false;
        }

        private void CreateCheek_Click(object sender, RoutedEventArgs e)
        {
            foreach (var ch in Cheeks) { ch.IsVisible = ch.IsExpanded = false; }
            Cheeks.Add(new CheekPreset { Name = "New Cheeks", IsVisible = true, IsExpanded = true });
        }

        private void DeleteCheek_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.DataContext is CheekPreset preset) Cheeks.Remove(preset); }

        private void CheekName_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is TextBlock tb && VisualTreeHelper.GetParent(tb) is Grid grid && grid.Children.Count > 1 && grid.Children[1] is TextBox txt)
            {
                tb.Visibility = Visibility.Collapsed; txt.Visibility = Visibility.Visible; txt.Focus(); txt.SelectAll(); e.Handled = true;
            }
        }

        private void CheekName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txt && VisualTreeHelper.GetParent(txt) is Grid grid && grid.Children.Count > 0 && grid.Children[0] is TextBlock tb)
            {
                txt.Visibility = Visibility.Collapsed; tb.Visibility = Visibility.Visible;
            }
        }

        private void CheekName_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Keyboard.ClearFocus(); }

        private void CheekVisibility_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is CheekPreset sel)
            {
                foreach (var ch in Cheeks) if (ch != sel) ch.IsVisible = false;
                UpdateEyeVisibility();
            }
        }

        private void CheekExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is CheekPreset exp)
                foreach (var ch in Cheeks) if (ch != exp) ch.IsExpanded = false;
        }

        private void Settings_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => RefreshAll();
        private void StressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateEyeNarrowness();
        // Re-runs the layout after a resize so the face and the splash follow the window.
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshAll();
            UpdateSplashLayout();
        }

        private void CloseMenu_Click(object sender, RoutedEventArgs e)
        {
            SettingsMenu.Visibility = Visibility.Collapsed;
            this.WindowState = WindowState.Normal;
            Toggle3DMode(true);
        }

        // Collects every editor value into a FaceConfig and writes it through ConfigManager.
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ConfigManager.Save(new FaceConfig
            {
                EyeScale = ScaleSlider.Value,
                CubeFaceScale = CubeScaleSlider.Value,
                EyeDistance = DistanceSlider.Value,
                EyeHeightOffset = HeightSlider.Value,
                StressState = (int)StressSlider.Value,
                GazeFree = this.GazeFree,
                GazeStraight = this.GazeStraight,
                GazeLeft = this.GazeLeft,
                GazeRight = this.GazeRight,
                GazeDown = this.GazeDown,
                BlinkFilePath = this.BlinkFilePath,
                BlinkScale = this.BlinkScale,
                SquintFilePath = this.SquintFilePath,
                SquintScale = this.SquintScale,
                IsLeftSquinting = this.IsLeftSquinting,
                IsRightSquinting = this.IsRightSquinting,
                HasSweatDrop = this.HasSweatDrop,
                Mouths = new System.Collections.Generic.List<MouthPreset>(this.Mouths),
                Eyebrows = new System.Collections.Generic.List<EyebrowPreset>(this.Eyebrows),
                Cheeks = new System.Collections.Generic.List<CheekPreset>(this.Cheeks)
            });
        }

        // Pushes a loaded FaceConfig into the sliders and preset collections, or seeds the default face.
        private void ApplyLoadedConfig(FaceConfig? config)
        {
            if (config == null)
            {
                // No config on disk, so build the default presets that used to live in the constructor.
                if (Eyebrows.Count == 0) Eyebrows.Add(new EyebrowPreset { Name = "New Eyebrows", IsVisible = true, IsExpanded = false, LeftOffset = 200, RightOffset = 200 });
                if (Cheeks.Count == 0) Cheeks.Add(new CheekPreset { Name = "New Cheeks", IsVisible = true, IsExpanded = false, LeftOffset = 7.804878048780504, RightOffset = 7.804878048780504 });
                return;
            }

            {
                ScaleSlider.Value = config.EyeScale;
                CubeScaleSlider.Value = config.CubeFaceScale;
                DistanceSlider.Value = config.EyeDistance;
                HeightSlider.Value = config.EyeHeightOffset;
                StressSlider.Value = config.StressState;
                GazeFree = config.GazeFree;
                GazeStraight = config.GazeStraight;
                GazeLeft = config.GazeLeft;
                GazeRight = config.GazeRight;
                GazeDown = config.GazeDown;
                BlinkFilePath = config.BlinkFilePath;
                BlinkScale = config.BlinkScale;
                SquintFilePath = config.SquintFilePath;
                SquintScale = config.SquintScale;
                IsLeftSquinting = config.IsLeftSquinting;
                IsRightSquinting = config.IsRightSquinting;
                HasSweatDrop = config.HasSweatDrop;
                Mouths.Clear();
                if (config.Mouths != null) foreach (var mouth in config.Mouths) Mouths.Add(mouth);
                Eyebrows.Clear();
                if (config.Eyebrows != null) foreach (var eb in config.Eyebrows) Eyebrows.Add(eb);
                Cheeks.Clear();
                if (config.Cheeks != null) foreach (var ch in config.Cheeks) Cheeks.Add(ch);
            }
        }
    }
}