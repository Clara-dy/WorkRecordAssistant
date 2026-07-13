using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkRecordAssistant.Helpers;

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
        VersionNumber = record.VersionNumber;
        VersionInfo = record.VersionInfo;
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

    [ObservableProperty]
    private string? _versionNumber;

    [ObservableProperty]
    private string? _versionInfo;

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; }

    public bool SupportsTapToComplete => !IsEditing;

    public bool ShowCompleteButton => false;

    public bool ShowStartDateHint => !IsCompleted;

    public string StartDateHint => $"开始 {CreatedAt:yyyy-MM-dd}";

    public bool ShowTimeVersionHint =>
        VersionDisplayHelper.ShouldShowTimeVersionHint(ShowTime, VersionNumber, VersionInfo);

    public bool ShowTimePart => ShowTime || !string.IsNullOrWhiteSpace(VersionNumber);

    public string TimePart => ShowTimePart ? CreatedAt.ToString("HH:mm") : string.Empty;

    public bool ShowVersionNumberPart => !string.IsNullOrWhiteSpace(VersionNumber);

    public string VersionNumberPart => VersionNumber?.Trim() ?? string.Empty;

    public bool ShowVersionInfoPart => !string.IsNullOrWhiteSpace(VersionInfo);

    public string VersionInfoPart => VersionInfo?.Trim() ?? string.Empty;

    public string TimeVersionHint =>
        VersionDisplayHelper.BuildTimeVersionHint(CreatedAt, ShowTime, VersionNumber, VersionInfo);

    public bool IsDisplayUnderlined => IsCompleted;

    public string DisplayText => Content;

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(ShowCompleteButton));
    }

    partial void OnShowTimeChanged(bool value)
    {
        NotifyVersionDisplayProperties();
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnVersionNumberChanged(string? value) => NotifyVersionDisplayProperties();

    partial void OnVersionInfoChanged(string? value) => NotifyVersionDisplayProperties();

    private void NotifyVersionDisplayProperties()
    {
        OnPropertyChanged(nameof(ShowTimeVersionHint));
        OnPropertyChanged(nameof(TimeVersionHint));
        OnPropertyChanged(nameof(ShowTimePart));
        OnPropertyChanged(nameof(TimePart));
        OnPropertyChanged(nameof(ShowVersionNumberPart));
        OnPropertyChanged(nameof(VersionNumberPart));
        OnPropertyChanged(nameof(ShowVersionInfoPart));
        OnPropertyChanged(nameof(VersionInfoPart));
    }

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
