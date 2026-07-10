using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkRecordAssistant.Helpers;
using WorkRecordAssistant.Services;

namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 长期任务归档箱 ViewModel。
/// </summary>
public partial class LongTermArchiveViewModel : ObservableObject
{
    private readonly IDataService _dataService;

    public LongTermArchiveViewModel(IDataService dataService)
    {
        _dataService = dataService;
    }

    public ObservableCollection<ArchivedLongTermTaskItemViewModel> Items { get; } = [];

    public async Task LoadAsync()
    {
        var tasks = await _dataService.GetArchivedLongTermTasksAsync();
        Items.Clear();
        foreach (var task in tasks)
            Items.Add(new ArchivedLongTermTaskItemViewModel(task));
    }

    [RelayCommand]
    private void CopyAll()
    {
        if (Items.Count == 0) return;
        var text = CopyTemplateHelper.BuildPlainCopyText(Items.Select(i => i.Content));
        ClipboardService.CopyText(text);
        StatusMessage = "已复制全部归档任务";
    }

    [RelayCommand]
    private void CopyItem(ArchivedLongTermTaskItemViewModel? item)
    {
        if (item is null) return;
        ClipboardService.CopyText(item.Content.Trim());
        StatusMessage = "已复制该任务";
    }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    partial void OnStatusMessageChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        Task.Delay(2000).ContinueWith(_ =>
        {
            App.Current.Dispatcher.Invoke(() => StatusMessage = string.Empty);
        });
    }
}
