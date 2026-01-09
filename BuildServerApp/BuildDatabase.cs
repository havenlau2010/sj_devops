using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace BuildServerApp;

/// <summary>
/// Database service for storing build records and compilation status
/// </summary>
public class BuildDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    public BuildDatabase(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "builds.db");
        _connectionString = $"Data Source={_dbPath}";
        InitializeDatabase();
    }

    /// <summary>
    /// Initialize database and create tables if they don't exist
    /// </summary>
    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS BuildRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BuildTime TEXT NOT NULL,
                Success INTEGER NOT NULL,
                Duration INTEGER NOT NULL,
                LogFilePath TEXT,
                ErrorLogFilePath TEXT,
                TotalProjects INTEGER NOT NULL,
                SuccessfulProjects INTEGER NOT NULL,
                FailedProjects INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ProjectBuildRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BuildRecordId INTEGER NOT NULL,
                ProjectName TEXT NOT NULL,
                ProjectPath TEXT NOT NULL,
                Success INTEGER NOT NULL,
                ExitCode INTEGER NOT NULL,
                BuildCommand TEXT NOT NULL,
                ErrorMessage TEXT,
                NodeVersion TEXT,
                FOREIGN KEY (BuildRecordId) REFERENCES BuildRecords(Id)
            );

            CREATE INDEX IF NOT EXISTS idx_build_time ON BuildRecords(BuildTime);
            CREATE INDEX IF NOT EXISTS idx_build_success ON BuildRecords(Success);
            CREATE INDEX IF NOT EXISTS idx_project_build_record ON ProjectBuildRecords(BuildRecordId);
        ";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Create a new build record and return its ID
    /// </summary>
    public long CreateBuildRecord(DateTime buildTime, int totalProjects)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO BuildRecords (BuildTime, Success, Duration, TotalProjects, SuccessfulProjects, FailedProjects)
            VALUES ($buildTime, 0, 0, $totalProjects, 0, 0);
            SELECT last_insert_rowid();
        ";
        command.Parameters.AddWithValue("$buildTime", buildTime.ToString("O")); // ISO 8601 format
        command.Parameters.AddWithValue("$totalProjects", totalProjects);

        var result = command.ExecuteScalar();
        return (long)result;
    }

    /// <summary>
    /// Update build record with final status
    /// </summary>
    public void UpdateBuildRecord(long buildRecordId, bool success, long durationMs, 
        string? logFilePath, string? errorLogFilePath, int successfulProjects, int failedProjects)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE BuildRecords 
            SET Success = $success,
                Duration = $duration,
                LogFilePath = $logFilePath,
                ErrorLogFilePath = $errorLogFilePath,
                SuccessfulProjects = $successfulProjects,
                FailedProjects = $failedProjects
            WHERE Id = $id
        ";
        command.Parameters.AddWithValue("$id", buildRecordId);
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$duration", durationMs);
        command.Parameters.AddWithValue("$logFilePath", logFilePath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$errorLogFilePath", errorLogFilePath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$successfulProjects", successfulProjects);
        command.Parameters.AddWithValue("$failedProjects", failedProjects);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Add a project build record
    /// </summary>
    public void AddProjectBuildRecord(long buildRecordId, string projectName, string projectPath,
        bool success, int exitCode, string buildCommand, string? errorMessage, string? nodeVersion)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ProjectBuildRecords 
            (BuildRecordId, ProjectName, ProjectPath, Success, ExitCode, BuildCommand, ErrorMessage, NodeVersion)
            VALUES ($buildRecordId, $projectName, $projectPath, $success, $exitCode, $buildCommand, $errorMessage, $nodeVersion)
        ";
        command.Parameters.AddWithValue("$buildRecordId", buildRecordId);
        command.Parameters.AddWithValue("$projectName", projectName);
        command.Parameters.AddWithValue("$projectPath", projectPath);
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$exitCode", exitCode);
        command.Parameters.AddWithValue("$buildCommand", buildCommand);
        command.Parameters.AddWithValue("$errorMessage", errorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$nodeVersion", nodeVersion ?? (object)DBNull.Value);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Get recent build records
    /// </summary>
    public List<BuildRecordInfo> GetRecentBuilds(int limit = 50)
    {
        var records = new List<BuildRecordInfo>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, BuildTime, Success, Duration, LogFilePath, ErrorLogFilePath, 
                   TotalProjects, SuccessfulProjects, FailedProjects
            FROM BuildRecords
            ORDER BY BuildTime DESC
            LIMIT $limit
        ";
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new BuildRecordInfo
            {
                Id = reader.GetInt64(0),
                BuildTime = DateTime.Parse(reader.GetString(1)),
                Success = reader.GetInt32(2) == 1,
                Duration = reader.GetInt64(3),
                LogFilePath = reader.IsDBNull(4) ? null : reader.GetString(4),
                ErrorLogFilePath = reader.IsDBNull(5) ? null : reader.GetString(5),
                TotalProjects = reader.GetInt32(6),
                SuccessfulProjects = reader.GetInt32(7),
                FailedProjects = reader.GetInt32(8)
            });
        }

        return records;
    }

    /// <summary>
    /// Get project build records for a specific build
    /// </summary>
    public List<ProjectBuildRecordInfo> GetProjectBuildRecords(long buildRecordId)
    {
        var records = new List<ProjectBuildRecordInfo>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, ProjectName, ProjectPath, Success, ExitCode, BuildCommand, ErrorMessage, NodeVersion
            FROM ProjectBuildRecords
            WHERE BuildRecordId = $buildRecordId
            ORDER BY Id
        ";
        command.Parameters.AddWithValue("$buildRecordId", buildRecordId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new ProjectBuildRecordInfo
            {
                Id = reader.GetInt64(0),
                ProjectName = reader.GetString(1),
                ProjectPath = reader.GetString(2),
                Success = reader.GetInt32(3) == 1,
                ExitCode = reader.GetInt32(4),
                BuildCommand = reader.GetString(5),
                ErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
                NodeVersion = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return records;
    }

    /// <summary>
    /// Get build statistics
    /// </summary>
    public BuildStatistics GetStatistics()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                COUNT(*) as TotalBuilds,
                SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) as SuccessfulBuilds,
                SUM(CASE WHEN Success = 0 THEN 1 ELSE 0 END) as FailedBuilds,
                AVG(Duration) as AverageDuration
            FROM BuildRecords
        ";

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new BuildStatistics
            {
                TotalBuilds = reader.GetInt32(0),
                SuccessfulBuilds = reader.GetInt32(1),
                FailedBuilds = reader.GetInt32(2),
                AverageDuration = reader.IsDBNull(3) ? 0 : reader.GetDouble(3)
            };
        }

        return new BuildStatistics();
    }

    public void Dispose()
    {
        // SQLite connections are disposed in using statements
        // This method is here for future cleanup if needed
    }
}

/// <summary>
/// Build record information
/// </summary>
public class BuildRecordInfo
{
    public long Id { get; set; }
    public DateTime BuildTime { get; set; }
    public bool Success { get; set; }
    public long Duration { get; set; }
    public string? LogFilePath { get; set; }
    public string? ErrorLogFilePath { get; set; }
    public int TotalProjects { get; set; }
    public int SuccessfulProjects { get; set; }
    public int FailedProjects { get; set; }
}

/// <summary>
/// Project build record information
/// </summary>
public class ProjectBuildRecordInfo
{
    public long Id { get; set; }
    public string ProjectName { get; set; }
    public string ProjectPath { get; set; }
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string BuildCommand { get; set; }
    public string? ErrorMessage { get; set; }
    public string? NodeVersion { get; set; }
}

/// <summary>
/// Build statistics
/// </summary>
public class BuildStatistics
{
    public int TotalBuilds { get; set; }
    public int SuccessfulBuilds { get; set; }
    public int FailedBuilds { get; set; }
    public double AverageDuration { get; set; }
}
