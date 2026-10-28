using Radivum.Model;

namespace Radivum.Oss.Analysis;

/// <summary>
/// Runs the open-source deterministic checks over a snapshot.
/// </summary>
public sealed class CheckEngine : ICheckEngine
{
    /// <inheritdoc />
    public IReadOnlyList<Finding> Check(NetworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return Checks.Run(snapshot);
    }
}
