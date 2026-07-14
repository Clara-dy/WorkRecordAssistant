using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WorkRecordAssistant.Models;

namespace WorkRecordAssistant.Services;

/// <summary>
/// 基于 SQLite 的本地数据服务。
/// </summary>
public sealed class SqliteDataService : IDataService
{
    private readonly ISettingsService _settingsService;
    private string _dbPath = string.Empty;

    public SqliteDataService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task InitializeAsync()
    {
        var dataDir = _settingsService.GetDataDirectory();
        Directory.CreateDirectory(dataDir);
        _dbPath = Path.Combine(dataDir, "workrecords.db");

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var createRecords = """
            CREATE TABLE IF NOT EXISTS WorkRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Date TEXT NOT NULL,
                Content TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_WorkRecords_Date ON WorkRecords(Date);
            """;

        var createButtons = """
            CREATE TABLE IF NOT EXISTS QuickButtons (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Url TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsVisible INTEGER NOT NULL DEFAULT 1
            );
            """;

        await ExecuteNonQueryAsync(connection, createRecords);
        await ExecuteNonQueryAsync(connection, createButtons);

        var createLongTermTasks = """
            CREATE TABLE IF NOT EXISTS LongTermTasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Content TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsArchived INTEGER NOT NULL DEFAULT 0,
                CompletedAt TEXT
            );
            """;

        await ExecuteNonQueryAsync(connection, createLongTermTasks);
        await MigrateLongTermTasksTableAsync(connection);
        await MigrateWorkRecordsTableAsync(connection);
        await MigrateLongTermToStarredAsync(connection);

        var createSubTasks = """
            CREATE TABLE IF NOT EXISTS TaskSubItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ParentType TEXT NOT NULL,
                ParentId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                IsCompleted INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_TaskSubItems_Parent ON TaskSubItems(ParentType, ParentId);
            """;
        await ExecuteNonQueryAsync(connection, createSubTasks);
        await MigrateVersionColumnsAsync(connection);
        await RepairMismatchedCreatedAtAsync(connection);

        var createSubTaskTemplates = """
            CREATE TABLE IF NOT EXISTS SubTaskTemplates (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Content TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );
            """;
        await ExecuteNonQueryAsync(connection, createSubTaskTemplates);

        await SeedDefaultButtonsIfEmptyAsync(connection);
    }

