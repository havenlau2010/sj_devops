namespace BuildServerApp;

public partial class ProjectForm : Form
{
    private TextBox txtName;
    private TextBox txtPath;
    private TextBox txtBuildCmd;
    private TextBox txtDistDir;
    
    public ProjectConfig Result { get; private set; }

    public ProjectForm()
    {
        InitializeComponent();
    }

    private TextBox txtGroup;
    private TextBox txtNodeModulesDir;
    private TextBox txtNodeVersion;
    private CheckBox chkIsPublish;

    private void InitializeComponent()
    {
        this.Size = new Size(400, 440);
        this.Text = "Add Project";
        this.StartPosition = FormStartPosition.CenterParent;

        var lblGroup = new Label { Text = "Group:", Location = new Point(10, 20), AutoSize = true };
        txtGroup = new TextBox { Location = new Point(120, 20), Width = 200 };

        var lblName = new Label { Text = "Name:", Location = new Point(10, 60), AutoSize = true };
        txtName = new TextBox { Location = new Point(120, 60), Width = 200 };

        var lblPath = new Label { Text = "Path:", Location = new Point(10, 100), AutoSize = true };
        txtPath = new TextBox { Location = new Point(120, 100), Width = 200 };

        var lblNodeModules = new Label { Text = "Node Modules:", Location = new Point(10, 140), AutoSize = true };
        txtNodeModulesDir = new TextBox { Location = new Point(120, 140), Width = 200, Text = "../../node_modules" };

        var lblNodeVersion = new Label { Text = "Node Version:", Location = new Point(10, 180), AutoSize = true };
        txtNodeVersion = new TextBox { Location = new Point(120, 180), Width = 200, Text = "16.17.0" };

        var lblBuildCmd = new Label { Text = "Build Cmd:", Location = new Point(10, 220), AutoSize = true };
        txtBuildCmd = new TextBox { Location = new Point(120, 220), Width = 200, Text = "npm run build" };

        var lblDistDir = new Label { Text = "Dist Dir:", Location = new Point(10, 260), AutoSize = true };
        txtDistDir = new TextBox { Location = new Point(120, 260), Width = 200, Text = "dist" };
        
        chkIsPublish = new CheckBox { Text = "Is Publish", Location = new Point(120, 290), Checked = true, AutoSize = true };

        var btnSave = new Button { Text = "Save", Location = new Point(120, 330), DialogResult = DialogResult.OK };
        btnSave.Click += (s, e) => {
            Result = new ProjectConfig
            {
                Group = txtGroup.Text,
                Name = txtName.Text,
                Path = txtPath.Text,
                NodeModulesDir = txtNodeModulesDir.Text,
                NodeVersion = txtNodeVersion.Text,
                BuildCmd = txtBuildCmd.Text,
                DistDir = txtDistDir.Text,
                IsPublish = chkIsPublish.Checked
            };
        };

        var btnCancel = new Button { Text = "Cancel", Location = new Point(220, 330), DialogResult = DialogResult.Cancel };

        this.Controls.Add(lblGroup); this.Controls.Add(txtGroup);
        this.Controls.Add(lblName); this.Controls.Add(txtName);
        this.Controls.Add(lblPath); this.Controls.Add(txtPath);
        this.Controls.Add(lblNodeModules); this.Controls.Add(txtNodeModulesDir);
        this.Controls.Add(lblNodeVersion); this.Controls.Add(txtNodeVersion);
        this.Controls.Add(lblBuildCmd); this.Controls.Add(txtBuildCmd);
        this.Controls.Add(lblDistDir); this.Controls.Add(txtDistDir);
        this.Controls.Add(chkIsPublish);
        this.Controls.Add(btnSave);
        this.Controls.Add(btnCancel);
        
        this.AcceptButton = btnSave;
        this.CancelButton = btnCancel;
    }
}
