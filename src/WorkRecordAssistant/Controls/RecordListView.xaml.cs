using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkRecordAssistant.ViewModels;

namespace WorkRecordAssistant.Controls;

public partial class RecordListView : UserControl
{
    public static readonly DependencyProperty RecordsProperty =
        DependencyProperty.Register(nameof(Records), typeof(ObservableCollection<WorkRecordItemViewModel>),
            typeof(RecordListView), new PropertyMetadata(null));

    public static readonly DependencyProperty LongTermTasksProperty =
        DependencyProperty.Register(nameof(LongTermTasks), typeof(ObservableCollection<LongTermTaskItemViewModel>),
            typeof(RecordListView), new PropertyMetadata(null));

    private enum ReorderTarget { None, Daily, LongTerm }

    private IRecordListItemViewModel? _dragItem;
    private RecordListItem? _dragSourceItem;
    private ListBox? _activeListBox;
    private ReorderTarget _reorderTarget = ReorderTarget.None;
    private int _insertIndex = -1;
    private bool _isReordering;

    public RecordListView()
    {
        InitializeComponent();
    }

    public ObservableCollection<WorkRecordItemViewModel>? Records
    {
        get => (ObservableCollection<WorkRecordItemViewModel>?)GetValue(RecordsProperty);
        set => SetValue(RecordsProperty, value);
    }

    public ObservableCollection<LongTermTaskItemViewModel>? LongTermTasks
    {
        get => (ObservableCollection<LongTermTaskItemViewModel>?)GetValue(LongTermTasksProperty);
        set => SetValue(LongTermTasksProperty, value);
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
    }

    private void RecordListItem_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RecordListItem item) return;

        item.DeleteRequested -= Item_DeleteRequested;
        item.CompleteRequested -= Item_CompleteRequested;
        item.ReorderDragStarted -= Item_ReorderDragStarted;
        item.EditingChanged -= Item_EditingChanged;
    }

    private void Item_DeleteRequested(object? sender, IRecordListItemViewModel e) =>
        DeleteRequested?.Invoke(this, e);

    private void Item_CompleteRequested(object? sender, IRecordListItemViewModel e) =>
        CompleteRequested?.Invoke(this, e);

    private void Item_EditingChanged(object? sender, bool editing) =>
        RecordEditingChanged?.Invoke(this, editing);

    private void Item_ReorderDragStarted(object? sender, IRecordListItemViewModel e)
    {
        if (sender is not RecordListItem item) return;

        _dragItem = e;
        _dragSourceItem = item;
        _isReordering = true;

        if (e.IsLongTermTask)
        {
            _reorderTarget = ReorderTarget.LongTerm;
            _activeListBox = LongTermListBox;
            _insertIndex = LongTermTasks?.IndexOf((LongTermTaskItemViewModel)e) ?? -1;
        }
        else
        {
            _reorderTarget = ReorderTarget.Daily;
            _activeListBox = RecordListBox;
            _insertIndex = Records?.IndexOf((WorkRecordItemViewModel)e) ?? -1;
        }

        InsertIndicator.Visibility = Visibility.Visible;
        ShowInsertIndicator(_insertIndex >= 0 ? _insertIndex : 0);

        CaptureMouse();
        MouseMove += RecordListView_MouseMove;
        MouseLeftButtonUp += RecordListView_MouseLeftButtonUp;
    }

    private void RecordListView_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isReordering || _activeListBox is null) return;

        var screenPoint = PointToScreen(e.GetPosition(this));
        var index = GetInsertIndex(_activeListBox, screenPoint);
        if (index < 0) return;

        _insertIndex = index;
        ShowInsertIndicator(index);
    }

    private async void RecordListView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isReordering) return;

        StopReorderTracking();

        if (_dragItem is null || _insertIndex < 0) goto Cleanup;

        if (_reorderTarget == ReorderTarget.LongTerm && LongTermTasks is not null)
        {
            var collection = LongTermTasks;
            var item = (LongTermTaskItemViewModel)_dragItem;
            var oldIndex = collection.IndexOf(item);
            var newIndex = _insertIndex;
            if (oldIndex >= 0 && oldIndex != newIndex)
            {
                if (newIndex > oldIndex) newIndex--;
                collection.Move(oldIndex, newIndex);

                if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
                    await vm.SaveLongTermTaskOrderAsync();
            }
        }
        else if (_reorderTarget == ReorderTarget.Daily && Records is not null)
        {
            var collection = Records;
            var item = (WorkRecordItemViewModel)_dragItem;
            var oldIndex = collection.IndexOf(item);
            var newIndex = _insertIndex;
            if (oldIndex >= 0 && oldIndex != newIndex)
            {
                if (newIndex > oldIndex) newIndex--;
                collection.Move(oldIndex, newIndex);

                if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
                    await vm.SaveRecordOrderAsync();
            }
        }

        Cleanup:
        _dragSourceItem?.ResetInteraction();
        _dragItem = null;
        _dragSourceItem = null;
        _activeListBox = null;
        _reorderTarget = ReorderTarget.None;
        _insertIndex = -1;
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
        if (_activeListBox is null || _activeListBox.Items.Count == 0) return;

        FrameworkElement? reference;
        double y;

        if (index >= _activeListBox.Items.Count)
        {
            reference = _activeListBox.ItemContainerGenerator
                .ContainerFromIndex(_activeListBox.Items.Count - 1) as FrameworkElement;
            if (reference is null) return;
            y = reference.TransformToAncestor(this).Transform(new Point(0, reference.ActualHeight)).Y;
        }
        else
        {
            reference = _activeListBox.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (reference is null) return;
            y = reference.TransformToAncestor(this).Transform(new Point(0, 0)).Y;
        }

        InsertIndicator.Margin = new Thickness(8, y, 8, 0);
        InsertIndicator.Visibility = Visibility.Visible;
    }

    private void OpenArchive_Click(object sender, RoutedEventArgs e) =>
        OpenArchiveRequested?.Invoke(this, EventArgs.Empty);
}
