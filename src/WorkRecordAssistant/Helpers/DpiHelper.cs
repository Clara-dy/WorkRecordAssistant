using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WorkRecordAssistant.Helpers;

/// <summary>
/// 屏幕物理像素与 WPF DIP 坐标互转。
/// </summary>
public static class DpiHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    public static void SyncWindowPositionFromNative(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return;

        var dip = PhysicalRectToDipRect(window, rect.Left, rect.Top, rect.Right, rect.Bottom);
        window.Left = dip.Left;
        window.Top = dip.Top;
    }
    public static Point ScreenPixelsToDips(Window window, Point screenPixels)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null)
            return screenPixels;

        return source.CompositionTarget.TransformFromDevice.Transform(screenPixels);
    }

    public static Point DipsToScreenPixels(Window window, Point dips)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null)
            return dips;

        return source.CompositionTarget.TransformToDevice.Transform(dips);
    }

    public static double GetDpiScaleX(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    public static Rect PhysicalRectToDipRect(Window window, int left, int top, int right, int bottom)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null)
            return new Rect(left, top, right - left, bottom - top);

        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new Point(left, top));
        var bottomRight = fromDevice.Transform(new Point(right, bottom));
        return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }
}
