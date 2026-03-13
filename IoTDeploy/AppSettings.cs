namespace IoTDeploy;

public class AppSettings
{
    public GitHubSettings GitHub { get; set; } = new();
    public RunnerSettings Runner { get; set; } = new();

    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(GitHub.AppId))
            yield return Strings.ValidateAppId;
        if (GitHub.InstallationId == 0)
            yield return Strings.ValidateInstallationId;
        if (string.IsNullOrWhiteSpace(GitHub.Owner))
            yield return Strings.ValidateOwner;
        if (string.IsNullOrWhiteSpace(GitHub.PemFilePattern))
            yield return Strings.ValidatePemFilePattern;
        if (Runner.WorkflowTimeoutMinutes <= 0)
            yield return Strings.ValidateWorkflowTimeout;
        if (Runner.Labels.Length == 0)
            yield return Strings.ValidateLabels;
    }
}

public class GitHubSettings
{
    public string AppId { get; set; } = "";
    public long InstallationId { get; set; }
    public string Owner { get; set; } = "";
    public string PemFilePattern { get; set; } = "iot-deployer.*.private-key.pem";
}

public class RunnerSettings
{
    public int WorkflowTimeoutMinutes { get; set; } = 2;
    public string[] Labels { get; set; } = ["iot"];
}
