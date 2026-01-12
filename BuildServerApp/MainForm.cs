using BuildServer.Core;
namespace BuildServerApp;

public partial class MainForm : Form
{
    private HttpServer _server;
    private BuildManager _manager;
    private RichTextBox txtLogs;
    private DataGridView gridProjects;
    private TabControl tabControl;
    private BuildLogsForm logsForm;

    public MainForm()
    {
        InitializeComponent();
        SetupBasicUI(); // Initialize basic controls like txtLogs
        
        // Now manager can log to txtLogs
        _manager = new BuildManager("config.json", Log);
        _server = new HttpServer(_manager, Log);
        
        SetupTabs(); // Now setup tabs that depend on _manager/database
        RefreshProjectList();
        
        // Auto-start server in background
        this.Shown += (s, e) => _server.Start(3000);
    }

    private void SetupBasicUI()
    {
        this.Size = new Size(1000, 800);
        this.Text = "Build Tool";

        tabControl = new TabControl { Dock = DockStyle.Fill };
        this.Controls.Add(tabControl);
    }

    private void SetupTabs()
    {
        var tabProjects = new TabPage("项目管理");
        SetupProjectManagementTab(tabProjects);
        
        var tabLogs = new TabPage("日志管理");
        SetupLogsManagementTab(tabLogs);

        tabControl.TabPages.Add(tabProjects);
        tabControl.TabPages.Add(tabLogs);
    }

    private void SetupProjectManagementTab(TabPage tab)
    {
        // Top Panel for Buttons
        var panelTop = new Panel { Dock = DockStyle.Top, Height = 50 };
        
        var btnAddProject = new Button { Text = "添加项目", Location = new Point(10, 10), Width = 100 };
        btnAddProject.Click += (s, e) => {
            using var form = new ProjectForm();
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                _manager.AddProject(form.Result);
                RefreshProjectList();
            }
        };

        var btnSave = new Button { Text = "保存配置", Location = new Point(120, 10), Width = 100, BackColor = Color.LightBlue };
        btnSave.Click += (s, e) => {
            _manager.SaveConfig();
            Log("Configuration saved to config.json");
        };

        var btnDeploy = new Button { Text = "一键部署", Location = new Point(230, 10), Width = 100, BackColor = Color.LightGreen };
        btnDeploy.Click += async (s, e) => {
            btnDeploy.Enabled = false;
            await _manager.RunWorkflowAsync();
            btnDeploy.Enabled = true;
        };

        panelTop.Controls.Add(btnAddProject);
        panelTop.Controls.Add(btnSave);
        panelTop.Controls.Add(btnDeploy);

        // Middle Grid for Projects
        gridProjects = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false };
        gridProjects.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Publish", DataPropertyName = "IsPublish", Width = 60 });
        gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Group", DataPropertyName = "Group", Width = 100 });
        gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", DataPropertyName = "Name", Width = 150 });
        gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Path", DataPropertyName = "Path", Width = 300 });
        gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "NodeModules", DataPropertyName = "NodeModulesDir", Width = 150 });
        gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "NodeVersion", DataPropertyName = "NodeVersion", Width = 100 });
        gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Build Cmd", DataPropertyName = "BuildCmd", Width = 120 });
        gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Dist Dir", DataPropertyName = "DistDir", Width = 80 });

        // Bottom Logs
        var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 300 };
        txtLogs = new RichTextBox { Dock = DockStyle.Fill };
        panelBottom.Controls.Add(txtLogs);

        tab.Controls.Add(gridProjects);
        tab.Controls.Add(panelBottom);
        tab.Controls.Add(panelTop);
    }

    private void SetupLogsManagementTab(TabPage tab)
    {
        // Get database instance from BuildManager
        var databaseField = typeof(BuildManager).GetField("_database", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var database = databaseField?.GetValue(_manager) as BuildDatabase;

        if (database != null)
        {
            logsForm = new BuildLogsForm(database);
            logsForm.Dock = DockStyle.Fill;
            tab.Controls.Add(logsForm);
        }
        else
        {
            var lblError = new Label 
            { 
                Text = "数据库未初始化,无法显示日志管理功能", 
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Red
            };
            tab.Controls.Add(lblError);
        }
    }

    private void RefreshProjectList()
    {
        if (_manager == null) return;
        var projects = _manager.Projects;
        // Use BindingList to update UI if list changes, but simple reassignment works for refresh
        gridProjects.DataSource = null;
        gridProjects.DataSource = projects;
    }

    private void Log(string message)
    {
        if (txtLogs == null) return;
        if (InvokeRequired)
        {
            Invoke(new Action<string>(Log), message);
            return;
        }
        txtLogs.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
        txtLogs.ScrollToCaret();
    }
}
