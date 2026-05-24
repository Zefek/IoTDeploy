using Microsoft.IdentityModel.Tokens;
using Octokit;
using Octokit.Models.Response;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;

namespace IoTDeploy;

public record WorkflowProgress(
    int CompletedSteps,
    int TotalSteps,
    string CurrentStepName,
    string JobName,
    bool IsCompleted,
    string? Conclusion);

public record ArtifactRunInfo(
    long RunId,
    string HeadSha,
    string ShortSha,
    DateTimeOffset CreatedAt,
    string ArtifactName,
    long ArtifactId,
    long SizeInBytes);

public record WorkflowInfo(long Id, string Name, string Path);

public class GithubProvider
{
    private static readonly ILogger Logger = Log.ForContext<GithubProvider>();

    private GitHubClient gitHubClient;
    private string _appId;
    private long _installationId;
    private string _owner;
    private int _workflowTimeoutMinutes;
    private string _pemKeyPath;
    private AccessToken? _installationAccessToken;

    public GithubProvider()
    {
    }

    public async Task Init(AppSettings settings)
    {
        _appId = settings.GitHub.AppId;
        _installationId = settings.GitHub.InstallationId;
        _owner = settings.GitHub.Owner;
        _workflowTimeoutMinutes = settings.Runner.WorkflowTimeoutMinutes;
        _pemKeyPath = FindPemFile(settings.GitHub.PemFilePattern);
        Logger.Information("Inicializuji GitHub App klienta (AppId={AppId}, Owner={Owner})", _appId, _owner);
        gitHubClient = await CreateInstallationClient();
        Logger.Information("GitHub klient úspěšně inicializován");
    }

    private static string FindPemFile(string pattern)
    {
        var matches = Directory.GetFiles(AppContext.BaseDirectory, pattern);
        if (matches.Length == 0)
            throw new FileNotFoundException(string.Format(Strings.PemFileNotFound, pattern));
        return matches[0];
    }

    private async Task<GitHubClient> CreateInstallationClient()
    {
        string pemContent;
        try
        {
            pemContent = File.ReadAllText(_pemKeyPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(string.Format(Strings.CannotReadPrivateKey, _pemKeyPath, ex.Message), ex);
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pemContent);
            var jwt = GenerateJwt(_appId, rsa);

            var jwtClient = new GitHubClient(new ProductHeaderValue("IoTDeploy"))
            {
                Credentials = new Credentials(jwt, AuthenticationType.Bearer)
            };

            _installationAccessToken = await jwtClient.GitHubApps.CreateInstallationToken(_installationId);
        }
        catch (AuthorizationException)
        {
            throw new InvalidOperationException(Strings.AuthFailed);
        }
        catch (NotFoundException)
        {
            throw new InvalidOperationException(string.Format(Strings.InstallationNotFound, _installationId));
        }

        return new GitHubClient(new ProductHeaderValue("IoTDeploy"))
        {
            Credentials = new Credentials(_installationAccessToken.Token)
        };
    }

    private string GenerateJwt(string appId, RSA rsaKey)
    {
        var now = DateTimeOffset.UtcNow;

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = appId,
            IssuedAt = now.AddSeconds(-30).UtcDateTime,
            Expires = now.AddMinutes(10).UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(rsaKey),
                SecurityAlgorithms.RsaSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        handler.OutboundClaimTypeMap.Clear();
        return handler.WriteToken(handler.CreateToken(tokenDescriptor));
    }

    private async Task EnsureValidTokenAsync()
    {
        if (_installationAccessToken == null ||
            DateTimeOffset.UtcNow >= _installationAccessToken.ExpiresAt.AddMinutes(-5))
        {
            Logger.Debug("Obnovuji installation access token (vyprší {ExpiresAt})", _installationAccessToken?.ExpiresAt);
            gitHubClient = await CreateInstallationClient();
            Logger.Debug("Token obnoven, platí do {ExpiresAt}", _installationAccessToken?.ExpiresAt);
        }
    }

    public async Task<IReadOnlyList<Repository>> GetRepositories()
    {
        await EnsureValidTokenAsync();
        var result = await gitHubClient.GitHubApps.Installation.GetAllRepositoriesForCurrent();
        return result.Repositories;
    }

