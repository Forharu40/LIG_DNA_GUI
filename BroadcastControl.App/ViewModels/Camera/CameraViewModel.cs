using System.Windows.Media;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App.ViewModels.Camera;

public sealed class CameraViewModel : ViewModelBase
{
    private ImageSource? _eoFrame;
    private ImageSource? _irFrame;
    private double _zoomLevel = 1.0;
    private double _brightness = 50;
    private double _contrast = 50;

    public ImageSource? EoFrame
    {
        get => _eoFrame;
        set => SetProperty(ref _eoFrame, value);
    }

    public ImageSource? IrFrame
    {
        get => _irFrame;
        set => SetProperty(ref _irFrame, value);
    }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set => SetProperty(ref _zoomLevel, value);
    }

    public double Brightness
    {
        get => _brightness;
        set => SetProperty(ref _brightness, value);
    }

    public double Contrast
    {
        get => _contrast;
        set => SetProperty(ref _contrast, value);
    }
}
