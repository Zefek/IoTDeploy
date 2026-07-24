namespace IoTDeploy;

public interface IPrerequisite
{
    string Label { get; }

    Task ProvisionAsync(string runnerDir, IProgress<string> progress, CancellationToken ct = default);
}
