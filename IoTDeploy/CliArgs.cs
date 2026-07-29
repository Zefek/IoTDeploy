namespace IoTDeploy;

internal record CliArgs(
    string Repo,
    string Branch,
    string Env,
    string? Port,
    string? UseArtifact,
    string? ArtifactName,
    string? WorkflowName);
