using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WorkRecordAssistant.Helpers;

/// <summary>
/// 任务行手势：单击/双击、即时左滑删除。
/// </summary>
internal sealed class TaskRowGestureHelper
{
    private const int SingleClickDelayMs = 250;
    private const double SwipeDeleteThreshold = -72;
    private const double GestureDecideDistance = 8;
    private const double TapSlop = 8;

    private readonly UIElement _target;
    private readonly TranslateTransform? _slideTransform;
    private readonly Action _onSingleClick;
    private readonly Action _onDoubleClick;
    private readonly Action _onSwipeDelete;
    private readonly Action<bool>? _onSwipeActiveChanged;

    private readonly DispatcherTimer _singleClickTimer;

    private Point _pressPoint;
    private bool _isPressed;
    private bool _gestureDecided;
    private bool _isSwipeMode;

    public TaskRowGestureHelper(
        UIElement target,
        Action onSingleClick,
        Action onDoubleClick,
        Action onSwipeDelete,
        TranslateTransform? slideTransform = null,
        Action<bool>? onSwipeActiveChanged = null)
    {
        _target = target;
        _slideTransform = slideTransform;
        _onSingleClick = onSingleClick;
        _onDoubleClick = onDoubleClick;
        _onSwipeDelete = onSwipeDelete;
        _onSwipeActiveChanged = onSwipeActiveChanged;

        _singleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SingleClickDelayMs) };
        _singleClickTimer.Tick += (_, _) => ExecuteSingleClick();

        _target.MouseLeftButtonDown += Target_MouseLeftButtonDown;
        _target.MouseMove += Target_MouseMove;
        _target.MouseLeftButtonUp += Target_MouseLeftButtonUp;
    }

    public void Detach()
    {
        CancelSingleClickTimer();
        _target.MouseLeftButtonDown -= Target_MouseLeftButtonDown;
        _target.MouseMove -= Target_MouseMove;
        _target.MouseLeftButtonUp -= Target_MouseLeftButtonUp;
    }

    public void CancelPendingClick() => CancelSingleClickTimer();

    public void ResetSwipe()
    {
        _isPressed = false;
        _gestureDecided = false;
        _isSwipeMode = false;
        CancelSingleClickTimer();
        _onSwipeActiveChanged?.Invoke(false);
        if (_slideTransform is not null && _slideTransform.X != 0)
            TaskRowGestureAnimations.AnimateSlide(_slideTransform, 0);
    }

    private void Target_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            CancelSingleClickTimer();
            _isPressed = false;
            _target.ReleaseMouseCapture();
            _onDoubleClick();
            e.Handled = true;
            return;
        }

        _isPressed = true;
        _pressPoint = e.GetPosition(_target);
        _gestureDecided = false;
        _isSwipeMode = false;
        _target.CaptureMouse();
        e.Handled = true;
    }

    private void Target_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPressed) return;

        var current = e.GetPosition(_target);
        var deltaX = current.X - _pressPoint.X;
        var deltaY = current.Y - _pressPoint.Y;

        if (!_gestureDecided &&
            Math.Abs(deltaX) > GestureDecideDistance &&
            Math.Abs(deltaX) > Math.Abs(deltaY) &&
            deltaX < 0)
        {
            _gestureDecided = true;
            _isSwipeMode = true;
            CancelSingleClickTimer();
            _onSwipeActiveChanged?.Invoke(true);
        }

        if (_isSwipeMode && _slideTransform is not null)
        {
            _slideTransform.X = Math.Max(SwipeDeleteThreshold - 20, Math.Min(0, deltaX));
            e.Handled = true;
        }
    }

    private void Target_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPressed) return;

        _isPressed = false;
        _target.ReleaseMouseCapture();

        if (_isSwipeMode)
        {
            CancelSingleClickTimer();
            var shouldDelete = _slideTransform is not null && _slideTransform.X <= SwipeDeleteThreshold;
            _isSwipeMode = false;
            _gestureDecided = false;
            _onSwipeActiveChanged?.Invoke(false);

            if (shouldDelete)
                _onSwipeDelete();
            else if (_slideTransform is not null)
                TaskRowGestureAnimations.AnimateSlide(_slideTransform, 0, () => _onSwipeActiveChanged?.Invoke(false));

            e.Handled = true;
            return;
        }

        var current = e.GetPosition(_target);
        if (!_gestureDecided &&
            Math.Abs(current.X - _pressPoint.X) < TapSlop &&
            Math.Abs(current.Y - _pressPoint.Y) < TapSlop)
        {
            ScheduleSingleClick();
        }

        e.Handled = true;
    }

    private void ScheduleSingleClick()
    {
        CancelSingleClickTimer();
        _singleClickTimer.Start();
    }

    private void ExecuteSingleClick()
    {
        CancelSingleClickTimer();
        _onSingleClick();
    }

    private void CancelSingleClickTimer() => _singleClickTimer.Stop();
}

internal static class TaskRowGestureAnimations
{
    public static void AnimateSlide(TranslateTransform transform, double to, Action? onComplete = null)
    {
        var anim = new DoubleAnimation(transform.X, to, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (onComplete is not null)
            anim.Completed += (_, _) => onComplete();
        transform.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
