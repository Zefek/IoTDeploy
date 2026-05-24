using IoTDeploy;
using Serilog;
using System.Text.Json;

if (args.Length == 1 && args[0] is "-h" or "--help")
{
    Console.WriteLine(Strings.Usage);
    return 0;
}

CliArgs cli;
try
{
    cli = ParseArgs(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Strings.Usage);
    return 1;
}

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

var configErrors = settings.Validate().ToList();
if (configErrors.Count > 0)
{
    Console.Error.WriteLine(Strings.InvalidConfiguration);
    foreach (var e in configErrors) Console.Error.WriteLine($"  • {e}");
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
var runner = new Runner(Guid.NewGuid().ToString("N"));
try
{
    Console.WriteLine(Strings.ConnectingToGitHub);
    await githubProvider.Init(settings);

    var workflows = await githubProvider.GetWorkflows(cli.Repo);
    if (workflows.Count == 0)
        throw new InvalidOperationException(string.Format(Strings.WorkflowsNotFound, cli.Repo));

    WorkflowInfo selectedWorkflow;
    if (string.IsNullOrEmpty(cli.WorkflowName))
    {
        if (workflows.Count > 1)
            throw new InvalidOperationException(string.Format(Strings.WorkflowAmbiguous,
                cli.Repo, string.Join(", ", workflows.Select(w => w.Name))));
        selectedWorkflow = workflows[0];
    }
    else
    {
        selectedWorkflow = workflows.FirstOrDefault(w =>
            string.Equals(w.Name, cli.WorkflowName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(System.IO.Path.GetFileName(w.Path), cli.WorkflowName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(w.Path, cli.WorkflowName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(string.Format(Strings.WorkflowNotFound,
                cli.WorkflowName, cli.Repo, string.Join(", ", workflows.Select(w => w.Name))));
    }

    Console.WriteLine(string.Format(Strings.DeployInfo, cli.Repo, cli.Branch, selectedWorkflow.Name, cli.Port ?? "-"));

    var payload = new Dictionary<string, string>();
    if (!string.IsNullOrEmpty(cli.Port))
        payload["serial_port"] = cli.Port;

    if (!string.IsNullOrEmpty(cli.UseArtifact))
    {
        if (string.Equals(cli.UseArtifact, "latest", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(string.Format(Strings.ResolvingLatestArtifact, cli.Branch));
            var info = await githubProvider.ResolveLatestArtifactAsync(
                cli.Repo, cli.Branch, cli.ArtifactName, null, cts.Token);
            Console.WriteLine(string.Format(Strings.ResolvedLatestArtifact,
                info.RunId, info.ShortSha, info.CreatedAt, info.ArtifactName));
            payload["artifact_run_id"] = info.RunId.ToString();
            payload["artifact_name"] = info.ArtifactName;
        }
        else
        {
            payload["artifact_run_id"] = cli.UseArtifact;
            if (!string.IsNullOrEmpty(cli.ArtifactName))
                payload["artifact_name"] = cli.ArtifactName;
        }
    }

    var runId = await githubProvider.RunWorkflow(cli.Repo, cli.Branch, selectedWorkflow.Id, payload, progress, cts.Token);

    Console.WriteLine(Strings.FetchingJobLabels);
    var requiredLabels = await githubProvider.GetQueuedJobLabelsAsync(cli.Repo, runId, cts.Token);
    Console.WriteLine(string.Format(Strings.RequiredLabels, string.Join(", ", requiredLabels)));

    var token = await githubProvider.GetTokenForRunner(cli.Repo);
    await runner.Download(progress, cts.Token);
    await runner.Config(settings.GitHub.Owner, cli.Repo, token.Token, requiredLabels.ToArray(), progress, cts.Token);
    await runner.DownloadTools(progress, cts.Token);

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
                var wp = await githubProvider.GetWorkflowProgressAsync(cli.Repo, runId);
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
            var wp = await githubProvider.GetWorkflowProgressAsync(cli.Repo, runId);
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

static CliArgs ParseArgs(string[] args)
{
    var positional = new List<string>();
    string? useArtifact = null;
    string? artifactName = null;
    string? workflowName = null;

    for (var i = 0; i < args.Length; i++)
    {
        var a = args[i];
        switch (a)
        {
            case "--useartifact":
                useArtifact = RequireValue(args, ref i, a);
                break;
            case "--artifact-name":
                artifactName = RequireValue(args, ref i, a);
                break;
            case "--workflow":
                workflowName = RequireValue(args, ref i, a);
                break;
            default:
                if (a.StartsWith("--"))
                    throw new ArgumentException(string.Format(Strings.UnknownFlag, a));
                positional.Add(a);
                break;
        }
    }

    if (positional.Count is < 2 or > 3)
        throw new ArgumentException(Strings.ErrorInvalidArgs);

    return new CliArgs(
        Repo: positional[0],
        Branch: positional[1],
        Port: positional.Count == 3 ? positional[2] : null,
        UseArtifact: useArtifact,
        ArtifactName: artifactName,
        WorkflowName: workflowName);
}

static string RequireValue(string[] args, ref int i, string flag)
{
    if (i + 1 >= args.Length)
        throw new ArgumentException(string.Format(Strings.MissingFlagValue, flag));
    return args[++i];
}

internal record CliArgs(
    string Repo,
    string Branch,
    string? Port,
    string? UseArtifact,
    string? ArtifactName,
    string? WorkflowName);
