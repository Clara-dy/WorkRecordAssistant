using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkRecordAssistant.Helpers;
using WorkRecordAssistant.ViewModels;

namespace WorkRecordAssistant.Controls;

public partial class SubTaskListItem : UserControl
{
    private TaskRowGestureHelper? _gesture;
    private string _originalContent = string.Empty;

    public SubTaskListItem()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    public event EventHandler<SubTaskItemViewModel>? DeleteRequested;
    public event EventHandler<SubTaskItemViewModel>? CompleteRequested;
    public event EventHandler<bool>? EditingChanged;

    private SubTaskItemViewModel? ViewModel => DataContext as SubTaskItemViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureGesture();
        UpdateToolTip();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _gesture?.Detach();
        _gesture = null;
        if (ViewModel is INotifyPropertyChanged npc)
            npc.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldNpc)
            oldNpc.PropertyChanged -= ViewModel_PropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newNpc)
            newNpc.PropertyChanged += ViewModel_PropertyChanged;
        UpdateToolTip();
    }

    private void EnsureGesture()
    {
        if (_gesture is not null) return;

        _gesture = new TaskRowGestureHelper(
            SubTaskHeaderArea,
            onSingleClick: () =>
            {
                if (ViewModel is { IsEditing: false })
                    CompleteRequested?.Invoke(this, ViewModel);
            },
            onDoubleClick: BeginEdit,
            onSwipeDelete: () =>
            {
                if (ViewModel is not null)
                    DeleteRequested?.Invoke(this, ViewModel);
            },
            slideTransform: SlideTransform,
            onSwipeActiveChanged: active => DeleteBackground.Opacity = active ? 1 : 0);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SubTaskItemViewModel.IsEditing)
            or nameof(SubTaskItemViewModel.IsCompleted))
        {
            UpdateToolTip();
        }
    }

    private void UpdateToolTip()
    {
        SubTaskHeaderArea.ToolTip = ViewModel switch
        {
            { IsCompleted: true } => "单击恢复未完成 · 双击修改 · 左滑删除 · Enter 保存",
            { IsEditing: true } => "Enter 保存 · Shift+Enter 换行 · Esc 取消",
            _ => "单击完成 · 双击修改 · 左滑删除"
        };
    }

    private void CompleteToggle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _gesture?.CancelPendingClick();
        if (ViewModel is { IsEditing: false })
            CompleteRequested?.Invoke(this, ViewModel);
    }

    private void BeginEdit()
    {
        if (ViewModel is null || ViewModel.IsEditing) return;
        _gesture?.CancelPendingClick();
        _originalContent = ViewModel.Content;
        ViewModel.IsEditing = true;
        EditingChanged?.Invoke(this, true);
        EditTextBox.Focus();
        EditTextBox.SelectAll();
    }

    private async void EditTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelEdit();
            e.Handled = true;
            return;
        }

        if (IsSubmitEnter(e))
        {
            e.Handled = true;
            EditTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            await CommitEditAsync();
        }
    }

    private static bool IsSubmitEnter(KeyEventArgs e) =>
        (e.Key == Key.Enter || e.Key == Key.Return)
        && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

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

        if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            await vm.UpdateSubTaskAsync(ViewModel);
    }
}