    public async Task<IReadOnlyList<WorkRecord>> GetRecordsAsync(string date)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var records = new List<WorkRecord>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Date, Content, CreatedAt, UpdatedAt, SortOrder, IsCompleted, CompletedAt, IsStarred, VersionNumber, VersionInfo
            FROM WorkRecords
            WHERE Date = $date
            ORDER BY SortOrder, CreatedAt
            """;
        command.Parameters.AddWithValue("$date", date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(MapRecord(reader));
        }

        return records;
    }

    public async Task<IReadOnlyList<WorkRecord>> GetRecordsForDisplayAsync(string viewDate)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var records = new List<WorkRecord>();
        await using var command = connection.CreateCommand();
        // Incomplete: carry forward through today; also show tasks created for the
        // exact view day (so future-dated tasks appear when browsing that day).
        command.CommandText = """
            SELECT Id, Date, Content, CreatedAt, UpdatedAt, SortOrder, IsCompleted, CompletedAt, IsStarred, VersionNumber, VersionInfo
            FROM WorkRecords
            WHERE (
                (IsCompleted = 0
                 AND (
                    (Date <= $viewDate AND $viewDate <= $today)
                    OR Date = $viewDate
                 ))
                OR (IsCompleted = 1
                    AND Date <= $viewDate
                    AND date(CompletedAt) >= $viewDate)
              )
            ORDER BY IsCompleted, SortOrder, CreatedAt
            """;
        command.Parameters.AddWithValue("$viewDate", viewDate);
        command.Parameters.AddWithValue("$today", today);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(MapRecord(reader));
        }

        return records;
    }

    public async Task SetRecordStarredAsync(int id, bool isStarred)
    {
        var now = DateTime.Now.ToString("O");
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE WorkRecords
            SET IsStarred = $isStarred, UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$isStarred", isStarred ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<WorkRecord> AddRecordAsync(string date, string content)
    {
        var now = DateTime.Now;
        // CreatedAt must belong to the selected day; using DateTime.Now alone
        // would make past-day tasks only appear from "today" onward.
        var createdAt = ResolveCreatedAtForDate(date, now);
        var sortOrder = await GetNextSortOrderAsync(date);

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO WorkRecords (Date, Content, CreatedAt, UpdatedAt, SortOrder, IsCompleted, CompletedAt, IsStarred)
            VALUES ($date, $content, $createdAt, $updatedAt, $sortOrder, 0, NULL, 0);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$date", date);
        command.Parameters.AddWithValue("$content", content.Trim());
        command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$sortOrder", sortOrder);

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        return new WorkRecord
        {
            Id = id,
            Date = date,
            Content = content.Trim(),
            CreatedAt = createdAt,
            UpdatedAt = now,
            SortOrder = sortOrder,
            IsCompleted = false
        };
    }

    private static DateTime ResolveCreatedAtForDate(string date, DateTime now)
    {
        var dateOnly = DateTime.ParseExact(
            date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (dateOnly.Date == now.Date)
            return now;

        return dateOnly.Date.Add(now.TimeOfDay);
    }

    public async Task CompleteRecordAsync(int id, string completionDate)
    {
        var completedAt = DateTime.ParseExact(completionDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            .AddHours(12);
        var now = DateTime.Now;
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE WorkRecords
            SET IsCompleted = 1,
                CompletedAt = $completedAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$completedAt", completedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UncompleteRecordAsync(int id)
    {
        var now = DateTime.Now;
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE WorkRecords
            SET IsCompleted = 0,
                CompletedAt = NULL,
                UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateRecordContentAsync(int id, string content)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var now = DateTime.Now.ToString("O");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE WorkRecords
            SET Content = $content, UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$content", content.Trim());
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateRecordVersionAsync(int id, string? versionNumber, string? versionInfo)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var now = DateTime.Now.ToString("O");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE WorkRecords
            SET VersionNumber = $versionNumber, VersionInfo = $versionInfo, UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$versionNumber", string.IsNullOrWhiteSpace(versionNumber) ? DBNull.Value : versionNumber.Trim());
        command.Parameters.AddWithValue("$versionInfo", string.IsNullOrWhiteSpace(versionInfo) ? DBNull.Value : versionInfo.Trim());
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ReorderRecordsAsync(IReadOnlyList<int> orderedIds)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        for (var i = 0; i < orderedIds.Count; i++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE WorkRecords
                SET SortOrder = $sortOrder
                WHERE Id = $id
                """;
            command.Parameters.AddWithValue("$sortOrder", i);
            command.Parameters.AddWithValue("$id", orderedIds[i]);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task UpdateRecordAsync(WorkRecord record)
    {
        record.UpdatedAt = DateTime.Now;

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE WorkRecords
            SET Content = $content, UpdatedAt = $updatedAt, SortOrder = $sortOrder
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$content", record.Content);
        command.Parameters.AddWithValue("$updatedAt", record.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$sortOrder", record.SortOrder);
        command.Parameters.AddWithValue("$id", record.Id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteRecordAsync(int id)
    {
        await DeleteSubTasksForParentAsync(TaskParentType.WorkRecord, id);

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM WorkRecords WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<LongTermTask>> GetLongTermTasksAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var tasks = new List<LongTermTask>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Content, CreatedAt, UpdatedAt, SortOrder, IsArchived, CompletedAt
            FROM LongTermTasks
            ORDER BY IsArchived, SortOrder, CreatedAt
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tasks.Add(MapLongTermTask(reader));
        }

        return tasks;
    }

    public async Task<LongTermTask> AddLongTermTaskAsync(string content)
    {
        var now = DateTime.Now;
        var sortOrder = await GetNextLongTermTaskSortOrderAsync();

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LongTermTasks (Content, CreatedAt, UpdatedAt, SortOrder, IsArchived)
            VALUES ($content, $createdAt, $updatedAt, $sortOrder, 0);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$content", content.Trim());
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$sortOrder", sortOrder);

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        return new LongTermTask
        {
            Id = id,
            Content = content.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            SortOrder = sortOrder
        };
    }

    public async Task UpdateLongTermTaskContentAsync(int id, string content)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var now = DateTime.Now.ToString("O");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LongTermTasks
            SET Content = $content, UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$content", content.Trim());
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task ReorderLongTermTasksAsync(IReadOnlyList<int> orderedIds)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        for (var i = 0; i < orderedIds.Count; i++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE LongTermTasks
                SET SortOrder = $sortOrder
                WHERE Id = $id
                """;
            command.Parameters.AddWithValue("$sortOrder", i);
            command.Parameters.AddWithValue("$id", orderedIds[i]);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task DeleteLongTermTaskAsync(int id)
    {
        await DeleteSubTasksForParentAsync(TaskParentType.LongTermTask, id);

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM LongTermTasks WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task CompleteLongTermTaskAsync(int id)
    {
        var now = DateTime.Now;
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LongTermTasks
            SET IsArchived = 1,
                CompletedAt = $completedAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$completedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UncompleteLongTermTaskAsync(int id)
    {
        var now = DateTime.Now;
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LongTermTasks
            SET IsArchived = 0,
                CompletedAt = NULL,
                UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<LongTermTask>> GetArchivedLongTermTasksAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var tasks = new List<LongTermTask>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Content, CreatedAt, UpdatedAt, SortOrder, IsArchived, CompletedAt
            FROM LongTermTasks
            WHERE IsArchived = 1
            ORDER BY CompletedAt DESC, CreatedAt DESC
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tasks.Add(MapLongTermTask(reader));
        }

        return tasks;
    }

    public async Task<IReadOnlyList<WorkRecord>> GetCompletedWorkRecordsAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var records = new List<WorkRecord>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Date, Content, CreatedAt, UpdatedAt, SortOrder, IsCompleted, CompletedAt, IsStarred, VersionNumber, VersionInfo
            FROM WorkRecords
            WHERE IsCompleted = 1
            ORDER BY CompletedAt DESC, CreatedAt DESC
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            records.Add(MapRecord(reader));

        return records;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<TaskSubItem>>> GetSubTasksAsync(
        TaskParentType parentType, IEnumerable<int> parentIds)
    {
        var ids = parentIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, IReadOnlyList<TaskSubItem>>();

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var placeholders = string.Join(", ", ids.Select((_, i) => $"$id{i}"));
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, ParentType, ParentId, Content, IsCompleted, CreatedAt, UpdatedAt, SortOrder, VersionNumber, VersionInfo
            FROM TaskSubItems
            WHERE ParentType = $parentType AND ParentId IN ({placeholders})
            ORDER BY IsCompleted, SortOrder, CreatedAt
            """;
        command.Parameters.AddWithValue("$parentType", parentType.ToDbValue());
        for (var i = 0; i < ids.Count; i++)
            command.Parameters.AddWithValue($"$id{i}", ids[i]);

        var grouped = ids.ToDictionary(id => id, _ => (IReadOnlyList<TaskSubItem>)new List<TaskSubItem>());
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = MapSubTask(reader);
            ((List<TaskSubItem>)grouped[item.ParentId]).Add(item);
        }

        return grouped;
    }

    public async Task<TaskSubItem> AddSubTaskAsync(TaskParentType parentType, int parentId, string content)
    {
        var now = DateTime.Now;
        var sortOrder = await GetNextSubTaskSortOrderAsync(parentType, parentId);

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TaskSubItems (ParentType, ParentId, Content, IsCompleted, CreatedAt, UpdatedAt, SortOrder)
            VALUES ($parentType, $parentId, $content, 0, $createdAt, $updatedAt, $sortOrder);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$parentType", parentType.ToDbValue());
        command.Parameters.AddWithValue("$parentId", parentId);
        command.Parameters.AddWithValue("$content", content.Trim());
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$sortOrder", sortOrder);

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        return new TaskSubItem
        {
            Id = id,
            ParentType = parentType,
            ParentId = parentId,
            Content = content.Trim(),
            IsCompleted = false,
            CreatedAt = now,
            UpdatedAt = now,
            SortOrder = sortOrder
        };
    }

    public async Task UpdateSubTaskContentAsync(int id, string content)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var now = DateTime.Now.ToString("O");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TaskSubItems
            SET Content = $content, UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$content", content.Trim());
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateSubTaskVersionAsync(int id, string? versionNumber, string? versionInfo)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var now = DateTime.Now.ToString("O");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TaskSubItems
            SET VersionNumber = $versionNumber, VersionInfo = $versionInfo, UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$versionNumber", string.IsNullOrWhiteSpace(versionNumber) ? DBNull.Value : versionNumber.Trim());
        command.Parameters.AddWithValue("$versionInfo", string.IsNullOrWhiteSpace(versionInfo) ? DBNull.Value : versionInfo.Trim());
        command.Parameters.AddWithValue("$updatedAt", now);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task CompleteSubTaskAsync(int id)
    {
        var now = DateTime.Now;
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TaskSubItems
            SET IsCompleted = 1, UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UncompleteSubTaskAsync(int id)
    {
        var now = DateTime.Now;
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TaskSubItems
            SET IsCompleted = 0, UpdatedAt = $updatedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteSubTaskAsync(int id)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TaskSubItems WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteSubTasksForParentAsync(TaskParentType parentType, int parentId)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM TaskSubItems
            WHERE ParentType = $parentType AND ParentId = $parentId
            """;
        command.Parameters.AddWithValue("$parentType", parentType.ToDbValue());
        command.Parameters.AddWithValue("$parentId", parentId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<QuickButton>> GetQuickButtonsAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var buttons = new List<QuickButton>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Url, SortOrder, IsVisible
            FROM QuickButtons
            ORDER BY SortOrder
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            buttons.Add(new QuickButton
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Url = reader.GetString(2),
                SortOrder = reader.GetInt32(3),
                IsVisible = reader.GetInt32(4) == 1
            });
        }

        return buttons;
    }

    public async Task SaveQuickButtonsAsync(IEnumerable<QuickButton> buttons)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM QuickButtons";
            await delete.ExecuteNonQueryAsync();
        }

        var order = 0;
        foreach (var button in buttons)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO QuickButtons (Name, Url, SortOrder, IsVisible)
                VALUES ($name, $url, $sortOrder, $isVisible)
                """;
            insert.Parameters.AddWithValue("$name", button.Name);
            insert.Parameters.AddWithValue("$url", button.Url);
            insert.Parameters.AddWithValue("$sortOrder", order++);
            insert.Parameters.AddWithValue("$isVisible", button.IsVisible ? 1 : 0);
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<SubTaskTemplate>> GetSubTaskTemplatesAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var templates = new List<SubTaskTemplate>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Content, SortOrder
            FROM SubTaskTemplates
            ORDER BY SortOrder
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            templates.Add(new SubTaskTemplate
            {
                Id = reader.GetInt32(0),
                Content = reader.GetString(1),
                SortOrder = reader.GetInt32(2)
            });
        }

        return templates;
    }

    public async Task SaveSubTaskTemplatesAsync(IEnumerable<SubTaskTemplate> templates)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM SubTaskTemplates";
            await delete.ExecuteNonQueryAsync();
        }

