using IoTDeploy;
using Serilog;
using System.Text.Json;

var usage = Strings.Usage;

if (args.Length == 1 && args[0] is "-h" or "--help")
{
    Console.WriteLine(usage);
    return 0;
}

if (args.Length != 4)
{
    Console.Error.WriteLine(Strings.ErrorExpected4Args);
    Console.Error.WriteLine(usage);
    return 1;
}

var (repo, branch, env, port) = (args[0], args[1], args[2], args[3]);

AppSettings settings;
try
{
    settings = LoadSettings();
}
catch (Exception ex)
{
    Console.Error.WriteLine(string.Format(Strings.ErrorConfiguration, ex.Message));
    return 1;
}

var errors = settings.Validate().ToList();
if (errors.Count > 0)
{
    Console.Error.WriteLine(Strings.InvalidConfiguration);
    foreach (var e in errors) Console.Error.WriteLine($"  • {e}");
    return 1;
}

var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "IoTDeploy", "logs", "deployer-cli-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .CreateLogger();

var progress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine($"\n{Strings.CancellingDeploy}");
    cts.Cancel();
};

var githubProvider = new GithubProvider();
var runner = new Runner();
try
{
    Console.WriteLine(Strings.ConnectingToGitHub);
    await githubProvider.Init(settings);

    Console.WriteLine(string.Format(Strings.DeployInfo, repo, branch, env, port));

    var token = await githubProvider.GetTokenForRunner(repo);
    await runner.Download(progress, cts.Token);
    await runner.Config(settings.GitHub.Owner, repo, token.Token, settings.Runner.Labels, progress, cts.Token);
    await runner.DownloadTools(progress, cts.Token);
    var (_, runId) = await githubProvider.RunWorkflow(repo, branch, env,
        new Dictionary<string, string> { { "serial_port", port } }, progress, cts.Token);

    // Start monitoring workflow progress in background
    var lastStepName = "";
    using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
    var monitorTask = Task.Run(async () =>
    {
        await Task.Delay(3000, monitorCts.Token);
        while (!monitorCts.Token.IsCancellationRequested)
        {
            try
            {
                var wp = await githubProvider.GetWorkflowProgressAsync(repo, runId);
                if (wp != null && wp.TotalSteps > 0)
                {
                    var stepName = wp.CurrentStepName;
                    if (!string.IsNullOrEmpty(stepName) && stepName != lastStepName)
                    {
                        lastStepName = stepName;
                        Console.WriteLine($"  [{wp.CompletedSteps}/{wp.TotalSteps}] {wp.JobName}: {stepName}");
                    }
                }
                if (wp is { IsCompleted: true }) return;
            }
            catch { }
            await Task.Delay(5000, monitorCts.Token);
        }
    }, monitorCts.Token);

    await runner.Run(progress, cts.Token);
    monitorCts.Cancel();
    try { await monitorTask; } catch (OperationCanceledException) { }

    // Check final workflow conclusion
    for (var i = 0; i < 6; i++)
    {
        try
        {
            var wp = await githubProvider.GetWorkflowProgressAsync(repo, runId);
            if (wp is { IsCompleted: true, Conclusion: not null })
            {
                if (wp.Conclusion == "success")
                {
                    Console.WriteLine(Strings.DeploySuccess);
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine(string.Format(Strings.DeployFailed, wp.Conclusion));
                    return 1;
                }
            }
        }
        catch { }
        await Task.Delay(3000);
    }

    Console.WriteLine(Strings.DeployUnknown);
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine(Strings.DeployCancelled);
    return 1;
}
catch (TimeoutException ex)
{
    Console.Error.WriteLine(string.Format(Strings.TimeoutError, ex.Message));
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(string.Format(Strings.DeployError, ex.Message));
    Log.Error(ex, "Deploy error");
    return 1;
}
finally
{
    runner.Delete();
    Log.CloseAndFlush();
}

static AppSettings LoadSettings()
{
    var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(path))
        throw new FileNotFoundException(Strings.AppSettingsNotFound, path);

    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? throw new InvalidOperationException(Strings.AppSettingsInvalid);
}
