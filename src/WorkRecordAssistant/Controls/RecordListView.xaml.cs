using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkRecordAssistant.ViewModels;

namespace WorkRecordAssistant.Controls;

public partial class RecordListView : UserControl
{
    public static readonly DependencyProperty TaskItemsProperty =
        DependencyProperty.Register(nameof(TaskItems), typeof(ObservableCollection<IRecordListItemViewModel>),
            typeof(RecordListView), new PropertyMetadata(null));

    public static readonly DependencyProperty StarredTaskCountProperty =
        DependencyProperty.Register(nameof(StarredTaskCount), typeof(int),
            typeof(RecordListView), new PropertyMetadata(0));

    private enum ReorderTarget { None, Normal, Starred }

    private IRecordListItemViewModel? _dragItem;
    private RecordListItem? _dragSourceItem;
    private ReorderTarget _reorderTarget = ReorderTarget.None;
    private int _insertIndex = -1;
    private bool _isReordering;

    public RecordListView()
    {
        InitializeComponent();
    }

    public ObservableCollection<IRecordListItemViewModel>? TaskItems
    {
        get => (ObservableCollection<IRecordListItemViewModel>?)GetValue(TaskItemsProperty);
        set => SetValue(TaskItemsProperty, value);
    }

    public int StarredTaskCount
    {
        get => (int)GetValue(StarredTaskCountProperty);
        set => SetValue(StarredTaskCountProperty, value);
    }

    public event EventHandler<IRecordListItemViewModel>? DeleteRequested;
    public event EventHandler<IRecordListItemViewModel>? CompleteRequested;
    public event EventHandler? OpenArchiveRequested;
    public event EventHandler<bool>? RecordEditingChanged;

    private void RecordListItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RecordListItem item) return;

        item.DeleteRequested += Item_DeleteRequested;
        item.CompleteRequested += Item_CompleteRequested;
        item.ReorderDragStarted += Item_ReorderDragStarted;
        item.EditingChanged += Item_EditingChanged;
        item.SubTaskEditingChanged += Item_SubTaskEditingChanged;
    }

    private void RecordListItem_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RecordListItem item) return;

        item.DeleteRequested -= Item_DeleteRequested;
        item.CompleteRequested -= Item_CompleteRequested;
        item.ReorderDragStarted -= Item_ReorderDragStarted;
        item.EditingChanged -= Item_EditingChanged;
        item.SubTaskEditingChanged -= Item_SubTaskEditingChanged;
    }

    private void Item_DeleteRequested(object? sender, IRecordListItemViewModel e) =>
        DeleteRequested?.Invoke(this, e);

    private void Item_CompleteRequested(object? sender, IRecordListItemViewModel e) =>
        CompleteRequested?.Invoke(this, e);

    private void Item_EditingChanged(object? sender, bool editing) =>
        RecordEditingChanged?.Invoke(this, editing);

    private void Item_SubTaskEditingChanged(object? sender, bool editing) =>
        RecordEditingChanged?.Invoke(this, editing);

    private void Item_ReorderDragStarted(object? sender, IRecordListItemViewModel e)
    {
        if (sender is not RecordListItem item || TaskItems is null) return;

        _dragItem = e;
        _dragSourceItem = item;
        _isReordering = true;
        _reorderTarget = e.IsStarred ? ReorderTarget.Starred : ReorderTarget.Normal;
        _insertIndex = TaskItems.IndexOf(e);

        InsertIndicator.Visibility = Visibility.Visible;
        ShowInsertIndicator(_insertIndex >= 0 ? _insertIndex : 0);

        CaptureMouse();
        MouseMove += RecordListView_MouseMove;
        MouseLeftButtonUp += RecordListView_MouseLeftButtonUp;
    }

    private void RecordListView_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isReordering) return;

        var screenPoint = PointToScreen(e.GetPosition(this));
        var index = GetInsertIndex(TaskListBox, screenPoint);
        if (index < 0) return;

        _insertIndex = ClampInsertIndex(index);
        ShowInsertIndicator(_insertIndex);
    }

    private async void RecordListView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isReordering) return;

        StopReorderTracking();

        if (_dragItem is null || TaskItems is null || _insertIndex < 0) goto Cleanup;

        var oldIndex = TaskItems.IndexOf(_dragItem);
        var newIndex = ClampInsertIndex(_insertIndex);
        if (oldIndex >= 0 && oldIndex != newIndex)
        {
            if (newIndex > oldIndex) newIndex--;
            TaskItems.Move(oldIndex, newIndex);

            if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            {
                if (_reorderTarget == ReorderTarget.Starred)
                    await vm.SaveStarredOrderAsync();
                else
                    await vm.SaveRecordOrderAsync();
            }
        }

        Cleanup:
        _dragSourceItem?.ResetInteraction();
        _dragItem = null;
        _dragSourceItem = null;
        _reorderTarget = ReorderTarget.None;
        _insertIndex = -1;
    }

    private int ClampInsertIndex(int index)
    {
        if (TaskItems is null || TaskItems.Count == 0) return 0;

        var starredCount = StarredTaskCount;
        if (_reorderTarget == ReorderTarget.Starred)
            return Math.Clamp(index, 0, starredCount);

        var normalStart = starredCount;
        var normalEnd = TaskItems.Count;
        if (normalEnd <= normalStart) return normalStart;
        return Math.Clamp(index, normalStart, normalEnd);
    }

    private void StopReorderTracking()
    {
        _isReordering = false;
        InsertIndicator.Visibility = Visibility.Collapsed;
        ReleaseMouseCapture();
        MouseMove -= RecordListView_MouseMove;
        MouseLeftButtonUp -= RecordListView_MouseLeftButtonUp;
    }

    private static int GetInsertIndex(ListBox listBox, Point screenPoint)
    {
        if (listBox.Items.Count == 0) return 0;

        for (var i = 0; i < listBox.Items.Count; i++)
        {
            var container = listBox.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container is null)
            {
                listBox.UpdateLayout();
                container = listBox.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container is null) continue;
            }

            var topLeft = container.PointToScreen(new Point(0, 0));
            var mid = topLeft.Y + container.ActualHeight / 2;

            if (screenPoint.Y < mid)
                return i;
        }

        return listBox.Items.Count;
    }

    private void ShowInsertIndicator(int index)
    {
        if (TaskListBox.Items.Count == 0) return;

        FrameworkElement? reference;
        double y;

        if (index >= TaskListBox.Items.Count)
        {
            reference = TaskListBox.ItemContainerGenerator
                .ContainerFromIndex(TaskListBox.Items.Count - 1) as FrameworkElement;
            if (reference is null) return;
            y = reference.TransformToAncestor(this).Transform(new Point(0, reference.ActualHeight)).Y;
        }
        else
        {
            reference = TaskListBox.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (reference is null) return;
            y = reference.TransformToAncestor(this).Transform(new Point(0, 0)).Y;
        }

        InsertIndicator.Margin = new Thickness(8, y, 8, 0);
        InsertIndicator.Visibility = Visibility.Visible;
    }

    private void OpenArchive_Click(object sender, RoutedEventArgs e) =>
        OpenArchiveRequested?.Invoke(this, EventArgs.Empty);
}
