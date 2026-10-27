using System.Text.Json.Nodes;
using Dapper;
using DeeKeep.Common.Data;
using DeeKeep.Common.Events;
using DeeKeep.Common.Hosting;
using DeeKeep.Common.Web;
using DeeKeep.Contracts;

namespace DeeKeep.Common.Jobs;

/// <summary>Persistance des tâches d'un service dans <c>deekeep_web.db</c>.</summary>
public sealed class JobStore(WebDb webDb, ServiceIdentity identity, EventLog events)
{
    private sealed record Row(
        string id, string service, string type, string? title, string status, long priority, double progress, long done,
        long failed, long total, string? message, string? payload, string? result, string? error, long attempts,
        long max_attempts, string? group_key, double not_before, double created_at, double updated_at, double? started_at,
        double? finished_at);

    public event Action? Enqueued;

    public JobDto Enqueue(EnqueueJobRequest request)
    {
        var id = Guid.NewGuid();
        var now = Clock.UnixNow();
        using (var conn = webDb.Open())
        {
            conn.Execute("""
                INSERT INTO jobs (id, service, type, title, status, priority, payload, group_key, max_attempts, created_at, updated_at)
                VALUES (@id, @service, @type, @title, 'Queued', @priority, @payload, @group, @max, @now, @now)
                """, new
            {
                id = id.ToString(), service = identity.Name, type = request.Type, title = request.Title, priority = request.Priority,
                payload = request.Payload?.ToJsonString(), group = request.GroupKey, max = Math.Max(1, request.MaxAttempts), now,
            });
        }
        var job = Get(id)!;
        Publish(job);
        Enqueued?.Invoke();
        return job;
    }

    public JobDto? Get(Guid id)
    {
        using var conn = webDb.Open();
        var row = conn.QueryFirstOrDefault<Row>("SELECT * FROM jobs WHERE id = @id", new { id = id.ToString() });
        return row is null ? null : Map(row);
    }

    public IReadOnlyList<JobDto> List(bool activeOnly, int limit = 200, string? type = null, string? groupKey = null)
    {
        using var conn = webDb.Open();
        var sql = "SELECT * FROM jobs WHERE service = @service" +
                  (activeOnly ? " AND status IN ('Queued','Running')" : "") +
                  (type is null ? "" : " AND type = @type") +
                  (groupKey is null ? "" : " AND group_key = @groupKey") +
                  " ORDER BY created_at DESC LIMIT @limit";
        return conn.Query<Row>(sql, new { service = identity.Name, type, groupKey, limit }).Select(Map).ToList();
    }

    /// <summary>Prend la prochaine tâche due, de façon atomique (plusieurs ouvriers, voire plusieurs processus).</summary>
    public JobDto? TryDequeue(IReadOnlyCollection<string> types)
    {
        if (types.Count == 0) return null;
        using var conn = webDb.Open();
        var now = Clock.UnixNow();
        var row = conn.QueryFirstOrDefault<Row>($"""
            UPDATE jobs SET status = 'Running', attempts = attempts + 1, started_at = COALESCE(started_at, @now), updated_at = @now
            WHERE id = (
                SELECT id FROM jobs
                WHERE service = @service AND status = 'Queued' AND not_before <= @now AND type IN @types
                ORDER BY priority DESC, created_at LIMIT 1)
            RETURNING *
            """, new { service = identity.Name, now, types });
        if (row is null) return null;
        var job = Map(row);
        Publish(job);
        return job;
    }

    public void Progress(Guid id, int done, int failed, int total, string? message)
    {
        using var conn = webDb.Open();
        var progress = total > 0 ? Math.Clamp((done + failed) / (double)total, 0, 1) : 0;
        conn.Execute("UPDATE jobs SET done = @done, failed = @failed, total = @total, progress = @progress, " +
                     "message = COALESCE(@message, message), updated_at = @now WHERE id = @id",
            new { id = id.ToString(), done, failed, total, progress, message, now = Clock.UnixNow() });
    }

