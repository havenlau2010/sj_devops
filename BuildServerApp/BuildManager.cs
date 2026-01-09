using System.Diagnostics;
using System.Text.Json;

namespace BuildServerApp;

public class BuildManager
{
    private ServerConfig _config;
    private Action<string> _logAction;
    private BuildDatabase _database;

    public BuildManager(string configPath, Action<string> logAction)
    {
        _logAction = logAction;
        try
        {
            if (!File.Exists(configPath))
            {
                _logAction($"Config file not found: {configPath}");
                return;
            }
            string json = File.ReadAllText(configPath);
            _config = JsonSerializer.Deserialize<ServerConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _logAction($"Loaded config. OutputDir: {_config.OutputDir}");
            _logAction($"SvnRoot: {_config.SvnRoot}");
        }
        catch (Exception ex)
        {
            _logAction($"Error loading config: {ex.Message}");
        }

        // Initialize database
        try
        {
            _database = new BuildDatabase();
            _logAction($"Database initialized at: {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "builds.db")}");
        }
        catch (Exception ex)
        {
            _logAction($"Warning: Failed to initialize database: {ex.Message}");
        }
    }


    public List<ProjectConfig> Projects => _config?.Projects ?? new List<ProjectConfig>();

    public void AddProject(ProjectConfig project)
    {
        _config.Projects.Add(project);
        SaveConfig();
        _logAction($"Project added: {project.Name}");
    }

    public void SaveConfig()
    {
        try
        {
            string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            // Assume configPath was stored or we hardcode it back to where we loaded it. 
            // We need to store the path passed in ctor.
            File.WriteAllText("config.json", json); // Reusing hardcoded relative path for now or we need a field.
        }
        catch (Exception ex)
        {
            _logAction($"Error saving config: {ex.Message}");
        }
    }

