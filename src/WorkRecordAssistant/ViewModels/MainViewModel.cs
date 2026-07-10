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

    public ObservableCollection<WorkRecordItemViewModel> Records { get; } = [];

    public ObservableCollection<LongTermTaskItemViewModel> LongTermTasks { get; } = [];

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
    private string _statusMessage = string.Empty;

    public string DateDisplay => SelectedDate.ToString("yyyy-MM-dd");

    public bool IsEditing => IsInputFocused || IsRecordEditing;

    public async Task InitializeAsync()
    {
        ShowRecordTime = _settingsService.Current.ShowRecordTime;
        await LoadQuickButtonsAsync();
        await LoadLongTermTasksAsync();
        await LoadRecordsAsync();
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(DateDisplay));
        _ = LoadRecordsAsync();
    }

    partial void OnShowRecordTimeChanged(bool value)
    {
        foreach (var record in Records)
            record.ShowTime = value;
    }

    partial void OnIsRecordEditingChanged(bool value) => OnPropertyChanged(nameof(IsEditing));

    [RelayCommand]
    private async Task AddRecordAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var record = await _dataService.AddRecordAsync(DateDisplay, InputText);
        InputText = string.Empty;
        await LoadRecordsAsync();
        ShowStatus("已添加记录");
    }

    [RelayCommand]
    private async Task AddLongTermTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        await _dataService.AddLongTermTaskAsync(InputText);
        InputText = string.Empty;
        await LoadLongTermTasksAsync();
        ShowStatus("已添加长期任务");
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

    public async Task UpdateLongTermTaskAsync(LongTermTaskItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.Content))
        {
            await DeleteLongTermTaskAsync(item);
            return;
        }

        await _dataService.UpdateLongTermTaskContentAsync(item.Id, item.Content);
        ShowStatus("已更新长期任务");
    }

    public async Task UpdateRecordItemAsync(IRecordListItemViewModel item)
    {
        switch (item)
        {
            case WorkRecordItemViewModel record:
                await UpdateRecordAsync(record);
                break;
            case LongTermTaskItemViewModel task:
                await UpdateLongTermTaskAsync(task);
                break;
        }
    }

    public async Task SaveRecordOrderAsync()
    {
        var ids = Records.Select(r => r.Id).ToList();
        await _dataService.ReorderRecordsAsync(DateDisplay, ids);
        for (var i = 0; i < Records.Count; i++)
            Records[i].SortOrder = i;
        ShowStatus("已更新排序");
    }

    public async Task SaveLongTermTaskOrderAsync()
    {
        var ids = LongTermTasks.Select(t => t.Id).ToList();
        await _dataService.ReorderLongTermTasksAsync(ids);
        for (var i = 0; i < LongTermTasks.Count; i++)
            LongTermTasks[i].SortOrder = i;
        ShowStatus("已更新长期任务排序");
    }

    [RelayCommand]
    private async Task DeleteRecordAsync(WorkRecordItemViewModel? item)
    {
        if (item is null) return;
        await _dataService.DeleteRecordAsync(item.Id);
        Records.Remove(item);
        ShowStatus("已删除");
    }

    [RelayCommand]
    private async Task DeleteLongTermTaskAsync(LongTermTaskItemViewModel? item)
    {
        if (item is null) return;
        await _dataService.DeleteLongTermTaskAsync(item.Id);
        LongTermTasks.Remove(item);
        ShowStatus("已删除长期任务");
    }

    public async Task DeleteRecordItemAsync(IRecordListItemViewModel item)
    {
        switch (item)
        {
            case WorkRecordItemViewModel record:
                if (DeleteRecordCommand.CanExecute(record))
                    await DeleteRecordCommand.ExecuteAsync(record);
                break;
            case LongTermTaskItemViewModel task:
                if (DeleteLongTermTaskCommand.CanExecute(task))
                    await DeleteLongTermTaskCommand.ExecuteAsync(task);
                break;
        }
    }

    [RelayCommand]
    private void CopyToday()
    {
        var text = CopyTemplateHelper.BuildPlainCopyText(Records.Select(r => r.Content));
        if (string.IsNullOrEmpty(text)) return;

        ClipboardService.CopyText(text);
        ShowStatus("已复制到剪贴板");
    }

    public async Task CompleteLongTermTaskAsync(LongTermTaskItemViewModel item)
    {
        await _dataService.CompleteLongTermTaskAsync(item.Id);
        LongTermTasks.Remove(item);
        ShowStatus("长期任务已结束并归档");
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
        await LoadRecordsAsync();
        ShowStatus(settings.DefaultSortOrder == RecordSortOrder.NewestFirst ? "最新在前" : "最旧在前");
    }

    public async Task LoadLongTermTasksAsync()
    {
        var tasks = await _dataService.GetLongTermTasksAsync();

        LongTermTasks.Clear();
        foreach (var task in tasks.OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt))
        {
            LongTermTasks.Add(new LongTermTaskItemViewModel(task)
            {
                SortOrder = task.SortOrder
            });
        }
    }

    public async Task LoadRecordsAsync()
    {
        var records = await _dataService.GetRecordsAsync(DateDisplay);
        var sorted = _settingsService.Current.DefaultSortOrder == RecordSortOrder.NewestFirst
            ? records.OrderByDescending(r => r.CreatedAt)
            : records.OrderBy(r => r.CreatedAt);

        Records.Clear();
        foreach (var record in sorted)
        {
            Records.Add(new WorkRecordItemViewModel(record, ShowRecordTime)
            {
                SortOrder = record.SortOrder
            });
        }
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
