using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace BuildServer.Core;

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
                FailedProjects INTEGER NOT NULL,
                LogContent TEXT
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
                LogContent TEXT,
                FOREIGN KEY (BuildRecordId) REFERENCES BuildRecords(Id)
            );

            CREATE TABLE IF NOT EXISTS ServerSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT
            );

            CREATE TABLE IF NOT EXISTS Projects (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL,
                GroupName TEXT,
                NodeModulesDir TEXT,
                BuildCmd TEXT,
                DistDir TEXT,
                IsPublish INTEGER NOT NULL DEFAULT 1,
                NodeVersion TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_build_time ON BuildRecords(BuildTime);
            CREATE INDEX IF NOT EXISTS idx_build_success ON BuildRecords(Success);
            CREATE INDEX IF NOT EXISTS idx_project_build_record ON ProjectBuildRecords(BuildRecordId);
        ";
        command.ExecuteNonQuery();

        // Migration: Check if LogContent exists in BuildRecords, if not add it
        try {
            var migCmd = connection.CreateCommand();
            migCmd.CommandText = "SELECT LogContent FROM BuildRecords LIMIT 1";
            migCmd.ExecuteNonQuery();
        } catch {
            var addColCmd = connection.CreateCommand();
            addColCmd.CommandText = "ALTER TABLE BuildRecords ADD COLUMN LogContent TEXT";
            addColCmd.ExecuteNonQuery();
        }

        // Migration: Check if LogContent exists in ProjectBuildRecords, if not add it
        try {
            var migCmd2 = connection.CreateCommand();
            migCmd2.CommandText = "SELECT LogContent FROM ProjectBuildRecords LIMIT 1";
            migCmd2.ExecuteNonQuery();
        } catch {
            var addColCmd2 = connection.CreateCommand();
            addColCmd2.CommandText = "ALTER TABLE ProjectBuildRecords ADD COLUMN LogContent TEXT";
            addColCmd2.ExecuteNonQuery();
        }
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
    /// Update build record with final status including log content
    /// </summary>
    public void UpdateBuildRecord(long buildRecordId, bool success, long durationMs, 
        string? logFilePath, string? errorLogFilePath, int successfulProjects, int failedProjects, string? logContent)
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
                FailedProjects = $failedProjects,
                LogContent = $logContent
            WHERE Id = $id
        ";
        command.Parameters.AddWithValue("$id", buildRecordId);
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$duration", durationMs);
        command.Parameters.AddWithValue("$logFilePath", logFilePath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$errorLogFilePath", errorLogFilePath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$successfulProjects", successfulProjects);
        command.Parameters.AddWithValue("$failedProjects", failedProjects);
        command.Parameters.AddWithValue("$logContent", logContent ?? (object)DBNull.Value);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Add a project build record with log content
    /// </summary>
    public void AddProjectBuildRecord(long buildRecordId, string projectName, string projectPath,
        bool success, int exitCode, string buildCommand, string? errorMessage, string? nodeVersion, string? logContent)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ProjectBuildRecords 
            (BuildRecordId, ProjectName, ProjectPath, Success, ExitCode, BuildCommand, ErrorMessage, NodeVersion, LogContent)
            VALUES ($buildRecordId, $projectName, $projectPath, $success, $exitCode, $buildCommand, $errorMessage, $nodeVersion, $logContent)
        ";
        command.Parameters.AddWithValue("$buildRecordId", buildRecordId);
        command.Parameters.AddWithValue("$projectName", projectName);
        command.Parameters.AddWithValue("$projectPath", projectPath);
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$exitCode", exitCode);
        command.Parameters.AddWithValue("$buildCommand", buildCommand);
        command.Parameters.AddWithValue("$errorMessage", errorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$nodeVersion", nodeVersion ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$logContent", logContent ?? (object)DBNull.Value);

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Get recent build records (metadata only)
    /// </summary>
    public List<BuildRecordInfo> GetRecentBuilds(int limit = 50)
    {
        var records = new List<BuildRecordInfo>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        // Exclude LogContent for performance in list view
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
    /// Get project build records for a specific build (metadata only)
    /// </summary>
    public List<ProjectBuildRecordInfo> GetProjectBuildRecords(long buildRecordId)
    {
        var records = new List<ProjectBuildRecordInfo>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        // Exclude LogContent for performance
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
    /// Get full log content for a build
    /// </summary>
    public string? GetBuildLogContent(long buildRecordId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT LogContent FROM BuildRecords WHERE Id = $id";
        command.Parameters.AddWithValue("$id", buildRecordId);
        var result = command.ExecuteScalar();
        return result is DBNull ? null : result as string;
    }

    /// <summary>
    /// Get full log content for a project build
    /// </summary>
    public string? GetProjectBuildLogContent(long projectBuildRecordId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT LogContent FROM ProjectBuildRecords WHERE Id = $id";
        command.Parameters.AddWithValue("$id", projectBuildRecordId);
        var result = command.ExecuteScalar();
        return result is DBNull ? null : result as string;
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

    // --- Configuration Configuration Methods ---

    public ServerConfig? LoadServerConfig()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Check if settings exist
        var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM ServerSettings";
        var count = (long)checkCmd.ExecuteScalar();

        if (count == 0) return null;

        var config = new ServerConfig();
        
        // Load Settings
        var settingsCmd = connection.CreateCommand();
        settingsCmd.CommandText = "SELECT Key, Value FROM ServerSettings";
        using (var reader = settingsCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                string key = reader.GetString(0);
                string value = reader.GetString(1);

                switch (key)
                {
                    case "Port": config.Port = int.Parse(value); break;
                    case "SvnRoot": config.SvnRoot = value; break;
                    case "OutputDir": config.OutputDir = value; break;
                    case "NvmRoot": config.NvmRoot = value; break;
                }
            }
        }

        // Load Projects
        var projectsCmd = connection.CreateCommand();
        projectsCmd.CommandText = "SELECT Name, Path, GroupName, NodeModulesDir, BuildCmd, DistDir, IsPublish, NodeVersion FROM Projects";
        using (var reader = projectsCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                config.Projects.Add(new ProjectConfig
                {
                    Name = reader.GetString(0),
                    Path = reader.GetString(1),
                    Group = reader.IsDBNull(2) ? null : reader.GetString(2),
                    NodeModulesDir = reader.IsDBNull(3) ? null : reader.GetString(3),
                    BuildCmd = reader.IsDBNull(4) ? null : reader.GetString(4),
                    DistDir = reader.IsDBNull(5) ? null : reader.GetString(5),
                    IsPublish = reader.GetInt32(6) == 1,
                    NodeVersion = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }
        }

        return config;
    }

    public void SaveServerConfig(ServerConfig config)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Save Settings
            var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT OR REPLACE INTO ServerSettings (Key, Value) VALUES ('Port', $port);
                INSERT OR REPLACE INTO ServerSettings (Key, Value) VALUES ('SvnRoot', $svnRoot);
                INSERT OR REPLACE INTO ServerSettings (Key, Value) VALUES ('OutputDir', $outputDir);
                INSERT OR REPLACE INTO ServerSettings (Key, Value) VALUES ('NvmRoot', $nvmRoot);
            ";
            cmd.Parameters.AddWithValue("$port", config.Port.ToString());
            cmd.Parameters.AddWithValue("$svnRoot", config.SvnRoot ?? "");
            cmd.Parameters.AddWithValue("$outputDir", config.OutputDir ?? "");
            cmd.Parameters.AddWithValue("$nvmRoot", config.NvmRoot ?? "");
            cmd.ExecuteNonQuery();

            // Save Projects (Full Replace for simplicity)
            var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM Projects";
            delCmd.ExecuteNonQuery();

            foreach (var p in config.Projects)
            {
                var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT INTO Projects (Name, Path, GroupName, NodeModulesDir, BuildCmd, DistDir, IsPublish, NodeVersion)
                    VALUES ($name, $path, $group, $nodeModules, $buildCmd, $distDir, $isPublish, $nodeVersion)
                ";
                insertCmd.Parameters.AddWithValue("$name", p.Name);
                insertCmd.Parameters.AddWithValue("$path", p.Path);
                insertCmd.Parameters.AddWithValue("$group", p.Group ?? (object)DBNull.Value);
                insertCmd.Parameters.AddWithValue("$nodeModules", p.NodeModulesDir ?? (object)DBNull.Value);
                insertCmd.Parameters.AddWithValue("$buildCmd", p.BuildCmd ?? (object)DBNull.Value);
                insertCmd.Parameters.AddWithValue("$distDir", p.DistDir ?? (object)DBNull.Value);
                insertCmd.Parameters.AddWithValue("$isPublish", p.IsPublish ? 1 : 0);
                insertCmd.Parameters.AddWithValue("$nodeVersion", p.NodeVersion ?? (object)DBNull.Value);
                insertCmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
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
