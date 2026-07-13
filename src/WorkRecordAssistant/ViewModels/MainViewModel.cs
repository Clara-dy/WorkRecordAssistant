using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkRecordAssistant.Helpers;
using WorkRecordAssistant.Models;
using WorkRecordAssistant.Services;

namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 主窗口 ViewModel。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IDataService _dataService;
    private readonly ISettingsService _settingsService;

    public MainViewModel(IDataService dataService, ISettingsService settingsService)
    {
        _dataService = dataService;
        _settingsService = settingsService;
    }

    public ObservableCollection<WorkRecordItemViewModel> StarredTasks { get; } = [];

    public ObservableCollection<WorkRecordItemViewModel> Records { get; } = [];

    public ObservableCollection<IRecordListItemViewModel> TaskItems { get; } = [];

    public ObservableCollection<QuickButtonViewModel> QuickButtons { get; } = [];

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isInputFocused;

    [ObservableProperty]
    private bool _showRecordTime;

    [ObservableProperty]
    private bool _isRecordEditing;

    [ObservableProperty]
    private bool _isInputPanelVisible;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string DateDisplay => SelectedDate.ToString("yyyy-MM-dd");

    public bool IsEditing => IsInputFocused || IsRecordEditing;

    public int StarredTaskCount => StarredTasks.Count;

    public async Task InitializeAsync()
    {
        ShowRecordTime = _settingsService.Current.ShowRecordTime;
        await LoadQuickButtonsAsync();
        await LoadTasksAsync();
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(DateDisplay));
        _ = LoadTasksAsync();
    }

    partial void OnShowRecordTimeChanged(bool value)
    {
        foreach (var item in TaskItems.OfType<WorkRecordItemViewModel>())
            item.ShowTime = value;
    }

    partial void OnIsRecordEditingChanged(bool value) => OnPropertyChanged(nameof(IsEditing));

    [RelayCommand]
    private void ShowTaskInput()
    {
        IsInputPanelVisible = true;
    }

    [RelayCommand]
    private async Task AddRecordAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        await _dataService.AddRecordAsync(DateDisplay, InputText);
        InputText = string.Empty;
        IsInputPanelVisible = false;
        await LoadTasksAsync();
        ShowStatus("已添加任务");
    }

    public async Task UpdateRecordAsync(WorkRecordItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.Content))
        {
            await DeleteRecordAsync(item);
            return;
        }

        await _dataService.UpdateRecordContentAsync(item.Id, item.Content);
        ShowStatus("已更新");
    }

    public async Task UpdateRecordItemAsync(IRecordListItemViewModel item)
    {
        if (item is WorkRecordItemViewModel record)
            await UpdateRecordAsync(record);
    }

    public async Task SaveRecordOrderAsync()
    {
        var normalItems = TaskItems.OfType<WorkRecordItemViewModel>().Where(i => !i.IsStarred).ToList();
        Records.Clear();
        foreach (var item in normalItems)
            Records.Add(item);

        var ids = normalItems.Select(r => r.Id).ToList();
        await _dataService.ReorderRecordsAsync(ids);
        for (var i = 0; i < normalItems.Count; i++)
            normalItems[i].SortOrder = i;
        ShowStatus("已更新排序");
    }

    public async Task SaveStarredOrderAsync()
    {
        var starredItems = TaskItems.OfType<WorkRecordItemViewModel>().Where(i => i.IsStarred).ToList();
        StarredTasks.Clear();
        foreach (var item in starredItems)
            StarredTasks.Add(item);

        var ids = starredItems.Select(r => r.Id).ToList();
        await _dataService.ReorderRecordsAsync(ids);
        for (var i = 0; i < starredItems.Count; i++)
            starredItems[i].SortOrder = i;
        OnPropertyChanged(nameof(StarredTaskCount));
        ShowStatus("已更新星级任务排序");
    }

    [RelayCommand]
    private async Task DeleteRecordAsync(WorkRecordItemViewModel? item)
    {
        if (item is null) return;
        await _dataService.DeleteRecordAsync(item.Id);
        StarredTasks.Remove(item);
        Records.Remove(item);
        RefreshTaskItems();
        ShowStatus("已删除");
    }

    public async Task DeleteRecordItemAsync(IRecordListItemViewModel item)
    {
        if (item is WorkRecordItemViewModel record && DeleteRecordCommand.CanExecute(record))
            await DeleteRecordCommand.ExecuteAsync(record);
    }

    public async Task SetRecordStarredAsync(IRecordListItemViewModel item, bool isStarred)
    {
        if (item is not WorkRecordItemViewModel record) return;

        await _dataService.SetRecordStarredAsync(record.Id, isStarred);
        ShowStatus(isStarred ? "已标注为星级任务" : "已取消星级");
        await LoadTasksAsync();
    }

    [RelayCommand]
    private void CopyToday()
    {
        var lines = new List<string>();
        foreach (var record in TaskItems.OfType<WorkRecordItemViewModel>().Where(r => r.IsCompleted))
        {
            lines.Add(record.Content);
            foreach (var sub in record.SubTasks.Where(s => s.IsCompleted))
                lines.Add($"  - {sub.Content}");
        }

        var text = CopyTemplateHelper.BuildPlainCopyText(lines);
        if (string.IsNullOrEmpty(text)) return;

        ClipboardService.CopyText(text);
        ShowStatus("已复制到剪贴板");
    }

    public async Task AddSubTaskAsync(IRecordListItemViewModel parent)
    {
        if (string.IsNullOrWhiteSpace(parent.NewSubTaskText)) return;

        var sub = await _dataService.AddSubTaskAsync(TaskParentType.WorkRecord, parent.Id, parent.NewSubTaskText);
        parent.SubTasks.Add(new SubTaskItemViewModel(sub));
        parent.NewSubTaskText = string.Empty;
        parent.IsAddingSubTask = false;
        ShowStatus("已添加子任务");
    }

    public async Task ToggleSubTaskCompletionAsync(SubTaskItemViewModel sub, IRecordListItemViewModel parent)
    {
        if (sub.IsCompleted)
        {
            await _dataService.UncompleteSubTaskAsync(sub.Id);
            sub.IsCompleted = false;
            ShowStatus("子任务已恢复为未完成");
        }
        else
        {
            await _dataService.CompleteSubTaskAsync(sub.Id);
            sub.IsCompleted = true;
            ShowStatus("子任务已完成");
        }

        SortSubTasks(parent.SubTasks);
    }

    public async Task UpdateSubTaskAsync(SubTaskItemViewModel sub)
    {
        if (string.IsNullOrWhiteSpace(sub.Content))
        {
            var parent = FindParentOfSubTask(sub);
            if (parent is not null)
                await DeleteSubTaskAsync(sub, parent);
            return;
        }

        await _dataService.UpdateSubTaskContentAsync(sub.Id, sub.Content);
        ShowStatus("已更新子任务");
    }

    public async Task DeleteSubTaskAsync(SubTaskItemViewModel sub, IRecordListItemViewModel parent)
    {
        await _dataService.DeleteSubTaskAsync(sub.Id);
        parent.SubTasks.Remove(sub);
        ShowStatus("已删除子任务");
    }

    private IRecordListItemViewModel? FindParentOfSubTask(SubTaskItemViewModel sub)
    {
        foreach (var item in TaskItems)
        {
            if (item.SubTasks.Contains(sub))
                return item;
        }

        return null;
    }

    private static void SortSubTasks(ObservableCollection<SubTaskItemViewModel> subTasks)
    {
        var sorted = subTasks.OrderBy(s => s.IsCompleted).ThenBy(s => s.SortOrder).ToList();
        subTasks.Clear();
        foreach (var sub in sorted)
            subTasks.Add(sub);
    }

    private static void PopulateSubTasks(
        IRecordListItemViewModel item,
        IReadOnlyList<TaskSubItem> subTasks)
    {
        item.SubTasks.Clear();
        foreach (var sub in subTasks)
            item.SubTasks.Add(new SubTaskItemViewModel(sub));
    }

    public async Task CompleteRecordAsync(WorkRecordItemViewModel item)
    {
        if (item.IsCompleted)
        {
            await _dataService.UncompleteRecordAsync(item.Id);
            ShowStatus("已恢复为未完成");
        }
        else
        {
            await _dataService.CompleteRecordAsync(item.Id, DateDisplay);
            ShowStatus("任务已完成");
        }

        await LoadTasksAsync();
    }

    public async Task CompleteRecordItemAsync(IRecordListItemViewModel item)
    {
        if (item is WorkRecordItemViewModel record)
            await CompleteRecordAsync(record);
    }

    [RelayCommand]
    private void OpenQuickButton(QuickButtonViewModel? button)
    {
        if (button is null || string.IsNullOrWhiteSpace(button.Url)) return;
        ClipboardService.OpenUrl(button.Url);
    }

    [RelayCommand]
    private void GoPreviousDay() => SelectedDate = SelectedDate.AddDays(-1);

    [RelayCommand]
    private void GoNextDay() => SelectedDate = SelectedDate.AddDays(1);

    [RelayCommand]
    private void GoToday() => SelectedDate = DateTime.Today;

    [RelayCommand]
    private async Task ToggleSortOrderAsync()
    {
        var settings = _settingsService.Current;
        settings.DefaultSortOrder = settings.DefaultSortOrder == RecordSortOrder.NewestFirst
            ? RecordSortOrder.OldestFirst
            : RecordSortOrder.NewestFirst;
        await _settingsService.SaveAsync();
        await LoadTasksAsync();
        ShowStatus(settings.DefaultSortOrder == RecordSortOrder.NewestFirst ? "最新在前" : "最旧在前");
    }

    public async Task LoadTasksAsync()
    {
        var allRecords = await _dataService.GetRecordsForDisplayAsync(DateDisplay);
        var starred = allRecords.Where(r => r.IsStarred).ToList();
        var records = allRecords.Where(r => !r.IsStarred).ToList();
        var allIds = allRecords.Select(r => r.Id);
        var subTasks = await _dataService.GetSubTasksAsync(TaskParentType.WorkRecord, allIds);

        StarredTasks.Clear();
        var starredIncomplete = starred.Where(r => !r.IsCompleted).OrderBy(r => r.SortOrder).ThenBy(r => r.CreatedAt);
        var starredCompleted = starred.Where(r => r.IsCompleted).OrderBy(r => r.SortOrder).ThenBy(r => r.CreatedAt);
        foreach (var record in starredIncomplete.Concat(starredCompleted))
        {
            var vm = CreateRecordViewModel(record);
            if (subTasks.TryGetValue(record.Id, out var subs))
                PopulateSubTasks(vm, subs);
            StarredTasks.Add(vm);
        }

        var incomplete = records.Where(r => !r.IsCompleted)
            .OrderBy(r => r.CreatedAt)
            .ToList();
        var completed = records.Where(r => r.IsCompleted);
        var sortedCompleted = _settingsService.Current.DefaultSortOrder == RecordSortOrder.NewestFirst
            ? completed.OrderByDescending(r => r.CompletedAt ?? r.CreatedAt)
            : completed.OrderBy(r => r.CompletedAt ?? r.CreatedAt);

        Records.Clear();
        foreach (var record in incomplete.Concat(sortedCompleted))
        {
            var vm = CreateRecordViewModel(record);
            if (subTasks.TryGetValue(record.Id, out var subs))
                PopulateSubTasks(vm, subs);
            Records.Add(vm);
        }

        RefreshTaskItems();
    }

    private WorkRecordItemViewModel CreateRecordViewModel(WorkRecord record) =>
        new(record, ShowRecordTime)
        {
            SortOrder = record.SortOrder,
            IsCompleted = record.IsCompleted,
            IsStarred = record.IsStarred
        };

    private void RefreshTaskItems()
    {
        TaskItems.Clear();
        foreach (var task in StarredTasks)
            TaskItems.Add(task);
        foreach (var record in Records)
            TaskItems.Add(record);
        OnPropertyChanged(nameof(StarredTaskCount));
    }

    public async Task LoadQuickButtonsAsync()
    {
        var buttons = await _dataService.GetQuickButtonsAsync();
        QuickButtons.Clear();
        foreach (var button in buttons.Where(b => b.IsVisible))
        {
            QuickButtons.Add(new QuickButtonViewModel
            {
                Id = button.Id,
                Name = button.Name,
                Url = button.Url,
                IsVisible = button.IsVisible,
                SortOrder = button.SortOrder
            });
        }
    }

    public async Task SaveWindowStateAsync(double left, double top, double width, double height, SnapEdge snapEdge)
    {
        var settings = _settingsService.Current;
        settings.WindowLeft = left;
        settings.WindowTop = top;
        settings.WindowWidth = width;
        settings.WindowHeight = height;
        settings.SnapEdge = snapEdge;
        await _settingsService.SaveAsync();
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        Task.Delay(2000).ContinueWith(_ =>
        {
            App.Current.Dispatcher.Invoke(() => StatusMessage = string.Empty);
        });
    }
}
