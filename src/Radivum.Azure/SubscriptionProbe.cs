namespace Radivum.Azure;

/// <summary>
/// What reading one requested subscription's metadata established before its resources were queried.
/// </summary>
/// <remarks>
/// The probe exists because Azure Resource Graph silently omits a subscription the caller cannot read: a mixed
/// request answers with the readable rows and no error at all. Reading the subscription itself from ARM turns that
/// silence into a named scope state.
/// <para>
/// A readable document is not proof of Reader over everything the subscription contains. It establishes identity,
/// tenant and state; completeness of resource visibility is still decided by the resource queries, and this record
/// deliberately makes no claim beyond metadata.
/// </para>
/// </remarks>
/// <param name="SubscriptionId">Subscription the probe was for, as requested.</param>
/// <param name="Name">Display name ARM answered, or <see langword="null"/>.</param>
/// <param name="State">State ARM answered, or <see langword="null"/>.</param>
/// <param name="TenantId">Tenant ARM answered, or <see langword="null"/> when the read did not answer.</param>
/// <param name="Error">Why the metadata could not be read, or <see langword="null"/> when it could.</param>
internal sealed record SubscriptionProbe(
    string SubscriptionId,
    string? Name,
    string? State,
    string? TenantId,
    string? Error)
{
    /// <summary>Whether ARM answered with the subscription's own document.</summary>
    public bool IsReadable => Error is null;
}