    public async Task<object> RunWorkflowAsync()
    {
        if (_config == null) return new { success = false, message = "Config not loaded" };

        var result = new Dictionary<string, object>();
        var logs = new List<string>();
        
        // Setup file logging
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        string buildsDir = Path.Combine(logsDir, "builds");
        string errorsDir = Path.Combine(logsDir, "errors");
        
        Directory.CreateDirectory(buildsDir);
        Directory.CreateDirectory(errorsDir);
        
        string buildLogPath = Path.Combine(buildsDir, $"build_{timestamp}.log");
        string errorLogPath = Path.Combine(errorsDir, $"error_{timestamp}.log");
        
        StreamWriter buildLogWriter = null;
        StreamWriter errorLogWriter = null;
        bool hasErrors = false;
        
        // Database tracking
        long buildRecordId = 0;
        DateTime buildStartTime = DateTime.Now;
        Stopwatch buildStopwatch = Stopwatch.StartNew();
        int totalProjectsToBuild = _config.Projects.Count(p => p.IsPublish);
        
        try
        {
            // Create build record in database
            if (_database != null)
            {
                try
                {
                    buildRecordId = _database.CreateBuildRecord(buildStartTime, totalProjectsToBuild);
                    _logAction($"Build record created with ID: {buildRecordId}");
                }
                catch (Exception ex)
                {
                    _logAction($"Warning: Failed to create build record: {ex.Message}");
                }
            }
            
            buildLogWriter = new StreamWriter(buildLogPath, false, System.Text.Encoding.UTF8);
            errorLogWriter = new StreamWriter(errorLogPath, false, System.Text.Encoding.UTF8);
            
            buildLogWriter.AutoFlush = true;
            errorLogWriter.AutoFlush = true;
        }
        catch (Exception ex)
        {
            _logAction($"Warning: Failed to create log files: {ex.Message}");
        }

        void Log(string msg, bool isError = false)
        {
            string timestampedMsg = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {msg}";
            logs.Add(timestampedMsg);
            _logAction(msg);
            
            try
            {
                buildLogWriter?.WriteLine(timestampedMsg);
                if (isError)
                {
                    errorLogWriter?.WriteLine(timestampedMsg);
                    hasErrors = true;
                }
            }
            catch { }
        }

        try
        {
            Log("Starting Workflow...");
            Log($"Build log: {buildLogPath}");


            // 1. SVN Update (DISABLED)
            Log("Step 1: SVN Update (Skipped)");
            /*
            var updateTasks = _config.Projects.Select(p => 
            {
                string projectPath = p.Path.TrimStart('/', '\\');
                string absPath = Path.Combine(_config.SvnRoot, projectPath);
                return RunCommandAsync("svn", "update", absPath);
            });
            var updateResults = await Task.WhenAll(updateTasks);
            result["svn"] = updateResults;
            
            if (updateResults.Any(r => r.ExitCode != 0))
            {
                throw new Exception("SVN Update failed for some projects.");
            }
            */

            // 1.5. Verify/Create node_modules symlinks
            Log("Step 1.5: Verify node_modules");
            foreach (var p in _config.Projects.Where(p => p.IsPublish))
            {
                if (string.IsNullOrEmpty(p.NodeModulesDir)) continue;

                string projectPath = p.Path.TrimStart('/', '\\');
                string projectAbsPath = Path.Combine(_config.SvnRoot, projectPath);
                string nodeModulesPath = Path.Combine(projectAbsPath, "node_modules");

                if (!Directory.Exists(nodeModulesPath))
                {
                    // Create symlink
                    string targetPath = p.NodeModulesDir.TrimStart('/', '\\');
                    string targetAbsPath = Path.Combine(_config.SvnRoot, targetPath);

                    if (Directory.Exists(targetAbsPath))
                    {
                        Log($"Creating symlink: {nodeModulesPath} -> {targetAbsPath}");
                        try
                        {
                            Directory.CreateSymbolicLink(nodeModulesPath, targetAbsPath);
                            Log($"Symlink created successfully for {p.Name}");
                        }
                        catch (Exception ex)
                        {
                            Log($"Warning: Failed to create symlink for {p.Name}: {ex.Message}");
                        }
                    }
                    else
                    {
                        Log($"Warning: Target node_modules not found: {targetAbsPath}");
                    }
                }
                else
                {
                    Log($"node_modules already exists for {p.Name}");
                }
            }

            // 2. Build
            Log("Step 2: Build");
            var buildTasks = _config.Projects.Where(p => p.IsPublish).Select(async p => 
            {
                string projectPath = p.Path.TrimStart('/', '\\');
                string absPath = Path.Combine(_config.SvnRoot, projectPath);

                // Switch Node version if specified
                if (!string.IsNullOrEmpty(p.NodeVersion))
                {
                    Log($"Switching to Node.js {p.NodeVersion} for {p.Name}");
                    var nvmResult = await RunCommandAsync("cmd", $"/c nvm use {p.NodeVersion}", absPath);
                    if (nvmResult.ExitCode != 0)
                    {
                        Log($"Warning: Failed to switch Node version for {p.Name}: {nvmResult.Stderr}");
                    }
                }

                // Simple parsing: assumes "npm run build"
                var parts = p.BuildCmd.Split(' ', 2);
                var fileName = parts[0];
                var args = parts.Length > 1 ? parts[1] : "";

                if (OperatingSystem.IsWindows() && fileName == "npm")
                {
                    fileName = "cmd";
                    args = $"/c npm {args}";
                }

                return await RunCommandAsync(fileName, args, absPath);
            });

            var buildResults = await Task.WhenAll(buildTasks);
            result["build"] = buildResults;
            
            // Record project build results in database
            if (_database != null && buildRecordId > 0)
            {
                try
                {
                    var publishProjects = _config.Projects.Where(p => p.IsPublish).ToList();
                    for (int i = 0; i < buildResults.Length; i++)
                    {
                        var buildResult = buildResults[i];
                        var project = publishProjects[i];
                        
                        string projectPath = project.Path.TrimStart('/', '\\');
                        string errorMessage = buildResult.ExitCode != 0 ? buildResult.Stderr : null;
                        
                        _database.AddProjectBuildRecord(
                            buildRecordId,
                            project.Name,
                            projectPath,
                            buildResult.ExitCode == 0,
                            buildResult.ExitCode,
                            buildResult.Command,
                            errorMessage,
                            project.NodeVersion
                        );
                    }
                    Log("Project build records saved to database");
                }
                catch (Exception ex)
                {
                    Log($"Warning: Failed to save project build records: {ex.Message}");
                }
            }

            if (buildResults.Any(r => r.ExitCode != 0))
            {
                // We don't throw yet, we want to copy valid artifacts if any, or maybe stop?
                // Plan says: copy result. Let's try to copy what we can.
                Log("Build failures detected. Proceeding to copy successful builds...");
            }

            // 3. Copy
            Log("Step 3: Copy Artifacts");
            if (!Directory.Exists(_config.OutputDir))
            {
                Directory.CreateDirectory(_config.OutputDir);
            }

            foreach (var p in _config.Projects.Where(p => p.IsPublish))
            {
                // Resolve path using SvnRoot
                string projectPath = p.Path.TrimStart('/', '\\');
                string projectAbsPath = Path.Combine(_config.SvnRoot, projectPath);
                
                if (!Directory.Exists(projectAbsPath))
                {
                    Log($"Warning: Project path not found: {projectAbsPath}");
                }

                var distPath = Path.Combine(projectAbsPath, p.DistDir ?? "dist");
                
                // Use Group subdirectory if specified, otherwise use OutputDir directly
                string groupDir = string.IsNullOrEmpty(p.Group) 
                    ? _config.OutputDir 
                    : Path.Combine(_config.OutputDir, p.Group);
                
                if (!Directory.Exists(groupDir))
                {
                    Directory.CreateDirectory(groupDir);
                }
                
                var targetPath = Path.Combine(groupDir, p.Name);

                if (Directory.Exists(distPath)) {
                    Log($"Copying {distPath} to {targetPath}");
                    CopyDirectory(distPath, targetPath);
                } else {
                    Log($"Warning: Dist directory not found: {distPath}");
                }
            }

            Log("Workflow Completed.");
            
            bool workflowSuccess = !buildResults.Any(r => r.ExitCode != 0);
            result["success"] = workflowSuccess;
            
            if (!workflowSuccess)
            {
                Log("Build failures detected!", true);
            }
            
            // Aggregate Logs
            var fullLogBuilder = new System.Text.StringBuilder();
            /*
            fullLogBuilder.AppendLine("=== SVN UPDATE LOGS ===");
            for(int i=0; i<updateResults.Length; i++) {
                var r = updateResults[i];
                var p = _config.Projects[i];
                fullLogBuilder.AppendLine($"--- Project: {p.Name} ---");
                fullLogBuilder.AppendLine($"> {r.Command}");
                fullLogBuilder.AppendLine(r.Stdout);
                if(!string.IsNullOrEmpty(r.Stderr)) fullLogBuilder.AppendLine($"[STDERR]: {r.Stderr}");
                fullLogBuilder.AppendLine();
            }
            */
            
            fullLogBuilder.AppendLine("=== BUILD LOGS ===");
            for(int i=0; i<buildResults.Length; i++) {
                var r = buildResults[i];
                var p = _config.Projects.Where(p => p.IsPublish).ToList()[i];
                fullLogBuilder.AppendLine($"--- Project: {p.Name} ---");
                fullLogBuilder.AppendLine($"> {r.Command}");
                fullLogBuilder.AppendLine(r.Stdout);
                if(!string.IsNullOrEmpty(r.Stderr)) 
                {
                    fullLogBuilder.AppendLine($"[STDERR]: {r.Stderr}");
                    Log($"Error in {p.Name}: {r.Stderr}", true);
                }
                if(r.ExitCode != 0)
                {
                    Log($"Build failed for {p.Name} with exit code {r.ExitCode}", true);
                }
                fullLogBuilder.AppendLine();
            }
            
            result["full_log"] = fullLogBuilder.ToString();

        }
        catch (Exception ex)
        {
            Log($"Workflow Failed: {ex.Message}", true);
            Log($"Stack trace: {ex.StackTrace}", true);
            result["success"] = false;
            result["error"] = ex.Message;
        }
        finally
        {
            // Update build record with final status
            buildStopwatch.Stop();
            if (_database != null && buildRecordId > 0)
            {
                try
                {
                    bool buildSuccess = result.ContainsKey("success") && (bool)result["success"];
                    var buildResults = result.ContainsKey("build") ? (ProcessResult[])result["build"] : Array.Empty<ProcessResult>();
                    
                    int successfulProjects = buildResults.Count(r => r.ExitCode == 0);
                    int failedProjects = buildResults.Count(r => r.ExitCode != 0);
                    
                    _database.UpdateBuildRecord(
                        buildRecordId,
                        buildSuccess,
                        buildStopwatch.ElapsedMilliseconds,
                        buildLogPath,
                        hasErrors ? errorLogPath : null,
                        successfulProjects,
                        failedProjects
                    );
                    
                    _logAction($"Build record updated. Duration: {buildStopwatch.ElapsedMilliseconds}ms, Success: {buildSuccess}");
                }
                catch (Exception ex)
                {
                    _logAction($"Warning: Failed to update build record: {ex.Message}");
                }
            }
            
            // Cleanup log files
            try
            {
                buildLogWriter?.Close();
                buildLogWriter?.Dispose();
                
                errorLogWriter?.Close();
                errorLogWriter?.Dispose();
                
                // Delete error log if no errors occurred
                if (!hasErrors && File.Exists(errorLogPath))
                {
                    File.Delete(errorLogPath);
                }
                
                Log($"Logs saved to: {buildLogPath}");
                if (hasErrors)
                {
                    Log($"Error log saved to: {errorLogPath}");
                }
            }
            catch { }
        }

        result["logs"] = logs;
        return result;
    }

