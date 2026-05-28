using System.Windows;
using BroadcastControl.App.Models.Camera;

namespace BroadcastControl.App.Services;

public static class DetectionOverlayService
{
    public static Rect GetUniformContentRect(int sourceWidth, int sourceHeight, double viewportWidth, double viewportHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return Rect.Empty;
        }

        var scale = Math.Min(viewportWidth / sourceWidth, viewportHeight / sourceHeight);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        return new Rect((viewportWidth - width) / 2.0, (viewportHeight - height) / 2.0, width, height);
    }

    public static bool IsInsideFrame(DetectionInfo detection, int frameWidth, int frameHeight)
    {
        return detection.X2 > 0 &&
               detection.Y2 > 0 &&
               detection.X1 < frameWidth &&
               detection.Y1 < frameHeight;
    }
}
