using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace WorkRecordAssistant.Helpers;

/// <summary>
/// 使用 Win32 物理坐标拖动窗口，避免高 DPI 下 WPF Left/Top 抖动。
/// </summary>
public sealed class WindowDragHelper
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly Window _window;
    private int _screenOffsetX;
    private int _screenOffsetY;
    private int _startRectLeft;
    private int _startRectTop;

    public WindowDragHelper(Window window) => _window = window;

    public bool IsDragging { get; private set; }

    public void Begin(MouseButtonEventArgs e)
    {
        var hwnd = EnsureHandle();
        var clickScreen = _window.PointToScreen(e.GetPosition(_window));
        GetWindowRect(hwnd, out var rect);

        _screenOffsetX = (int)Math.Round(clickScreen.X) - rect.Left;
        _screenOffsetY = (int)Math.Round(clickScreen.Y) - rect.Top;
        _startRectLeft = rect.Left;
        _startRectTop = rect.Top;
        IsDragging = true;
        _window.CaptureMouse();
    }

    public void Move()
    {
        if (!IsDragging) return;

        var hwnd = EnsureHandle();
        if (!GetCursorPos(out var cursor)) return;

        SetWindowPos(
            hwnd,
            HWND_TOPMOST,
            cursor.X - _screenOffsetX,
            cursor.Y - _screenOffsetY,
            0,
            0,
            SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void End(out bool didMove)
    {
        if (!IsDragging)
        {
            didMove = false;
            return;
        }

        IsDragging = false;
        if (_window.IsMouseCaptured)
            _window.ReleaseMouseCapture();

        var hwnd = EnsureHandle();
        GetWindowRect(hwnd, out var rect);
        didMove = Math.Abs(rect.Left - _startRectLeft) > 4 || Math.Abs(rect.Top - _startRectTop) > 4;

        SyncWpfPosition();
    }

    public void SyncWpfPosition() => DpiHelper.SyncWindowPositionFromNative(_window);

    private IntPtr EnsureHandle()
    {
        var helper = new WindowInteropHelper(_window);
        if (helper.Handle == IntPtr.Zero)
            helper.EnsureHandle();
        return helper.Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
