// Version: 40 stable
using System.ComponentModel;
using System.Text.Json.Serialization;
using TheGodFace.Utils;

namespace TheGodFace.Models
{
    // One mouth image with its placement, bound to the mouth list in MainWindow and saved to face_config.json.
    public class MouthPreset : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private string _name = "New Emotion";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

        private string _filePath = "";
        public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(nameof(FilePath)); OnPropertyChanged(nameof(FullPath)); } }

        // Absolute image path resolved from FilePath for the WPF image binding.
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

        // Scale with the flip sign applied, used by the mouth ScaleTransform.
        [JsonIgnore]
        public double ScaleXWithFlip => Scale * (FlipX ? -1 : 1);
    }

    // Eyebrow image shared by both eyes, each eye getting its own offset, rotation and flip.
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

        // Per-eye scale with the mirror sign, consumed by the eyebrow ScaleTransforms.
        [JsonIgnore]
        public double LeftScaleX => Scale * (FlipLeftX ? -1 : 1);
        [JsonIgnore]
        public double RightScaleX => Scale * (FlipRightX ? -1 : 1);

        // Canvas positions derived from the offsets, so the preset stores one number per eye.
        [JsonIgnore]
        public double LeftCanvasTop => 100 - LeftOffset;
        [JsonIgnore]
        public double RightCanvasTop => 100 - RightOffset;

        // Render flags combined with the preset visibility for the XAML bindings.
        [JsonIgnore]
        public bool IsLeftVisible => IsVisible && RenderLeft;
        [JsonIgnore]
        public bool IsRightVisible => IsVisible && RenderRight;
    }

    // Cheek decal with a mask that clips the eye graphics and an optional blush layer.
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

        // Toggles the blush images that sit behind the eyes and in the middle of the face.
        private bool _hasBlush = false;
        public bool HasBlush { get => _hasBlush; set { _hasBlush = value; OnPropertyChanged(nameof(HasBlush)); } }

        // Canvas positions derived from the offsets, mirroring the eyebrow preset behaviour.
        [JsonIgnore]
        public double LeftCanvasTop => 100 - LeftOffset;
        [JsonIgnore]
        public double RightCanvasTop => 100 - RightOffset;

        // Render flags combined with the preset visibility for the XAML bindings.
        [JsonIgnore]
        public bool IsLeftVisible => IsVisible && RenderLeft;
        [JsonIgnore]
        public bool IsRightVisible => IsVisible && RenderRight;
    }

    // Whole persistable face state: geometry sliders, gaze, decal files and all preset lists.
    public class FaceConfig
    {
        // Eye scaling shared by both eyes, the slider value from the editor is mirrored here.
        public double EyeScale { get; set; } = 1.723811367578239;
        // Scale of the face content on the cube front face in 3D mode.
        public double CubeFaceScale { get; set; } = 1.1696186905904113;
        // Distance between eye centers in canvas units.
        public double EyeDistance { get; set; } = 228.22250194741417;
        // Vertical offset of both eyes from the canvas center.
        public double EyeHeightOffset { get; set; } = -58.396279027760485;
        // Stress slider state: calm, excited or shock, it changes pupil size and eye bags.
        public int StressState { get; set; } = 1;

        public bool GazeFree { get; set; } = true;
        public bool GazeStraight { get; set; } = false;
        public bool GazeLeft { get; set; } = false;
        public bool GazeRight { get; set; } = false;
        public bool GazeDown { get; set; } = false;

        // Blink decal drawn over the eyes while the blink behavior runs.
        public string BlinkFilePath { get; set; } = "assets\\decals\\blink.png";
        public double BlinkScale { get; set; } = 0.1533391326353083;
        // Squint decal drawn instead of the eye when the model asks for a squint.
        public string SquintFilePath { get; set; } = "assets\\decals\\squint.png";
        public double SquintScale { get; set; } = 0.17557492170942327;

        public bool IsLeftSquinting { get; set; } = false;
        public bool IsRightSquinting { get; set; } = false;

        // Shows the sweat drop decal, usually set together with the shock stress state.
        public bool HasSweatDrop { get; set; } = false;

        // Mouth presets carried between sessions and shown one at a time.
        public System.Collections.Generic.List<MouthPreset> Mouths { get; set; } = new System.Collections.Generic.List<MouthPreset>();
        // Eyebrow presets, seeded with one default entry so the editor is never empty.
        public System.Collections.Generic.List<EyebrowPreset> Eyebrows { get; set; } = new System.Collections.Generic.List<EyebrowPreset>
        {
            new EyebrowPreset { Name = "New Eyebrows", IsVisible = true, IsExpanded = false, LeftOffset = 200, RightOffset = 200 }
        };
        // Cheek presets, seeded like the eyebrows so the face always has one decal set.
        public System.Collections.Generic.List<CheekPreset> Cheeks { get; set; } = new System.Collections.Generic.List<CheekPreset>
        {
            new CheekPreset { Name = "New Cheeks", IsVisible = true, IsExpanded = false, LeftOffset = 7.804878048780504, RightOffset = 7.804878048780504 }
        };
    }
}