        var order = 0;
        foreach (var template in templates)
        {
            var content = template.Content.Trim();
            if (string.IsNullOrEmpty(content)) continue;

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO SubTaskTemplates (Content, SortOrder)
                VALUES ($content, $sortOrder)
                """;
            insert.Parameters.AddWithValue("$content", content);
            insert.Parameters.AddWithValue("$sortOrder", order++);
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task ExportAllAsync(string filePath)
    {
        var payload = new ExportPayload
        {
            Records = await GetAllRecordsAsync(),
            LongTermTasks = await GetAllLongTermTasksAsync(),
            SubTasks = await GetAllSubTasksAsync(),
            QuickButtons = (await GetQuickButtonsAsync()).ToList(),
            SubTaskTemplates = (await GetSubTaskTemplatesAsync()).ToList()
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task ImportAllAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var payload = JsonSerializer.Deserialize<ExportPayload>(json, JsonOptions)
                      ?? throw new InvalidOperationException("导入文件格式无效。");

        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var deleteRecords = connection.CreateCommand())
        {
            deleteRecords.Transaction = transaction;
            deleteRecords.CommandText = "DELETE FROM WorkRecords";
            await deleteRecords.ExecuteNonQueryAsync();
        }

        await using (var deleteLongTerm = connection.CreateCommand())
        {
            deleteLongTerm.Transaction = transaction;
            deleteLongTerm.CommandText = "DELETE FROM LongTermTasks";
            await deleteLongTerm.ExecuteNonQueryAsync();
        }

        await using (var deleteSubTasks = connection.CreateCommand())
        {
            deleteSubTasks.Transaction = transaction;
            deleteSubTasks.CommandText = "DELETE FROM TaskSubItems";
            await deleteSubTasks.ExecuteNonQueryAsync();
        }

        foreach (var record in payload.Records)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO WorkRecords (Date, Content, CreatedAt, UpdatedAt, SortOrder, IsCompleted, CompletedAt, IsStarred)
                VALUES ($date, $content, $createdAt, $updatedAt, $sortOrder, $isCompleted, $completedAt, $isStarred)
                """;
            insert.Parameters.AddWithValue("$date", record.Date);
            insert.Parameters.AddWithValue("$content", record.Content);
            insert.Parameters.AddWithValue("$createdAt", record.CreatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$updatedAt", record.UpdatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$sortOrder", record.SortOrder);
            insert.Parameters.AddWithValue("$isCompleted", record.IsCompleted ? 1 : 0);
            insert.Parameters.AddWithValue("$completedAt",
                record.CompletedAt.HasValue ? record.CompletedAt.Value.ToString("O") : DBNull.Value);
            insert.Parameters.AddWithValue("$isStarred", record.IsStarred ? 1 : 0);
            await insert.ExecuteNonQueryAsync();
        }

