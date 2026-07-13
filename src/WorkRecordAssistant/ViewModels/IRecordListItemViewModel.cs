namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 任务列表项通用接口。
/// </summary>
public interface IRecordListItemViewModel
{
    int Id { get; }

    string Content { get; set; }

    string DisplayText { get; }

    bool IsEditing { get; set; }

    bool IsStarred { get; }

    bool IsCompleted { get; }

    bool ShowCompleteButton { get; }

    bool SupportsTapToComplete { get; }

    bool ShowStartDateHint { get; }

    string StartDateHint { get; }

    bool IsDisplayUnderlined { get; }

    int SortOrder { get; set; }

    System.Collections.ObjectModel.ObservableCollection<SubTaskItemViewModel> SubTasks { get; }

    bool IsAddingSubTask { get; set; }

    string NewSubTaskText { get; set; }
}
