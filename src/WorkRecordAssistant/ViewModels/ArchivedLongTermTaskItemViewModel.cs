namespace WorkRecordAssistant.ViewModels;

/// <summary>
/// 已归档长期任务列表项 ViewModel。
/// </summary>
public class ArchivedLongTermTaskItemViewModel
{
    public ArchivedLongTermTaskItemViewModel(Models.LongTermTask task)
    {
        Content = task.Content;
        CreatedAt = task.CreatedAt;
        CompletedAt = task.CompletedAt;
    }

    public string Content { get; }

    public DateTime CreatedAt { get; }

    public DateTime? CompletedAt { get; }

    public string DisplayText
    {
        get
        {
            var completed = CompletedAt?.ToString("yyyy-MM-dd") ?? "—";
            return $"{Content}\n创建 {CreatedAt:yyyy-MM-dd} · 结束 {completed}";
        }
    }
}
