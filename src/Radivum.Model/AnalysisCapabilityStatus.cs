namespace Radivum.Model;

/// <summary>
/// Declared state of an analysis capability. These three values are the Workbench's existing
/// section vocabulary, so a declared state can be consumed without translation.
/// </summary>
public static class AnalysisCapabilityStatus
{
    /// <summary>Everything the capability needs is present.</summary>
    public const string Available = "available";

    /// <summary>The capability is usable but based on incomplete evidence.</summary>
    public const string Limited = "limited";

    /// <summary>The capability cannot be evaluated at all.</summary>
    public const string Unavailable = "unavailable";
}
