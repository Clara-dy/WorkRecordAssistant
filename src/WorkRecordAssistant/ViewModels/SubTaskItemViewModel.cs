using CommunityToolkit.Mvvm.ComponentModel;
using WorkRecordAssistant.Helpers;
using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 子任务列表项 ViewModel。
/// </summary>
public partial class SubTaskItemViewModel : ObservableObject
{
    public SubTaskItemViewModel(TaskSubItem item, bool showTime = false)
    {
        Id = item.Id;
        ParentType = item.ParentType;
        ParentId = item.ParentId;
        Content = item.Content;
        IsCompleted = item.IsCompleted;
        SortOrder = item.SortOrder;
        CreatedAt = item.CreatedAt;
        VersionNumber = item.VersionNumber;
        VersionInfo = item.VersionInfo;
        ShowTime = showTime;
    }

    public int Id { get; }

    public TaskParentType ParentType { get; }

    public int ParentId { get; }

    public DateTime CreatedAt { get; }

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private int _sortOrder;

    [ObservableProperty]
    private bool _showTime;

    [ObservableProperty]
    private string? _versionNumber;

    [ObservableProperty]
    private string? _versionInfo;

    public bool SupportsTapToComplete => !IsEditing;

    public bool IsDisplayUnderlined => IsCompleted;

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

    public string DisplayText => Content;

    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(DisplayText));

    partial void OnShowTimeChanged(bool value) => NotifyVersionDisplayProperties();

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
        OnPropertyChanged(nameof(IsDisplayUnderlined));
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(SupportsTapToComplete));
}
