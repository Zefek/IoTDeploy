using Serilog;
using System.IO.Compression;
using System.Text.Json;

namespace IoTDeploy;

public class ArduinoCliPrerequisite : IPrerequisite
{
    public const string LabelName = "iot";

    private static readonly ILogger Logger = Log.ForContext<ArduinoCliPrerequisite>();
    private readonly AppSettings _settings;

    public ArduinoCliPrerequisite(AppSettings settings)
    {
        _settings = settings;
    }

    public string Label => LabelName;

    public async Task ProvisionAsync(string runnerDir, IProgress<string> progress, CancellationToken ct = default)
    {
        var cacheDir = Path.Combine(AppContext.BaseDirectory, "runners");
        Directory.CreateDirectory(cacheDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("IoTDeploy");

        progress.Report(Strings.CheckingArduinoVersion);
        var json = await HttpRetry.ExecuteAsync(() => http.GetStringAsync(_settings.Runner.ArduinoReleasesApiUrl, ct), ct);
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString()!;
        var version = tag.TrimStart('v');

        var zipName = $"arduino-cli_{version}_Windows_64bit.zip";
        var zipPath = Path.Combine(cacheDir, zipName);

        if (!File.Exists(zipPath))
        {
            progress.Report(string.Format(Strings.DownloadingArduino, version));
            var assets = doc.RootElement.GetProperty("assets");
            var asset = assets.EnumerateArray().First(a => a.GetProperty("name").GetString() == zipName);
            var url = asset.GetProperty("browser_download_url").GetString()!;

            using var stream = await HttpRetry.ExecuteAsync(() => http.GetStreamAsync(url, ct), ct);
            using var fileStream = File.Create(zipPath);
            await stream.CopyToAsync(fileStream, ct);

            foreach (var old in Directory.GetFiles(cacheDir, "arduino-cli_*_Windows_64bit.zip")
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
        var extractPath = Path.Combine(runnerDir, "_work", "_tool", "arduino-cli");
        var arduinoCachePath = Path.Combine(AppContext.BaseDirectory, "arduino_cache");
        if (Directory.Exists(extractPath))
            DeleteDirectory(extractPath);
        Directory.CreateDirectory(extractPath);
        await ZipFile.ExtractToDirectoryAsync(zipPath, extractPath, ct);
        if (!Directory.Exists(arduinoCachePath))
            Directory.CreateDirectory(arduinoCachePath);

        var configContent =
            $"""
            directories:
              data: '{arduinoCachePath}\\data'
              downloads: '{arduinoCachePath}\\downloads'
              libraries: '{arduinoCachePath}\\libraries'
              user: '{extractPath}\\user'
              builtin:
                libraries: '{arduinoCachePath}\\builtin.libraries'
            library:
              enable_unsafe_install: true
            """;
        await File.WriteAllTextAsync(Path.Combine(extractPath, "config.yaml"), configContent, ct);
        Logger.Debug("Arduino CLI {Version} naseedováno do {ExtractPath}", version, extractPath);
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
    }
}
