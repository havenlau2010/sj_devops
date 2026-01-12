namespace BuildServerApp;

/// <summary>
/// User control for displaying build logs and records from database
/// </summary>
public partial class BuildLogsForm : UserControl
{
    private BuildDatabase _database;
    private DataGridView _gridBuilds;
    private DataGridView _gridProjects;
    private RichTextBox _txtLog;
    private ComboBox _cmbFilter;
    private ComboBox _cmbLimit;
    private Button _btnRefresh;
    private Button _btnLoadBuildLog;
    private Button _btnLoadErrorLog;
    private Label _lblStatus;
    
    private long _selectedBuildId = 0;

    public BuildLogsForm(BuildDatabase database)
    {
        _database = database;
        InitializeComponent();
        SetupUI();
        LoadBuildRecords();
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();
        this.Name = "BuildLogsForm";
        this.Size = new Size(1000, 700);
        this.ResumeLayout(false);
    }

    private void SetupUI()
    {
        // Top toolbar panel
        var panelToolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.WhiteSmoke };
        
        _btnRefresh = new Button 
        { 
            Text = "刷新", 
            Location = new Point(10, 8), 
            Width = 80,
            BackColor = Color.LightBlue
        };
        _btnRefresh.Click += (s, e) => LoadBuildRecords();
        
        var lblFilter = new Label 
        { 
            Text = "筛选:", 
            Location = new Point(100, 12), 
            Width = 40,
            TextAlign = ContentAlignment.MiddleRight
        };
        
