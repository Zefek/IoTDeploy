using Serilog;
using System.Text.Json;
using IoTDeploy;

namespace IoTDeployUI;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        AppSettings settings;
        try
        {
            settings = LoadSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Strings.ConfigErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var errors = settings.Validate().ToList();
        if (errors.Count > 0)
        {
            MessageBox.Show(
                Strings.InvalidConfigPrefix + string.Join("\n• ", errors),
                Strings.ConfigErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IoTDeploy", "logs", "deployer-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        try
        {
            Application.Run(new Form1(settings, logPath));
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static AppSettings LoadSettings()
    {
        var path = Path.Combine(Application.StartupPath, "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(Strings.AppSettingsNotFound, path);

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException(Strings.AppSettingsInvalid);
    }
}
