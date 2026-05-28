using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BroadcastControl.App.Models.Camera;
using BroadcastControl.App.Models.Motor;
using BroadcastControl.App.Models.Network;
using BroadcastControl.App.Models.Vlm;
using BroadcastControl.App.ViewModels;

namespace BroadcastControl.App;

public partial class MainWindow : Window
{
    private void CameraViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel.UpdateViewportSize(e.NewSize.Width, e.NewSize.Height);
        UpdateRecordingViewportState();
        RenderDetectionOverlay(forceRefresh: true);
    }

    private void CameraViewport_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TrySelectDetectionAtPoint(e.GetPosition(CameraActiveView.CameraViewportElement)))
        {
            e.Handled = true;
            return;
        }

        if (!_viewModel.ShowZoomMiniMap)
        {
            return;
        }

        _isDraggingZoom = true;
        _lastZoomDragPoint = e.GetPosition(CameraActiveView.CameraViewportElement);
        CameraActiveView.CameraViewportElement.CaptureMouse();
    }

    private void CameraViewport_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingZoom)
        {
            return;
        }

        var currentPoint = e.GetPosition(CameraActiveView.CameraViewportElement);
        var delta = currentPoint - _lastZoomDragPoint;
        _lastZoomDragPoint = currentPoint;

        _viewModel.PanZoom(delta.X, delta.Y);
    }

    private void CameraViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_viewModel.CanUseZoomControls)
        {
            return;
        }

        _viewModel.AdjustZoomByWheel(e.Delta / 120.0);
        e.Handled = true;
    }

    private void CameraViewport_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingZoom)
        {
            return;
        }

        _isDraggingZoom = false;
        CameraActiveView.CameraViewportElement.ReleaseMouseCapture();
    }

    private void RenderDetectionOverlay(bool forceRefresh = false)
    {
        if (_isRenderingOverlay)
        {
            return;
        }

        if (CameraActiveView.DetectionOverlayCanvasElement is null)
        {
            return;
        }

        _isRenderingOverlay = true;
        try
        {
            CameraActiveView.DetectionOverlayCanvasElement.Children.Clear();

            var latestFrame = _viewModel.IsEoPrimary ? _latestEoFrame : _latestIrFrame;
            if (latestFrame is null)
            {
                _lastOverlaySignature = null;
                return;
            }

            if (!TryGetRenderableFrameAndDetection(out var frameToRender, out var detectionPacket))
            {
                _lastOverlaySignature = null;
                return;
            }

            var displayDetections = ApplyThreatLevels(FilterDisplayDetections(detectionPacket.Detections));
            if (displayDetections.Count == 0)
            {
                _lastOverlaySignature = null;
                return;
            }

            var rotation = GetCurrentDisplayRotation();
            var originalSourceWidth = detectionPacket.Width > 0 ? detectionPacket.Width : frameToRender.Width;
            var originalSourceHeight = detectionPacket.Height > 0 ? detectionPacket.Height : frameToRender.Height;
            if (originalSourceWidth <= 0 || originalSourceHeight <= 0)
            {
                return;
            }

            var rotatedSourceWidth = GetRotatedWidth(originalSourceWidth, originalSourceHeight, rotation);
            var rotatedSourceHeight = GetRotatedHeight(originalSourceWidth, originalSourceHeight, rotation);
            var rotatedDetections = displayDetections
                .Select(d => RotateDetectionForDisplay(d, originalSourceWidth, originalSourceHeight, rotation))
                .ToArray();
            var activeTrackId = detectionPacket.ActiveTrackId;
            var overlaySignature = $"{rotation}:{activeTrackId}:{BuildOverlaySignature(rotatedDetections)}";
            if (!forceRefresh && string.Equals(_lastOverlaySignature, overlaySignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastOverlaySignature = overlaySignature;

            var viewportWidth = Math.Max(CameraActiveView.CameraViewportElement.ActualWidth, 1);
            var viewportHeight = Math.Max(CameraActiveView.CameraViewportElement.ActualHeight, 1);
            var baseScale = Math.Min(viewportWidth / rotatedSourceWidth, viewportHeight / rotatedSourceHeight);
            var scaleX = baseScale;
            var scaleY = baseScale;
            var scaledWidth = rotatedSourceWidth * scaleX;
            var scaledHeight = rotatedSourceHeight * scaleY;
            var baseLeft = (viewportWidth - scaledWidth) / 2.0;
            var baseTop = (viewportHeight - scaledHeight) / 2.0;

            foreach (var detection in rotatedDetections)
            {
                var rectLeft = baseLeft + (detection.X1 * scaleX);
                var rectTop = baseTop + (detection.Y1 * scaleY);
                var rectWidth = Math.Max(2, (detection.X2 - detection.X1) * scaleX);
                var rectHeight = Math.Max(2, (detection.Y2 - detection.Y1) * scaleY);

                if (rectWidth < 2 || rectHeight < 2)
                {
                    continue;
                }

                var isTrackId = detection.ObjectId == activeTrackId && activeTrackId != 0xFF;
                AddDetectionVisualToCanvas(rectLeft, rectTop, rectWidth, rectHeight, detection, isTrackId);
            }

            if (!_hasRenderedDetectionOverlay)
            {
                _hasRenderedDetectionOverlay = true;
            }
        }
        finally
        {
            _isRenderingOverlay = false;
        }
    }

    private DisplayRotation GetCurrentDisplayRotation()
    {
        return DisplayRotation.None;
    }

    private bool TrySelectDetectionAtPoint(Point viewportPoint)
    {
        if (!TryGetRenderableFrameAndDetection(out var frameToRender, out var detectionPacket))
        {
            return false;
        }

        var displayDetections = ApplyThreatLevels(FilterDisplayDetections(detectionPacket.Detections));
        if (displayDetections.Count == 0)
        {
            return false;
        }

        var rotation = GetCurrentDisplayRotation();
        var originalSourceWidth = detectionPacket.Width > 0 ? detectionPacket.Width : frameToRender.Width;
        var originalSourceHeight = detectionPacket.Height > 0 ? detectionPacket.Height : frameToRender.Height;
        if (originalSourceWidth <= 0 || originalSourceHeight <= 0)
        {
            return false;
        }

        var rotatedSourceWidth = GetRotatedWidth(originalSourceWidth, originalSourceHeight, rotation);
        var rotatedSourceHeight = GetRotatedHeight(originalSourceWidth, originalSourceHeight, rotation);
        var viewportWidth = Math.Max(CameraActiveView.CameraViewportElement.ActualWidth, 1);
        var viewportHeight = Math.Max(CameraActiveView.CameraViewportElement.ActualHeight, 1);
        var baseScale = Math.Min(viewportWidth / rotatedSourceWidth, viewportHeight / rotatedSourceHeight);
        var scaledWidth = rotatedSourceWidth * baseScale;
        var scaledHeight = rotatedSourceHeight * baseScale;
        var baseLeft = (viewportWidth - scaledWidth) / 2.0;
        var baseTop = (viewportHeight - scaledHeight) / 2.0;

        var zoomLevel = Math.Max(_viewModel.ZoomLevel, 1.0);
        var viewportCenter = new Point(viewportWidth / 2.0, viewportHeight / 2.0);

        var selectedDetection = displayDetections
            .Select(detection => RotateDetectionForDisplay(detection, originalSourceWidth, originalSourceHeight, rotation))
            .Select(detection => new
            {
                Detection = detection,
                Rect = TransformRectForZoom(
                    new Rect(
                        baseLeft + (detection.X1 * baseScale),
                        baseTop + (detection.Y1 * baseScale),
                        Math.Max(2, (detection.X2 - detection.X1) * baseScale),
                        Math.Max(2, (detection.Y2 - detection.Y1) * baseScale)),
                    viewportCenter,
                    zoomLevel,
                    _viewModel.ZoomTransformX,
                    _viewModel.ZoomTransformY)
            })
            .Where(item => item.Rect.Contains(viewportPoint))
            .OrderByDescending(item => GetThreatWeight(item.Detection.ThreatLevel))
            .ThenByDescending(item => item.Detection.Score)
            .FirstOrDefault();

        if (selectedDetection is null)
        {
            return false;
        }

        _viewModel.SelectYoloObject(selectedDetection.Detection.ObjectId, selectedDetection.Detection.ThreatLevel);
        return true;
    }

    private static Rect TransformRectForZoom(Rect rect, Point center, double scale, double translateX, double translateY)
    {
        var topLeft = TransformPointForZoom(rect.TopLeft, center, scale, translateX, translateY);
        var bottomRight = TransformPointForZoom(rect.BottomRight, center, scale, translateX, translateY);
        return new Rect(topLeft, bottomRight);
    }

    private static Point TransformPointForZoom(Point point, Point center, double scale, double translateX, double translateY)
    {
        return new Point(
            center.X + ((point.X - center.X) * scale) + translateX,
            center.Y + ((point.Y - center.Y) * scale) + translateY);
    }

    private static string BuildOverlaySignature(IReadOnlyList<DetectionInfo> detections)
    {
        return string.Join(
            "|",
            detections.Select(d => $"{d.ObjectId}:{d.ClassName}:{d.ThreatLevel}:{d.X1:0}:{d.Y1:0}:{d.X2:0}:{d.Y2:0}"));
    }

    private static int GetRotatedWidth(int sourceWidth, int sourceHeight, DisplayRotation rotation)
    {
        return rotation == DisplayRotation.RotateLeft90 ? sourceHeight : sourceWidth;
    }

    private static int GetRotatedHeight(int sourceWidth, int sourceHeight, DisplayRotation rotation)
    {
        return rotation == DisplayRotation.RotateLeft90 ? sourceWidth : sourceHeight;
    }

    private static DetectionInfo RotateDetectionForDisplay(
        DetectionInfo detection,
        int sourceWidth,
        int sourceHeight,
        DisplayRotation rotation)
    {
        return rotation switch
        {
            DisplayRotation.Rotate180 => new DetectionInfo(
                detection.ClassName,
                detection.Score,
                (float)(sourceWidth - detection.X2),
                (float)(sourceHeight - detection.Y2),
                (float)(sourceWidth - detection.X1),
                (float)(sourceHeight - detection.Y1),
                detection.ObjectId,
                detection.ThreatLevel),
            DisplayRotation.RotateLeft90 => RotateDetectionLeft90(detection, sourceWidth),
            _ => detection
        };
    }

    private static DetectionInfo RotateDetectionLeft90(DetectionInfo detection, int sourceWidth)
    {
        var rotatedCorners = new[]
        {
            RotatePointLeft90(detection.X1, detection.Y1, sourceWidth),
            RotatePointLeft90(detection.X2, detection.Y1, sourceWidth),
            RotatePointLeft90(detection.X2, detection.Y2, sourceWidth),
            RotatePointLeft90(detection.X1, detection.Y2, sourceWidth)
        };

        var x1 = rotatedCorners.Min(point => point.X);
        var y1 = rotatedCorners.Min(point => point.Y);
        var x2 = rotatedCorners.Max(point => point.X);
        var y2 = rotatedCorners.Max(point => point.Y);

        return new DetectionInfo(
            detection.ClassName,
            detection.Score,
            (float)x1,
            (float)y1,
            (float)x2,
            (float)y2,
            detection.ObjectId,
            detection.ThreatLevel);
    }

    private static Point RotatePointLeft90(double x, double y, int sourceWidth)
    {
        return new Point(y, sourceWidth - x);
    }

    private static void CacheFrame(ReceivedVideoFrame frame, Dictionary<uint, ReceivedVideoFrame> frameCache)
    {
        frameCache[frame.FrameIndex] = frame;
        TrimCache(frameCache);
    }

    private static void CacheDetectionPacket(
        DetectionPacket detectionPacket,
        Dictionary<uint, DetectionPacket> detectionCache)
    {
        detectionCache[detectionPacket.FrameId] = detectionPacket;
        TrimCache(detectionCache);
    }

    private bool TryGetRenderableDetectionPacket(
        uint currentFrameId,
        Dictionary<uint, DetectionPacket> detectionCache,
        out DetectionPacket detectionPacket)
    {
        if (detectionCache.TryGetValue(currentFrameId, out detectionPacket))
        {
            return true;
        }

        var recentCandidates = detectionCache
            .Where(pair => pair.Value.Detections.Count > 0 && pair.Key <= currentFrameId)
            .OrderByDescending(pair => pair.Key)
            .ToArray();

        foreach (var candidate in recentCandidates)
        {
            var frameGap = currentFrameId >= candidate.Key
                ? currentFrameId - candidate.Key
                : uint.MaxValue;
            if (frameGap > OverlayFrameTolerance)
            {
                break;
            }

            detectionPacket = candidate.Value;
            return true;
        }

        var fallbackCandidates = detectionCache
            .Where(pair => pair.Value.Detections.Count > 0)
            .OrderByDescending(pair => pair.Key)
            .ToArray();

        foreach (var candidate in fallbackCandidates)
        {
            detectionPacket = candidate.Value;
            return true;
        }

        detectionPacket = default;
        return false;
    }

    private bool TryGetRenderableFrameAndDetection(out ReceivedVideoFrame frame, out DetectionPacket detectionPacket)
    {
        var latestFrame = _viewModel.IsEoPrimary ? _latestEoFrame : _latestIrFrame;
        var frameCache = _viewModel.IsEoPrimary ? _eoFrameCache : _irFrameCache;
        var detectionCache = _viewModel.IsEoPrimary ? _eoDetectionCache : _irDetectionCache;

        if (latestFrame is not null &&
            detectionCache.TryGetValue(latestFrame.Value.FrameIndex, out detectionPacket))
        {
            frame = latestFrame.Value;
            return true;
        }

        var exactPairs = detectionCache
            .Where(pair => pair.Value.Detections.Count > 0 && frameCache.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Key)
            .ToArray();

        foreach (var pair in exactPairs)
        {
            frame = frameCache[pair.Key];
            detectionPacket = pair.Value;
            return true;
        }

        if (latestFrame is not null &&
            TryGetRenderableDetectionPacket(latestFrame.Value.FrameIndex, detectionCache, out detectionPacket))
        {
            frame = latestFrame.Value;
            return true;
        }

        frame = default;
        detectionPacket = default;
        return false;
    }

    private IReadOnlyList<DetectionInfo> FilterDisplayDetections(IReadOnlyList<DetectionInfo> detections)
    {
        var filtered = detections
            .Where(ShouldDisplayDetectionSafe)
            .ToArray();
        return filtered;
    }

    private IReadOnlyList<DetectionInfo> ApplyThreatLevels(IReadOnlyList<DetectionInfo> detections)
    {
        return detections
            .Select(detection => detection with { ThreatLevel = GetDetectionThreatLevel(detection) })
            .ToArray();
    }

    private static IReadOnlyList<DetectionInfo> EnsureDetectionObjectIds(IReadOnlyList<DetectionInfo> detections)
    {
        if (detections.Count == 0)
        {
            return detections;
        }

        var allIdsLookMissing = detections.All(detection => detection.ObjectId <= 0);
        var usedIds = new HashSet<int>();
        var nextDummyId = 1;
        var normalized = new DetectionInfo[detections.Count];

        for (var index = 0; index < detections.Count; index++)
        {
            var detection = detections[index];
            var objectId = detection.ObjectId;
            if (allIdsLookMissing || objectId < 0 || objectId > 254 || !usedIds.Add(objectId))
            {
                while (usedIds.Contains(nextDummyId) && nextDummyId < 255)
                {
                    nextDummyId++;
                }

                objectId = Math.Clamp(nextDummyId, 1, 254);
                usedIds.Add(objectId);
                nextDummyId++;
            }

            normalized[index] = detection with { ObjectId = objectId };
        }

        return normalized;
    }

    private string GetDetectionThreatLevel(DetectionInfo detection)
    {
        if (_objectThreatLevels.TryGetValue(detection.ObjectId, out var objectThreatLevel))
        {
            return NormalizeThreatLevel(objectThreatLevel);
        }

        if (!string.IsNullOrWhiteSpace(detection.ThreatLevel))
        {
            return NormalizeThreatLevel(detection.ThreatLevel);
        }

        if (_objectThreatLevels.Count == 0 && !string.IsNullOrWhiteSpace(_latestGlobalVlmThreatLevel))
        {
            return NormalizeThreatLevel(_latestGlobalVlmThreatLevel);
        }

        return EstimateThreatLevelFromClass(detection.ClassName);
    }

    private static string EstimateThreatLevelFromClass(string className)
    {
        var normalizedClass = className.Trim().ToLowerInvariant();
        if (normalizedClass is "airplane" or "car" or "motorcycle" or "bus" or "truck" or "train" or "boat" or "tank" or "drone" or "missile" or "weapon")
        {
            return "?믪쓬";
        }

        if (normalizedClass is "person" or "bicycle" or "cell phone" or "laptop")
        {
            return "以묎컙";
        }

        return "??쓬";
    }

    private static string NormalizeThreatLevel(string threatLevel)
    {
        return threatLevel.Trim().ToLowerInvariant() switch
        {
            "high" or "?믪쓬" => "?믪쓬",
            "medium" or "mid" or "以묎컙" => "以묎컙",
            _ => "??쓬"
        };
    }

    private static int GetThreatWeight(string threatLevel)
    {
        return NormalizeThreatLevel(threatLevel) switch
        {
            "?믪쓬" => 3,
            "以묎컙" => 2,
            _ => 1
        };
    }

    private static string GetHighestThreatLevel(IReadOnlyList<DetectionInfo> detections)
    {
        return detections
            .OrderByDescending(detection => GetThreatWeight(detection.ThreatLevel))
            .Select(detection => NormalizeThreatLevel(detection.ThreatLevel))
            .FirstOrDefault("??쓬");
    }

    private bool ShouldDisplayDetectionSafe(DetectionInfo detection)
    {
        var className = detection.ClassName.ToLowerInvariant();
        var primaryTarget = _viewModel.SelectedPrimaryTarget;
        if (detection.Score < DisplayScoreThreshold)
        {
            return false;
        }

        if (primaryTarget == "\uBCF5\uD569")
        {
            return true;
        }

        if (primaryTarget == "\uC0AC\uB78C")
        {
            return className == "person";
        }

        if (primaryTarget == "\uACF5\uC911 \uBB34\uAE30\uCCB4\uACC4")
        {
            return className == "airplane";
        }

        if (primaryTarget == "\uC721\uC0C1 \uBB34\uAE30\uCCB4\uACC4")
        {
            return className is "bicycle" or "car" or "motorcycle" or "bus" or "truck" or "train";
        }

        if (primaryTarget == "\uD574\uC0C1 \uBB34\uAE30\uCCB4\uACC4")
        {
            return className == "boat";
        }

        if (primaryTarget == "\uD1B5\uC2E0 \uC7A5\uBE44")
        {
            return className is "cell phone" or "laptop";
        }

        if (primaryTarget == "\uBE44\uAD70\uC0AC \uD45C\uC801")
        {
            return NonMilitaryTargetClasses.Contains(className);
        }

        return true;
    }

    private bool ShouldDisplayDetection(DetectionInfo detection) => ShouldDisplayDetectionSafe(detection);

    private void NotifyDetectionAlertIfNeeded(uint frameId, IReadOnlyList<DetectionInfo> detections)
    {
        _lastDetectionAlertSignature = detections.Count == 0 ? null : $"{frameId}:{detections.Count}";
    }

    private void UpdateRiskAndMobileAlert(uint frameId, IReadOnlyList<DetectionInfo> detections)
    {
        if (detections.Count == 0)
        {
            _viewModel.ApplyVlmAnalysisResult("??쓬", "VLM 遺꾩꽍: ?꾩옱 ?좏깮??二??먯?泥?湲곗? ?꾪뿕 媛앹껜媛 ?뺤씤?섏? ?딆븯?듬땲??");
            return;
        }

        var analysis = BuildVlmStyleAnalysis(detections);
        var detectionSummary = BuildDetectionSummary(detections);
        var systemThreatLevel = GetHighestThreatLevel(detections);
        _viewModel.ApplyVlmAnalysisResult(systemThreatLevel, $"{analysis} ?먯? ?댁슜: {detectionSummary}");

        var alertSignature = $"{_viewModel.SelectedPrimaryTarget}:{frameId}:{BuildOverlaySignature(detections)}";
        var now = DateTimeOffset.Now;
        if (string.Equals(_lastDetectionAlertSignature, alertSignature, StringComparison.Ordinal) ||
            now - _lastMobileAlertAt < MobileAlertCooldown)
        {
            return;
        }

        _lastDetectionAlertSignature = alertSignature;
        _lastMobileAlertAt = now;
        var evidencePng = CaptureElementAsPng(CameraActiveView.CameraPanelElement);
        _ = _mobileAlertHubService.PublishAlertAsync(
            "?댁슜?듭젣 ?꾪뿕 ?뚮┝",
            analysis,
            detectionSummary,
            _viewModel.CurrentThreatLevel,
            evidencePng);
        _viewModel.AppendImportantLog("紐⑤컮???깆쑝濡??꾪뿕 ?뚮┝???꾩넚?덉뒿?덈떎.");
    }

    private string BuildVlmStyleAnalysis(IReadOnlyList<DetectionInfo> detections)
    {
        return
            $"{_viewModel.LargeFeedTitle} ?곸긽?먯꽌 二??먯?泥?'{_viewModel.SelectedPrimaryTarget}' 湲곗? ?꾪뿕 媛앹껜 {detections.Count}媛쒓? ?뺤씤?섏뿀?듬땲?? " +
            "?댁슜?먮뒗 ???붾㈃??諛붿슫??諛뺤뒪 ?꾩튂瑜??뺤씤?섍퀬 異붿쟻/?뱁솕 ?곹깭瑜??좎??섏떗?쒖삤.";
    }

    private static string BuildDetectionSummary(IReadOnlyList<DetectionInfo> detections)
    {
        return string.Join(
            "\n",
            detections
                .OrderByDescending(d => d.Score)
                .Take(8)
                .Select((d, index) => $"{index + 1}. {d.ClassName} object{d.ObjectId} / ?꾪뿕??{d.ThreatLevel} / ?좊ː??{d.Score:0.00} / bbox ({d.X1:0}, {d.Y1:0})-({d.X2:0}, {d.Y2:0})"));
    }

    private static IReadOnlyList<DetectionTargetItem> BuildDetectionTargetItems(
        IReadOnlyList<DetectionInfo> detections,
        ReceivedVideoFrame? frame,
        int sourceWidth,
        int sourceHeight)
    {
        return detections
            .Take(30)
            .Select(detection =>
            {
                var threatBrush = GetDetectionThreatBrush(detection.ThreatLevel);
                threatBrush.Freeze();
                return new DetectionTargetItem(
                    detection.ObjectId,
                    detection.ClassName,
                    $"{detection.Score * 100.0f:0.0}%",
                    NormalizeThreatLevel(detection.ThreatLevel),
                    threatBrush,
                    TryCreateDetectionThumbnail(frame?.Bitmap, detection, sourceWidth, sourceHeight));
            })
            .ToArray();
    }

    private static ImageSource? TryCreateDetectionThumbnail(
        BitmapSource? bitmap,
        DetectionInfo detection,
        int sourceWidth,
        int sourceHeight)
    {
        if (bitmap is null || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return null;
        }

        try
        {
            var scaleX = bitmap.PixelWidth / (double)sourceWidth;
            var scaleY = bitmap.PixelHeight / (double)sourceHeight;
            var x = Math.Clamp((int)Math.Floor(detection.X1 * scaleX), 0, Math.Max(0, bitmap.PixelWidth - 1));
            var y = Math.Clamp((int)Math.Floor(detection.Y1 * scaleY), 0, Math.Max(0, bitmap.PixelHeight - 1));
            var right = Math.Clamp((int)Math.Ceiling(detection.X2 * scaleX), x + 1, bitmap.PixelWidth);
            var bottom = Math.Clamp((int)Math.Ceiling(detection.Y2 * scaleY), y + 1, bitmap.PixelHeight);
            var rect = new Int32Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
            var cropped = new CroppedBitmap(bitmap, rect);
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? CaptureElementAsPng(FrameworkElement element)
    {
        var width = Math.Max(1, (int)Math.Round(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Round(element.ActualHeight));
        if (width < 2 || height < 2)
        {
            return null;
        }

        try
        {
            element.UpdateLayout();
            var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(element);
            renderTarget.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(renderTarget));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static void TrimCache<T>(Dictionary<uint, T> cache)
    {
        while (cache.Count > OverlayCacheLimit)
        {
            var oldestKey = cache.Keys.Min();
            cache.Remove(oldestKey);
        }
    }

    private void AddDetectionVisualToCanvas(
        double rectLeft,
        double rectTop,
        double rectWidth,
        double rectHeight,
        DetectionInfo detection,
        bool isTracked)
    {
        var accentBrush = GetDetectionThreatBrush(detection.ThreatLevel);
        accentBrush.Freeze();
        var mainRectangle = new Rectangle
        {
            Width = rectWidth,
            Height = rectHeight,
            Stroke = accentBrush,
            StrokeThickness = isTracked ? 4 : 2,
            RadiusX = 2,
            RadiusY = 2,
            Fill = isTracked
                ? new SolidColorBrush(Color.FromArgb(35,255,70,70))
                : Brushes.Transparent
        };
        Canvas.SetLeft(mainRectangle, rectLeft);
        Canvas.SetTop(mainRectangle, rectTop);
        CameraActiveView.DetectionOverlayCanvasElement.Children.Add(mainRectangle);

        var cornerLength = Math.Max(12, Math.Min(rectWidth, rectHeight) * 0.18);
        var cornerThickness = isTracked ? 5 : 3;
        AddCornerToCanvas(rectLeft, rectTop, cornerLength, true, true, accentBrush, cornerThickness);
        AddCornerToCanvas(rectLeft + rectWidth, rectTop, cornerLength, false, true, accentBrush, cornerThickness);
        AddCornerToCanvas(rectLeft, rectTop + rectHeight, cornerLength, true, false, accentBrush, cornerThickness);
        AddCornerToCanvas(rectLeft + rectWidth, rectTop + rectHeight, cornerLength, false, false, accentBrush, cornerThickness);

        var labelText = new TextBlock
        {
            Text = detection.LabelText,
            Foreground = accentBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };

        labelText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelWidth = labelText.DesiredSize.Width;
        var labelHeight = labelText.DesiredSize.Height;
        var labelLeft = Math.Max(0, Math.Min(rectLeft, Math.Max(0, CameraActiveView.CameraViewportElement.ActualWidth - labelWidth - 4)));
        var preferredTop = rectTop - labelHeight - 6;
        var labelTop = preferredTop >= 0 ? preferredTop : Math.Min(CameraActiveView.CameraViewportElement.ActualHeight - labelHeight - 4, rectTop + 6);
        Canvas.SetLeft(labelText, labelLeft);
        Canvas.SetTop(labelText, Math.Max(0, labelTop));
        CameraActiveView.DetectionOverlayCanvasElement.Children.Add(labelText);
    }

    private static SolidColorBrush GetDetectionThreatBrush(string threatLevel)
    {
        return NormalizeThreatLevel(threatLevel) switch
        {
            "?믪쓬" => new SolidColorBrush(Color.FromRgb(255, 107, 107)),
            "以묎컙" => new SolidColorBrush(Color.FromRgb(255, 193, 69)),
            _ => new SolidColorBrush(Color.FromRgb(123, 216, 143))
        };
    }

    private void AddCornerToCanvas(
        double anchorX,
        double anchorY,
        double length,
        bool isLeft,
        bool isTop,
        Brush strokeBrush,
        double strokeThickness)
    {
        var horizontal = new Line
        {
            X1 = anchorX,
            Y1 = anchorY,
            X2 = anchorX + (isLeft ? length : -length),
            Y2 = anchorY,
            Stroke = strokeBrush,
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square
        };

        var vertical = new Line
        {
            X1 = anchorX,
            Y1 = anchorY,
            X2 = anchorX,
            Y2 = anchorY + (isTop ? length : -length),
            Stroke = strokeBrush,
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square
        };

        CameraActiveView.DetectionOverlayCanvasElement.Children.Add(horizontal);
        CameraActiveView.DetectionOverlayCanvasElement.Children.Add(vertical);
    }
    private void RotateLargeFeedButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.RotateLargeFeedClockwise();
        RenderDetectionOverlay(forceRefresh: true);
        e.Handled = true;
    }

    private void RotateInsetFeedButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.RotateInsetFeedClockwise();
        RenderDetectionOverlay(forceRefresh: true);
        e.Handled = true;
    }

    private void RotateAuxCameraButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
    }
}
