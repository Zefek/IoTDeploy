using Serilog;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace IoTDeploy;

public class Runner
{
    private static readonly ILogger Logger = Log.ForContext<Runner>();

    public async Task Download(IProgress<string> progress, CancellationToken ct = default)
    {
        var runnersDir = Path.Combine(AppContext.BaseDirectory, "runners");
        Directory.CreateDirectory(runnersDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("IoTDeploy");

        progress.Report(Strings.CheckingRunnerVersion);
        var json = await WithRetryAsync(() => http.GetStringAsync("https://api.github.com/repos/actions/runner/releases/latest", ct), ct);
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
            using var stream = await WithRetryAsync(() => http.GetStreamAsync(url, ct), ct);
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

        Logger.Debug("Rozbaluji runner do {ExtractPath}", Path.Combine(runnersDir, "runner1"));
        progress.Report(Strings.ExtractingRunner);
        var extractPath = Path.Combine(runnersDir, "runner1");
        if (Directory.Exists(extractPath))
            DeleteDirectory(extractPath);
        Directory.CreateDirectory(extractPath);
        ZipFile.ExtractToDirectory(zipPath, extractPath);
    }

    public async Task Config(string owner, string repository, string token, string[] labels, IProgress<string> progress, CancellationToken ct = default)
    {
        progress.Report(Strings.ConfiguringRunner);
        var guid = Guid.NewGuid();
        var path = Path.Combine(AppContext.BaseDirectory, "runners", "runner1", "config.cmd");
        var labelList = string.Join(",", labels);
        var args = $"--url https://github.com/{owner}/{repository} --name {guid} --labels {labelList} --ephemeral --unattended";
        var workDir = Path.Combine(AppContext.BaseDirectory, "runners", "runner1");
        var envVars = new Dictionary<string, string> { ["ACTIONS_RUNNER_INPUT_TOKEN"] = token };
        await RunProcessAsync(path, args, workDir, progress, Strings.RunnerConfigFailed, ct, envVars);
    }

    public async Task DownloadTools(IProgress<string> progress, CancellationToken ct = default)
    {
        var runnersDir = Path.Combine(AppContext.BaseDirectory, "runners");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("IoTDeploy");

        progress.Report(Strings.CheckingArduinoVersion);
        var json = await WithRetryAsync(() => http.GetStringAsync("https://api.github.com/repos/arduino/arduino-cli/releases/latest", ct), ct);
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString()!;
        var version = tag.TrimStart('v');

        var zipName = $"arduino-cli_{version}_Windows_64bit.zip";
        var zipPath = Path.Combine(runnersDir, zipName);

        if (!File.Exists(zipPath))
        {
            progress.Report(string.Format(Strings.DownloadingArduino, version));
            var assets = doc.RootElement.GetProperty("assets");
            var asset = assets.EnumerateArray().First(a => a.GetProperty("name").GetString() == zipName);
            var url = asset.GetProperty("browser_download_url").GetString()!;

            using var stream = await WithRetryAsync(() => http.GetStreamAsync(url, ct), ct);
            using var fileStream = File.Create(zipPath);
            await stream.CopyToAsync(fileStream, ct);

            foreach (var old in Directory.GetFiles(runnersDir, "arduino-cli_*_Windows_64bit.zip")
                .Where(f => f != zipPath))
            {
                File.Delete(old);
            }
        }
        else
        {
            progress.Report(string.Format(Strings.ArduinoAlreadyDownloaded, version));
        }

        progress.Report(Strings.ExtractingArduino);
        var extractPath = Path.Combine(AppContext.BaseDirectory, "runners", "runner1", "_work", "_tool", "arduino-cli");
        if (Directory.Exists(extractPath))
            DeleteDirectory(extractPath);
        Directory.CreateDirectory(extractPath);
        ZipFile.ExtractToDirectory(zipPath, extractPath);

        var configContent =
            $"""
            directories:
              data: '{extractPath}\\data'
              downloads: '{extractPath}\\downloads'
              libraries: '{extractPath}\\libraries'
              user: '{extractPath}\\user'
              builtin:
                libraries: '{extractPath}\\builtin.libraries'
            library:
              enable_unsafe_install: true
            """;
        await File.WriteAllTextAsync(Path.Combine(extractPath, "config.yaml"), configContent);
    }

    public async Task Run(IProgress<string> progress, CancellationToken ct = default)
    {
        progress.Report(Strings.RunnerRunning);
        var path = Path.Combine(AppContext.BaseDirectory, "runners", "runner1", "run.cmd");
        var workDir = Path.Combine(AppContext.BaseDirectory, "runners", "runner1");
        await RunProcessAsync(path, "", workDir, progress, Strings.RunnerFailed, ct);
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

        ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { } });

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
            throw new Exception($"{errorPrefix}:\n{detail}");
        }
        Logger.Debug("Proces {FileName} skončil úspěšně (exit code 0)", Path.GetFileName(fileName));
    }

    public void Delete()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "runners", "runner1");
        if (Directory.Exists(path))
        {
            DeleteDirectory(path);
            Logger.Debug("Runner adresář smazán");
        }
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
    }

    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> action, CancellationToken ct, int maxAttempts = 3)
    {
        var delay = TimeSpan.FromSeconds(1);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                Logger.Warning(ex, "HTTP chyba (pokus {Attempt}/{Max}), zkouším znovu za {Delay}s", attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                delay *= 2;
            }
        }
    }
}
