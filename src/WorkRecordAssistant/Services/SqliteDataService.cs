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

        await SeedDefaultButtonsIfEmptyAsync(connection);
    }

    public async Task<IReadOnlyList<WorkRecord>> GetRecordsAsync(string date)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        var records = new List<WorkRecord>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Date, Content, CreatedAt, UpdatedAt, SortOrder
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

    public async Task<WorkRecord> AddRecordAsync(string date, string content)
    {
        var now = DateTime.Now;
        var sortOrder = await GetNextSortOrderAsync(date);

        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO WorkRecords (Date, Content, CreatedAt, UpdatedAt, SortOrder)
            VALUES ($date, $content, $createdAt, $updatedAt, $sortOrder);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$date", date);
        command.Parameters.AddWithValue("$content", content.Trim());
        command.Parameters.AddWithValue("$createdAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$sortOrder", sortOrder);

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        return new WorkRecord
        {
            Id = id,
            Date = date,
            Content = content.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            SortOrder = sortOrder
        };
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

    public async Task ReorderRecordsAsync(string date, IReadOnlyList<int> orderedIds)
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
                WHERE Id = $id AND Date = $date
                """;
            command.Parameters.AddWithValue("$sortOrder", i);
            command.Parameters.AddWithValue("$id", orderedIds[i]);
            command.Parameters.AddWithValue("$date", date);
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
            WHERE IsArchived = 0
            ORDER BY SortOrder, CreatedAt
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

    public async Task ExportAllAsync(string filePath)
    {
        var payload = new ExportPayload
        {
            Records = await GetAllRecordsAsync(),
            LongTermTasks = await GetAllLongTermTasksAsync(),
            QuickButtons = (await GetQuickButtonsAsync()).ToList()
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

        foreach (var record in payload.Records)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO WorkRecords (Date, Content, CreatedAt, UpdatedAt, SortOrder)
                VALUES ($date, $content, $createdAt, $updatedAt, $sortOrder)
                """;
            insert.Parameters.AddWithValue("$date", record.Date);
            insert.Parameters.AddWithValue("$content", record.Content);
            insert.Parameters.AddWithValue("$createdAt", record.CreatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$updatedAt", record.UpdatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$sortOrder", record.SortOrder);
            await insert.ExecuteNonQueryAsync();
        }

        foreach (var task in payload.LongTermTasks ?? [])
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO LongTermTasks (Content, CreatedAt, UpdatedAt, SortOrder, IsArchived, CompletedAt)
                VALUES ($content, $createdAt, $updatedAt, $sortOrder, $isArchived, $completedAt)
                """;
            insert.Parameters.AddWithValue("$content", task.Content);
            insert.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$updatedAt", task.UpdatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$sortOrder", task.SortOrder);
            insert.Parameters.AddWithValue("$isArchived", task.IsArchived ? 1 : 0);
            insert.Parameters.AddWithValue("$completedAt",
                task.CompletedAt.HasValue ? task.CompletedAt.Value.ToString("O") : DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        await SaveQuickButtonsAsync(payload.QuickButtons);
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
        command.CommandText = "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM LongTermTasks WHERE IsArchived = 0";
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
            SELECT Id, Date, Content, CreatedAt, UpdatedAt, SortOrder
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
        SortOrder = reader.GetInt32(5)
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
        public List<QuickButton> QuickButtons { get; set; } = [];
    }
}
