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
    private readonly Action<bool> _setCollapsedChrome;

    private SnapEdge _snapEdge = SnapEdge.None;
    private bool _isExpanded = true;
    private bool _isAnimating;
    private bool _isDragging;
    private double _expandedLeft;
    private double _expandedTop;
    private double _fullWidth;
    private double _savedMinWidth;
    private double _savedMaxWidth;
    private int _pendingAnimationCount;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _presenceTimer;
    private DateTime _hoverExpandBlockedUntil = DateTime.MinValue;
    private DateTime? _cursorOutsideSince;

    public FloatingWindowBehavior(
        Window window,
        Func<AppSettings> getSettings,
        Func<bool> isEditing,
        Action<bool> setCollapsedChrome)
    {
        _window = window;
        _getSettings = getSettings;
        _isEditing = isEditing;
        _setCollapsedChrome = setCollapsedChrome;
        _savedMinWidth = window.MinWidth;
        _savedMaxWidth = window.MaxWidth;

        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_isDragging && _snapEdge != SnapEdge.None && _isExpanded)
                CollapseToEdge(force: true);
        };

        _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _presenceTimer.Tick += (_, _) => OnPresenceTick();

        _window.Loaded += OnLoaded;
        _window.LocationChanged += OnLocationChanged;
        _window.SizeChanged += OnSizeChanged;
        _window.IsVisibleChanged += OnIsVisibleChanged;
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

        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_isDragging) return;
            TrySnapToEdge();
        });
    }

    public void EnsureOnScreen()
    {
        if (!WindowBoundsHelper.TryGetWindowRectPhysical(_window, out var rect)
            || !WindowBoundsHelper.TryGetWorkAreaForWindow(_window, out var work))
            return;

        var (x, y) = WindowBoundsHelper.ConstrainPosition(
            rect.Left, rect.Top, rect.Width, rect.Height, work);
        if (x != rect.Left || y != rect.Top)
            ApplyWindowPositionPhysical(x, y);
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
        if (_isDragging || !_isExpanded) return;
        if (e.NewSize.Width > GetStripWidth() + 40)
            _fullWidth = e.NewSize.Width;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_window.IsVisible)
        {
            if (_snapEdge != SnapEdge.None)
                StartPresenceMonitor();
            return;
        }

        _hideTimer.Stop();
        StopPresenceMonitor();
    }

    private void OnWindowMouseEnter()
    {
        if (_isDragging) return;
        _hideTimer.Stop();
        _cursorOutsideSince = null;
        if (_isExpanded) return;
        if (DateTime.UtcNow < _hoverExpandBlockedUntil) return;
        TryExpandFromHover(animated: true);
    }

    /// <summary>
    /// 全局鼠标检测：收起时悬停/点击细条展开；展开时鼠标离开则延迟收起。
    /// </summary>
    private void OnPresenceTick()
    {
        if (_isDragging || _snapEdge == SnapEdge.None || _isAnimating)
            return;

        if (!_isExpanded)
            return;

        if (IsCursorOverWindow())
        {
            _hideTimer.Stop();
            _cursorOutsideSince = null;
            return;
        }

        _cursorOutsideSince ??= DateTime.UtcNow;

        var elapsed = DateTime.UtcNow - _cursorOutsideSince.Value;
        var grace = GetMouseLeaveGracePeriod();
        var hideDelay = TimeSpan.FromMilliseconds(Math.Max(100, _getSettings().AutoHideDelayMs));

        if (elapsed < grace)
            return;

        if (elapsed >= grace + hideDelay)
        {
            CollapseToEdge(force: true);
            _cursorOutsideSince = null;
            return;
        }

        if (!_hideTimer.IsEnabled)
        {
            var remaining = (grace + hideDelay) - elapsed;
            _hideTimer.Interval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(50);
            _hideTimer.Start();
        }
    }

    private void TryExpandFromHover(bool animated)
    {
        if (_isExpanded || _snapEdge == SnapEdge.None || _isAnimating || _isDragging) return;
        if (DateTime.UtcNow < _hoverExpandBlockedUntil) return;

        _hideTimer.Stop();
        ExpandFromEdge(animated);
    }

    private void ExpandFromEdge(bool animated)
    {
        _hideTimer.Stop();
        ClearAnimations();
        _setCollapsedChrome(false);
        _window.Top = _expandedTop;

        var screen = GetWorkArea();
        var (targetLeft, targetWidth) = GetExpandedBounds(screen, _snapEdge);
        RestoreExpandedMinWidth();

        if (!animated)
        {
            ApplyExpandedBounds(targetLeft, targetWidth);
            MarkExpanded();
            StartPresenceMonitor();
            return;
        }

        RunBoundsAnimation(_window.Left, _window.Width, targetLeft, targetWidth, () =>
        {
            ApplyExpandedBounds(targetLeft, targetWidth);
            MarkExpanded();
            StartPresenceMonitor();
        });
    }

    private void TrySnapToEdge()
    {
        DpiHelper.SyncWindowPositionFromNative(_window);

        if (!WindowBoundsHelper.TryGetWindowRectPhysical(_window, out var rect)
            || !WindowBoundsHelper.TryGetWorkAreaForWindow(_window, out var work))
        {
            return;
        }

        var thresholdPx = WindowBoundsHelper.GetSnapThresholdPx(_window, _getSettings().SnapDistancePx);
        _fullWidth = Math.Max(_fullWidth, GetWindowWidth());
        _expandedTop = _window.Top;

        var edge = WindowBoundsHelper.DetectSnapEdge(rect.Left, rect.Right, work, thresholdPx);
        if (edge == SnapEdge.Left)
        {
            _snapEdge = SnapEdge.Left;
            _expandedLeft = GetWorkArea().Left;
            CollapseToEdge(force: true);
            StartPresenceMonitor();
            return;
        }

        if (edge == SnapEdge.Right)
        {
            _snapEdge = SnapEdge.Right;
            _expandedLeft = GetWorkArea().Right - _fullWidth;
            CollapseToEdge(force: true);
            StartPresenceMonitor();
            return;
        }

        _snapEdge = SnapEdge.None;
        _isExpanded = true;
        _expandedLeft = _window.Left;
        _setCollapsedChrome(false);
        RestoreExpandedMinWidth();

        var (clampedX, clampedY) = WindowBoundsHelper.ConstrainPosition(
            rect.Left, rect.Top, rect.Width, rect.Height, work);
        if (clampedX != rect.Left || clampedY != rect.Top)
            ApplyWindowPositionPhysical(clampedX, clampedY);

        StopPresenceMonitor();
    }

    private void CollapseToEdge(bool force = false)
    {
        if (_isDragging || _snapEdge == SnapEdge.None) return;
        if (!force && (_isEditing() || !_isExpanded || _isAnimating)) return;

        ApplyCollapsedBounds(GetCollapsedBounds(GetWorkArea(), _snapEdge));
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
        CaptureFullWidthBeforeCollapse();

        _setCollapsedChrome(true);
        EnsureCollapsedMinWidth(bounds.Width);
        _window.UpdateLayout();

        ClearAnimations();

        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero
            || !WindowBoundsHelper.TryGetWindowRectPhysical(_window, out var rect)
            || !WindowBoundsHelper.TryGetWorkAreaForWindow(_window, out var work))
        {
            var height = _window.Height > 0 ? _window.Height : _window.ActualHeight;
            ApplyWindowBoundsDip(bounds.Left, _expandedTop, bounds.Width, height);
            _isExpanded = false;
            _isAnimating = false;
            _hoverExpandBlockedUntil = DateTime.UtcNow.AddMilliseconds(800);
            _cursorOutsideSince = null;
            return;
        }

        var stripPx = Math.Max(20, (int)Math.Round(bounds.Width * DpiHelper.GetDpiScaleX(_window)));
        var x = _snapEdge == SnapEdge.Left ? work.Left : work.Right - stripPx;

        SetWindowPos(hwnd, HWND_TOPMOST, x, rect.Top, stripPx, rect.Height, SWP_NOACTIVATE);
        DpiHelper.SyncWindowBoundsFromNative(_window);

        _isExpanded = false;
        _isAnimating = false;
        _hoverExpandBlockedUntil = DateTime.UtcNow.AddMilliseconds(800);
        _cursorOutsideSince = null;
    }

    private void CaptureFullWidthBeforeCollapse()
    {
        var width = GetWindowWidth();
        if (width > GetStripWidth() + 40)
            _fullWidth = Math.Max(_fullWidth, width);
        else if (_fullWidth <= GetStripWidth() + 40)
            _fullWidth = Math.Max(_fullWidth, _savedMinWidth > 0 ? _savedMinWidth : 380);
    }

    private void ApplyExpandedBounds(double left, double width)
    {
        RestoreExpandedMinWidth();
        var height = _window.Height > 0 ? _window.Height : _window.ActualHeight;
        ApplyWindowBoundsDip(left, _expandedTop, width, height);
        _fullWidth = width;
        _isExpanded = true;
        _isAnimating = false;
    }

    private void ApplyWindowPositionPhysical(int x, int y)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return;

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        DpiHelper.SyncWindowPositionFromNative(_window);
    }

    private void ApplyWindowBoundsDip(double left, double top, double width, double height)
    {
        ClearAnimations();
        _window.Left = left;
        _window.Top = top;
        _window.Width = width;
        if (height > 0)
            _window.Height = height;

        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var source = PresentationSource.FromVisual(_window);
        int widthPx;
        int heightPx;
        int xPx;
        int yPx;

        if (source?.CompositionTarget is not null)
        {
            var toDevice = source.CompositionTarget.TransformToDevice;
            var topLeftPx = toDevice.Transform(new Point(left, top));
            var bottomRightPx = toDevice.Transform(new Point(left + width, top + height));
            xPx = (int)Math.Round(topLeftPx.X);
            yPx = (int)Math.Round(topLeftPx.Y);
            widthPx = Math.Max(1, (int)Math.Round(bottomRightPx.X - topLeftPx.X));
            heightPx = Math.Max(1, (int)Math.Round(bottomRightPx.Y - topLeftPx.Y));
        }
        else
        {
            var scaleX = DpiHelper.GetDpiScaleX(_window);
            var scaleY = DpiHelper.GetDpiScaleY(_window);
            xPx = (int)Math.Round(left * scaleX);
            yPx = (int)Math.Round(top * scaleY);
            widthPx = Math.Max(1, (int)Math.Round(width * scaleX));
            heightPx = Math.Max(1, (int)Math.Round(height * scaleY));
        }

        SetWindowPos(hwnd, HWND_TOPMOST, xPx, yPx, widthPx, heightPx, SWP_NOACTIVATE);
        DpiHelper.SyncWindowBoundsFromNative(_window);
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
        if (_window.MaxWidth < double.PositiveInfinity)
            _savedMaxWidth = _window.MaxWidth;
        _window.MinWidth = stripWidth;
        _window.MaxWidth = stripWidth;
    }

    private void RestoreExpandedMinWidth()
    {
        if (_savedMinWidth > 0)
            _window.MinWidth = _savedMinWidth;
        if (_savedMaxWidth > 0)
            _window.MaxWidth = _savedMaxWidth;
    }

    private void StartPresenceMonitor()
    {
        if (_snapEdge == SnapEdge.None) return;
        _presenceTimer.Start();
    }

    private void StopPresenceMonitor() => _presenceTimer.Stop();

    private bool IsCursorOverWindow()
    {
        if (!_window.IsVisible) return false;
        if (!GetCursorPos(out var cursor)) return false;

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return false;

        const int pxPadding = 4;
        return cursor.X >= rect.Left + pxPadding
               && cursor.X <= rect.Right - pxPadding
               && cursor.Y >= rect.Top + pxPadding
               && cursor.Y <= rect.Bottom - pxPadding;
    }

    private void MarkExpanded() => _cursorOutsideSince = null;

    private TimeSpan GetMouseLeaveGracePeriod()
    {
        var animationMs = _isAnimating ? _getSettings().AnimationDurationMs : 0;
        return TimeSpan.FromMilliseconds(animationMs + 200);
    }

    private void ClearAnimations()
    {
        _window.BeginAnimation(Window.LeftProperty, null);
        _window.BeginAnimation(FrameworkElement.WidthProperty, null);
        _isAnimating = false;
        _pendingAnimationCount = 0;
    }

    private double GetWindowWidth()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rect))
        {
            var source = PresentationSource.FromVisual(_window);
            if (source?.CompositionTarget is not null)
            {
                var dipRect = DpiHelper.PhysicalRectToDipRect(
                    _window, rect.Left, rect.Top, rect.Right, rect.Bottom);
                return dipRect.Width;
            }
        }

        return _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
    }

    private Rect GetWorkArea()
    {
        if (WindowBoundsHelper.TryGetWorkAreaForWindow(_window, out var work))
        {
            var source = PresentationSource.FromVisual(_window);
            if (source?.CompositionTarget is not null)
            {
                return DpiHelper.PhysicalRectToDipRect(
                    _window, work.Left, work.Top, work.Right, work.Bottom);
            }
        }

        return SystemParameters.WorkArea;
    }

    #region Win32

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

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
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    #endregion
}