    public async Task<IReadOnlyList<Branch>> GetBranches(string repository)
    {
        await EnsureValidTokenAsync();
        try
        {
            return await gitHubClient.Repository.Branch.GetAll(_owner, repository);
        }
        catch (NotFoundException)
        {
            throw new InvalidOperationException(string.Format(Strings.RepositoryNotFound, repository));
        }
    }

    public async Task<IReadOnlyList<DeploymentEnvironment>> GetEnvironments(string repository)
    {
        await EnsureValidTokenAsync();
        return (await gitHubClient.Repository.Environment.GetAll(_owner, repository)).Environments;
    }

    public async Task<IReadOnlyList<WorkflowInfo>> GetWorkflows(string repository)
    {
        await EnsureValidTokenAsync();
        try
        {
            var response = await gitHubClient.Actions.Workflows.List(_owner, repository);
            return response.Workflows
                .Where(w => string.Equals(w.State.StringValue, "active", StringComparison.OrdinalIgnoreCase))
                .Select(w => new WorkflowInfo(w.Id, w.Name, w.Path))
                .ToList();
        }
        catch (NotFoundException)
        {
            throw new InvalidOperationException(string.Format(Strings.RepositoryNotFound, repository));
        }
    }

    public async Task<long> RunWorkflow(string repository, string branchName, long workflowId, Dictionary<string, string> parameters, IProgress<string> progress, CancellationToken ct = default)
    {
        await EnsureValidTokenAsync();

        progress.Report(Strings.StartingWorkflow);
        var createdAt = DateTimeOffset.UtcNow;
        try
        {
            var dispatch = new CreateWorkflowDispatch(branchName)
            {
                Inputs = parameters.ToDictionary(kv => kv.Key, kv => (object)kv.Value)
            };

            await gitHubClient.Actions.Workflows.CreateDispatch(_owner, repository, workflowId, dispatch);
        }
        catch (NotFoundException)
        {
            throw new InvalidOperationException(string.Format(Strings.BranchNotFound, branchName, repository));
        }
        catch (ApiValidationException ex)
        {
            throw new InvalidOperationException(string.Format(Strings.DispatchFailed, ex.Message), ex);
        }

        Logger.Information("Workflow dispatch vytvořen (WorkflowId={WorkflowId}, repo={Repo}, branch={Branch})",
            workflowId, repository, branchName);

        progress.Report(Strings.WaitingForApproval);
        var run = await WaitForQueuedRunAsync(repository, workflowId, createdAt, progress, ct);

        Logger.Information("Workflow run spuštěn (RunId={RunId})", run.Id);
        return run.Id;
    }

