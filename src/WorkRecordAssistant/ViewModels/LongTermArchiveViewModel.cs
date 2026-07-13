using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkRecordAssistant.Helpers;
using WorkRecordAssistant.Models;
using WorkRecordAssistant.Services;

namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 任务归档箱 ViewModel。
/// </summary>
public partial class LongTermArchiveViewModel : ObservableObject
{
    private readonly IDataService _dataService;
    private readonly List<ArchivedTaskItemViewModel> _allItems = [];

    public LongTermArchiveViewModel(IDataService dataService)
    {
        _dataService = dataService;
    }

    public ObservableCollection<ArchivedTaskItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private ArchiveTaskFilter _filter = ArchiveTaskFilter.All;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public async Task LoadAsync()
    {
        var completed = await _dataService.GetCompletedWorkRecordsAsync();

        _allItems.Clear();
        foreach (var record in completed)
            _allItems.Add(new ArchivedTaskItemViewModel(record));

        _allItems.Sort((a, b) =>
        {
            var aTime = a.CompletedAt ?? a.CreatedAt;
            var bTime = b.CompletedAt ?? b.CreatedAt;
            return bTime.CompareTo(aTime);
        });

        ApplyFilter();
    }

    [RelayCommand]
    private void SetFilter(ArchiveTaskFilter filter)
    {
        Filter = filter;
        ApplyFilter();
    }

    partial void OnFilterChanged(ArchiveTaskFilter value) => ApplyFilter();

    private void ApplyFilter()
    {
        Items.Clear();
        foreach (var item in _allItems.Where(MatchesFilter))
            Items.Add(item);
    }

    private bool MatchesFilter(ArchivedTaskItemViewModel item) => Filter switch
    {
        ArchiveTaskFilter.Starred => item.IsStarred,
        ArchiveTaskFilter.Normal => !item.IsStarred,
        _ => true
    };

    [RelayCommand]
    private void CopyAll()
    {
        if (Items.Count == 0) return;
        var text = CopyTemplateHelper.BuildPlainCopyText(Items.Select(i => i.Content));
        ClipboardService.CopyText(text);
        StatusMessage = "已复制全部归档任务";
    }

    [RelayCommand]
    private void CopyItem(ArchivedTaskItemViewModel? item)
    {
        if (item is null) return;
        ClipboardService.CopyText(item.Content.Trim());
        StatusMessage = "已复制该任务";
    }

    partial void OnStatusMessageChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        Task.Delay(2000).ContinueWith(_ =>
        {
            App.Current.Dispatcher.Invoke(() => StatusMessage = string.Empty);
        });
    }
}
