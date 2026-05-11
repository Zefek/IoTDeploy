using Octokit;
using Serilog;
using System.IO.Ports;
using IoTDeploy;

namespace IoTDeployUI;

public partial class Form1 : Form
{
    private static readonly ILogger Logger = Log.ForContext<Form1>();

    private readonly GithubProvider githubProvider;
    private readonly AppSettings settings;
    private readonly string _logPath;
    private CancellationTokenSource? _cts;

    public Form1(AppSettings settings, string logPath)
    {
        InitializeComponent();
        this.settings = settings;
        _logPath = logPath;
        githubProvider = new GithubProvider();
    }

    private void label1_Click(object sender, EventArgs e)
    {
    }

    private async void Form1_Load(object sender, EventArgs e)
    {
        SetUiBusy(Strings.ConnectingToGitHub);
        try
        {
            await githubProvider.Init(settings);
            cmbRepository.Items.Clear();
            foreach (var repo in await githubProvider.GetRepositories())
            {
                cmbRepository.Items.Add(repo.Name);
            }
            cmbBranch.Items.Clear();
            cmbEnvironment.Items.Clear();
            RefreshComPorts();
            SetUiIdle(Strings.Ready);
        }
        catch (FileNotFoundException ex)
        {
            Logger.Error(ex, "Chybí soubor privátního klíče");
            SetUiIdle("");
            MessageBox.Show(ex.Message, Strings.MissingKeyFileTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "Chyba autentizace při inicializaci");
            SetUiIdle("");
            MessageBox.Show(ex.Message, Strings.AuthErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Neočekávaná chyba při inicializaci");
            SetUiIdle("");
            MessageBox.Show(string.Format(Strings.UnexpectedInitError, ex.Message), Strings.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnDeploy_Click(object sender, EventArgs e)
    {
        RefreshComPorts();

        if (cmbRepository.SelectedItem is null || cmbBranch.SelectedItem is null ||
            cmbEnvironment.SelectedItem is null)
        {
            MessageBox.Show(Strings.MissingValuesMessage, Strings.MissingValuesTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var repositoryName = cmbRepository.SelectedItem.ToString()!;
        var branchName = cmbBranch.SelectedItem.ToString()!;
        var environmentName = cmbEnvironment.SelectedItem.ToString()!;
        var comportName = cmbPort.SelectedItem?.ToString();

        if (!string.IsNullOrEmpty(comportName) && !SerialPort.GetPortNames().Contains(comportName))
        {
            MessageBox.Show(string.Format(Strings.PortUnavailable, comportName), Strings.PortUnavailableTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var payload = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(comportName))
            payload["serial_port"] = comportName;

        if (chkUseArtifact.Checked)
        {
            if (cmbArtifact.SelectedItem is not ArtifactComboItem artifactItem)
            {
                MessageBox.Show(Strings.MissingArtifactSelection, Strings.MissingValuesTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            payload["artifact_run_id"] = artifactItem.Info.RunId.ToString();
            payload["artifact_name"] = artifactItem.Info.ArtifactName;
        }

        Logger.Information("Zahajuji deploy: repo={Repo}, branch={Branch}, env={Env}, payload={@Payload}",
            repositoryName, branchName, environmentName, payload);

        var progress = new Progress<string>(msg => lblStatus.Text = msg);

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var runner = new IoTDeploy.Runner();

        SetUiBusy(Strings.StartingDeploy);
        lblResult.Visible = false;
        try
        {
            var token = await githubProvider.GetTokenForRunner(repositoryName);
            await runner.Download(progress, ct);
            await runner.Config(settings.GitHub.Owner, repositoryName, token.Token, settings.Runner.Labels, progress, ct);
            await runner.DownloadTools(progress, ct);
            var (_, runId) = await githubProvider.RunWorkflow(repositoryName, branchName, environmentName,
                payload, progress, ct);

            // Switch progress bar to continuous mode for step tracking
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 0;

            // Start monitoring workflow progress in background
            using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var monitorTask = MonitorWorkflowAsync(repositoryName, runId, monitorCts.Token);

            // Run the self-hosted runner (blocks until workflow job completes)
            await runner.Run(progress, ct);
            monitorCts.Cancel();
            try { await monitorTask; } catch (OperationCanceledException) { }

            // Check final workflow conclusion
            var conclusion = await GetFinalConclusionAsync(repositoryName, runId);
            Logger.Information("Deploy dokončen: {Conclusion}", conclusion);
            ShowDeployResult(conclusion);
        }
        catch (OperationCanceledException)
        {
            Logger.Information("Deploy zrušen uživatelem");
            ShowDeployResult("cancelled");
        }
        catch (TimeoutException ex)
        {
            Logger.Warning(ex, "Timeout při deployi");
            ShowDeployResult("failure");
            MessageBox.Show(ex.Message, Strings.TimeoutTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "Chyba deploye");
            ShowDeployResult("failure");
            MessageBox.Show(ex.Message, Strings.DeployErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Neočekávaná chyba při deployi");
            ShowDeployResult("failure");
            MessageBox.Show(string.Format(Strings.UnexpectedError, ex.Message), Strings.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            runner.Delete();
            _cts.Dispose();
            _cts = null;
            SetUiIdle(lblStatus.Text);
        }
    }

    private async void cmbRepository_SelectedIndexChanged(object sender, EventArgs e)
    {
        var repositoryName = cmbRepository.SelectedItem?.ToString();
        if (repositoryName is null) return;

        SetUiBusy(Strings.LoadingBranchesAndEnvs);
        try
        {
            cmbBranch.Items.Clear();
            foreach (var branch in await githubProvider.GetBranches(repositoryName))
            {
                cmbBranch.Items.Add(branch.Name);
            }

            cmbEnvironment.Items.Clear();
            foreach (var env in await githubProvider.GetEnvironments(repositoryName))
            {
                cmbEnvironment.Items.Add(env.Name);
            }
            cmbArtifact.Items.Clear();
            SetUiIdle(Strings.Ready);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "Chyba při načítání větví/prostředí pro repo={Repo}", repositoryName);
            SetUiIdle("");
            MessageBox.Show(ex.Message, Strings.LoadErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Neočekávaná chyba při načítání repo={Repo}", repositoryName);
            SetUiIdle("");
            MessageBox.Show(string.Format(Strings.UnexpectedError, ex.Message), Strings.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void cmbBranch_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (chkUseArtifact.Checked)
            await ReloadArtifactsAsync();
    }

    private async void chkUseArtifact_CheckedChanged(object sender, EventArgs e)
    {
        cmbArtifact.Enabled = chkUseArtifact.Checked;
        if (chkUseArtifact.Checked)
            await ReloadArtifactsAsync();
        else
            cmbArtifact.Items.Clear();
    }

    private async Task ReloadArtifactsAsync()
    {
        var repo = cmbRepository.SelectedItem?.ToString();
        var branch = cmbBranch.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(repo) || string.IsNullOrEmpty(branch))
        {
            cmbArtifact.Items.Clear();
            return;
        }

        cmbArtifact.Items.Clear();
        cmbArtifact.Items.Add(Strings.LoadingArtifacts);
        cmbArtifact.SelectedIndex = 0;
        cmbArtifact.Enabled = false;
        try
        {
            var infos = await githubProvider.GetSuccessfulRunsWithArtifactsAsync(
                repo, branch, artifactName: null, workflowName: null, top: 10);
            cmbArtifact.Items.Clear();
            if (infos.Count == 0)
            {
                cmbArtifact.Items.Add(Strings.NoArtifactsAvailable);
                cmbArtifact.SelectedIndex = 0;
                return;
            }
            cmbArtifact.Items.Add(new ArtifactComboItem(infos[0], IsLatest: true));
            foreach (var info in infos)
                cmbArtifact.Items.Add(new ArtifactComboItem(info, IsLatest: false));
            cmbArtifact.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Chyba při načítání artefaktů");
            cmbArtifact.Items.Clear();
            cmbArtifact.Items.Add(string.Format(Strings.ArtifactsLoadFailed, ex.Message));
            cmbArtifact.SelectedIndex = 0;
        }
        finally
        {
            cmbArtifact.Enabled = chkUseArtifact.Checked;
        }
    }

    private void btnCancel_Click(object sender, EventArgs e)
    {
        Logger.Information("Uživatel požádal o zrušení deploye");
        _cts?.Cancel();
        btnCancel.Enabled = false;
    }

    private void btnOpenLog_Click(object sender, EventArgs e)
    {
        // Najdeme aktuální soubor logu (Serilog přidává datum do názvu)
        var logDir = Path.GetDirectoryName(_logPath)!;
        var logPattern = Path.GetFileNameWithoutExtension(_logPath).TrimEnd('-') + "*" + Path.GetExtension(_logPath);
        var todayLog = Directory.GetFiles(logDir, logPattern)
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();

        if (todayLog == null || !File.Exists(todayLog))
        {
            MessageBox.Show(string.Format(Strings.LogNotFound, logDir),
                Strings.LogNotFoundTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        System.Diagnostics.Process.Start("notepad.exe", todayLog);
    }

    private void RefreshComPorts()
    {
        var selected = cmbPort.SelectedItem?.ToString();
        cmbPort.Items.Clear();
        cmbPort.Items.AddRange(SerialPort.GetPortNames());
        if (selected != null && cmbPort.Items.Contains(selected))
            cmbPort.SelectedItem = selected;
    }

    private async Task MonitorWorkflowAsync(string repository, long runId, CancellationToken ct)
    {
        await Task.Delay(3000, ct); // let the workflow start
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var progress = await githubProvider.GetWorkflowProgressAsync(repository, runId);
                if (progress != null)
                {
                    if (InvokeRequired)
                        Invoke(() => UpdateWorkflowProgress(progress));
                    else
                        UpdateWorkflowProgress(progress);

                    if (progress.IsCompleted) return;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Chyba při monitorování workflow");
            }
            await Task.Delay(5000, ct);
        }
    }

    private void UpdateWorkflowProgress(IoTDeploy.WorkflowProgress progress)
    {
        if (progress.TotalSteps > 0)
        {
            progressBar.Maximum = progress.TotalSteps;
            progressBar.Value = Math.Min(progress.CompletedSteps, progress.TotalSteps);
            var stepInfo = string.IsNullOrEmpty(progress.CurrentStepName)
                ? $"{progress.CompletedSteps}/{progress.TotalSteps}"
                : $"{progress.CompletedSteps}/{progress.TotalSteps} – {progress.CurrentStepName}";
            lblStatus.Text = $"{progress.JobName}: {stepInfo}";
        }
    }

    private async Task<string> GetFinalConclusionAsync(string repository, long runId)
    {
        // Poll a few times to get the final conclusion (workflow might finish shortly after runner exits)
        for (var i = 0; i < 6; i++)
        {
            try
            {
                var progress = await githubProvider.GetWorkflowProgressAsync(repository, runId);
                if (progress is { IsCompleted: true, Conclusion: not null })
                    return progress.Conclusion;
            }
            catch { }
            await Task.Delay(3000);
        }
        return "unknown";
    }

    private void ShowDeployResult(string conclusion)
    {
        switch (conclusion)
        {
            case "success":
                lblResult.Text = Strings.DeploySuccess;
                lblResult.ForeColor = Color.Green;
                break;
            case "cancelled":
                lblResult.Text = Strings.DeployCancelled;
                lblResult.ForeColor = Color.Orange;
                break;
            default:
                lblResult.Text = string.Format(Strings.DeployFailed, conclusion);
                lblResult.ForeColor = Color.Red;
                break;
        }
        lblResult.Visible = true;
        lblStatus.Text = "";
    }

    private void SetUiBusy(string status)
    {
        btnDeploy.Enabled = false;
        btnCancel.Enabled = true;
        btnCancel.Visible = true;
        cmbRepository.Enabled = false;
        cmbBranch.Enabled = false;
        cmbEnvironment.Enabled = false;
        cmbPort.Enabled = false;
        chkUseArtifact.Enabled = false;
        cmbArtifact.Enabled = false;
        progressBar.Visible = true;
        lblStatus.Text = status;
    }

    private void SetUiIdle(string status)
    {
        btnDeploy.Enabled = true;
        btnCancel.Enabled = false;
        btnCancel.Visible = false;
        cmbRepository.Enabled = true;
        cmbBranch.Enabled = true;
        cmbEnvironment.Enabled = true;
        cmbPort.Enabled = true;
        chkUseArtifact.Enabled = true;
        cmbArtifact.Enabled = chkUseArtifact.Checked;
        progressBar.Visible = false;
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.Value = 0;
        lblStatus.Text = status;
    }

    private sealed record ArtifactComboItem(IoTDeploy.ArtifactRunInfo Info, bool IsLatest)
    {
        public override string ToString() => IsLatest
            ? string.Format(Strings.ArtifactItemLatest, Info.RunId, Info.ShortSha, Info.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
            : string.Format(Strings.ArtifactItem, Info.RunId, Info.ShortSha, Info.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
    }
}