    private async Task<WorkflowRun> WaitForQueuedRunAsync(string repository, long workflowId, DateTimeOffset createdAfter, IProgress<string> progress, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(_workflowTimeoutMinutes);
        var attempt = 0;
        Logger.Debug("Čekám na workflow run v repo={Repo}, workflow={WorkflowId} (timeout={Timeout}min)", repository, workflowId, _workflowTimeoutMinutes);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(3000, ct);
            attempt++;
            progress.Report(string.Format(Strings.WaitingForWorkflow, attempt * 3));
            var runs = await gitHubClient.Actions.Workflows.Runs.ListByWorkflow(
                _owner, repository, workflowId);

            var run = runs.WorkflowRuns.FirstOrDefault(r =>
                r.CreatedAt >= createdAfter &&
                r.Status.StringValue != "completed");
            if (run != null) return run;
        }
        Logger.Warning("Timeout při čekání na workflow run (repo={Repo}, workflow={WorkflowId}, timeout={Timeout}min)", repository, workflowId, _workflowTimeoutMinutes);
        throw new TimeoutException(string.Format(Strings.WorkflowTimeout, _workflowTimeoutMinutes));
    }

    public async Task<IReadOnlyList<string>> GetQueuedJobLabelsAsync(string repository, long runId, CancellationToken ct = default)
    {
        await EnsureValidTokenAsync();
        var jobs = await gitHubClient.Actions.Workflows.Jobs.List(_owner, repository, runId);
        var job = jobs.Jobs.FirstOrDefault(j => j.Status.StringValue == "queued")
               ?? jobs.Jobs.FirstOrDefault();
        if (job == null)
            throw new InvalidOperationException(string.Format(Strings.NoJobsInRun, runId));
        return job.Labels ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public async Task<AccessToken> GetTokenForRunner(string repository)
    {
        await EnsureValidTokenAsync();
        try
        {
            return await gitHubClient.Actions.SelfHostedRunners.CreateRepositoryRegistrationToken(_owner, repository);
        }
        catch (NotFoundException)
        {
            throw new InvalidOperationException(string.Format(Strings.RepositoryNotFoundOrNoActions, repository));
        }
    }

    public async Task<IReadOnlyList<ArtifactRunInfo>> GetSuccessfulRunsWithArtifactsAsync(
        string repository, string branch, string? artifactName, string? workflowName, int top, CancellationToken ct = default)
    {
        await EnsureValidTokenAsync();

        var request = new WorkflowRunsRequest
        {
            Status = new StringEnum<CheckRunStatusFilter>(CheckRunStatusFilter.Success),
            Branch = branch
        };
        var options = new ApiOptions { PageSize = Math.Max(top * 3, 30), PageCount = 1 };

        WorkflowRunsResponse runs;
        try
        {
            runs = string.IsNullOrEmpty(workflowName)
                ? await gitHubClient.Actions.Workflows.Runs.List(_owner, repository, request, options)
                : await gitHubClient.Actions.Workflows.Runs.ListByWorkflow(_owner, repository, workflowName, request, options);
        }
        catch (NotFoundException)
        {
            throw new InvalidOperationException(string.Format(Strings.RepositoryNotFound, repository));
        }

        var result = new List<ArtifactRunInfo>();
        foreach (var run in runs.WorkflowRuns)
        {
            if (result.Count >= top) break;
            ct.ThrowIfCancellationRequested();

            ListArtifactsResponse artifacts;
            try
            {
                artifacts = await gitHubClient.Actions.Artifacts.ListWorkflowArtifacts(_owner, repository, run.Id);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Nepodařilo se načíst artefakty pro run {RunId}", run.Id);
                continue;
            }

            var match = artifacts.Artifacts.FirstOrDefault(a =>
                !a.Expired && (string.IsNullOrEmpty(artifactName) || a.Name == artifactName));
            if (match == null) continue;

            var sha = run.HeadSha ?? "";
            result.Add(new ArtifactRunInfo(
                run.Id, sha, sha.Length >= 7 ? sha[..7] : sha,
                run.CreatedAt, match.Name, match.Id, match.SizeInBytes));
        }
        return result;
    }

    public async Task<ArtifactRunInfo> ResolveLatestArtifactAsync(
        string repository, string branch, string? artifactName, string? workflowName, CancellationToken ct = default)
    {
        var list = await GetSuccessfulRunsWithArtifactsAsync(repository, branch, artifactName, workflowName, 1, ct);
        if (list.Count == 0)
            throw new InvalidOperationException(string.Format(Strings.NoArtifactFound,
                artifactName ?? "*", branch, repository));
        return list[0];
    }

    public async Task<WorkflowProgress?> GetWorkflowProgressAsync(string repository, long runId)
    {
        await EnsureValidTokenAsync();
        var run = await gitHubClient.Actions.Workflows.Runs.Get(_owner, repository, runId);
        var isCompleted = run.Status.StringValue == "completed";
        var conclusion = isCompleted ? (run.Conclusion?.StringValue ?? "unknown") : null;

        try
        {
            var jobs = await gitHubClient.Actions.Workflows.Jobs.List(_owner, repository, runId);
            var job = jobs.Jobs.FirstOrDefault(j => j.Status.StringValue == "in_progress")
                ?? jobs.Jobs.LastOrDefault();

            if (job?.Steps != null && job.Steps.Count > 0)
            {
                var total = job.Steps.Count;
                var completed = job.Steps.Count(s => !string.IsNullOrEmpty(s.Conclusion?.StringValue));
                var current = job.Steps.FirstOrDefault(s => s.Status.StringValue == "in_progress");
                var stepName = current?.Name
                    ?? job.Steps.LastOrDefault(s => !string.IsNullOrEmpty(s.Conclusion?.StringValue))?.Name
                    ?? "";
                return new WorkflowProgress(completed, total, stepName, job.Name ?? "", isCompleted, conclusion);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Nepodařilo se načíst kroky workflow run {RunId}", runId);
        }

        return isCompleted ? new WorkflowProgress(0, 0, "", "", true, conclusion) : null;
    }
}
