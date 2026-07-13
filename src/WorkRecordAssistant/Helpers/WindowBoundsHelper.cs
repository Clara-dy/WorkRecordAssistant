using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.Helpers;

/// <summary>
/// 窗口物理坐标与工作区约束、贴边磁吸。
/// </summary>
public static class WindowBoundsHelper
{
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    public static bool TryGetWindowRectPhysical(Window window, out Rect rect)
    {
        rect = default;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;
        return GetWindowRect(hwnd, out rect);
    }

    public static bool TryGetWorkAreaPhysical(int centerX, int centerY, out Rect workArea)
    {
        workArea = default;
        var monitor = MonitorFromPoint(new POINT { X = centerX, Y = centerY }, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        workArea = info.rcWork;
        return true;
    }

    public static bool TryGetWorkAreaForWindow(Window window, out Rect workArea)
    {
        if (TryGetWindowRectPhysical(window, out var rect))
        {
            var centerX = (rect.Left + rect.Right) / 2;
            var centerY = (rect.Top + rect.Bottom) / 2;
            if (TryGetWorkAreaPhysical(centerX, centerY, out workArea))
                return true;
        }

        return TryGetWorkAreaPhysical(0, 0, out workArea);
    }

    /// <summary>
    /// 将窗口完全限制在工作区内，不允许拖出屏幕。
    /// </summary>
    public static (int X, int Y) ConstrainPosition(int x, int y, int width, int height, Rect work)
    {
        var minX = work.Left;
        var maxX = Math.Max(work.Left, work.Right - Math.Max(1, width));
        var minY = work.Top;
        var maxY = Math.Max(work.Top, work.Bottom - Math.Max(1, height));

        return (
            Math.Clamp(x, minX, maxX),
            Math.Clamp(y, minY, maxY));
    }

    public static int ApplyHorizontalEdgeMagnet(int x, int width, Rect work, int thresholdPx)
    {
        var right = x + width;
        var distLeft = Math.Abs(x - work.Left);
        var distRight = Math.Abs(work.Right - right);

        if (distLeft <= thresholdPx && distLeft <= distRight)
            return work.Left;
        if (distRight <= thresholdPx)
            return work.Right - width;

        return x;
    }

    public static SnapEdge? DetectSnapEdge(int left, int right, Rect work, int thresholdPx)
    {
        if (Math.Abs(left - work.Left) <= 1)
            return SnapEdge.Left;
        if (Math.Abs(work.Right - right) <= 1)
            return SnapEdge.Right;

        var distLeft = Math.Abs(left - work.Left);
        var distRight = Math.Abs(work.Right - right);

        var nearLeft = distLeft <= thresholdPx;
        var nearRight = distRight <= thresholdPx;

        if (nearLeft && nearRight)
            return distLeft <= distRight ? SnapEdge.Left : SnapEdge.Right;
        if (nearLeft) return SnapEdge.Left;
        if (nearRight) return SnapEdge.Right;
        return null;
    }

    public static int GetSnapThresholdPx(Window window, int snapDistancePx) =>
        Math.Max(48, (int)Math.Ceiling(snapDistancePx * DpiHelper.GetDpiScaleX(window)));

    public static bool TryResolveWorkArea(
        Window window, int proposedX, int proposedY, int width, int height, out Rect workArea)
    {
        var centerX = proposedX + width / 2;
        var centerY = proposedY + height / 2;
        if (TryGetWorkAreaPhysical(centerX, centerY, out workArea))
            return true;

        return TryGetWorkAreaForWindow(window, out workArea);
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
