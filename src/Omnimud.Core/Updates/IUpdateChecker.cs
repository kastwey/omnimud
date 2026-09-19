namespace Omnimud.Core.Updates;

/// <summary>
/// Asks whether a newer Omnimud has been published. It only ASKS: nothing is ever downloaded or run, and
/// nothing about the user travels with the question.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>Never throws for network or server problems (they are a <see cref="UpdateCheckFailed"/>); only for a cancelled <paramref name="ct"/>.</summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);
}

/// <summary>What a check found: <see cref="UpToDate"/>, <see cref="UpdateAvailable"/> or <see cref="UpdateCheckFailed"/>.</summary>
public abstract record UpdateCheckResult;

/// <param name="Current">The version that is running.</param>
/// <param name="NoReleasesYet">The project has not published any release yet (the server answered 404).</param>
public sealed record UpToDate(SemanticVersion Current, bool NoReleasesYet = false) : UpdateCheckResult;

/// <param name="Version">The published version, newer than the running one.</param>
/// <param name="Title">Name of the release; never empty (falls back to the version).</param>
/// <param name="Notes">Release notes as plain text, without control characters and capped in length; may be empty.</param>
/// <param name="ReleasePage">Page of the release, ONLY when it is an https address of the project at github.com; null = do not offer to open anything.</param>
public sealed record UpdateAvailable(SemanticVersion Version, string Title, string Notes, Uri? ReleasePage) : UpdateCheckResult;

/// <param name="Detail">Technical detail for the curious (HTTP status, exception message); never shown alone.</param>
public sealed record UpdateCheckFailed(UpdateCheckFailure Reason, string? Detail = null) : UpdateCheckResult;

public enum UpdateCheckFailure
{
    /// <summary>No network, DNS failure, proxy problem, TLS failure.</summary>
    Network,
    /// <summary>The server did not answer in time.</summary>
    Timeout,
    /// <summary>The public API limit for this address was reached (HTTP 403 or 429).</summary>
    RateLimited,
    /// <summary>Any other status, or an answer that is not what was expected.</summary>
    UnexpectedResponse
}
