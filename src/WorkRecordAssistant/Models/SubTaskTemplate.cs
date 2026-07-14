namespace WorkRecordAssistant.Models;

/// <summary>
/// 右键快速添加子任务的模板项。
/// </summary>
public class SubTaskTemplate
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