        _cmbFilter = new ComboBox 
        { 
            Location = new Point(145, 9), 
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbFilter.Items.AddRange(new object[] { "全部", "仅成功", "仅失败" });
        _cmbFilter.SelectedIndex = 0;
        _cmbFilter.SelectedIndexChanged += (s, e) => LoadBuildRecords();
        
        var lblLimit = new Label 
        { 
            Text = "显示:", 
            Location = new Point(275, 12), 
            Width = 40,
            TextAlign = ContentAlignment.MiddleRight
        };
        
        _cmbLimit = new ComboBox 
        { 
            Location = new Point(320, 9), 
            Width = 100,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbLimit.Items.AddRange(new object[] { "最近10条", "最近20条", "最近50条", "最近100条" });
        _cmbLimit.SelectedIndex = 2; // Default to 50
        _cmbLimit.SelectedIndexChanged += (s, e) => LoadBuildRecords();
        
        _lblStatus = new Label 
        { 
            Text = "就绪", 
            Location = new Point(430, 12), 
            Width = 400,
            ForeColor = Color.Gray
        };
        
        panelToolbar.Controls.AddRange(new Control[] { _btnRefresh, lblFilter, _cmbFilter, lblLimit, _cmbLimit, _lblStatus });
        
        // Build records grid (top section)
        _gridBuilds = new DataGridView 
        { 
            Dock = DockStyle.Top,
            Height = 200,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        
        _gridBuilds.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", DataPropertyName = "Id", Width = 50 });
        _gridBuilds.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "构建时间", DataPropertyName = "BuildTime", Width = 150, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm:ss" } });
        _gridBuilds.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", DataPropertyName = "SuccessText", Width = 60 });
        _gridBuilds.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "耗时(秒)", DataPropertyName = "DurationSeconds", Width = 80 });
        _gridBuilds.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "总项目", DataPropertyName = "TotalProjects", Width = 70 });
        _gridBuilds.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "成功", DataPropertyName = "SuccessfulProjects", Width = 60 });
        _gridBuilds.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "失败", DataPropertyName = "FailedProjects", Width = 60 });
        _gridBuilds.Columns.Add(new DataGridViewTextBoxColumn { Name = "LogFilePath", HeaderText = "日志文件", DataPropertyName = "LogFilePath", Width = 300 });
        _gridBuilds.Columns.Add(new DataGridViewTextBoxColumn { Name = "ErrorLogFilePath", HeaderText = "错误日志路径", DataPropertyName = "ErrorLogFilePath", Width = 0, Visible = false });
        
        _gridBuilds.SelectionChanged += GridBuilds_SelectionChanged;
        _gridBuilds.RowPrePaint += GridBuilds_RowPrePaint;
        
        // Project records grid (middle section)
        var panelProjects = new Panel { Dock = DockStyle.Top, Height = 200 };
        var lblProjects = new Label { Text = "项目构建详情:", Dock = DockStyle.Top, Height = 25, Padding = new Padding(5) };
        
        _gridProjects = new DataGridView 
        { 
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "项目名称", DataPropertyName = "ProjectName", Width = 150 });
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", DataPropertyName = "SuccessText", Width = 60 });
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "退出码", DataPropertyName = "ExitCode", Width = 70 });
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "构建命令", DataPropertyName = "BuildCommand", Width = 200 });
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Node版本", DataPropertyName = "NodeVersion", Width = 80 });
        _gridProjects.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "错误信息", DataPropertyName = "ErrorMessage", Width = 300 });
        
        _gridProjects.RowPrePaint += GridProjects_RowPrePaint;
        
        panelProjects.Controls.Add(_gridProjects);
        panelProjects.Controls.Add(lblProjects);
        
        // Log viewer (bottom section)
        var panelLog = new Panel { Dock = DockStyle.Fill };
        var panelLogToolbar = new Panel { Dock = DockStyle.Top, Height = 35 };
        
        var lblLog = new Label { Text = "日志内容:", Location = new Point(5, 8), Width = 80 };
        
        _btnLoadBuildLog = new Button 
        { 
            Text = "加载构建日志", 
            Location = new Point(90, 5), 
            Width = 110,
            Enabled = false
        };
        _btnLoadBuildLog.Click += BtnLoadBuildLog_Click;
        
        _btnLoadErrorLog = new Button 
        { 
            Text = "加载错误日志", 
            Location = new Point(210, 5), 
            Width = 110,
            Enabled = false
        };
        _btnLoadErrorLog.Click += BtnLoadErrorLog_Click;
        
        panelLogToolbar.Controls.AddRange(new Control[] { lblLog, _btnLoadBuildLog, _btnLoadErrorLog });
        
        _txtLog = new RichTextBox 
        { 
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGray
        };
        
        panelLog.Controls.Add(_txtLog);
        panelLog.Controls.Add(panelLogToolbar);
        
        // Add all panels to form
        this.Controls.Add(panelLog);
        this.Controls.Add(panelProjects);
        this.Controls.Add(_gridBuilds);
        this.Controls.Add(panelToolbar);
    }

    private void LoadBuildRecords()
    {
        try
        {
            _lblStatus.Text = "正在加载构建记录...";
            _lblStatus.ForeColor = Color.Blue;
            
            int limit = _cmbLimit.SelectedIndex switch
            {
                0 => 10,
                1 => 20,
                2 => 50,
                3 => 100,
                _ => 50
            };
            
            var records = _database.GetRecentBuilds(limit);
            
            // Apply filter
            if (_cmbFilter.SelectedIndex == 1) // Success only
            {
                records = records.Where(r => r.Success).ToList();
            }
            else if (_cmbFilter.SelectedIndex == 2) // Failed only
            {
                records = records.Where(r => !r.Success).ToList();
            }
            
            // Create display objects with formatted properties
            var displayRecords = records.Select(r => new
            {
                r.Id,
                r.BuildTime,
                SuccessText = r.Success ? "✓ 成功" : "✗ 失败",
                DurationSeconds = Math.Round(r.Duration / 1000.0, 2),
                r.TotalProjects,
                r.SuccessfulProjects,
                r.FailedProjects,
                r.LogFilePath,
                r.ErrorLogFilePath,
                Success = r.Success
            }).ToList();
            
            _gridBuilds.DataSource = displayRecords;
            
            _lblStatus.Text = $"已加载 {displayRecords.Count} 条构建记录";
            _lblStatus.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"加载失败: {ex.Message}";
            _lblStatus.ForeColor = Color.Red;
            MessageBox.Show($"加载构建记录失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void GridBuilds_SelectionChanged(object sender, EventArgs e)
    {
        if (_gridBuilds.SelectedRows.Count == 0) return;
        
        var row = _gridBuilds.SelectedRows[0];
        _selectedBuildId = Convert.ToInt64(row.Cells["Id"].Value);
        
        LoadProjectRecords(_selectedBuildId);
        
        // Enable log buttons
        var logPath = row.Cells["LogFilePath"].Value?.ToString();
        var errorLogPath = row.Cells["ErrorLogFilePath"].Value?.ToString();
        
        _btnLoadBuildLog.Enabled = !string.IsNullOrEmpty(logPath);
        _btnLoadErrorLog.Enabled = !string.IsNullOrEmpty(errorLogPath);
        
        _txtLog.Clear();
    }

    private void LoadProjectRecords(long buildRecordId)
    {
        try
        {
            var records = _database.GetProjectBuildRecords(buildRecordId);
            
            var displayRecords = records.Select(r => new
            {
                r.Id, // Include ID for selection
                r.ProjectName,
                SuccessText = r.Success ? "✓ 成功" : "✗ 失败",
                r.ExitCode,
                r.BuildCommand,
                r.NodeVersion,
                r.ErrorMessage,
                Success = r.Success
            }).ToList();
            
            _gridProjects.DataSource = displayRecords;
            _gridProjects.MouseDown -= GridProjects_MouseDown; // Remove existing to avoid dupes if any
            _gridProjects.MouseDown += GridProjects_MouseDown;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载项目记录失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void GridProjects_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var hitTest = _gridProjects.HitTest(e.X, e.Y);
            if (hitTest.RowIndex >= 0)
            {
                _gridProjects.ClearSelection();
                _gridProjects.Rows[hitTest.RowIndex].Selected = true;
                
                var row = _gridProjects.Rows[hitTest.RowIndex];
                if (row.DataBoundItem == null) return;
                
                // Need Id from the data item (using Reflection because anonymous type)
                // Wait, LoadProjectRecords projects into an anonymous type that DOES include r.Id?
                // Yes: r.Id is mapped to Id (implicit naming) if I didn't change it. 
                // Let's check LoadProjectRecords...
                // It does r.Id, so it has Id property.
                
                var ctxMenu = new ContextMenuStrip();
                var item = ctxMenu.Items.Add("查看项目日志");
                item.Click += (s, ev) => 
                {
                    try {
                        long projRecId = (long)row.DataBoundItem.GetType().GetProperty("Id").GetValue(row.DataBoundItem);
                        string projectName = (string)row.DataBoundItem.GetType().GetProperty("ProjectName").GetValue(row.DataBoundItem);
                        LoadProjectLog(projRecId, projectName);
                    } catch (Exception ex) {
                         MessageBox.Show($"无法获取项目ID: {ex.Message}");
                    }
                };
                ctxMenu.Show(_gridProjects, e.Location);
            }
        }
    }

    private void BtnLoadBuildLog_Click(object sender, EventArgs e)
    {
        if (_gridBuilds.SelectedRows.Count == 0) return;
        
        long buildId = Convert.ToInt64(_gridBuilds.SelectedRows[0].Cells["Id"].Value);
        LoadBuildLogFromDb(buildId, "构建日志");
    }

    private void BtnLoadErrorLog_Click(object sender, EventArgs e)
    {
         MessageBox.Show("错误日志暂不支持DB读取，请查看构建日志详情。", "提示");
         // Error log is usually subset of build log anyway.
    }

    private void LoadBuildLogFromDb(long buildId, string logType)
    {
        try
        {
            _txtLog.Clear();
            _txtLog.Text = $"正在从数据库加载 {logType}...\n";
            
            string content = _database.GetBuildLogContent(buildId);
            
            if (string.IsNullOrEmpty(content))
            {
                // Fallback to file if DB content is empty (for old records)
                var logPath = _gridBuilds.SelectedRows[0].Cells["LogFilePath"].Value?.ToString();
                 if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
                 {
                     _txtLog.Text += "数据库中无内容，尝试加载本地文件...\n";
                     content = File.ReadAllText(logPath, System.Text.Encoding.UTF8);
                 }
                 else
                 {
                     _txtLog.Text = "无日志内容 (数据库和本地文件均未找到)";
                     return;
                 }
            }
            
            _txtLog.Text = content;
            _lblStatus.Text = $"已加载 {logType} (ID: {buildId})";
            _lblStatus.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载{logType}失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadProjectLog(long projectRecordId, string projectName)
    {
        try
        {
            _txtLog.Clear();
            _txtLog.Text = $"正在从数据库加载项目日志: {projectName}...\n";
            
            string content = _database.GetProjectBuildLogContent(projectRecordId);
            
            if (string.IsNullOrEmpty(content))
            {
                 _txtLog.Text = "该项目无日志记录 (可能为旧数据)";
                 return;
            }
            
            _txtLog.Text = content;
            _lblStatus.Text = $"已加载项目日志: {projectName}";
            _lblStatus.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载项目日志失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void GridBuilds_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _gridBuilds.Rows.Count) return;
        
        var row = _gridBuilds.Rows[e.RowIndex];
        if (row.DataBoundItem == null) return;
        
        var success = (bool)row.DataBoundItem.GetType().GetProperty("Success").GetValue(row.DataBoundItem);
        
        if (success)
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(230, 255, 230); // Light green
        }
        else
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 230, 230); // Light red
        }
    }

    private void GridProjects_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _gridProjects.Rows.Count) return;
        
        var row = _gridProjects.Rows[e.RowIndex];
        if (row.DataBoundItem == null) return;
        
        var success = (bool)row.DataBoundItem.GetType().GetProperty("Success").GetValue(row.DataBoundItem);
        
        if (success)
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(230, 255, 230); // Light green
        }
        else
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 230, 230); // Light red
        }
    }
}
