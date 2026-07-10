using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WorkRecordAssistant.ViewModels;

namespace WorkRecordAssistant.Controls;

public partial class RecordListItem : UserControl
{
    private const int LongPressMs = 400;
    private const double SwipeDeleteThreshold = -72;
    private const double GestureDecideDistance = 10;

    private readonly DispatcherTimer _longPressTimer;
    private Point _pressPoint;
    private string _originalContent = string.Empty;
    private bool _longPressActivated;
    private bool _gestureDecided;
    private bool _isSwipeMode;
    private bool _isPressed;
    private bool _reorderDelegated;

    public RecordListItem()
    {
        InitializeComponent();

        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LongPressMs) };
        _longPressTimer.Tick += LongPressTimer_Tick;

        Loaded += (_, _) =>
        {
            ContentArea.MouseLeftButtonDown += ContentArea_MouseLeftButtonDown;
            ContentArea.MouseMove += ContentArea_MouseMove;
            ContentArea.MouseLeftButtonUp += ContentArea_MouseLeftButtonUp;
        };
    }

    public event EventHandler<IRecordListItemViewModel>? DeleteRequested;
    public event EventHandler<IRecordListItemViewModel>? CompleteRequested;
    public event EventHandler<IRecordListItemViewModel>? ReorderDragStarted;
    public event EventHandler<bool>? EditingChanged;

    private IRecordListItemViewModel? ViewModel => DataContext as IRecordListItemViewModel;

    private Brush GetDefaultContentBackground() =>
        ViewModel?.IsLongTermTask == true
            ? (Brush)Application.Current.FindResource("LongTermTaskBackgroundBrush")
            : Brushes.Transparent;

    public void ResetInteraction()
    {
        _isPressed = false;
        _longPressActivated = false;
        _gestureDecided = false;
        _isSwipeMode = false;
        _reorderDelegated = false;
        ContentArea.Background = GetDefaultContentBackground();
        Opacity = 1;

        if (SlideTransform.X != 0)
            AnimateSlideBack();
        else
            DeleteBackground.Opacity = 0;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.IsEditing == true) return;
        e.Handled = true;
        StartReorderDrag();
    }

    private void ContentArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.IsEditing == true) return;

        _isPressed = true;
        _reorderDelegated = false;
        _pressPoint = e.GetPosition(ContentArea);
        _longPressActivated = false;
        _gestureDecided = false;
        _isSwipeMode = false;
        _longPressTimer.Start();
        ContentArea.CaptureMouse();
        e.Handled = true;
    }

    private void LongPressTimer_Tick(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (!_isPressed || ViewModel is null) return;

        _longPressActivated = true;
        ContentArea.Background = (Brush)Application.Current.FindResource("AccentSubtleBrush");
    }

    private void ContentArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPressed || _reorderDelegated || ViewModel is null) return;

        var current = e.GetPosition(ContentArea);
        var deltaX = current.X - _pressPoint.X;
        var deltaY = current.Y - _pressPoint.Y;

        if (_longPressActivated && !_gestureDecided &&
            (Math.Abs(deltaX) > GestureDecideDistance || Math.Abs(deltaY) > GestureDecideDistance))
        {
            _gestureDecided = true;
            if (Math.Abs(deltaX) > Math.Abs(deltaY) && deltaX < 0)
            {
                _isSwipeMode = true;
                DeleteBackground.Opacity = 1;
            }
            else if (Math.Abs(deltaY) > Math.Abs(deltaX))
            {
                StartReorderDrag();
            }
        }

        if (_isSwipeMode)
        {
            var offset = Math.Max(SwipeDeleteThreshold - 20, Math.Min(0, deltaX));
            SlideTransform.X = offset;
            DeleteBackground.Opacity = Math.Min(1, Math.Abs(offset) / 72);
        }
    }

    private void ContentArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_reorderDelegated) return;
        FinishContentInteraction(e);
    }

    private void StartReorderDrag()
    {
        if (ViewModel is null || _reorderDelegated) return;

        _reorderDelegated = true;
        _longPressTimer.Stop();
        ContentArea.ReleaseMouseCapture();
        Opacity = 0.55;
        ReorderDragStarted?.Invoke(this, ViewModel);
    }

    private void FinishContentInteraction(MouseButtonEventArgs e)
    {
        _longPressTimer.Stop();
        ContentArea.ReleaseMouseCapture();

        if (ViewModel is null)
        {
            ResetInteraction();
            return;
        }

        if (_isSwipeMode)
        {
            if (SlideTransform.X <= SwipeDeleteThreshold)
                DeleteRequested?.Invoke(this, ViewModel);
            AnimateSlideBack();
        }
        else if (!_longPressActivated)
        {
            var current = e.GetPosition(ContentArea);
            if (Math.Abs(current.X - _pressPoint.X) < 8 && Math.Abs(current.Y - _pressPoint.Y) < 8)
                BeginEdit();
        }

        ResetInteraction();
    }

    private void AnimateSlideBack()
    {
        var anim = new DoubleAnimation(SlideTransform.X, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) => DeleteBackground.Opacity = 0;
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void BeginEdit()
    {
        if (ViewModel is null) return;
        _originalContent = ViewModel.Content;
        ViewModel.IsEditing = true;
        EditingChanged?.Invoke(this, true);
        EditTextBox.Focus();
        EditTextBox.SelectAll();
    }

    private async void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await CommitEditAsync();
    }

    private void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ViewModel is not null)
            CompleteRequested?.Invoke(this, ViewModel);
    }

    private async void EditTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await CommitEditAsync();
        }
        else if (e.Key == Key.Escape)
        {
            CancelEdit();
        }
    }

    private void CancelEdit()
    {
        if (ViewModel is null) return;
        ViewModel.Content = _originalContent;
        ViewModel.IsEditing = false;
        EditingChanged?.Invoke(this, false);
    }

    private async Task CommitEditAsync()
    {
        if (ViewModel is null || !ViewModel.IsEditing) return;

        ViewModel.IsEditing = false;
        EditingChanged?.Invoke(this, false);

        var window = Window.GetWindow(this);
        if (window?.DataContext is MainViewModel vm)
            await vm.UpdateRecordItemAsync(ViewModel);
    }
}
