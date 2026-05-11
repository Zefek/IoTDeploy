namespace IoTDeployUI;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        cmbRepository = new ComboBox();
        label1 = new Label();
        label2 = new Label();
        cmbBranch = new ComboBox();
        label3 = new Label();
        cmbEnvironment = new ComboBox();
        btnDeploy = new Button();
        btnCancel = new Button();
        btnOpenLog = new Button();
        label4 = new Label();
        cmbPort = new ComboBox();
        chkUseArtifact = new CheckBox();
        cmbArtifact = new ComboBox();
        progressBar = new ProgressBar();
        lblStatus = new Label();
        lblResult = new Label();
        SuspendLayout();
        //
        // cmbRepository
        //
        cmbRepository.FormattingEnabled = true;
        cmbRepository.Location = new Point(127, 12);
        cmbRepository.Name = "cmbRepository";
        cmbRepository.Size = new Size(305, 33);
        cmbRepository.TabIndex = 0;
        cmbRepository.SelectedIndexChanged += cmbRepository_SelectedIndexChanged;
        //
        // label1
        //
        label1.AutoSize = true;
        label1.Location = new Point(12, 15);
        label1.Name = "label1";
        label1.Size = new Size(97, 25);
        label1.TabIndex = 1;
        label1.Text = Strings.LabelRepository;
        label1.Click += label1_Click;
        //
        // label2
        //
        label2.AutoSize = true;
        label2.Location = new Point(12, 54);
        label2.Name = "label2";
        label2.Size = new Size(65, 25);
        label2.TabIndex = 3;
        label2.Text = Strings.LabelBranch;
        //
        // cmbBranch
        //
        cmbBranch.FormattingEnabled = true;
        cmbBranch.Location = new Point(127, 51);
        cmbBranch.Name = "cmbBranch";
        cmbBranch.Size = new Size(305, 33);
        cmbBranch.TabIndex = 2;
        cmbBranch.SelectedIndexChanged += cmbBranch_SelectedIndexChanged;
        //
        // label3
        //
        label3.AutoSize = true;
        label3.Location = new Point(12, 93);
        label3.Name = "label3";
        label3.Size = new Size(112, 25);
        label3.TabIndex = 5;
        label3.Text = Strings.LabelEnvironment;
        //
        // cmbEnvironment
        //
        cmbEnvironment.FormattingEnabled = true;
        cmbEnvironment.Location = new Point(127, 90);
        cmbEnvironment.Name = "cmbEnvironment";
        cmbEnvironment.Size = new Size(305, 33);
        cmbEnvironment.TabIndex = 4;
        //
        // btnDeploy
        //
        btnDeploy.Location = new Point(127, 168);
        btnDeploy.Name = "btnDeploy";
        btnDeploy.Size = new Size(225, 34);
        btnDeploy.TabIndex = 6;
        btnDeploy.Text = Strings.ButtonDeploy;
        btnDeploy.UseVisualStyleBackColor = true;
        btnDeploy.Click += btnDeploy_Click;
        //
        // btnCancel
        //
        btnCancel.Location = new Point(360, 168);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(100, 34);
        btnCancel.TabIndex = 12;
        btnCancel.Text = Strings.ButtonCancel;
        btnCancel.UseVisualStyleBackColor = true;
        btnCancel.Visible = false;
        btnCancel.Click += btnCancel_Click;
        //
        // btnOpenLog
        //
        btnOpenLog.Location = new Point(468, 168);
        btnOpenLog.Name = "btnOpenLog";
        btnOpenLog.Size = new Size(72, 34);
        btnOpenLog.TabIndex = 11;
        btnOpenLog.Text = Strings.ButtonLog;
        btnOpenLog.UseVisualStyleBackColor = true;
        btnOpenLog.Click += btnOpenLog_Click;
        //
        // label4
        //
        label4.AutoSize = true;
        label4.Location = new Point(12, 132);
        label4.Name = "label4";
        label4.Size = new Size(53, 25);
        label4.TabIndex = 8;
        label4.Text = Strings.LabelCom;
        //
        // cmbPort
        //
        cmbPort.FormattingEnabled = true;
        cmbPort.Location = new Point(127, 129);
        cmbPort.Name = "cmbPort";
        cmbPort.Size = new Size(305, 33);
        cmbPort.TabIndex = 7;
        //
        // chkUseArtifact
        //
        chkUseArtifact.AutoSize = true;
        chkUseArtifact.Location = new Point(12, 174);
        chkUseArtifact.Name = "chkUseArtifact";
        chkUseArtifact.TabIndex = 8;
        chkUseArtifact.Text = Strings.LabelUseArtifact;
        chkUseArtifact.UseVisualStyleBackColor = true;
        chkUseArtifact.CheckedChanged += chkUseArtifact_CheckedChanged;
        //
        // cmbArtifact
        //
        cmbArtifact.FormattingEnabled = true;
        cmbArtifact.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbArtifact.Enabled = false;
        cmbArtifact.Location = new Point(180, 171);
        cmbArtifact.Name = "cmbArtifact";
        cmbArtifact.Size = new Size(462, 33);
        cmbArtifact.TabIndex = 9;
        //
        // btnDeploy (overrides earlier location)
        //
        btnDeploy.Location = new Point(127, 217);
        btnCancel.Location = new Point(360, 217);
        btnOpenLog.Location = new Point(468, 217);
        //
        // progressBar
        //
        progressBar.Location = new Point(12, 264);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(630, 12);
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.MarqueeAnimationSpeed = 30;
        progressBar.TabIndex = 10;
        progressBar.Visible = false;
        //
        // lblStatus
        //
        lblStatus.AutoSize = false;
        lblStatus.Location = new Point(12, 286);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(630, 25);
        lblStatus.TabIndex = 11;
        lblStatus.Text = "";
        //
        // lblResult
        //
        lblResult.AutoSize = false;
        lblResult.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        lblResult.Location = new Point(12, 317);
        lblResult.Name = "lblResult";
        lblResult.Size = new Size(630, 30);
        lblResult.TabIndex = 13;
        lblResult.Text = "";
        lblResult.Visible = false;
        //
        // Form1
        //
        AutoScaleDimensions = new SizeF(10F, 25F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(660, 360);
        Controls.Add(lblResult);
        Controls.Add(lblStatus);
        Controls.Add(progressBar);
        Controls.Add(chkUseArtifact);
        Controls.Add(cmbArtifact);
        Controls.Add(label4);
        Controls.Add(cmbPort);
        Controls.Add(btnOpenLog);
        Controls.Add(btnCancel);
        Controls.Add(btnDeploy);
        Controls.Add(label3);
        Controls.Add(cmbEnvironment);
        Controls.Add(label2);
        Controls.Add(cmbBranch);
        Controls.Add(label1);
        Controls.Add(cmbRepository);
        Name = "Form1";
        Text = Strings.FormTitle;
        Load += Form1_Load;
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private ComboBox cmbRepository;
    private Label label1;
    private Label label2;
    private ComboBox cmbBranch;
    private Label label3;
    private ComboBox cmbEnvironment;
    private Button btnDeploy;
    private Button btnCancel;
    private Button btnOpenLog;
    private Label label4;
    private ComboBox cmbPort;
    private CheckBox chkUseArtifact;
    private ComboBox cmbArtifact;
    private ProgressBar progressBar;
    private Label lblStatus;
    private Label lblResult;
}
