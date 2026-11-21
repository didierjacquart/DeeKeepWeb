using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Web;

namespace DeeKeep.Gateway.Infrastructure;

/// <summary>
/// Rend rejouables les actions envoyées par la file hors ligne du client : une
/// requête <c>/api</c> (POST, PUT, DELETE) portant <c>X-Idempotency-Key</c> déjà
/// traitée avec succès renvoie la réponse mémorisée au lieu d'être exécutée une
/// seconde fois (une extraction relancée après une coupure ne doit pas produire
/// deux fichiers).
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, WebDb webDb, ILogger<IdempotencyMiddleware> logger)
{
    public const string Header = "X-Idempotency-Key";
    private static readonly TimeSpan Retention = TimeSpan.FromDays(3);
    private static DateTime _lastPurge = DateTime.MinValue;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var key = ctx.Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(key) || key.Length > 128 || HttpMethods.IsGet(ctx.Request.Method) || !ctx.Request.Path.StartsWithSegments("/api"))
        {
            await next(ctx);
            return;
        }

        string? stored;
        using (var conn = webDb.Open())
        {
            stored = conn.ExecuteScalar<string?>("SELECT response FROM idempotency WHERE key = @key", new { key });
        }
        if (stored is not null)
        {
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.Headers["X-Idempotent-Replay"] = "true";
            await ctx.Response.WriteAsync(stored);
            return;
        }

        var original = ctx.Response.Body;
        await using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        try
        {
            await next(ctx);
            buffer.Position = 0;
            var body = await new StreamReader(buffer).ReadToEndAsync();
            // Seul un succès est mémorisé : après une erreur, le client doit pouvoir réessayer.
            if (ctx.Response.StatusCode is >= 200 and < 300)
            {
                try
                {
                    using var conn = webDb.Open();
                    conn.Execute("INSERT OR IGNORE INTO idempotency (key, response, created_at) VALUES (@key, @body, @now)",
                        new { key, body, now = Clock.UnixNow() });
                    if (DateTime.UtcNow - _lastPurge > TimeSpan.FromHours(1))
                    {
                        conn.Execute("DELETE FROM idempotency WHERE created_at < @limit", new { limit = Clock.UnixNow() - Retention.TotalSeconds });
                        _lastPurge = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Clé d'idempotence non mémorisée");
                }
            }
            buffer.Position = 0;
            await buffer.CopyToAsync(original);
        }
        finally
        {
            ctx.Response.Body = original;
        }
    }
}
