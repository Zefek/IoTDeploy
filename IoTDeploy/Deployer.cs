using Serilog;

namespace IoTDeploy;

internal class Deployer
{
    private static readonly ILogger Logger = Log.ForContext<Deployer>();
    private readonly AppSettings _settings;
    private readonly GithubProvider _github;
    private readonly Runner _runner;
    private readonly IProgress<string> _progress;
    private readonly IReadOnlyList<IPrerequisite> _prerequisites;

    public Deployer(AppSettings settings, GithubProvider github, Runner runner, IProgress<string> progress, IReadOnlyList<IPrerequisite> prerequisites)
    {
        _settings = settings;
        _github = github;
        _runner = runner;
        _progress = progress;
        _prerequisites = prerequisites;
    }

    public async Task<int> RunAsync(CliArgs cli, CancellationToken ct)
    {
        Console.WriteLine(Strings.ConnectingToGitHub);
        await _github.Init();

        var workflows = await _github.GetWorkflows(cli.Repo);
        if (workflows.Count == 0)
            throw new InvalidOperationException(string.Format(Strings.WorkflowsNotFound, cli.Repo));

        var selectedWorkflow = SelectWorkflow(workflows, cli);
        Console.WriteLine(string.Format(Strings.DeployInfo, cli.Repo, cli.Branch, selectedWorkflow.Name, cli.Env, cli.Port ?? "-"));

        var payload = await BuildPayloadAsync(cli, ct);
        var runId = await _github.RunWorkflow(cli.Repo, cli.Branch, selectedWorkflow.Id, payload, _progress, ct);

        Console.WriteLine(Strings.FetchingJobLabels);
        var requiredLabels = await _github.GetQueuedJobLabelsAsync(cli.Repo, runId, ct);
        Console.WriteLine(string.Format(Strings.RequiredLabels, string.Join(", ", requiredLabels)));

        var token = await _github.GetTokenForRunner(cli.Repo);
        await _runner.Download(_progress, ct);
        await _runner.Config(_settings.GitHub.Owner, cli.Repo, token.Token, requiredLabels.ToArray(), _progress, ct);
        await _runner.Provision(_prerequisites, _progress, ct);

        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var monitorTask = MonitorProgressAsync(cli.Repo, runId, monitorCts.Token);

        await _runner.Run(_progress, ct);
        await monitorCts.CancelAsync();
        try { await monitorTask; } catch (OperationCanceledException) { Logger.Debug("Monitorovací úloha ukončena"); }

        return await WaitForConclusionAsync(cli.Repo, runId, ct);
    }

    private static WorkflowInfo SelectWorkflow(IReadOnlyList<WorkflowInfo> workflows, CliArgs cli)
    {
        if (string.IsNullOrEmpty(cli.WorkflowName))
        {
            if (workflows.Count > 1)
                throw new InvalidOperationException(string.Format(Strings.WorkflowAmbiguous,
                    cli.Repo, string.Join(", ", workflows.Select(w => w.Name))));
            return workflows[0];
        }

        return workflows.FirstOrDefault(w =>
            string.Equals(w.Name, cli.WorkflowName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(w.Path), cli.WorkflowName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(w.Path, cli.WorkflowName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(string.Format(Strings.WorkflowNotFound,
                cli.WorkflowName, cli.Repo, string.Join(", ", workflows.Select(w => w.Name))));
    }

    private async Task<Dictionary<string, string>> BuildPayloadAsync(CliArgs cli, CancellationToken ct)
    {
        var payload = new Dictionary<string, string> { ["environment"] = cli.Env };
        if (!string.IsNullOrEmpty(cli.Port))
            payload["serial_port"] = cli.Port;

        if (string.IsNullOrEmpty(cli.UseArtifact))
            return payload;

        if (!string.Equals(cli.UseArtifact, "latest", StringComparison.OrdinalIgnoreCase))
        {
            payload["artifact_run_id"] = cli.UseArtifact;
            if (!string.IsNullOrEmpty(cli.ArtifactName))
                payload["artifact_name"] = cli.ArtifactName;
            return payload;
        }

        Console.WriteLine(string.Format(Strings.ResolvingLatestArtifact, cli.Branch));
        var info = await _github.ResolveLatestArtifactAsync(cli.Repo, cli.Branch, cli.ArtifactName, null, ct);
        Console.WriteLine(string.Format(Strings.ResolvedLatestArtifact,
            info.RunId, info.ShortSha, info.CreatedAt, info.ArtifactName));
        payload["artifact_run_id"] = info.RunId.ToString();
        payload["artifact_name"] = info.ArtifactName;
        return payload;
    }

    private async Task MonitorProgressAsync(string repository, long runId, CancellationToken ct)
    {
        var lastStepName = "";
        await Task.Delay(3000, ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var wp = await _github.GetWorkflowProgressAsync(repository, runId);
                lastStepName = ReportStep(wp, lastStepName);
                if (wp is { IsCompleted: true }) return;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Chyba při monitorování průběhu workflow");
            }
            await Task.Delay(5000, ct);
        }
    }

    private static string ReportStep(WorkflowProgress? wp, string lastStepName)
    {
        if (wp is not { TotalSteps: > 0 })
            return lastStepName;

        var stepName = wp.CurrentStepName;
        if (string.IsNullOrEmpty(stepName) || stepName == lastStepName)
            return lastStepName;

        Console.WriteLine($"  [{wp.CompletedSteps}/{wp.TotalSteps}] {wp.JobName}: {stepName}");
        return stepName;
    }

    private async Task<int> WaitForConclusionAsync(string repository, long runId, CancellationToken ct)
    {
        for (var i = 0; i < 6; i++)
        {
            WorkflowProgress? wp = null;
            try
            {
                wp = await _github.GetWorkflowProgressAsync(repository, runId);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Nepodařilo se načíst výsledek workflow");
            }

            if (wp is { IsCompleted: true, Conclusion: not null })
                return await ReportConclusionAsync(wp.Conclusion);

            await Task.Delay(3000, ct);
        }

        Console.WriteLine(Strings.DeployUnknown);
        return 0;
    }

    private static async Task<int> ReportConclusionAsync(string conclusion)
    {
        if (conclusion == "success")
        {
            Console.WriteLine(Strings.DeploySuccess);
            return 0;
        }

        await Console.Error.WriteLineAsync(string.Format(Strings.DeployFailed, conclusion));
        return 1;
    }
}