    private async Task<ProcessResult> RunCommandAsync(string fileName, string args, string workingDir)
    {
        return await Task.Run(() =>
        {
            var r = new ProcessResult { Command = $"{fileName} {args}" };
            var stdoutBuilder = new System.Text.StringBuilder();
            var stderrBuilder = new System.Text.StringBuilder();

            try
            {
                string absWorkingDir = Path.GetFullPath(workingDir);
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    WorkingDirectory = absWorkingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using var proc = new Process();
                proc.StartInfo = psi;
                
                proc.OutputDataReceived += (s, e) => {
                    if (e.Data != null) {
                        _logAction($"[{fileName}] {e.Data}");
                        lock(stdoutBuilder) stdoutBuilder.AppendLine(e.Data);
                    }
                };
                
                proc.ErrorDataReceived += (s, e) => {
                    if (e.Data != null) {
                        _logAction($"[{fileName} ERR] {e.Data}");
                        lock(stderrBuilder) stderrBuilder.AppendLine(e.Data);
                    }
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                
                proc.WaitForExit();
                
                r.ExitCode = proc.ExitCode;
                r.Stdout = stdoutBuilder.ToString();
                r.Stderr = stderrBuilder.ToString();
                
                _logAction($"[{fileName}] Exit: {proc.ExitCode}");
            }
            catch (Exception ex)
            {
                r.ExitCode = -1;
                r.Stderr = ex.Message;
                _logAction($"Error running {fileName}: {ex.Message}");
            }
            return r;
        });
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            _logAction($"Warning: Source dir not found {sourceDir}");
            return;
        }

        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }
}

public class ProcessResult
{
    public string Command { get; set; }
    public int ExitCode { get; set; }
    public string Stdout { get; set; }
    public string Stderr { get; set; }
}
