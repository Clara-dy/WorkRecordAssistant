using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 任务列表项 ViewModel。
/// </summary>
public partial class WorkRecordItemViewModel : ObservableObject, IRecordListItemViewModel
{
    public WorkRecordItemViewModel(Models.WorkRecord record, bool showTime)
    {
        Id = record.Id;
        Content = record.Content;
        CreatedAt = record.CreatedAt;
        UpdatedAt = record.UpdatedAt;
        SortOrder = record.SortOrder;
        IsCompleted = record.IsCompleted;
        IsStarred = record.IsStarred;
        ShowTime = showTime;
    }

    public int Id { get; }

    public ObservableCollection<SubTaskItemViewModel> SubTasks { get; } = [];

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _showTime;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private int _sortOrder;

    [ObservableProperty]
    private bool _isAddingSubTask;

    [ObservableProperty]
    private string _newSubTaskText = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isStarred;

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; }

    public bool SupportsTapToComplete => !IsEditing;

    public bool ShowCompleteButton => false;

    public bool ShowStartDateHint => !IsCompleted;

    public string StartDateHint => $"开始 {CreatedAt:yyyy-MM-dd}";

    public bool IsDisplayUnderlined => IsCompleted;

    public string DisplayText => Content;

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(ShowCompleteButton));
    }

    partial void OnShowTimeChanged(bool value) => OnPropertyChanged(nameof(DisplayText));

    partial void OnIsCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(SupportsTapToComplete));
        OnPropertyChanged(nameof(ShowStartDateHint));
        OnPropertyChanged(nameof(IsDisplayUnderlined));
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnIsStarredChanged(bool value) => OnPropertyChanged(nameof(IsStarred));

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(ShowCompleteButton));
        OnPropertyChanged(nameof(SupportsTapToComplete));
    }
}
