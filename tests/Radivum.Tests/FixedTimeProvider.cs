namespace Radivum.Tests;

/// <summary>
/// Test clock that reports a fixed instant, so timestamps captured by production services are
/// reproducible.
/// </summary>
/// <param name="timestamp">Instant the clock reports.</param>
internal sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
{
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => timestamp;
}
