using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 长期任务列表项 ViewModel。
/// </summary>
public partial class LongTermTaskItemViewModel : ObservableObject, IRecordListItemViewModel
{
    public LongTermTaskItemViewModel(Models.LongTermTask task)
    {
        Id = task.Id;
        Content = task.Content;
        CreatedAt = task.CreatedAt;
        UpdatedAt = task.UpdatedAt;
        SortOrder = task.SortOrder;
    }

    public int Id { get; }

    public bool IsLongTermTask => true;

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private int _sortOrder;

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; }

    public string DisplayText => $"{CreatedAt:yyyy-MM-dd HH:mm}  {Content}";

    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(DisplayText));

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(DisplayText));
}
