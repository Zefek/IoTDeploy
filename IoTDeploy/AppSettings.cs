namespace IoTDeploy;

public class AppSettings
{
    public GitHubSettings GitHub { get; set; } = new();
    public RunnerSettings Runner { get; set; } = new();

    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(GitHub.AppId))
            yield return "GitHub.AppId není nastaveno";
        if (GitHub.InstallationId == 0)
            yield return "GitHub.InstallationId není nastaveno";
        if (string.IsNullOrWhiteSpace(GitHub.Owner))
            yield return "GitHub.Owner není nastaveno";
        if (string.IsNullOrWhiteSpace(GitHub.PemFilePattern))
            yield return "GitHub.PemFilePattern není nastaveno";
        if (Runner.WorkflowTimeoutMinutes <= 0)
            yield return "Runner.WorkflowTimeoutMinutes musí být větší než 0";
        if (Runner.Labels.Length == 0)
            yield return "Runner.Labels nesmí být prázdné";
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
