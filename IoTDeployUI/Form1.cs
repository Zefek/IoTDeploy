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
        SetUiBusy("Připojuji se k GitHubu...");
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
            SetUiIdle("Připraveno.");
        }
        catch (FileNotFoundException ex)
        {
            Logger.Error(ex, "Chybí soubor privátního klíče");
            SetUiIdle("");
            MessageBox.Show(ex.Message, "Chybí soubor klíče", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "Chyba autentizace při inicializaci");
            SetUiIdle("");
            MessageBox.Show(ex.Message, "Chyba autentizace", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Neočekávaná chyba při inicializaci");
            SetUiIdle("");
            MessageBox.Show($"Neočekávaná chyba při inicializaci:\n{ex.Message}", "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnDeploy_Click(object sender, EventArgs e)
    {
        RefreshComPorts();
        /*
        if (cmbRepository.SelectedItem is null || cmbBranch.SelectedItem is null ||
            cmbEnvironment.SelectedItem is null || cmbPort.SelectedItem is null)
        {
            MessageBox.Show("Vyplňte všechny hodnoty před deployem.", "Chybí hodnoty", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        */
        var repositoryName = cmbRepository.SelectedItem.ToString()!;
        var branchName = cmbBranch.SelectedItem.ToString()!;
        var environmentName = cmbEnvironment.SelectedItem.ToString()!;
        var comportName = "COM3";// cmbPort.SelectedItem.ToString()!;
        /*
        if (!SerialPort.GetPortNames().Contains(comportName))
        {
            MessageBox.Show($"Port {comportName} není dostupný.\nOvěřte připojení zařízení.", "Port nedostupný", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        */
        Logger.Information("Zahajuji deploy: repo={Repo}, branch={Branch}, env={Env}, port={Port}",
            repositoryName, branchName, environmentName, comportName);

        var progress = new Progress<string>(msg => lblStatus.Text = msg);

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var runner = new IoTDeploy.Runner();

        SetUiBusy("Spouštím deploy...");
        lblResult.Visible = false;
        try
        {
            var token = await githubProvider.GetTokenForRunner(repositoryName);
            await runner.Download(progress, ct);
            await runner.Config(settings.GitHub.Owner, repositoryName, token.Token, settings.Runner.Labels, progress, ct);
            await runner.DownloadTools(progress, ct);
            var (_, runId) = await githubProvider.RunWorkflow(repositoryName, branchName, environmentName,
                new Dictionary<string, string> { { "serial_port", comportName } }, progress, ct);

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
            MessageBox.Show(ex.Message, "Timeout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "Chyba deploye");
            ShowDeployResult("failure");
            MessageBox.Show(ex.Message, "Chyba deploye", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Neočekávaná chyba při deployi");
            ShowDeployResult("failure");
            MessageBox.Show($"Neočekávaná chyba:\n{ex.Message}", "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        SetUiBusy("Načítám větve a prostředí...");
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
            SetUiIdle("Připraveno.");
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex, "Chyba při načítání větví/prostředí pro repo={Repo}", repositoryName);
            SetUiIdle("");
            MessageBox.Show(ex.Message, "Chyba načítání", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Neočekávaná chyba při načítání repo={Repo}", repositoryName);
            SetUiIdle("");
            MessageBox.Show($"Neočekávaná chyba:\n{ex.Message}", "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show($"Log soubor zatím neexistuje.\nOčekávaná složka: {logDir}",
                "Log nenalezen", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                lblResult.Text = "Deploy dokončen úspěšně";
                lblResult.ForeColor = Color.Green;
                break;
            case "cancelled":
                lblResult.Text = "Deploy zrušen";
                lblResult.ForeColor = Color.Orange;
                break;
            default:
                lblResult.Text = $"Deploy selhal ({conclusion})";
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
        progressBar.Visible = false;
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.Value = 0;
        lblStatus.Text = status;
    }
}
