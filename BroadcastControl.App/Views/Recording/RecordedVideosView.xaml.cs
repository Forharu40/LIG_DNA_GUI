using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BroadcastControl.App.Views.Recording;

public partial class RecordedVideosView : UserControl
{
    public RecordedVideosView()
    {
        InitializeComponent();
    }

    public Border RecordedVideosPanelElement => RecordedVideosPanel;
    public TextBlock RecordedVideosStatusTextElement => RecordedVideosStatusText;
    public ListBox RecordedVideoListElement => RecordedVideoList;
    public Grid RecordedVideoViewportElement => RecordedVideoViewport;
    public MediaElement RecordedVideoPlayerElement => RecordedVideoPlayer;
    public ScaleTransform RecordedVideoScaleTransformElement => RecordedVideoScaleTransform;
    public TranslateTransform RecordedVideoTranslateTransformElement => RecordedVideoTranslateTransform;
    public Border RecordedVideoZoomMiniMapElement => RecordedVideoZoomMiniMap;
    public Rectangle RecordedVideoMiniMapViewportElement => RecordedVideoMiniMapViewport;
    public TextBlock RecordedVideoCurrentTimeTextElement => RecordedVideoCurrentTimeText;
    public Slider RecordedVideoPositionSliderElement => RecordedVideoPositionSlider;
    public TextBlock RecordedVideoDurationTextElement => RecordedVideoDurationText;
    public Button RecordedVideoZoomResetButtonElement => RecordedVideoZoomResetButton;
    public Slider RecordedVideoZoomSliderElement => RecordedVideoZoomSlider;
    public ComboBox PlaybackSpeedComboElement => PlaybackSpeedCombo;

    public event RoutedEventHandler? RefreshRecordedVideosClicked;
    public event RoutedEventHandler? CloseRecordedVideosClicked;
    public event SelectionChangedEventHandler? RecordedVideoSelectionChanged;
    public event MouseWheelEventHandler? RecordedVideoViewportMouseWheel;
    public event MouseButtonEventHandler? RecordedVideoViewportMouseLeftButtonDown;
    public event MouseEventHandler? RecordedVideoViewportMouseMove;
    public event MouseButtonEventHandler? RecordedVideoViewportMouseLeftButtonUp;
    public event RoutedEventHandler? RecordedVideoMediaOpened;
    public event RoutedEventHandler? RecordedVideoMediaEnded;
    public event EventHandler<ExceptionRoutedEventArgs>? RecordedVideoMediaFailed;
    public event RoutedPropertyChangedEventHandler<double>? RecordedVideoPositionSliderValueChanged;
    public event MouseButtonEventHandler? RecordedVideoPositionSliderPreviewMouseLeftButtonDown;
    public event MouseButtonEventHandler? RecordedVideoPositionSliderPreviewMouseLeftButtonUp;
    public event RoutedEventHandler? RecordedVideoPlayClicked;
    public event RoutedEventHandler? RecordedVideoPauseClicked;
    public event RoutedEventHandler? RecordedVideoStopClicked;
    public event RoutedEventHandler? RecordedVideoZoomResetClicked;
    public event RoutedPropertyChangedEventHandler<double>? RecordedVideoZoomSliderValueChanged;
    public event SelectionChangedEventHandler? PlaybackSpeedSelectionChanged;

    private void RefreshRecordedVideosButton_OnClick(object sender, RoutedEventArgs e) => RefreshRecordedVideosClicked?.Invoke(sender, e);
    private void CloseRecordedVideosButton_OnClick(object sender, RoutedEventArgs e) => CloseRecordedVideosClicked?.Invoke(sender, e);
    private void RecordedVideoList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => RecordedVideoSelectionChanged?.Invoke(sender, e);
    private void RecordedVideoViewport_OnMouseWheel(object sender, MouseWheelEventArgs e) => RecordedVideoViewportMouseWheel?.Invoke(sender, e);
    private void RecordedVideoViewport_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => RecordedVideoViewportMouseLeftButtonDown?.Invoke(sender, e);
    private void RecordedVideoViewport_OnMouseMove(object sender, MouseEventArgs e) => RecordedVideoViewportMouseMove?.Invoke(sender, e);
    private void RecordedVideoViewport_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => RecordedVideoViewportMouseLeftButtonUp?.Invoke(sender, e);
    private void RecordedVideoPlayer_OnMediaOpened(object sender, RoutedEventArgs e) => RecordedVideoMediaOpened?.Invoke(sender, e);
    private void RecordedVideoPlayer_OnMediaEnded(object sender, RoutedEventArgs e) => RecordedVideoMediaEnded?.Invoke(sender, e);
    private void RecordedVideoPlayer_OnMediaFailed(object sender, ExceptionRoutedEventArgs e) => RecordedVideoMediaFailed?.Invoke(sender, e);
    private void RecordedVideoPositionSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => RecordedVideoPositionSliderValueChanged?.Invoke(sender, e);
    private void RecordedVideoPositionSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => RecordedVideoPositionSliderPreviewMouseLeftButtonDown?.Invoke(sender, e);
    private void RecordedVideoPositionSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => RecordedVideoPositionSliderPreviewMouseLeftButtonUp?.Invoke(sender, e);
    private void RecordedVideoPlayButton_OnClick(object sender, RoutedEventArgs e) => RecordedVideoPlayClicked?.Invoke(sender, e);
    private void RecordedVideoPauseButton_OnClick(object sender, RoutedEventArgs e) => RecordedVideoPauseClicked?.Invoke(sender, e);
    private void RecordedVideoStopButton_OnClick(object sender, RoutedEventArgs e) => RecordedVideoStopClicked?.Invoke(sender, e);
    private void RecordedVideoZoomResetButton_OnClick(object sender, RoutedEventArgs e) => RecordedVideoZoomResetClicked?.Invoke(sender, e);
    private void RecordedVideoZoomSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => RecordedVideoZoomSliderValueChanged?.Invoke(sender, e);
    private void PlaybackSpeedCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => PlaybackSpeedSelectionChanged?.Invoke(sender, e);
}