        foreach (var task in payload.LongTermTasks ?? [])
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO WorkRecords (Date, Content, CreatedAt, UpdatedAt, SortOrder, IsCompleted, CompletedAt, IsStarred)
                VALUES ($date, $content, $createdAt, $updatedAt, $sortOrder, $isCompleted, $completedAt, 1)
                """;
            insert.Parameters.AddWithValue("$date", task.CreatedAt.ToString("yyyy-MM-dd"));
            insert.Parameters.AddWithValue("$content", task.Content);
            insert.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$updatedAt", task.UpdatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$sortOrder", task.SortOrder);
            insert.Parameters.AddWithValue("$isCompleted", task.IsArchived ? 1 : 0);
            insert.Parameters.AddWithValue("$completedAt",
                task.CompletedAt.HasValue ? task.CompletedAt.Value.ToString("O") : DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }

        foreach (var subTask in payload.SubTasks ?? [])
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO TaskSubItems (ParentType, ParentId, Content, IsCompleted, CreatedAt, UpdatedAt, SortOrder)
                VALUES ($parentType, $parentId, $content, $isCompleted, $createdAt, $updatedAt, $sortOrder)
                """;
            insert.Parameters.AddWithValue("$parentType", subTask.ParentType.ToDbValue());
            insert.Parameters.AddWithValue("$parentId", subTask.ParentId);
            insert.Parameters.AddWithValue("$content", subTask.Content);
            insert.Parameters.AddWithValue("$isCompleted", subTask.IsCompleted ? 1 : 0);
            insert.Parameters.AddWithValue("$createdAt", subTask.CreatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$updatedAt", subTask.UpdatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$sortOrder", subTask.SortOrder);
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        await SaveQuickButtonsAsync(payload.QuickButtons);
        await SaveSubTaskTemplatesAsync(payload.SubTaskTemplates ?? []);
    }

    public async Task BackupAsync(string filePath)
    {
        await ExportAllAsync(filePath);
    }

    private SqliteConnection CreateConnection() => new($"Data Source={_dbPath}");

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> GetNextSortOrderAsync(string date)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM WorkRecords WHERE Date = $date";
        command.Parameters.AddWithValue("$date", date);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> GetNextLongTermTaskSortOrderAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM LongTermTasks";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> GetNextSubTaskSortOrderAsync(TaskParentType parentType, int parentId)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(MAX(SortOrder), -1) + 1
            FROM TaskSubItems
            WHERE ParentType = $parentType AND ParentId = $parentId
            """;
        command.Parameters.AddWithValue("$parentType", parentType.ToDbValue());
        command.Parameters.AddWithValue("$parentId", parentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task SeedDefaultButtonsIfEmptyAsync(SqliteConnection connection)
    {
        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM QuickButtons";
        var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        if (count > 0) return;

        var defaults = new[]
        {
            ("Jira", "https://jira.example.com"),
            ("禅道", "https://zentao.example.com"),
            ("GitLab", "https://gitlab.example.com")
        };

        for (var i = 0; i < defaults.Length; i++)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO QuickButtons (Name, Url, SortOrder, IsVisible)
                VALUES ($name, $url, $sortOrder, 1)
                """;
            insert.Parameters.AddWithValue("$name", defaults[i].Item1);
            insert.Parameters.AddWithValue("$url", defaults[i].Item2);
            insert.Parameters.AddWithValue("$sortOrder", i);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private async Task<List<WorkRecord>> GetAllRecordsAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var records = new List<WorkRecord>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Date, Content, CreatedAt, UpdatedAt, SortOrder, IsCompleted, CompletedAt, IsStarred, VersionNumber, VersionInfo
            FROM WorkRecords
            ORDER BY Date, SortOrder, CreatedAt
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(MapRecord(reader));
        }

        return records;
    }

    private async Task<List<LongTermTask>> GetAllLongTermTasksAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var tasks = new List<LongTermTask>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Content, CreatedAt, UpdatedAt, SortOrder, IsArchived, CompletedAt
            FROM LongTermTasks
            ORDER BY IsArchived, SortOrder, CreatedAt
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tasks.Add(MapLongTermTask(reader));
        }

        return tasks;
    }

    private async Task<List<TaskSubItem>> GetAllSubTasksAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var items = new List<TaskSubItem>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ParentType, ParentId, Content, IsCompleted, CreatedAt, UpdatedAt, SortOrder, VersionNumber, VersionInfo
            FROM TaskSubItems
            ORDER BY ParentType, ParentId, SortOrder, CreatedAt
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            items.Add(MapSubTask(reader));

        return items;
    }

    private static async Task MigrateWorkRecordsTableAsync(SqliteConnection connection)
    {
        if (!await ColumnExistsAsync(connection, "WorkRecords", "IsCompleted"))
        {
            await ExecuteNonQueryAsync(connection,
                "ALTER TABLE WorkRecords ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 1;");
        }

        if (!await ColumnExistsAsync(connection, "WorkRecords", "CompletedAt"))
        {
            await ExecuteNonQueryAsync(connection,
                "ALTER TABLE WorkRecords ADD COLUMN CompletedAt TEXT;");
            await ExecuteNonQueryAsync(connection,
                "UPDATE WorkRecords SET CompletedAt = CreatedAt WHERE CompletedAt IS NULL;");
        }

        if (!await ColumnExistsAsync(connection, "WorkRecords", "IsStarred"))
        {
            await ExecuteNonQueryAsync(connection,
                "ALTER TABLE WorkRecords ADD COLUMN IsStarred INTEGER NOT NULL DEFAULT 0;");
        }
    }

    private static async Task MigrateVersionColumnsAsync(SqliteConnection connection)
    {
        foreach (var table in new[] { "WorkRecords", "TaskSubItems" })
        {
            if (!await ColumnExistsAsync(connection, table, "VersionNumber"))
            {
                await ExecuteNonQueryAsync(connection,
                    $"ALTER TABLE {table} ADD COLUMN VersionNumber TEXT;");
            }

            if (!await ColumnExistsAsync(connection, table, "VersionInfo"))
            {
                await ExecuteNonQueryAsync(connection,
                    $"ALTER TABLE {table} ADD COLUMN VersionInfo TEXT;");
            }
        }
    }

    /// <summary>
    /// Repair rows where Date and CreatedAt disagree (e.g. added while viewing a past day).
    /// </summary>
    private static async Task RepairMismatchedCreatedAtAsync(SqliteConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT Id, Date, CreatedAt
            FROM WorkRecords
            WHERE date(CreatedAt) != Date
            """;

        var fixes = new List<(int Id, string CreatedAt)>();
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var date = reader.GetString(1);
                var createdAt = DateTime.Parse(reader.GetString(2));
                var repaired = ResolveCreatedAtForDate(date, createdAt);
                fixes.Add((id, repaired.ToString("O")));
            }
        }

        foreach (var (id, createdAt) in fixes)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE WorkRecords
                SET CreatedAt = $createdAt
                WHERE Id = $id
                """;
            update.Parameters.AddWithValue("$createdAt", createdAt);
            update.Parameters.AddWithValue("$id", id);
            await update.ExecuteNonQueryAsync();
        }
    }

    private static async Task MigrateLongTermToStarredAsync(SqliteConnection connection)
    {
        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM LongTermTasks";
        var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        if (count == 0) return;

        var idMap = new Dictionary<int, int>();

        await using (var selectCmd = connection.CreateCommand())
        {
            selectCmd.CommandText = """
                SELECT Id, Content, CreatedAt, UpdatedAt, SortOrder, IsArchived, CompletedAt
                FROM LongTermTasks
                """;

            await using var reader = await selectCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var oldId = reader.GetInt32(0);
                var content = reader.GetString(1);
                var createdAt = reader.GetString(2);
                var updatedAt = reader.GetString(3);
                var sortOrder = reader.GetInt32(4);
                var isArchived = reader.GetInt32(5) == 1;
                var completedAt = reader.IsDBNull(6) ? null : reader.GetString(6);
                var date = DateTime.Parse(createdAt).ToString("yyyy-MM-dd");

                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO WorkRecords (Date, Content, CreatedAt, UpdatedAt, SortOrder, IsCompleted, CompletedAt, IsStarred)
                    VALUES ($date, $content, $createdAt, $updatedAt, $sortOrder, $isCompleted, $completedAt, 1);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("$date", date);
                insert.Parameters.AddWithValue("$content", content);
                insert.Parameters.AddWithValue("$createdAt", createdAt);
                insert.Parameters.AddWithValue("$updatedAt", updatedAt);
                insert.Parameters.AddWithValue("$sortOrder", sortOrder);
                insert.Parameters.AddWithValue("$isCompleted", isArchived ? 1 : 0);
                insert.Parameters.AddWithValue("$completedAt", completedAt is null ? DBNull.Value : completedAt);

                var newId = Convert.ToInt32(await insert.ExecuteScalarAsync());
                idMap[oldId] = newId;
            }
        }

        foreach (var (oldId, newId) in idMap)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE TaskSubItems
                SET ParentType = 'WorkRecord', ParentId = $newId
                WHERE ParentType = 'LongTermTask' AND ParentId = $oldId
                """;
            update.Parameters.AddWithValue("$newId", newId);
            update.Parameters.AddWithValue("$oldId", oldId);
            await update.ExecuteNonQueryAsync();
        }

        await ExecuteNonQueryAsync(connection, "DELETE FROM LongTermTasks");
    }

    private static async Task MigrateLongTermTasksTableAsync(SqliteConnection connection)
    {
        if (!await ColumnExistsAsync(connection, "LongTermTasks", "IsArchived"))
        {
            await ExecuteNonQueryAsync(connection,
                "ALTER TABLE LongTermTasks ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;");
        }

        if (!await ColumnExistsAsync(connection, "LongTermTasks", "CompletedAt"))
        {
            await ExecuteNonQueryAsync(connection,
                "ALTER TABLE LongTermTasks ADD COLUMN CompletedAt TEXT;");
        }
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static LongTermTask MapLongTermTask(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Content = reader.GetString(1),
        CreatedAt = DateTime.Parse(reader.GetString(2)),
        UpdatedAt = DateTime.Parse(reader.GetString(3)),
        SortOrder = reader.GetInt32(4),
        IsArchived = reader.GetInt32(5) == 1,
        CompletedAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6))
    };

    private static WorkRecord MapRecord(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Date = reader.GetString(1),
        Content = reader.GetString(2),
        CreatedAt = DateTime.Parse(reader.GetString(3)),
        UpdatedAt = DateTime.Parse(reader.GetString(4)),
        SortOrder = reader.GetInt32(5),
        IsCompleted = reader.GetInt32(6) == 1,
        CompletedAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7)),
        IsStarred = reader.FieldCount > 8 && !reader.IsDBNull(8) && reader.GetInt32(8) == 1,
        VersionNumber = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : null,
        VersionInfo = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : null
    };

    private static TaskSubItem MapSubTask(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        ParentType = TaskParentTypeExtensions.FromDbValue(reader.GetString(1)),
        ParentId = reader.GetInt32(2),
        Content = reader.GetString(3),
        IsCompleted = reader.GetInt32(4) == 1,
        CreatedAt = DateTime.Parse(reader.GetString(5)),
        UpdatedAt = DateTime.Parse(reader.GetString(6)),
        SortOrder = reader.GetInt32(7),
        VersionNumber = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null,
        VersionInfo = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetString(9) : null
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class ExportPayload
    {
        public List<WorkRecord> Records { get; set; } = [];
        public List<LongTermTask> LongTermTasks { get; set; } = [];
        public List<TaskSubItem> SubTasks { get; set; } = [];
        public List<QuickButton> QuickButtons { get; set; } = [];
        public List<SubTaskTemplate> SubTaskTemplates { get; set; } = [];
    }
}
