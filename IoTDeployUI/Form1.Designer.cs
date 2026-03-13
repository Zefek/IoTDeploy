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
        label1.Text = "Repository";
        label1.Click += label1_Click;
        //
        // label2
        //
        label2.AutoSize = true;
        label2.Location = new Point(12, 54);
        label2.Name = "label2";
        label2.Size = new Size(65, 25);
        label2.TabIndex = 3;
        label2.Text = "Branch";
        //
        // cmbBranch
        //
        cmbBranch.FormattingEnabled = true;
        cmbBranch.Location = new Point(127, 51);
        cmbBranch.Name = "cmbBranch";
        cmbBranch.Size = new Size(305, 33);
        cmbBranch.TabIndex = 2;
        //
        // label3
        //
        label3.AutoSize = true;
        label3.Location = new Point(12, 93);
        label3.Name = "label3";
        label3.Size = new Size(112, 25);
        label3.TabIndex = 5;
        label3.Text = "Environment";
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
        btnDeploy.Text = "Deploy";
        btnDeploy.UseVisualStyleBackColor = true;
        btnDeploy.Click += btnDeploy_Click;
        //
        // btnCancel
        //
        btnCancel.Location = new Point(360, 168);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(100, 34);
        btnCancel.TabIndex = 12;
        btnCancel.Text = "Zrušit";
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
        btnOpenLog.Text = "Log";
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
        label4.Text = "COM";
        //
        // cmbPort
        //
        cmbPort.FormattingEnabled = true;
        cmbPort.Location = new Point(127, 129);
        cmbPort.Name = "cmbPort";
        cmbPort.Size = new Size(305, 33);
        cmbPort.TabIndex = 7;
        //
        // progressBar
        //
        progressBar.Location = new Point(12, 215);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(630, 12);
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.MarqueeAnimationSpeed = 30;
        progressBar.TabIndex = 9;
        progressBar.Visible = false;
        //
        // lblStatus
        //
        lblStatus.AutoSize = false;
        lblStatus.Location = new Point(12, 237);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(630, 25);
        lblStatus.TabIndex = 10;
        lblStatus.Text = "";
        //
        // lblResult
        //
        lblResult.AutoSize = false;
        lblResult.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        lblResult.Location = new Point(12, 268);
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
        ClientSize = new Size(660, 308);
        Controls.Add(lblResult);
        Controls.Add(lblStatus);
        Controls.Add(progressBar);
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
        Text = "IoT Deployer";
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
    private ProgressBar progressBar;
    private Label lblStatus;
    private Label lblResult;
}
