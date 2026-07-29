using Serilog;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace IoTDeploy;

public class Runner
{
    private static readonly ILogger Logger = Log.ForContext<Runner>();

    private readonly string _name;
    private readonly string _runnerDir;
    private readonly AppSettings _settings;
    private string[] _labels = [];

    public Runner(string name, AppSettings settings)
    {
        _name = name;
        _settings = settings;
        _runnerDir = Path.Combine(AppContext.BaseDirectory, "runners", name);
    }

    public async Task Download(IProgress<string> progress, CancellationToken ct = default)
    {
        var runnersDir = Path.Combine(AppContext.BaseDirectory, "runners");
        Directory.CreateDirectory(runnersDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("IoTDeploy");

        progress.Report(Strings.CheckingRunnerVersion);
        var json = await HttpRetry.ExecuteAsync(() => http.GetStringAsync(_settings.Runner.RunnerReleasesApiUrl, ct), ct);
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString()!;
        var version = tag.TrimStart('v');

        var zipName = $"actions-runner-win-x64-{version}.zip";
        var zipPath = Path.Combine(runnersDir, zipName);

        if (!File.Exists(zipPath))
        {
            progress.Report(string.Format(Strings.DownloadingRunner, version));
            Logger.Information("Stahuji runner {Version}", version);
            var url = $"https://github.com/actions/runner/releases/download/{tag}/{zipName}";
            using var stream = await HttpRetry.ExecuteAsync(() => http.GetStreamAsync(url, ct), ct);
            using var fileStream = File.Create(zipPath);
            await stream.CopyToAsync(fileStream, ct);

            foreach (var old in Directory.GetFiles(runnersDir, "actions-runner-win-x64-*.zip")
                .Where(f => f != zipPath))
            {
                File.Delete(old);
            }
        }
        else
        {
            Logger.Debug("Runner {Version} již je stažen, přeskakuji stahování", version);
            progress.Report(string.Format(Strings.RunnerAlreadyDownloaded, version));
        }

        Logger.Debug("Rozbaluji runner do {ExtractPath}", _runnerDir);
        progress.Report(Strings.ExtractingRunner);
        if (Directory.Exists(_runnerDir))
            DeleteDirectory(_runnerDir);
        Directory.CreateDirectory(_runnerDir);
        await ZipFile.ExtractToDirectoryAsync(zipPath, _runnerDir, ct);
    }

    public async Task Config(string owner, string repository, string token, string[] labels, IProgress<string> progress, CancellationToken ct = default)
    {
        progress.Report(Strings.ConfiguringRunner);
        _labels = labels;
        var path = Path.Combine(_runnerDir, "config.cmd");
        var labelList = string.Join(",", labels);
        var args = $"--url https://github.com/{owner}/{repository} --name {_name} --labels {labelList} --ephemeral --unattended";
        var envVars = new Dictionary<string, string> { ["ACTIONS_RUNNER_INPUT_TOKEN"] = token };
        await RunProcessAsync(path, args, _runnerDir, progress, Strings.RunnerConfigFailed, ct, envVars);
    }

    public async Task Provision(IEnumerable<IPrerequisite> prerequisites, IProgress<string> progress, CancellationToken ct = default)
    {
        foreach (var prerequisite in prerequisites.Where(p => _labels.Contains(p.Label, StringComparer.OrdinalIgnoreCase)))
        {
            Logger.Debug("Naseeduji prerekvizitu {Label} dle labelů runneru", prerequisite.Label);
            await prerequisite.ProvisionAsync(_runnerDir, progress, ct);
        }
    }

    public async Task Run(IProgress<string> progress, CancellationToken ct = default)
    {
        progress.Report(Strings.RunnerRunning);
        var path = Path.Combine(_runnerDir, "run.cmd");
        await RunProcessAsync(path, "", _runnerDir, progress, Strings.RunnerFailed, ct);
    }

    private static async Task RunProcessAsync(string fileName, string arguments, string workingDir, IProgress<string> progress, string errorPrefix, CancellationToken ct = default, Dictionary<string, string>? envVars = null)
    {
        var stderrLines = new List<string>();

        var p = new Process();
        p.StartInfo.FileName = fileName;
        p.StartInfo.Arguments = arguments;
        p.StartInfo.WorkingDirectory = workingDir;
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.RedirectStandardError = true;
        p.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
        p.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;

        if (envVars != null)
            foreach (var (key, value) in envVars)
                p.StartInfo.Environment[key] = value;

        ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (Exception ex) { Logger.Debug(ex, "Nepodařilo se ukončit proces {FileName}", Path.GetFileName(fileName)); } });

        p.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Logger.Debug("[{Process}] {Line}", Path.GetFileName(fileName), e.Data);
                progress.Report(e.Data);
            }
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Logger.Warning("[{Process}] STDERR: {Line}", Path.GetFileName(fileName), e.Data);
                stderrLines.Add(e.Data);
            }
        };

        Logger.Debug("Spouštím proces {FileName} v {WorkingDir}", fileName, workingDir);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(ct);

        if (p.ExitCode != 0)
        {
            var detail = stderrLines.Count > 0
                ? string.Join("\n", stderrLines)
                : $"exit code {p.ExitCode}";
            Logger.Error("{ErrorPrefix}: {Detail}", errorPrefix, detail);
            throw new InvalidOperationException($"{errorPrefix}:\n{detail}");
        }
        Logger.Debug("Proces {FileName} skončil úspěšně (exit code 0)", Path.GetFileName(fileName));
    }

    public void Delete()
    {
        if (Directory.Exists(_runnerDir))
        {
            DeleteDirectory(_runnerDir);
            Logger.Debug("Runner adresář {Path} smazán", _runnerDir);
        }
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
    }
}