    public JobDto Complete(Guid id, JobStatus status, JsonNode? result, string? error, string? message = null)
    {
        using (var conn = webDb.Open())
        {
            var now = Clock.UnixNow();
            conn.Execute("UPDATE jobs SET status = @status, result = @result, error = @error, message = COALESCE(@message, message), " +
                         "progress = CASE WHEN @status = 'Succeeded' THEN 1 ELSE progress END, finished_at = @now, updated_at = @now WHERE id = @id",
                new { id = id.ToString(), status = status.ToString(), result = result?.ToJsonString(), error, message, now });
        }
        var job = Get(id)!;
        Publish(job);
        return job;
    }

    /// <summary>Remet une tâche en file après un échec passager, avec attente exponentielle.</summary>
    public JobDto Retry(Guid id, string error, TimeSpan delay)
    {
        using (var conn = webDb.Open())
        {
            var now = Clock.UnixNow();
            conn.Execute("UPDATE jobs SET status = 'Queued', error = @error, not_before = @nb, updated_at = @now WHERE id = @id",
                new { id = id.ToString(), error, nb = now + delay.TotalSeconds, now });
        }
        var job = Get(id)!;
        Publish(job);
        return job;
    }

    /// <summary>Relance manuelle d'une tâche terminée (échec ou annulation).</summary>
    public JobDto? Requeue(Guid id)
    {
        using (var conn = webDb.Open())
        {
            var n = conn.Execute("UPDATE jobs SET status = 'Queued', attempts = 0, error = NULL, not_before = 0, finished_at = NULL, " +
                                 "updated_at = @now WHERE id = @id AND status IN ('Failed','Cancelled')",
                new { id = id.ToString(), now = Clock.UnixNow() });
            if (n == 0) return Get(id);
        }
        var job = Get(id)!;
        Publish(job);
        Enqueued?.Invoke();
        return job;
    }

    public bool CancelQueued(Guid id)
    {
        using var conn = webDb.Open();
        var now = Clock.UnixNow();
        var n = conn.Execute("UPDATE jobs SET status = 'Cancelled', finished_at = @now, updated_at = @now WHERE id = @id AND status = 'Queued'",
            new { id = id.ToString(), now });
        if (n > 0) Publish(Get(id)!);
        return n > 0;
    }

    /// <summary>Au démarrage : ce qui tournait au moment de l'arrêt repart en file.</summary>
    public int RecoverInterrupted()
    {
        using var conn = webDb.Open();
        return conn.Execute("UPDATE jobs SET status = 'Queued', message = 'Reprise après redémarrage', updated_at = @now " +
                            "WHERE service = @service AND status = 'Running'", new { service = identity.Name, now = Clock.UnixNow() });
    }

    public int PurgeFinished(double olderThan)
    {
        using var conn = webDb.Open();
        return conn.Execute("DELETE FROM jobs WHERE service = @service AND status IN ('Succeeded','Failed','Cancelled') AND updated_at < @olderThan",
            new { service = identity.Name, olderThan });
    }

    public int MaxAttempts(Guid id)
    {
        using var conn = webDb.Open();
        return conn.ExecuteScalar<int?>("SELECT max_attempts FROM jobs WHERE id = @id", new { id = id.ToString() }) ?? 1;
    }

    public void PublishItem(JobItemEvent item) => events.Publish(EventTypes.JobItem, item, item.JobId.ToString());

    public void Publish(JobDto job) => events.Publish(EventTypes.JobUpdated, job with { Payload = null }, job.Id.ToString());

    private static JobDto Map(Row r) => new(
        Guid.Parse(r.id), r.service, r.type, r.title, Enum.Parse<JobStatus>(r.status), r.progress, (int)r.done, (int)r.failed,
        (int)r.total, r.message, r.error, (int)r.attempts, r.group_key, r.payload is null ? null : JsonNode.Parse(r.payload),
        r.result is null ? null : JsonNode.Parse(r.result), r.created_at, r.updated_at, r.started_at, r.finished_at);
}
