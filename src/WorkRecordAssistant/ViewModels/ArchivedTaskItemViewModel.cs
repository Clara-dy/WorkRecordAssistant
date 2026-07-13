namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 归档箱任务列表项 ViewModel。
/// </summary>
public class ArchivedTaskItemViewModel
{
    public ArchivedTaskItemViewModel(Models.WorkRecord record)
    {
        IsStarred = record.IsStarred;
        Content = record.Content;
        CreatedAt = record.CreatedAt;
        CompletedAt = record.CompletedAt;
    }

    public bool IsStarred { get; }

    public string Content { get; }

    public DateTime CreatedAt { get; }

    public DateTime? CompletedAt { get; }

    public string DisplayText
    {
        get
        {
            var completed = CompletedAt?.ToString("yyyy-MM-dd") ?? "—";
            return $"{Content}\n开始 {CreatedAt:yyyy-MM-dd} · 完成 {completed}";
        }
    }
}
