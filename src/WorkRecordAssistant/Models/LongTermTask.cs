namespace WorkRecordAssistant.Models;

/// <summary>
/// 长期任务实体，跨日期置顶显示。
/// </summary>
public class LongTermTask
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    public DateTime? CompletedAt { get; set; }
}
