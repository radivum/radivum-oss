using Radivum.Model;

namespace Radivum.Oss.Analysis;

/// <summary>
/// Runs the open-source deterministic checks over a snapshot and returns a single ordered finding
/// set. Premium analysis composes this result with its own rules rather than extending this
/// contract, so the shared layer carries no edition-awareness.
/// </summary>
public interface ICheckEngine
{
    /// <summary>
    /// Runs the open-source deterministic checks.
    /// </summary>
    /// <param name="snapshot">Snapshot to analyse.</param>
    /// <returns>Findings ordered by severity, then by code.</returns>
    IReadOnlyList<Finding> Check(NetworkSnapshot snapshot);
}
