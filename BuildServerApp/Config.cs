using System.Text.Json;

namespace BuildServerApp;

public class ProjectConfig
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string Group { get; set; }
    public string NodeModulesDir { get; set; }
    public string BuildCmd { get; set; }
    public string DistDir { get; set; }
    public bool IsPublish { get; set; } = true;
    public string NodeVersion { get; set; }
}

public class ServerConfig
{
    public int Port { get; set; } = 3000;
    public string SvnRoot { get; set; }
    public string OutputDir { get; set; }
    public string NvmRoot { get; set; } = @"D:\Apps\nvm"; // Default guessed path
    public List<ProjectConfig> Projects { get; set; } = new List<ProjectConfig>();
}
