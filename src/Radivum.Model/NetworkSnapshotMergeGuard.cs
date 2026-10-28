namespace Radivum.Model;

/// <summary>
/// Validates snapshot compatibility before combining or comparing snapshots.
/// </summary>
public static class NetworkSnapshotMergeGuard
{
    /// <summary>
    /// Validates a snapshot before use in merge/comparison workflows.
    /// </summary>
    /// <param name="snapshot">Snapshot to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when required identity properties are missing.</exception>
    /// <exception cref="NotSupportedException">Thrown when schema version is unsupported.</exception>
    public static void Validate(NetworkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(snapshot.TenantId))
        {
            throw new ArgumentException("Snapshot tenant id is required.", nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(snapshot.SchemaVersion))
        {
            throw new ArgumentException("Snapshot schema version is required.", nameof(snapshot));
        }

        if (!string.Equals(snapshot.SchemaVersion, NetworkSnapshotSchema.Version, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Unsupported NetworkSnapshot schema version '{snapshot.SchemaVersion}'. Expected '{NetworkSnapshotSchema.Version}'.");
        }
    }

    /// <summary>
    /// Ensures two snapshots can be safely combined without cross-tenant contamination.
    /// </summary>
    /// <param name="left">Left snapshot.</param>
    /// <param name="right">Right snapshot.</param>
    /// <exception cref="ArgumentNullException">Thrown when either snapshot is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when snapshots belong to different tenants.</exception>
    public static void EnsureCompatibleTenants(NetworkSnapshot left, NetworkSnapshot right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Validate(left);
        Validate(right);

        if (!string.Equals(left.TenantId, right.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot combine snapshots from different tenants ('{left.TenantId}' != '{right.TenantId}').");
        }
    }
}
