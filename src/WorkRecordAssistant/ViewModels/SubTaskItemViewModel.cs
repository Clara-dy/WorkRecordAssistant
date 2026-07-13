using CommunityToolkit.Mvvm.ComponentModel;
using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 子任务列表项 ViewModel。
/// </summary>
public partial class SubTaskItemViewModel : ObservableObject
{
    public SubTaskItemViewModel(TaskSubItem item)
    {
        Id = item.Id;
        ParentType = item.ParentType;
        ParentId = item.ParentId;
        Content = item.Content;
        IsCompleted = item.IsCompleted;
        SortOrder = item.SortOrder;
    }

    public int Id { get; }

    public TaskParentType ParentType { get; }

    public int ParentId { get; }

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private int _sortOrder;

    public bool SupportsTapToComplete => !IsEditing;

    public bool IsDisplayUnderlined => IsCompleted;

    public string DisplayText => Content;

    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(DisplayText));

    partial void OnIsCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(SupportsTapToComplete));
        OnPropertyChanged(nameof(IsDisplayUnderlined));
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(SupportsTapToComplete));
}
