using Serilog;
using System.Diagnostics;

namespace IoTDeploy;

internal static class HttpRetry
{
    private static readonly ILogger Logger = Log.ForContext(typeof(HttpRetry));

    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct, int maxAttempts = 3)
    {
        var delay = TimeSpan.FromSeconds(1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                Logger.Warning(ex, "HTTP chyba (pokus {Attempt}/{Max}), zkouším znovu za {Delay}s", attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                delay *= 2;
            }
        }
        throw new UnreachableException();
    }
}
