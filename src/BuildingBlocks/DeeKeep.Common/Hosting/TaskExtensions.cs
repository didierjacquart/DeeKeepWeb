namespace DeeKeep.Common.Hosting;

public static class TaskExtensions
{
    /// <summary>Attend un signal ou l'échéance, sans lever d'exception à l'arrêt du service.</summary>
    public static async Task WaitQuietlyAsync(this SemaphoreSlim semaphore, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await semaphore.WaitAsync(timeout, ct);
        }
        catch (OperationCanceledException)
        {
            // Arrêt demandé.
        }
    }
}
