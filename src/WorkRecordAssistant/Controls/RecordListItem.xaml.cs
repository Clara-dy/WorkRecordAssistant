using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkRecordAssistant.Helpers;
using WorkRecordAssistant.ViewModels;
using WorkRecordAssistant.Views;

namespace WorkRecordAssistant.Controls;

public partial class RecordListItem : UserControl
{
    private TaskRowGestureHelper? _headerGesture;
    private bool _reorderDelegated;

    public RecordListItem()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    public event EventHandler<IRecordListItemViewModel>? DeleteRequested;
    public event EventHandler<IRecordListItemViewModel>? CompleteRequested;
    public event EventHandler<IRecordListItemViewModel>? ReorderDragStarted;
    public event EventHandler<bool>? EditingChanged;
    public event EventHandler<bool>? SubTaskEditingChanged;

    private IRecordListItemViewModel? ViewModel => DataContext as IRecordListItemViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureHeaderGesture();
        UpdateContentToolTip();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _headerGesture?.Detach();
        _headerGesture = null;
        if (ViewModel is INotifyPropertyChanged npc)
            npc.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldNpc)
            oldNpc.PropertyChanged -= ViewModel_PropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newNpc)
            newNpc.PropertyChanged += ViewModel_PropertyChanged;
        UpdateContentToolTip();
    }

    private void EnsureHeaderGesture()
    {
        if (_headerGesture is not null) return;

        _headerGesture = new TaskRowGestureHelper(
            TaskHeaderArea,
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
        if (e.PropertyName is nameof(IRecordListItemViewModel.IsEditing)
            or nameof(IRecordListItemViewModel.IsCompleted)
            or nameof(IRecordListItemViewModel.SupportsTapToComplete)
            or nameof(IRecordListItemViewModel.IsStarred))
        {
            UpdateContentToolTip();
            ContentArea.Background = GetDefaultContentBackground();
        }
    }

    private void UpdateContentToolTip()
    {
        TaskHeaderArea.ToolTip = ViewModel switch
        {
            { IsCompleted: true } => "单击恢复未完成 · 双击修改 · 左滑删除 · 右键星级/版本号/复制/快速子任务",
            { IsEditing: false } => "单击完成 · 双击修改 · 左滑删除 · 右键星级/版本号/复制/快速子任务",
            _ => "双击修改 · 左滑删除 · 右键星级/版本号/复制/快速子任务"
        };
    }

    private Brush GetDefaultContentBackground() =>
        ViewModel?.IsStarred == true
            ? (Brush)Application.Current.FindResource("StarredTaskBackgroundBrush")
            : Brushes.Transparent;

    public void ResetInteraction()
    {
        _reorderDelegated = false;
        _headerGesture?.CancelPendingClick();
        _headerGesture?.ResetSwipe();
        ContentArea.Background = GetDefaultContentBackground();
        Opacity = 1;
        DeleteBackground.Opacity = 0;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.IsEditing == true) return;
        e.Handled = true;
        _headerGesture?.CancelPendingClick();
        StartReorderDrag();
    }

    private void CompleteToggle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _headerGesture?.CancelPendingClick();
        if (ViewModel is { IsEditing: false })
            CompleteRequested?.Invoke(this, ViewModel);
    }

    private void StartReorderDrag()
    {
        if (ViewModel is null || _reorderDelegated) return;

        _reorderDelegated = true;
        Opacity = 0.55;
        ReorderDragStarted?.Invoke(this, ViewModel);
    }

    private string _originalContent = string.Empty;

    private void BeginEdit()
    {
        if (ViewModel is null || ViewModel.IsEditing) return;
        _headerGesture?.CancelPendingClick();
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

        if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            await vm.UpdateRecordItemAsync(ViewModel);
    }

    private async void StarMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            await vm.SetRecordStarredAsync(ViewModel, true);
    }

    private async void UnstarMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            await vm.SetRecordStarredAsync(ViewModel, false);
    }

    private async void SetVersionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not WorkRecordItemViewModel record) return;

        var dialog = new VersionEditorDialog(record.VersionNumber, record.VersionInfo)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            await vm.SetRecordVersionAsync(record, dialog.VersionNumber, dialog.VersionInfo);
    }

    private void CopyWithSubTasks_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not WorkRecordItemViewModel record) return;
        if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            vm.CopyRecord(record, includeSubTasks: true);
    }

    private void CopyTaskOnly_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not WorkRecordItemViewModel record) return;
        if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            vm.CopyRecord(record, includeSubTasks: false);
    }

    private void TaskContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var isStarred = ViewModel?.IsStarred == true;
        StarMenuItem.Visibility = isStarred ? Visibility.Collapsed : Visibility.Visible;
        UnstarMenuItem.Visibility = isStarred ? Visibility.Visible : Visibility.Collapsed;
        RebuildQuickSubTaskMenu();
    }

    private void RebuildQuickSubTaskMenu()
    {
        QuickSubTaskMenu.Items.Clear();

        if (Window.GetWindow(this)?.DataContext is not MainViewModel vm)
        {
            QuickSubTaskMenu.Items.Add(new MenuItem
            {
                Header = "（暂无）",
                IsEnabled = false
            });
            return;
        }

        if (vm.SubTaskTemplates.Count == 0)
        {
            QuickSubTaskMenu.Items.Add(new MenuItem
            {
                Header = "（暂无，点管理添加）",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var template in vm.SubTaskTemplates)
            {
                var item = new MenuItem
                {
                    Header = TruncateMenuHeader(template.Content),
                    Tag = template.Content,
                    ToolTip = template.Content
                };
                item.Click += QuickSubTaskTemplate_Click;
                QuickSubTaskMenu.Items.Add(item);
            }
        }

        QuickSubTaskMenu.Items.Add(new Separator());
        var manageItem = new MenuItem { Header = "管理快速列表…" };
        manageItem.Click += ManageQuickSubTasks_Click;
        QuickSubTaskMenu.Items.Add(manageItem);
    }

    private static string TruncateMenuHeader(string content)
    {
        var text = content.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return text.Length <= 40 ? text : text[..40] + "…";
    }

    private async void QuickSubTaskTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (sender is not MenuItem { Tag: string content }) return;
        if (Window.GetWindow(this)?.DataContext is not MainViewModel vm) return;

        await vm.AddSubTaskAsync(ViewModel, content);
    }

    private async void ManageQuickSubTasks_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is not MainViewModel vm) return;

        var dialog = new SubTaskTemplateManageDialog(vm.SubTaskTemplates)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        await vm.SaveSubTaskTemplatesAsync(dialog.ResultTemplates);
    }

    private void TaskHeaderArea_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.IsEditing == true) return;
        TaskHeaderArea.ContextMenu!.IsOpen = true;
        e.Handled = true;
    }

    private void SubTaskListItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not SubTaskListItem item) return;
        item.CompleteRequested += SubTask_CompleteRequested;
        item.DeleteRequested += SubTask_DeleteRequested;
        item.EditingChanged += SubTask_EditingChanged;
    }

    private void SubTaskListItem_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not SubTaskListItem item) return;
        item.CompleteRequested -= SubTask_CompleteRequested;
        item.DeleteRequested -= SubTask_DeleteRequested;
        item.EditingChanged -= SubTask_EditingChanged;
    }

    private async void SubTask_CompleteRequested(object? sender, SubTaskItemViewModel sub)
    {
        if (ViewModel is null) return;
        if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            await vm.ToggleSubTaskCompletionAsync(sub, ViewModel);
    }

    private async void SubTask_DeleteRequested(object? sender, SubTaskItemViewModel sub)
    {
        if (ViewModel is null) return;
        if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            await vm.DeleteSubTaskAsync(sub, ViewModel);
    }

    private void SubTask_EditingChanged(object? sender, bool editing) =>
        SubTaskEditingChanged?.Invoke(this, editing);

    private void AddSubTask_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        e.Handled = true;
        ViewModel.IsAddingSubTask = true;
    }

    private void NewSubTaskBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private async void NewSubTaskBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ViewModel is not null)
            {
                ViewModel.IsAddingSubTask = false;
                ViewModel.NewSubTaskText = string.Empty;
            }
            e.Handled = true;
            return;
        }

        if (IsSubmitEnter(e))
        {
            e.Handled = true;
            if (sender is TextBox box)
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (ViewModel is null) return;
            if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
                await vm.AddSubTaskAsync(ViewModel);
        }
    }

    private static bool IsSubmitEnter(KeyEventArgs e) =>
        (e.Key == Key.Enter || e.Key == Key.Return)
        && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
}
