namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 记录列表项通用接口，供日常记录与长期任务复用 UI。
/// </summary>
public interface IRecordListItemViewModel
{
    int Id { get; }

    string Content { get; set; }

    string DisplayText { get; }

    bool IsEditing { get; set; }

    bool IsLongTermTask { get; }

    int SortOrder { get; set; }
}
