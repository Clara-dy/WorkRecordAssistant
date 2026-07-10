using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 工作记录列表项 ViewModel。
/// </summary>
public partial class WorkRecordItemViewModel : ObservableObject, IRecordListItemViewModel
{
    public bool IsLongTermTask => false;
    public WorkRecordItemViewModel(Models.WorkRecord record, bool showTime)
    {
        Id = record.Id;
        Content = record.Content;
        CreatedAt = record.CreatedAt;
        UpdatedAt = record.UpdatedAt;
        SortOrder = record.SortOrder;
        ShowTime = showTime;
    }

    public int Id { get; }

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _showTime;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private int _sortOrder;

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; }

    public string DisplayText => ShowTime
        ? $"{CreatedAt:HH:mm}  {Content}"
        : Content;

    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(DisplayText));

    partial void OnShowTimeChanged(bool value) => OnPropertyChanged(nameof(DisplayText));

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(DisplayText));
}
