namespace DeeKeep.Contracts;

/// <summary>Disponibilité d'un outil externe (ffmpeg, yt-dlp, deno, fpcalc…).</summary>
public sealed record ToolStatus(string Name, bool Available, string? Version, string? Path, string? Detail);

public sealed record EventLinkStatus(string Producer, bool Connected, double? Since, string? LastError);

/// <summary>État d'un service, renvoyé par <c>GET /internal/info</c> et affiché dans la page Diagnostic.</summary>
public sealed record ServiceInfo(
    string Service,
    string Version,
    double StartedAt,
    long WorkingSetBytes,
    long ManagedHeapBytes,
    int ActiveJobs,
    int OutboxPending,
    IReadOnlyList<EventLinkStatus> Links,
    IReadOnlyList<ToolStatus> Tools);
