namespace Radivum.Model;

/// <summary>
/// Limits a collection scope carries, shared by every edition and transport.
/// </summary>
/// <remarks>
/// The maximum lives here rather than in the agent protocol because the open-source command lane may not reference
/// the protocol assembly. The protocol payload and the Workbench mirror the value and a guardrail pins the copies,
/// so raising it is one deliberate edit rather than a silently drifting constant.
/// <para>
/// The cap exists because reachability and routing derivation grow quadratically with the number of VNets. Raising
/// it needs a measurement on a realistically large estate first, not only a product decision.
/// </para>
/// </remarks>
public static class CollectionScopeLimits
{
    /// <summary>Most subscriptions one analysis may include.</summary>
    public const int MaxSubscriptions = 3;
}
