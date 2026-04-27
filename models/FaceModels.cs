// Version: 40 stable
using System.ComponentModel;
using System.Text.Json.Serialization;
using TheGodFace.Utils;

namespace TheGodFace.Models
{
    public class MouthPreset : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private string _name = "New Emotion";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

        private string _filePath = "";
        public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(FullPath)); } }

        [JsonIgnore]
        public string FullPath => PathHelper.GetFullPath(FilePath);

        private double _offsetX = -50;
        public double OffsetX { get => _offsetX; set { _offsetX = value; OnPropertyChanged(nameof(OffsetX)); } }

        private double _offsetY = -50;
        public double OffsetY { get => _offsetY; set { _offsetY = value; OnPropertyChanged(nameof(OffsetY)); } }

        private double _scale = 1.0;
        public double Scale { get => _scale; set { _scale = value; OnPropertyChanged(nameof(Scale)); OnPropertyChanged(nameof(ScaleXWithFlip)); } }

        private double _rotation = 0.0;
        public double Rotation { get => _rotation; set { _rotation = value; OnPropertyChanged(nameof(Rotation)); } }

        private bool _flipX = false;
        public bool FlipX { get => _flipX; set { _flipX = value; OnPropertyChanged(nameof(FlipX)); OnPropertyChanged(nameof(ScaleXWithFlip)); } }

        private bool _isVisible = true;
        public bool IsVisible { get => _isVisible; set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); } }

        private bool _isExpanded = false;
        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); } }

        [JsonIgnore]
        public double ScaleXWithFlip => Scale * (FlipX ? -1 : 1);
    }

    public class EyebrowPreset : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private string _name = "New Eyebrows";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

        private string _filePath = "assets\\decals\\brow.png";
        public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(FullPath)); } }

        [JsonIgnore]
        public string FullPath => PathHelper.GetFullPath(FilePath);

        private double _scale = 0.8825468164794008;
        public double Scale { get => _scale; set { _scale = value; OnPropertyChanged(nameof(Scale)); OnPropertyChanged(nameof(LeftScaleX)); OnPropertyChanged(nameof(RightScaleX)); } }

        private double _leftRotation = 0.0;
        public double LeftRotation { get => _leftRotation; set { _leftRotation = value; OnPropertyChanged(nameof(LeftRotation)); } }

        private double _rightRotation = 0.0;
        public double RightRotation { get => _rightRotation; set { _rightRotation = value; OnPropertyChanged(nameof(RightRotation)); } }

        private double _leftOffset = 200;
        public double LeftOffset { get => _leftOffset; set { _leftOffset = value; OnPropertyChanged(nameof(LeftOffset)); OnPropertyChanged(nameof(LeftCanvasTop)); } }

        private double _rightOffset = 200;
        public double RightOffset { get => _rightOffset; set { _rightOffset = value; OnPropertyChanged(nameof(RightOffset)); OnPropertyChanged(nameof(RightCanvasTop)); } }

        private bool _flipLeftX = false;
        public bool FlipLeftX { get => _flipLeftX; set { _flipLeftX = value; OnPropertyChanged(nameof(FlipLeftX)); OnPropertyChanged(nameof(LeftScaleX)); } }

        private bool _flipRightX = true;
        public bool FlipRightX { get => _flipRightX; set { _flipRightX = value; OnPropertyChanged(nameof(FlipRightX)); OnPropertyChanged(nameof(RightScaleX)); } }

        private bool _renderLeft = true;
        public bool RenderLeft { get => _renderLeft; set { _renderLeft = value; OnPropertyChanged(nameof(RenderLeft)); OnPropertyChanged(nameof(IsLeftVisible)); } }

        private bool _renderRight = true;
        public bool RenderRight { get => _renderRight; set { _renderRight = value; OnPropertyChanged(nameof(RenderRight)); OnPropertyChanged(nameof(IsRightVisible)); } }

        private bool _isVisible = true;
        public bool IsVisible { get => _isVisible; set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); OnPropertyChanged(nameof(IsLeftVisible)); OnPropertyChanged(nameof(IsRightVisible)); } }

        private bool _isExpanded = false;
        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); } }

        [JsonIgnore]
        public double LeftScaleX => Scale * (FlipLeftX ? -1 : 1);
        [JsonIgnore]
        public double RightScaleX => Scale * (FlipRightX ? -1 : 1);

        [JsonIgnore]
        public double LeftCanvasTop => 100 - LeftOffset;
        [JsonIgnore]
        public double RightCanvasTop => 100 - RightOffset;

        [JsonIgnore]
        public bool IsLeftVisible => IsVisible && RenderLeft;
        [JsonIgnore]
        public bool IsRightVisible => IsVisible && RenderRight;
    }

    public class CheekPreset : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private string _name = "New Cheeks";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

        private string _filePath = "assets\\decals\\cheek.png";
        public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(FullPath)); } }

        [JsonIgnore]
        public string FullPath => PathHelper.GetFullPath(FilePath);

        private double _scale = 0.7650936329588012;
        public double Scale { get => _scale; set { _scale = value; OnPropertyChanged(nameof(Scale)); } }

        private double _maskWidth = 197.8314606741573;
        public double MaskWidth { get => _maskWidth; set { _maskWidth = value; OnPropertyChanged(nameof(MaskWidth)); } }

        private double _maskHeight = 69.26033707865169;
        public double MaskHeight { get => _maskHeight; set { _maskHeight = value; OnPropertyChanged(nameof(MaskHeight)); } }

        private double _leftRotation = 0.0;
        public double LeftRotation { get => _leftRotation; set { _leftRotation = value; OnPropertyChanged(nameof(LeftRotation)); } }

        private double _rightRotation = 0.0;
        public double RightRotation { get => _rightRotation; set { _rightRotation = value; OnPropertyChanged(nameof(RightRotation)); } }

        private double _leftOffset = 7.804878048780504;
        public double LeftOffset { get => _leftOffset; set { _leftOffset = value; OnPropertyChanged(nameof(LeftOffset)); OnPropertyChanged(nameof(LeftCanvasTop)); } }

        private double _rightOffset = 7.804878048780504;
        public double RightOffset { get => _rightOffset; set { _rightOffset = value; OnPropertyChanged(nameof(RightOffset)); OnPropertyChanged(nameof(RightCanvasTop)); } }

        private bool _renderLeft = true;
        public bool RenderLeft { get => _renderLeft; set { _renderLeft = value; OnPropertyChanged(nameof(RenderLeft)); OnPropertyChanged(nameof(IsLeftVisible)); } }

        private bool _renderRight = true;
        public bool RenderRight { get => _renderRight; set { _renderRight = value; OnPropertyChanged(nameof(RenderRight)); OnPropertyChanged(nameof(IsRightVisible)); } }

        private bool _isVisible = true;
        public bool IsVisible { get => _isVisible; set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); OnPropertyChanged(nameof(IsLeftVisible)); OnPropertyChanged(nameof(IsRightVisible)); } }

        private bool _isExpanded = false;
        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); } }

        private bool _hasBlush = false;
        public bool HasBlush { get => _hasBlush; set { _hasBlush = value; OnPropertyChanged(nameof(HasBlush)); } }

        [JsonIgnore]
        public double LeftCanvasTop => 100 - LeftOffset;
        [JsonIgnore]
        public double RightCanvasTop => 100 - RightOffset;

        [JsonIgnore]
        public bool IsLeftVisible => IsVisible && RenderLeft;
        [JsonIgnore]
        public bool IsRightVisible => IsVisible && RenderRight;
    }

    public class FaceConfig
    {
        public double EyeScale { get; set; } = 1.723811367578239;
        public double CubeFaceScale { get; set; } = 1.1696186905904113;
        public double EyeDistance { get; set; } = 228.22250194741417;
        public double EyeHeightOffset { get; set; } = -58.396279027760485;
        public int StressState { get; set; } = 1;

        public bool GazeFree { get; set; } = true;
        public bool GazeStraight { get; set; } = false;
        public bool GazeLeft { get; set; } = false;
        public bool GazeRight { get; set; } = false;
        public bool GazeDown { get; set; } = false;

        public string BlinkFilePath { get; set; } = "assets\\decals\\blink.png";
        public double BlinkScale { get; set; } = 0.1533391326353083;
        public string SquintFilePath { get; set; } = "assets\\decals\\squint.png";
        public double SquintScale { get; set; } = 0.17557492170942327;

        public bool IsLeftSquinting { get; set; } = false;
        public bool IsRightSquinting { get; set; } = false;

        public bool HasSweatDrop { get; set; } = false;

        public System.Collections.Generic.List<MouthPreset> Mouths { get; set; } = new System.Collections.Generic.List<MouthPreset>();
        public System.Collections.Generic.List<EyebrowPreset> Eyebrows { get; set; } = new System.Collections.Generic.List<EyebrowPreset>
        {
            new EyebrowPreset { Name = "New Eyebrows", IsVisible = true, IsExpanded = false, LeftOffset = 200, RightOffset = 200 }
        };
        public System.Collections.Generic.List<CheekPreset> Cheeks { get; set; } = new System.Collections.Generic.List<CheekPreset>
        {
            new CheekPreset { Name = "New Cheeks", IsVisible = true, IsExpanded = false, LeftOffset = 7.804878048780504, RightOffset = 7.804878048780504 }
        };
    }
}