// Version: 42 (Stable + Idle Behaviors + Fixed Missing Methods)
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Threading;
using System.Threading.Tasks;
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

        // --- БАЗОВЫЕ КОНСТАНТЫ ГЕОМЕТРИИ ---
        private const double BaseEyeWidth = 120.0;
        private const double BaseEyeHeight = 200.0;
        private const double FixedLineThickness = 15.0;
        // ОБНОВЛЕНО: Максимальный масштаб зрачка установлен на 0.93
        private const double MaxPupilScale = 0.93;
        private const double DiagonalThicknessRatio = 0.9;

        // --- КОНСТАНТЫ ИНТЕРАКЦИИ ---
        private const int ClicksToOpenMenu = 3;
        private const int ClickTimeoutSeconds = 3;
        private const int BlinkDurationMs = 100;

        // --- КОНСТАНТЫ 3D И РЕНДЕРА ---
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

        // --- СТРЕСС И ЭМОЦИИ ---
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

        // ==========================================
        // ПАРАМЕТРЫ РУМЯНЦА (BLUSH)
        // ==========================================
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

        // ==========================================
        // ПАРАМЕТРЫ КАПЛИ ПОТА (SWEAT DROP)
        // ==========================================
        private bool _hasSweatDrop = false;
        public bool HasSweatDrop { get => _hasSweatDrop; set { _hasSweatDrop = value; OnPropertyChanged(nameof(HasSweatDrop)); } }

        private double _sweatDropScale = 0.45;
        public double SweatDropScale { get => _sweatDropScale; set { _sweatDropScale = value; OnPropertyChanged(nameof(SweatDropScale)); } }

        private double _sweatDropOffsetX = 115.0;
        public double SweatDropOffsetX { get => _sweatDropOffsetX; set { _sweatDropOffsetX = value; OnPropertyChanged(nameof(SweatDropOffsetX)); } }

        private double _sweatDropOffsetY = -20.0;
        public double SweatDropOffsetY { get => _sweatDropOffsetY; set { _sweatDropOffsetY = value; OnPropertyChanged(nameof(SweatDropOffsetY)); } }

        public string SweatDropFullPath => PathHelper.GetFullPath("assets\\decals\\sweat_drop.png");

        // ==========================================
        // АВТОНОМНОЕ ПОВЕДЕНИЕ (Idle Behaviors)
        // ==========================================
        private CancellationTokenSource _appLifetimeCts = new CancellationTokenSource();
        private CancellationTokenSource? _idleCts;
        private bool _isIdleActionRunning = false;

        private DateTime _lastMouseMoveTime = DateTime.Now;
        private Point _lastGlobalMousePos;

        public Point SaccadeOffset { get; set; } = new Point(0, 0);
        private Point? _wanderTarget = null;
        private readonly Random _random = new Random();

        // ==========================================

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

        // --- СВОЙСТВА УПРАВЛЕНИЯ ВЗГЛЯДОМ ---
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
        public bool GazeStraight
        {
            get => _gazeStraight;
            set
            {
                if (_gazeStraight == value) return;
                _gazeStraight = value;
                OnPropertyChanged(nameof(GazeStraight));
            }
        }

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

        private void UpdateGazeFreeState()
        {
            if (!GazeLeft && !GazeRight && !GazeDown && !GazeFree) GazeFree = true;
        }

        // --- СВОЙСТВА ЭФФЕКТОВ ГЛАЗ ---
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

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            if (ConfigManager.Load() == null)
            {
                Eyebrows.Add(new EyebrowPreset { Name = "New Eyebrows", IsVisible = true, IsExpanded = false, LeftOffset = 200, RightOffset = 200 });
                Cheeks.Add(new CheekPreset { Name = "New Cheeks", IsVisible = true, IsExpanded = false, LeftOffset = 7.804878048780504, RightOffset = 7.804878048780504 });
            }

            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;
            CompositionTarget.Rendering += OnRendering;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyLoadedConfig();
            Toggle3DMode(true);
            UpdateEyeVisibility();

            // Захватываем начальную позицию курсора для отслеживания движения
            GetCursorPos(out Win32Point pt);
            _lastGlobalMousePos = new Point(pt.X, pt.Y);

            // Запускаем фоновые циклы поведения
            StartBehaviors();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _appLifetimeCts.Cancel();
            _idleCts?.Cancel();
        }

        // ==========================================
        // ЛОГИКА АВТОНОМНОГО ПОВЕДЕНИЯ (ВСТРОЕННАЯ)
        // ==========================================

        private void StartBehaviors()
        {
            var token = _appLifetimeCts.Token;
            _ = ConstantBlinkLoop(token);
            _ = ConstantSaccadeLoop(token);
            _ = IdleMonitorLoop(token);
        }

        // 1. Постоянное случайное моргание
        private async Task ConstantBlinkLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(_random.Next(4000, 10001), token);
                    if (!token.IsCancellationRequested && GazeFree)
                    {
                        await TriggerBlinkAsync();
                    }
                }
            }
            catch (TaskCanceledException) { }
        }

        // 2. Постоянные саккады (глаза всегда слегка "живые", не зависят от простоя мыши)
        private async Task ConstantSaccadeLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Пауза между сериями разглядываний (от 2 до 5 секунд)
                    await Task.Delay(_random.Next(2000, 5001), token);

                    // 3 рывка глазами с интервалом ровно 1 секунда
                    for (int i = 0; i < 3; i++)
                    {
                        if (token.IsCancellationRequested) return;

                        // Увеличенный разброс (до 50 пикселей)
                        SaccadeOffset = new Point(_random.Next(-50, 51), _random.Next(-30, 31));

                        // Пауза на фиксацию взгляда в этой точке
                        await Task.Delay(1000, token);
                    }
                    // Возвращаем в центр
                    SaccadeOffset = new Point(0, 0);
                }
            }
            catch (TaskCanceledException) { }
        }

        // 3. Проверка простоя мыши
        private async Task IdleMonitorLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token);
                    double idleSeconds = (DateTime.Now - _lastMouseMoveTime).TotalSeconds;

                    if (idleSeconds >= 120.0 && !_isIdleActionRunning)
                    {
                        _ = RunWanderingTask();
                    }
                    else if (idleSeconds >= 60.0 && idleSeconds < 120.0 && !_isIdleActionRunning)
                    {
                        _ = RunSideEyeTask();
                    }
                }
            }
            catch (TaskCanceledException) { }
        }

        // 4. Отвод взгляда (Side Eye)
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

                    // Держим взгляд в сторону 2 - 4 секунды
                    await Task.Delay(_random.Next(2000, 4001), token);

                    if (!token.IsCancellationRequested)
                    {
                        await TriggerBlinkAsync();
                        GazeFree = true; // Сбрасывает остальные
                    }
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                _isIdleActionRunning = false;
            }
        }

        // 5. Разглядывание экрана (Wandering)
        private async Task RunWanderingTask()
        {
            _isIdleActionRunning = true;
            _idleCts = new CancellationTokenSource();
            var token = _idleCts.Token;

            try
            {
                // Включаем прямой взгляд (глаза смотрят прямо относительно куба, но следят за точкой на экране)
                GazeStraight = true;
                GazeFree = false;
                GazeLeft = false;
                GazeRight = false;
                GazeDown = false;

                double sw = SystemParameters.PrimaryScreenWidth;
                double sh = SystemParameters.PrimaryScreenHeight;
                _wanderTarget = new Point(_random.Next((int)sw), _random.Next((int)sh));

                // Разглядываем эту точку от 10 до 40 секунд
                // Саккады всё еще работают, поэтому глаза будут "ощупывать" область вокруг этой точки
                int examineTimeMs = _random.Next(10000, 40001);
                await Task.Delay(examineTimeMs, token);

                if (!token.IsCancellationRequested)
                {
                    _wanderTarget = null;
                    GazeStraight = false;
                    GazeFree = true;
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                _isIdleActionRunning = false;
            }
        }

        // ==========================================

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

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!IsVisible || PresentationSource.FromVisual(this) == null) return;

            GetCursorPos(out Win32Point mousePt);
            Point currentGlobalMousePos = new Point(mousePt.X, mousePt.Y);

            // --- ДЕТЕКТОР АКТИВНОСТИ МЫШИ (ПРОБУЖДЕНИЕ) ---
            double distMoved = Math.Abs(currentGlobalMousePos.X - _lastGlobalMousePos.X) + Math.Abs(currentGlobalMousePos.Y - _lastGlobalMousePos.Y);
            if (distMoved > 5.0)
            {
                _lastGlobalMousePos = currentGlobalMousePos;
                _lastMouseMoveTime = DateTime.Now;

                // Если мы были в режиме бездействия (сайд-ай или разглядывание экрана) — просыпаемся
                if (_isIdleActionRunning)
                {
                    _idleCts?.Cancel(); // Прерываем текущую анимацию

                    _wanderTarget = null;
                    GazeStraight = false;
                    GazeLeft = false;
                    GazeRight = false;
                    GazeDown = false;
                    GazeFree = true;

                    _ = TriggerBlinkAsync(); // Насильно моргаем
                }
            }

            // Цель для глаз (реальная мышь ИЛИ случайная точка на мониторе)
            Point targetGlobalPos = _wanderTarget ?? currentGlobalMousePos;

            Point mouseInGrid;
            try
            {
                mouseInGrid = MainGrid.PointFromScreen(targetGlobalPos);
            }
            catch
            {
                mouseInGrid = new Point(MainGrid.ActualWidth / 2, MainGrid.ActualHeight / 2);
            }

            double actualWidth = _is3DMode ? Face3DResolution : MainGrid.ActualWidth;
            double actualHeight = _is3DMode ? Face3DResolution : MainGrid.ActualHeight;
            if (actualWidth <= 0) actualWidth = 800;
            if (actualHeight <= 0) actualHeight = 600;

            double centerX = actualWidth / HalfDivisor;
            double centerY = actualHeight / HalfDivisor;

            // ПРИМЕНЯЕМ САККАДЫ (смещение к точке обзора)
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

            double leftSegmentLen = eyePhysicalOffset * factorL;
            double rightSegmentLen = eyePhysicalOffset * factorR;

            // Прямой взгляд делает глаза параллельными, но они всё равно следуют за targetGlobalPos
            if (GazeStraight)
            {
                leftSegmentLen = eyePhysicalOffset;
                rightSegmentLen = eyePhysicalOffset;
            }

            double leftTargetDx = (baseDx - leftSegmentLen) - leftEyePhysicalX;
            double rightTargetDx = (baseDx + rightSegmentLen) - rightEyePhysicalX;

            CalculateTrackingOffset(leftTargetDx, baseDy, ref _leftTrackingX, ref _leftTrackingY);
            CalculateTrackingOffset(rightTargetDx, baseDy, ref _rightTrackingX, ref _rightTrackingY);

            double averageY = (_leftTrackingY + _rightTrackingY) / HalfDivisor;
            _leftTrackingY = averageY;
            _rightTrackingY = averageY;

            // Куб должен вращаться ТОЛЬКО за реальной мышкой, а не за точкой блуждания
            if (_is3DMode && !_isSubmissiveMode)
            {
                Point rotTargetGrid;
                try { rotTargetGrid = MainGrid.PointFromScreen(currentGlobalMousePos); }
                catch { rotTargetGrid = new Point(MainGrid.ActualWidth / 2, MainGrid.ActualHeight / 2); }

                double rotDx = rotTargetGrid.X - (MainGrid.ActualWidth / HalfDivisor);
                double rotDy = rotTargetGrid.Y - (MainGrid.ActualHeight / HalfDivisor + (_currentHeightOffset * _currentScale));
                UpdateCubeRotation(rotDx, rotDy);
            }

            // Вызов восстановленных методов перерисовки
            UpdateEyeNarrowness();
            UpdateMasks();

            // Расчет позиции капли пота
            if (SweatDropImage != null)
            {
                double sweatWidth = 200;
                double sweatHeight = 200;

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

        private void UpdateMasks()
        {
            var activeBrow = Eyebrows.FirstOrDefault(e => e.IsVisible);
            var activeCheek = Cheeks.FirstOrDefault(c => c.IsVisible);

            bool isLeftSquint = IsLeftSquinting || _isLeftMouseHeldOnLeftEye;
            bool isRightSquint = IsRightSquinting || _isLeftMouseHeldOnRightEye;

            if (activeBrow == null && activeCheek == null)
            {
                LeftEyeGraphics.Clip = null;
                RightEyeGraphics.Clip = null;
                return;
            }

            Rect browRect = new Rect();
            if (activeBrow != null)
            {
                double browMaskVerticalOffsetN = 2.0;
                double browBottomEdge = 106.408;
                double browMaskTop = browBottomEdge - browMaskVerticalOffsetN;
                browRect = new Rect(-900, browMaskTop, 2000, 2000);
            }

            Rect cheekRect = new Rect();
            double cheekEllipseOffsetY = 22.0;
            double cheekEllipseWidth = 200.0;
            double cheekEllipseHeight = 29.53;

            if (activeCheek != null)
            {
                cheekEllipseWidth = activeCheek.MaskWidth;
                cheekEllipseHeight = activeCheek.MaskHeight;
                double cheekRectOffsetY = 2.0;
                double cheekBottomEdge = 100 + (cheekEllipseHeight / 2);
                cheekRect = new Rect(-900, -2000, 2000, 2000 + cheekBottomEdge - cheekRectOffsetY);
            }

            Geometry? leftBrowGeo = null;
            if (activeBrow != null && activeBrow.RenderLeft && !isLeftSquint)
            {
                TransformGroup tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform(activeBrow.LeftScaleX, activeBrow.Scale, 100, 100));
                tg.Children.Add(new RotateTransform(activeBrow.LeftRotation, 100, 100));
                tg.Children.Add(new TranslateTransform(-40, activeBrow.LeftCanvasTop));
                leftBrowGeo = new RectangleGeometry(browRect) { Transform = tg };
            }

            Geometry? leftCheekGeo = null;
            if (activeCheek != null && activeCheek.RenderLeft && !isLeftSquint)
            {
                TransformGroup tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform(activeCheek.Scale, activeCheek.Scale, 100, 100));
                tg.Children.Add(new TranslateTransform(-40, activeCheek.LeftCanvasTop));

                leftCheekGeo = new CombinedGeometry(
                    GeometryCombineMode.Exclude,
                    new RectangleGeometry(cheekRect),
                    new EllipseGeometry(new Point(100, 100 + cheekEllipseOffsetY), cheekEllipseWidth / 2, cheekEllipseHeight / 2)
                )
                { Transform = tg };
            }

            Geometry? finalLeftClip = null;
            if (leftBrowGeo != null && leftCheekGeo != null)
                finalLeftClip = new CombinedGeometry(GeometryCombineMode.Intersect, leftBrowGeo, leftCheekGeo);
            else if (leftBrowGeo != null)
                finalLeftClip = leftBrowGeo;
            else if (leftCheekGeo != null)
                finalLeftClip = leftCheekGeo;

            LeftEyeGraphics.Clip = finalLeftClip;

            Geometry? rightBrowGeo = null;
            if (activeBrow != null && activeBrow.RenderRight && !isRightSquint)
            {
                TransformGroup tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform(activeBrow.RightScaleX, activeBrow.Scale, 100, 100));
                tg.Children.Add(new RotateTransform(activeBrow.RightRotation, 100, 100));
                tg.Children.Add(new TranslateTransform(-40, activeBrow.RightCanvasTop));
                rightBrowGeo = new RectangleGeometry(browRect) { Transform = tg };
            }

            Geometry? rightCheekGeo = null;
            if (activeCheek != null && activeCheek.RenderRight && !isRightSquint)
            {
                TransformGroup tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform(activeCheek.Scale, activeCheek.Scale, 100, 100));
                tg.Children.Add(new TranslateTransform(-40, activeCheek.RightCanvasTop));

                rightCheekGeo = new CombinedGeometry(
                    GeometryCombineMode.Exclude,
                    new RectangleGeometry(cheekRect),
                    new EllipseGeometry(new Point(100, 100 + cheekEllipseOffsetY), cheekEllipseWidth / 2, cheekEllipseHeight / 2)
                )
                { Transform = tg };
            }

            Geometry? finalRightClip = null;
            if (rightBrowGeo != null && rightCheekGeo != null)
                finalRightClip = new CombinedGeometry(GeometryCombineMode.Intersect, rightBrowGeo, rightCheekGeo);
            else if (rightBrowGeo != null)
                finalRightClip = rightBrowGeo;
            else if (rightCheekGeo != null)
                finalRightClip = rightCheekGeo;

            RightEyeGraphics.Clip = finalRightClip;
        }

        private void CalculateTrackingOffset(double dx, double dy, ref double currentOffsetX, ref double currentOffsetY)
        {
            double boundX = BaseEyeWidth / HalfDivisor;
            double boundY = BaseEyeHeight / HalfDivisor;

            double scaleX = dx == 0 ? double.MaxValue : Math.Abs(boundX / dx);
            double scaleY = dy == 0 ? double.MaxValue : Math.Abs(boundY / dy);
            double scaleToBorder = Math.Min(scaleX, scaleY);

            double borderX = dx * scaleToBorder;
            double borderY = dy * scaleToBorder;

            double limitX = borderX * MaxTrackingOffsetRatio;
            double limitY = borderY * MaxTrackingOffsetRatio;

            double targetX = dx;
            double targetY = dy;

            double mouseSq = dx * dx + dy * dy;
            double limitSq = limitX * limitX + limitY * limitY;

            if (mouseSq > limitSq)
            {
                targetX = limitX;
                targetY = limitY;
            }

            currentOffsetX += (targetX - currentOffsetX) * TrackingSmoothness;
            currentOffsetY += (targetY - currentOffsetY) * TrackingSmoothness;
        }

        private void UpdateCubeRotation(double dx, double dy)
        {
            double rotThresholdX = MainGrid.ActualWidth * HeadTurnThresholdRatio;
            double rotThresholdY = MainGrid.ActualHeight * HeadTurnThresholdRatio;

            double overflowX = 0;
            if (dx > rotThresholdX) overflowX = dx - rotThresholdX;
            else if (dx < -rotThresholdX) overflowX = dx + rotThresholdX;

            double overflowY = 0;
            if (dy > rotThresholdY) overflowY = dy - rotThresholdY;
            else if (dy < -rotThresholdY) overflowY = dy + rotThresholdY;

            double targetRotY = overflowX * CubeRotationSensitivity;
            double targetRotX = overflowY * CubeRotationSensitivity;

            targetRotY = Math.Clamp(targetRotY, -MaxCubeRotationAngle, MaxCubeRotationAngle);
            targetRotX = Math.Clamp(targetRotX, -MaxCubeRotationAngle, MaxCubeRotationAngle);

            _cubeRotX += (targetRotX - _cubeRotX) * TrackingSmoothness;
            _cubeRotY += (targetRotY - _cubeRotY) * TrackingSmoothness;

            CubeRotX.Angle = _cubeRotX;
            CubeRotY.Angle = _cubeRotY;
        }

        private void Toggle3DMode(bool is3D)
        {
            _is3DMode = is3D;
            if (is3D)
            {
                if (Container2D.Children.Contains(FaceWrapper)) Container2D.Children.Remove(FaceWrapper);

                FaceWrapper.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                FaceWrapper.Margin = new Thickness(0);
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
                FaceWrapper.Margin = new Thickness(0);

                if (!Container2D.Children.Contains(FaceWrapper)) Container2D.Children.Add(FaceWrapper);

                Container3D.Visibility = Visibility.Collapsed;
                Container2D.Visibility = Visibility.Visible;
                this.Background = Brushes.Black;

                _cubeRotX = 0;
                _cubeRotY = 0;
            }
            RefreshAll();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized) Toggle3DMode(false);
            else if (this.WindowState == WindowState.Normal && SettingsMenu.Visibility == Visibility.Collapsed) Toggle3DMode(true);
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_is3DMode) return;
            double newZ = MainCamera.Position.Z - (e.Delta > 0 ? ZoomStep : -ZoomStep);
            MainCamera.Position = new Point3D(0, 0, Math.Clamp(newZ, MinZoom, MaxZoom));
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && _is3DMode)
            {
                _isSubmissiveMode = !_isSubmissiveMode;
                return;
            }

            if (e.ClickCount == 2)
            {
                this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
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
                double deltaX = currentPos.X - _lastMousePosRMB.X;
                double deltaY = currentPos.Y - _lastMousePosRMB.Y;

                _cubeRotY += deltaX * ManualRotationSensitivity;
                _cubeRotX += deltaY * ManualRotationSensitivity;

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

            if ((DateTime.Now - _lastClickTime).TotalSeconds > ClickTimeoutSeconds)
                _leftClickCount = 0;

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

        private void LeftEyeHitBox_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isLeftMouseHeldOnLeftEye = false;
            UpdateEyeVisibility();
        }

        private void LeftEyeHitBox_MouseLeave(object sender, MouseEventArgs e)
        {
            _isLeftMouseHeldOnLeftEye = false;
            UpdateEyeVisibility();
        }

        private void RightEyeHitBox_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _isLeftMouseHeldOnRightEye = true;
            UpdateEyeVisibility();
        }

        private void RightEyeHitBox_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isLeftMouseHeldOnRightEye = false;
            UpdateEyeVisibility();
        }

        private void RightEyeHitBox_MouseLeave(object sender, MouseEventArgs e)
        {
            _isLeftMouseHeldOnRightEye = false;
            UpdateEyeVisibility();
        }

        private void UpdateEyeVisibility()
        {
            if (LeftSquintImage == null || RightSquintImage == null) return;

            var activeCheek = Cheeks.FirstOrDefault(c => c.IsVisible);
            bool showBlush = activeCheek != null && activeCheek.HasBlush;

            bool isLeftSquint = IsLeftSquinting || _isLeftMouseHeldOnLeftEye;
            bool isLeftBlink = _isBlinking && !isLeftSquint;
            bool isLeftEyeVisible = !isLeftSquint && !isLeftBlink;
            bool isLeftBagVisible = isLeftEyeVisible && StressSlider.Value == StateShock;

            LeftSquintImage.Visibility = isLeftSquint ? Visibility.Visible : Visibility.Hidden;
            LeftBlinkImage.Visibility = isLeftBlink ? Visibility.Visible : Visibility.Hidden;
            LeftInnerRect.Visibility = isLeftEyeVisible ? Visibility.Visible : Visibility.Hidden;
            LeftEyeBorder.Visibility = isLeftEyeVisible ? Visibility.Visible : Visibility.Hidden;
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
            RightInnerRect.Visibility = isRightEyeVisible ? Visibility.Visible : Visibility.Hidden;
            RightEyeBorder.Visibility = isRightEyeVisible ? Visibility.Visible : Visibility.Hidden;
            RightEyeBag.Visibility = isRightBagVisible ? Visibility.Visible : Visibility.Hidden;

            if (RightEyebrowsControl != null) RightEyebrowsControl.Visibility = isRightSquint ? Visibility.Hidden : Visibility.Visible;
            if (RightCheeksControl != null) RightCheeksControl.Visibility = isRightSquint ? Visibility.Hidden : Visibility.Visible;
            if (RightBlushImage != null) RightBlushImage.Visibility = showBlush ? Visibility.Visible : Visibility.Hidden;

            if (CenterBlushBlurImage != null) CenterBlushBlurImage.Visibility = showBlush ? Visibility.Visible : Visibility.Hidden;
        }

        // ==========================================
        // ВОССТАНОВЛЕННЫЕ МЕТОДЫ РЕНДЕРА (RefreshAll, UpdateEyeNarrowness и т.д.)
        // ==========================================

        private void RefreshAll()
        {
            if (!IsLoaded) return;

            double actualWidth = _is3DMode ? Face3DResolution : MainGrid.ActualWidth;
            double actualHeight = _is3DMode ? Face3DResolution : MainGrid.ActualHeight;
            if (actualWidth <= 0) actualWidth = 800;
            if (actualHeight <= 0) actualHeight = 600;

            FaceWrapper.Width = actualWidth;
            FaceWrapper.Height = actualHeight;
            FaceCanvas.Width = actualWidth;
            FaceCanvas.Height = actualHeight;

            double projScale = _is3DMode ? CubeScaleSlider.Value : 1.0;
            double finalEyeScale = ScaleSlider.Value * projScale;

            _currentScale = finalEyeScale;
            _currentHeightOffset = HeightSlider.Value;

            LeftEyeContainer.RenderTransform = new ScaleTransform(finalEyeScale, finalEyeScale);
            RightEyeContainer.RenderTransform = new ScaleTransform(finalEyeScale, finalEyeScale);

            double centerX = actualWidth / HalfDivisor;
            double centerY = actualHeight / HalfDivisor;

            double scaledDistance = DistanceSlider.Value * finalEyeScale;
            double scaledHeightOff = HeightSlider.Value * finalEyeScale;

            Canvas.SetLeft(LeftEyeContainer, centerX - (scaledDistance / HalfDivisor) - (BaseEyeWidth / HalfDivisor));
            Canvas.SetLeft(RightEyeContainer, centerX + (scaledDistance / HalfDivisor) - (BaseEyeWidth / HalfDivisor));
            Canvas.SetTop(LeftEyeContainer, centerY - (BaseEyeHeight / HalfDivisor) + scaledHeightOff);
            Canvas.SetTop(RightEyeContainer, centerY - (BaseEyeHeight / HalfDivisor) + scaledHeightOff);

            if (CenterBlushBlurImage != null)
            {
                double blurWidth = 1500;
                double blurHeight = 800;
                double absoluteBlushBlurY = centerY - (BaseEyeHeight / HalfDivisor) + scaledHeightOff + (BlushBlurOffsetY * finalEyeScale);

                Canvas.SetLeft(CenterBlushBlurImage, centerX - (blurWidth / HalfDivisor));
                Canvas.SetTop(CenterBlushBlurImage, absoluteBlushBlurY - (blurHeight / HalfDivisor));
                CenterBlushBlurImage.RenderTransform = new ScaleTransform(BlushBlurScale * projScale, BlushBlurScale * projScale);
            }

            if (MouthsControl != null)
            {
                MouthsControl.RenderTransform = new ScaleTransform(projScale, projScale);
                Canvas.SetLeft(MouthsControl, centerX);
                Canvas.SetTop(MouthsControl, centerY);
            }

            LeftEyeBorder.StrokeThickness = FixedLineThickness / HalfDivisor;
            RightEyeBorder.StrokeThickness = FixedLineThickness / HalfDivisor;

            RefreshBagsGeometry();
            UpdateEyeNarrowness();
        }

        private void RefreshBagsGeometry()
        {
            double bagThickness = 2.0;
            LeftEyeBag.StrokeThickness = bagThickness;
            RightEyeBag.StrokeThickness = bagThickness;

            PointCollection leftPoints = new PointCollection
            {
                new Point(0 - BagOffsetX, BaseEyeHeight + BagOffsetY),
                new Point(0 - BagOffsetX, BaseEyeHeight + BagOffsetY + BagVerticalLen),
                new Point(0 - BagOffsetX + BagHorizontalLen, BaseEyeHeight + BagOffsetY + BagVerticalLen)
            };
            LeftEyeBag.Points = leftPoints;

            PointCollection rightPoints = new PointCollection
            {
                new Point(BaseEyeWidth + BagOffsetX, BaseEyeHeight + BagOffsetY),
                new Point(BaseEyeWidth + BagOffsetX, BaseEyeHeight + BagOffsetY + BagVerticalLen),
                new Point(BaseEyeWidth + BagOffsetX - BagHorizontalLen, BaseEyeHeight + BagOffsetY + BagVerticalLen)
            };
            RightEyeBag.Points = rightPoints;
        }

        private void UpdateEyeNarrowness()
        {
            if (LeftInnerRect == null) return;

            int state = (int)StressSlider.Value;
            double currentMultiplier = StressMultiplierCalm;

            if (state == StateExcited) currentMultiplier = StressMultiplierExcited;
            else if (state == StateShock) currentMultiplier = StressMultiplierShock;

            UpdateEyeVisibility();

            double maxW = BaseEyeWidth * MaxPupilScale;
            double maxH = BaseEyeHeight * MaxPupilScale;

            double nWidth = maxW * currentMultiplier;
            double nHeight = maxH * currentMultiplier;

            double diagThick = LeftEyeBorder.StrokeThickness * DiagonalThicknessRatio;

            DrawPupil(LeftInnerRect, LeftPath1, LeftPath2, LeftBezier1_Start, LeftBezier1_Seg, LeftBezier2_Start, LeftBezier2_Seg, nWidth, nHeight, diagThick, _leftTrackingX, _leftTrackingY);
            DrawPupil(RightInnerRect, RightPath1, RightPath2, RightBezier1_Start, RightBezier1_Seg, RightBezier2_Start, RightBezier2_Seg, nWidth, nHeight, diagThick, _rightTrackingX, _rightTrackingY);
        }

        private void DrawPupil(Canvas innerRect, System.Windows.Shapes.Path p1, System.Windows.Shapes.Path p2, PathFigure f1, QuadraticBezierSegment s1, PathFigure f2, QuadraticBezierSegment s2, double w, double h, double thick, double offsetX, double offsetY)
        {
            innerRect.Width = w;
            innerRect.Height = h;
            Canvas.SetLeft(innerRect, (BaseEyeWidth - w) / HalfDivisor);
            Canvas.SetTop(innerRect, (BaseEyeHeight - h) / HalfDivisor);

            p1.StrokeThickness = thick;
            p2.StrokeThickness = thick;

            double midX = w / HalfDivisor;
            double midY = h / HalfDivisor;

            double ctrlX = midX + (offsetX * HalfDivisor);
            double ctrlY = midY + (offsetY * HalfDivisor);

            f1.StartPoint = new Point(0, 0);
            s1.Point1 = new Point(ctrlX, ctrlY);
            s1.Point2 = new Point(w, h);

            f2.StartPoint = new Point(w, 0);
            s2.Point1 = new Point(ctrlX, ctrlY);
            s2.Point2 = new Point(0, h);
        }

        // ==========================================
        // ИНТЕРФЕЙС МЕНЮ И КОНФИГ
        // ==========================================

        private void CreateMouth_Click(object sender, RoutedEventArgs e)
        {
            foreach (var m in Mouths) { m.IsVisible = false; m.IsExpanded = false; }
            Mouths.Add(new MouthPreset { Name = "New Emotion", OffsetX = -50, OffsetY = 100, IsVisible = true, IsExpanded = true });
        }

        private void MouthFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MouthPreset preset)
            {
                var dialog = new OpenFileDialog { Filter = "Image Files|*.png;*.gif;*.jpg|All Files|*.*" };
                dialog.InitialDirectory = System.IO.Path.Combine(PathHelper.BaseAssetsDir, "assets", "mouth");
                if (dialog.ShowDialog() == true) preset.FilePath = PathHelper.GetRelativePath(dialog.FileName);
            }
        }

        private void DeleteMouth_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MouthPreset preset) Mouths.Remove(preset);
        }

        private void MouthName_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is TextBlock tb && VisualTreeHelper.GetParent(tb) is Grid grid)
            {
                if (grid.Children.Count > 1 && grid.Children[1] is TextBox txtBox)
                {
                    tb.Visibility = Visibility.Collapsed; txtBox.Visibility = Visibility.Visible;
                    txtBox.Focus(); txtBox.SelectAll(); e.Handled = true;
                }
            }
        }

        private void MouthName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txtBox && VisualTreeHelper.GetParent(txtBox) is Grid grid)
            {
                if (grid.Children.Count > 0 && grid.Children[0] is TextBlock tb)
                {
                    txtBox.Visibility = Visibility.Collapsed; tb.Visibility = Visibility.Visible;
                }
            }
        }

        private void MouthName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Keyboard.ClearFocus();
        }

        private void MouthVisibility_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is MouthPreset selectedMouth)
            {
                foreach (var mouth in Mouths) if (mouth != selectedMouth) mouth.IsVisible = false;
            }
        }

        private void MouthExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is MouthPreset expandedMouth)
            {
                foreach (var mouth in Mouths) if (mouth != expandedMouth) mouth.IsExpanded = false;
            }
        }

        private void MouthImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is MouthPreset mouth)
            {
                if (SettingsMenu.Visibility != Visibility.Visible || !mouth.IsExpanded) return;
                _isDraggingMouth = true; _draggedMouth = mouth;
                _dragStartMousePos = e.GetPosition(MouthsControl);
                _dragStartMouthOffset = new Point(mouth.OffsetX, mouth.OffsetY);
                fe.CaptureMouse(); e.Handled = true;
            }
        }

        private void MouthImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingMouth && _draggedMouth != null)
            {
                Point currentPos = e.GetPosition(MouthsControl);
                _draggedMouth.OffsetX = _dragStartMouthOffset.X + (currentPos.X - _dragStartMousePos.X);
                _draggedMouth.OffsetY = _dragStartMouthOffset.Y + (currentPos.Y - _dragStartMousePos.Y);
            }
        }

        private void MouthImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingMouth) { _isDraggingMouth = false; _draggedMouth = null; if (sender is FrameworkElement fe) fe.ReleaseMouseCapture(); }
        }

        private void CreateEyebrow_Click(object sender, RoutedEventArgs e)
        {
            foreach (var eb in Eyebrows) { eb.IsVisible = false; eb.IsExpanded = false; }
            Eyebrows.Add(new EyebrowPreset { Name = "New Eyebrows", IsVisible = true, IsExpanded = true });
        }

        private void DeleteEyebrow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is EyebrowPreset preset) Eyebrows.Remove(preset);
        }

        private void EyebrowName_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is TextBlock tb && VisualTreeHelper.GetParent(tb) is Grid grid)
            {
                if (grid.Children.Count > 1 && grid.Children[1] is TextBox txtBox)
                {
                    tb.Visibility = Visibility.Collapsed; txtBox.Visibility = Visibility.Visible;
                    txtBox.Focus(); txtBox.SelectAll(); e.Handled = true;
                }
            }
        }

        private void EyebrowName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txtBox && VisualTreeHelper.GetParent(txtBox) is Grid grid)
            {
                if (grid.Children.Count > 0 && grid.Children[0] is TextBlock tb)
                {
                    txtBox.Visibility = Visibility.Collapsed; tb.Visibility = Visibility.Visible;
                }
            }
        }

        private void EyebrowName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Keyboard.ClearFocus();
        }

        private void EyebrowVisibility_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is EyebrowPreset selected)
            {
                foreach (var eb in Eyebrows) if (eb != selected) eb.IsVisible = false;
            }
        }

        private void EyebrowExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is EyebrowPreset expanded)
            {
                foreach (var eb in Eyebrows) if (eb != expanded) eb.IsExpanded = false;
            }
        }

        private void CreateCheek_Click(object sender, RoutedEventArgs e)
        {
            foreach (var ch in Cheeks) { ch.IsVisible = false; ch.IsExpanded = false; }
            Cheeks.Add(new CheekPreset { Name = "New Cheeks", IsVisible = true, IsExpanded = true });
        }

        private void DeleteCheek_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CheekPreset preset) Cheeks.Remove(preset);
        }

        private void CheekName_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is TextBlock tb && VisualTreeHelper.GetParent(tb) is Grid grid)
            {
                if (grid.Children.Count > 1 && grid.Children[1] is TextBox txtBox)
                {
                    tb.Visibility = Visibility.Collapsed; txtBox.Visibility = Visibility.Visible;
                    txtBox.Focus(); txtBox.SelectAll(); e.Handled = true;
                }
            }
        }

        private void CheekName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txtBox && VisualTreeHelper.GetParent(txtBox) is Grid grid)
            {
                if (grid.Children.Count > 0 && grid.Children[0] is TextBlock tb)
                {
                    txtBox.Visibility = Visibility.Collapsed; tb.Visibility = Visibility.Visible;
                }
            }
        }

        private void CheekName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Keyboard.ClearFocus();
        }

        private void CheekVisibility_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is CheekPreset selected)
            {
                foreach (var ch in Cheeks) if (ch != selected) ch.IsVisible = false;
                UpdateEyeVisibility();
            }
        }

        private void CheekExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is CheekPreset expanded)
            {
                foreach (var ch in Cheeks) if (ch != expanded) ch.IsExpanded = false;
            }
        }

        private void Settings_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => RefreshAll();
        private void StressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateEyeNarrowness();
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshAll();

        private void CloseMenu_Click(object sender, RoutedEventArgs e)
        {
            SettingsMenu.Visibility = Visibility.Collapsed;
            this.WindowState = WindowState.Normal;
            Toggle3DMode(true);
        }

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

        private void ApplyLoadedConfig()
        {
            FaceConfig? config = ConfigManager.Load();
            if (config != null)
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