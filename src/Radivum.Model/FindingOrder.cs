namespace Radivum.Model;

/// <summary>
/// Canonical finding ordering shared by every check source, so a merged OSS and Pro result set is
/// deterministic regardless of which source produced which finding. Ordering is a determinism rule,
/// not analysis judgement, which is why it lives with the contracts rather than in an edition.
/// </summary>
public static class FindingOrder
{
    /// <summary>
    /// Severity rank used as the primary sort key. Unrecognized severities rank lowest.
    /// </summary>
    /// <param name="severity">Severity value of a finding.</param>
    /// <returns>Rank where a higher value means a more severe finding.</returns>
    public static int SeverityRank(string severity) => severity switch
    {
        "High" => 4,
        "Medium" => 3,
        "Warning" => 2,
        _ => 1
    };

    /// <summary>
    /// Orders findings by descending severity, then by code using an ordinal comparison.
    /// </summary>
    /// <param name="findings">Findings produced by any number of sources.</param>
    /// <returns>A new list in canonical order.</returns>
    public static List<Finding> Sort(IEnumerable<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        return findings
            .OrderByDescending(finding => SeverityRank(finding.Severity))
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ToList();
    }
}
