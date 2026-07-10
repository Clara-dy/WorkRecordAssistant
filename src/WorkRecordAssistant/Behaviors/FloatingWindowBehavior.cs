using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkRecordAssistant.Helpers;
using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.Behaviors;

/// <summary>
/// 窗口靠边吸附、自动隐藏/滑出行为。
/// 收起时缩小窗口宽度并保持在屏幕内；用全局鼠标检测替代不可靠的 MouseLeave。
/// </summary>
public sealed class FloatingWindowBehavior
{
    private readonly Window _window;
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<bool> _isEditing;

    private SnapEdge _snapEdge = SnapEdge.None;
    private bool _isExpanded = true;
    private bool _isAnimating;
    private bool _isDragging;
    private double _expandedLeft;
    private double _expandedTop;
    private double _fullWidth;
    private double _savedMinWidth;
    private DateTime _lastExpandUtc = DateTime.MinValue;
    private int _pendingAnimationCount;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _presenceTimer;

    public FloatingWindowBehavior(Window window, Func<AppSettings> getSettings, Func<bool> isEditing)
    {
        _window = window;
        _getSettings = getSettings;
        _isEditing = isEditing;
        _savedMinWidth = window.MinWidth;

        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_isEditing() && !_isDragging && _snapEdge != SnapEdge.None && _isExpanded)
                CollapseToEdge();
        };

        _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _presenceTimer.Tick += (_, _) => OnPresenceTick();

        _window.Loaded += OnLoaded;
        _window.LocationChanged += OnLocationChanged;
        _window.SizeChanged += OnSizeChanged;
        _window.MouseEnter += (_, _) => OnWindowMouseEnter();
        _window.StateChanged += (_, _) =>
        {
            if (_window.WindowState == WindowState.Normal)
                _window.Topmost = true;
        };
    }

    public SnapEdge CurrentSnapEdge => _snapEdge;

    public bool IsSnappedAtEdge => _snapEdge != SnapEdge.None;

    public bool IsCollapsedAtEdge => _snapEdge != SnapEdge.None && !_isExpanded;

    public void TryExpandIfCollapsed(bool animated = false)
    {
        if (!_isExpanded && _snapEdge != SnapEdge.None)
            TryExpandFromHover(animated);
    }

    public (double Left, double Top, double Width, double Height, SnapEdge SnapEdge) GetPersistedState()
    {
        var width = _fullWidth > 0 ? _fullWidth : GetWindowWidth();
        var left = _snapEdge != SnapEdge.None && !_isExpanded ? _expandedLeft : _window.Left;
        var top = _snapEdge != SnapEdge.None ? _expandedTop : _window.Top;
        return (left, top, width, _window.Height, _snapEdge);
    }

    public void RestoreSnapState(SnapEdge edge)
    {
        _snapEdge = edge;
        if (edge == SnapEdge.None) return;

        _fullWidth = Math.Max(_fullWidth, GetWindowWidth());
        _expandedTop = _window.Top;
        _isExpanded = false;

        var screen = GetWorkArea();
        _expandedLeft = edge == SnapEdge.Left ? screen.Left : screen.Right - _fullWidth;
        ApplyCollapsedBounds(GetCollapsedBounds(screen, edge));
        StartPresenceMonitor();
    }

    public void BeginDrag()
    {
        _isDragging = true;
        _hideTimer.Stop();
        ClearAnimations();

        if (!_isExpanded && _snapEdge != SnapEdge.None)
            ExpandFromEdge(animated: false);
    }

    public void EndDrag(bool didMove = true)
    {
        _isDragging = false;
        _expandedTop = _window.Top;

        if (!didMove && _snapEdge != SnapEdge.None)
        {
            _isExpanded = true;
            _expandedLeft = _window.Left;
            _fullWidth = GetWindowWidth();
            _hideTimer.Stop();
            MarkExpanded();
            StartPresenceMonitor();
            return;
        }

        TrySnapToEdge();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window.Topmost = true;
        var handle = new WindowInteropHelper(_window).Handle;
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_isAnimating || _isDragging) return;

        if (_snapEdge != SnapEdge.None && _isExpanded)
        {
            _expandedLeft = _window.Left;
            _expandedTop = _window.Top;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isExpanded || _isDragging)
            _fullWidth = GetWindowWidth();
    }

    private void OnWindowMouseEnter()
    {
        if (_isDragging) return;
        _hideTimer.Stop();
        TryExpandFromHover(animated: true);
    }

    /// <summary>
    /// 全局鼠标检测：收起时悬停/点击细条展开；展开时鼠标离开则延迟收起。
    /// </summary>
    private void OnPresenceTick()
    {
        if (_isDragging || _isEditing() || _snapEdge == SnapEdge.None || _isAnimating)
            return;

        var cursorOver = IsCursorOverWindow();

        if (!_isExpanded)
        {
            if (cursorOver)
                TryExpandFromHover(animated: true);
            return;
        }

        if (cursorOver)
        {
            _hideTimer.Stop();
            return;
        }

        if (DateTime.UtcNow - _lastExpandUtc < GetExpandGracePeriod())
            return;

        if (!_hideTimer.IsEnabled)
        {
            _hideTimer.Interval = TimeSpan.FromMilliseconds(
                Math.Max(100, _getSettings().AutoHideDelayMs));
            _hideTimer.Start();
        }
    }

    private void TryExpandFromHover(bool animated)
    {
        if (_isExpanded || _snapEdge == SnapEdge.None || _isAnimating || _isDragging) return;

        _hideTimer.Stop();
        ExpandFromEdge(animated);
    }

    private void ExpandFromEdge(bool animated)
    {
        _hideTimer.Stop();
        ClearAnimations();
        _window.Top = _expandedTop;

        var screen = GetWorkArea();
        var (targetLeft, targetWidth) = GetExpandedBounds(screen, _snapEdge);

        if (!animated)
        {
            ApplyExpandedBounds(targetLeft, targetWidth);
            MarkExpanded();
            return;
        }

        RunBoundsAnimation(_window.Left, _window.Width, targetLeft, targetWidth, () =>
        {
            ApplyExpandedBounds(targetLeft, targetWidth);
            MarkExpanded();
        });
    }

    private void TrySnapToEdge()
    {
        var threshold = _getSettings().SnapDistancePx;
        var screen = GetWorkArea();
        _fullWidth = Math.Max(_fullWidth, GetWindowWidth());
        _expandedTop = _window.Top;

        if (IsNearLeftEdge(screen, threshold))
        {
            _snapEdge = SnapEdge.Left;
            _expandedLeft = screen.Left;
            AlignToExpandedEdge();
            return;
        }

        if (IsNearRightEdge(screen, threshold))
        {
            _snapEdge = SnapEdge.Right;
            _expandedLeft = screen.Right - _fullWidth;
            AlignToExpandedEdge();
            return;
        }

        _snapEdge = SnapEdge.None;
        _isExpanded = true;
        _expandedLeft = _window.Left;
        RestoreExpandedMinWidth();
        StopPresenceMonitor();
    }

    /// <summary>
    /// 吸附到边缘但保持展开，等鼠标移开后再自动收起。
    /// </summary>
    private void AlignToExpandedEdge()
    {
        _hideTimer.Stop();
        var screen = GetWorkArea();
        var (left, width) = GetExpandedBounds(screen, _snapEdge);
        ApplyExpandedBounds(left, width);
        MarkExpanded();
        StartPresenceMonitor();
    }

    private void CollapseToEdge(bool force = false)
    {
        if (_isDragging || _snapEdge == SnapEdge.None || _isEditing()) return;
        if (!force && (!_isExpanded || _isAnimating)) return;

        _fullWidth = Math.Max(_fullWidth, GetWindowWidth());

        var screen = GetWorkArea();
        var (targetLeft, targetWidth) = GetCollapsedBounds(screen, _snapEdge);
        var animationMs = _getSettings().AnimationDurationMs;

        if (animationMs <= 0)
        {
            ApplyCollapsedBounds((targetLeft, targetWidth));
            StartPresenceMonitor();
            return;
        }

        ClearAnimations();
        RunBoundsAnimation(_window.Left, _window.Width, targetLeft, targetWidth, () =>
        {
            ApplyCollapsedBounds((targetLeft, targetWidth));
            StartPresenceMonitor();
        });
    }

    private void RunBoundsAnimation(
        double fromLeft, double fromWidth, double toLeft, double toWidth, Action onComplete)
    {
        _isAnimating = true;
        var duration = TimeSpan.FromMilliseconds(_getSettings().AnimationDurationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _pendingAnimationCount = 2;

        void OnAnimComplete()
        {
            if (--_pendingAnimationCount > 0) return;
            ClearAnimations();
            onComplete();
        }

        var leftAnim = new DoubleAnimation(fromLeft, toLeft, new Duration(duration)) { EasingFunction = ease };
        leftAnim.Completed += (_, _) => OnAnimComplete();

        var widthAnim = new DoubleAnimation(fromWidth, toWidth, new Duration(duration)) { EasingFunction = ease };
        widthAnim.Completed += (_, _) => OnAnimComplete();

        _window.BeginAnimation(Window.LeftProperty, leftAnim);
        _window.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
    }

    private void ApplyCollapsedBounds((double Left, double Width) bounds)
    {
        ClearAnimations();
        EnsureCollapsedMinWidth(bounds.Width);
        _window.Top = _expandedTop;
        _window.Left = bounds.Left;
        _window.Width = bounds.Width;
        _isExpanded = false;
        _isAnimating = false;
    }

    private void ApplyExpandedBounds(double left, double width)
    {
        ClearAnimations();
        RestoreExpandedMinWidth();
        _window.Top = _expandedTop;
        _window.Left = left;
        _window.Width = width;
        _fullWidth = width;
        _isExpanded = true;
        _isAnimating = false;
    }

    private (double Left, double Width) GetCollapsedBounds(Rect screen, SnapEdge edge)
    {
        var strip = GetStripWidth();
        return edge == SnapEdge.Left
            ? (screen.Left, strip)
            : (screen.Right - strip, strip);
    }

    private (double Left, double Width) GetExpandedBounds(Rect screen, SnapEdge edge)
    {
        var width = _fullWidth > 0 ? _fullWidth : GetWindowWidth();
        return edge == SnapEdge.Left
            ? (screen.Left, width)
            : (screen.Right - width, width);
    }

    private double GetStripWidth() =>
        Math.Max(20, _getSettings().HiddenStripWidthPx);

    private void EnsureCollapsedMinWidth(double stripWidth)
    {
        if (_window.MinWidth > stripWidth)
            _savedMinWidth = _window.MinWidth;
        _window.MinWidth = stripWidth;
    }

    private void RestoreExpandedMinWidth()
    {
        if (_savedMinWidth > 0)
            _window.MinWidth = _savedMinWidth;
    }

    private void StartPresenceMonitor()
    {
        if (_snapEdge == SnapEdge.None) return;
        _presenceTimer.Start();
    }

    private void StopPresenceMonitor() => _presenceTimer.Stop();

    private bool IsCursorOverWindow()
    {
        if (!GetCursorPos(out var cursor)) return false;

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return false;

        const int padding = 2;
        return cursor.X >= rect.Left - padding
               && cursor.X <= rect.Right + padding
               && cursor.Y >= rect.Top - padding
               && cursor.Y <= rect.Bottom + padding;
    }

    private void MarkExpanded() =>
        _lastExpandUtc = DateTime.UtcNow;

    private TimeSpan GetExpandGracePeriod()
    {
        var animationMs = _getSettings().AnimationDurationMs;
        var hideDelayMs = Math.Max(100, _getSettings().AutoHideDelayMs);
        return TimeSpan.FromMilliseconds(animationMs + hideDelayMs + 200);
    }

    private void ClearAnimations()
    {
        _window.BeginAnimation(Window.LeftProperty, null);
        _window.BeginAnimation(FrameworkElement.WidthProperty, null);
        _isAnimating = false;
        _pendingAnimationCount = 0;
    }

    private double GetWindowWidth() =>
        _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;

    private bool IsNearLeftEdge(Rect screen, double threshold) =>
        _window.Left - screen.Left <= threshold;

    private bool IsNearRightEdge(Rect screen, double threshold)
    {
        var right = _window.Left + GetWindowWidth();
        return screen.Right - right <= threshold;
    }

    private Rect GetWorkArea()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle != IntPtr.Zero)
        {
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var source = PresentationSource.FromVisual(_window);
                if (source?.CompositionTarget is not null)
                {
                    return DpiHelper.PhysicalRectToDipRect(
                        _window,
                        info.rcWork.Left,
                        info.rcWork.Top,
                        info.rcWork.Right,
                        info.rcWork.Bottom);
                }
            }
        }

        return SystemParameters.WorkArea;
    }

    #region Win32

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint MonitorDefaultToNearest = 2;

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    #endregion
}
