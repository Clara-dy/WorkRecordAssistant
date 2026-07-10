using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.Helpers;

/// <summary>
/// 复制模板渲染辅助。
/// </summary>
public static class CopyTemplateHelper
{
    public static string BuildCopyText(string date, IEnumerable<WorkRecord> records, AppSettings settings)
    {
        var sorted = settings.DefaultSortOrder == RecordSortOrder.NewestFirst
            ? records.OrderByDescending(r => r.CreatedAt).ToList()
            : records.OrderBy(r => r.CreatedAt).ToList();

        return BuildPlainCopyText(sorted.Select(r => r.Content));
    }

    /// <summary>
    /// 纯文本复制：仅内容，无序号，去除首尾空白与空行。
    /// </summary>
    public static string BuildPlainCopyText(IEnumerable<string> contents)
    {
        return string.Join('\n', contents
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrEmpty(c)));
    }
}
