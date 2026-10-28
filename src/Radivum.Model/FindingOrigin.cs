namespace Radivum.Model;

/// <summary>
/// Edition that produced a finding. The Workbench uses the origin to decide whether the findings
/// capability is complete or only partially covered.
/// </summary>
public enum FindingOrigin
{
    /// <summary>Finding produced by the open-source deterministic checks.</summary>
    Oss,

    /// <summary>Finding produced by a premium analysis source.</summary>
    Pro
}
