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
    await Console.Error.WriteLineAsync(ex.Message);
    await Console.Error.WriteLineAsync();
    await Console.Error.WriteLineAsync(Strings.Usage);
    return 1;
}

AppSettings settings;
try
{
    settings = LoadSettings();
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync(string.Format(Strings.ErrorConfiguration, ex.Message));
    return 1;
}

var configErrors = settings.Validate().ToList();
if (configErrors.Count > 0)
{
    await Console.Error.WriteLineAsync(Strings.InvalidConfiguration);
    foreach (var e in configErrors) await Console.Error.WriteLineAsync($"  • {e}");
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
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine($"\n{Strings.CancellingDeploy}");
    cts.Cancel();
};

var githubProvider = new GithubProvider(settings);
var runner = new Runner(Guid.NewGuid().ToString("N"), settings);
IReadOnlyList<IPrerequisite> prerequisites = [new ArduinoCliPrerequisite(settings)];
var deployer = new Deployer(settings, githubProvider, runner, progress, prerequisites);
try
{
    return await deployer.RunAsync(cli, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine(Strings.DeployCancelled);
    return 1;
}
catch (TimeoutException ex)
{
    await Console.Error.WriteLineAsync(string.Format(Strings.TimeoutError, ex.Message));
    return 1;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync(string.Format(Strings.DeployError, ex.Message));
    Log.Error(ex, "Deploy error");
    return 1;
}
finally
{
    runner.Delete();
    await Log.CloseAndFlushAsync();
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

    if (positional.Count is < 3 or > 4)
        throw new ArgumentException(Strings.ErrorInvalidArgs);

    return new CliArgs(
        Repo: positional[0],
        Branch: positional[1],
        Env: positional[2],
        Port: positional.Count == 4 ? positional[3] : null,
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
