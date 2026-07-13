using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace WorkRecordAssistant.Helpers;

/// <summary>
/// 使用 Win32 物理坐标拖动窗口，拖动时限制在工作区内并支持贴边磁吸。
/// </summary>
public sealed class WindowDragHelper
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly Window _window;
    private readonly Func<int> _getSnapDistancePx;
    private int _screenOffsetX;
    private int _screenOffsetY;
    private int _startRectLeft;
    private int _startRectTop;

    public WindowDragHelper(Window window, Func<int> getSnapDistancePx)
    {
        _window = window;
        _getSnapDistancePx = getSnapDistancePx;
    }

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
        if (!GetWindowRect(hwnd, out var rect)) return;

        var width = rect.Width;
        var height = rect.Height;
        var x = cursor.X - _screenOffsetX;
        var y = cursor.Y - _screenOffsetY;

        if (WindowBoundsHelper.TryResolveWorkArea(_window, x, y, width, height, out var work))
        {
            var threshold = WindowBoundsHelper.GetSnapThresholdPx(_window, _getSnapDistancePx());
            x = WindowBoundsHelper.ApplyHorizontalEdgeMagnet(x, width, work, threshold);
            (x, y) = WindowBoundsHelper.ConstrainPosition(x, y, width, height, work);
        }

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
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

        ClampWindowToWorkArea(hwnd, rect);

        SyncWpfPosition();
    }

    public void SyncWpfPosition() => DpiHelper.SyncWindowPositionFromNative(_window);

    public void ClampWindowToWorkArea()
    {
        var hwnd = EnsureHandle();
        if (!GetWindowRect(hwnd, out var rect)) return;
        ClampWindowToWorkArea(hwnd, rect);
        SyncWpfPosition();
    }

    private void ClampWindowToWorkArea(IntPtr hwnd, WindowBoundsHelper.Rect rect)
    {
        if (!WindowBoundsHelper.TryGetWorkAreaForWindow(_window, out var work)) return;

        var (x, y) = WindowBoundsHelper.ConstrainPosition(
            rect.Left, rect.Top, rect.Width, rect.Height, work);
        if (x != rect.Left || y != rect.Top)
            SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

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

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WindowBoundsHelper.Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
