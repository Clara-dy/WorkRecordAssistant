using System.Windows;
using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.Helpers;

/// <summary>
/// 窗口位置与尺寸校验，防止保存到屏幕外导致无法看见。
/// </summary>
public static class WindowGeometryHelper
{
    public const double DefaultWidth = 320;
    public const double DefaultHeight = 520;
    public const double MaxWidth = 480;
    public const double MaxHeight = 900;

    public static void SanitizeSettings(AppSettings settings)
    {
        settings.WindowWidth = Clamp(settings.WindowWidth, 280, MaxWidth);
        settings.WindowHeight = Clamp(settings.WindowHeight, 400, MaxHeight);

        if (!settings.WindowLeft.HasValue || !settings.WindowTop.HasValue)
            return;

        if (!IsReasonablePosition(settings.WindowLeft.Value, settings.WindowTop.Value,
                settings.WindowWidth, settings.WindowHeight))
        {
            settings.WindowLeft = null;
            settings.WindowTop = null;
            settings.SnapEdge = SnapEdge.None;
        }
    }

    public static void ApplyToWindow(Window window, AppSettings settings)
    {
        window.Width = Clamp(settings.WindowWidth, window.MinWidth, MaxWidth);
        window.Height = Clamp(settings.WindowHeight, window.MinHeight, MaxHeight);

        if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue &&
            IsReasonablePosition(settings.WindowLeft.Value, settings.WindowTop.Value,
                window.Width, window.Height))
        {
            window.Left = settings.WindowLeft.Value;
            window.Top = settings.WindowTop.Value;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    public static bool IsReasonablePosition(double left, double top, double width, double height)
    {
        var screen = SystemParameters.WorkArea;
        const double minVisible = 80;

        var visibleWidth = Math.Min(width, minVisible);
        var visibleHeight = Math.Min(height, minVisible);

        var right = left + visibleWidth;
        var bottom = top + visibleHeight;

        return right > screen.Left + 20
               && left < screen.Right - 20
               && bottom > screen.Top + 20
               && top < screen.Bottom - 20;
    }

    public static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));
}
