using System.Text.Json;
using Radivum.Azure;
using Radivum.Model;
using Xunit;

namespace Radivum.Tests;

/// <summary>
/// Covers the batched resolution of declared-link targets. Resolution is enrichment: it may degrade to an
/// unconfirmed target, but it never removes a link, never widens the queried subscription, and never fails the
/// collection that produced the snapshot.
/// </summary>
public sealed class LinkedResourceResolverTests
{
    private const string SubscriptionId = "sub-1";
    private const string InScopeScope = "/subscriptions/" + SubscriptionId + "/resourceGroups/rg-app-prod/providers";
    private const string SqlServerId = InScopeScope + "/Microsoft.Sql/servers/sql-prod";
    private const string KeyVaultId = "/subscriptions/sub-2/resourceGroups/rg-shared/providers/Microsoft.KeyVault/vaults/kv-prod";
    private const string InterfaceId = InScopeScope + "/Microsoft.Network/networkInterfaces/nic-vm01";

    private static readonly DateTimeOffset CollectedAt = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveAsync_GivenAnInScopeTarget_MarksTheLinkResolved()
    {
        var client = new RecordingResourceGraphClient();
        var snapshot = SnapshotWith(Link(InterfaceId, SqlServerId));

        var resolved = await LinkedResourceResolver.ResolveAsync(snapshot, client, "token");

        var link = Assert.Single(resolved.Resources.LinkedResources!);
        Assert.Equal(LinkedResourceInfo.ResolutionResolved, link.Resolution);
        Assert.Equal("sql-prod", link.DisplayName);
        Assert.Equal("Microsoft.Sql/servers", link.AzureResourceType);
        Assert.Equal("rg-data", link.ResourceGroup);
        Assert.Equal("westeurope", link.Location);

        var evidence = Assert.Single(resolved.CollectionMetadata, item => item.ItemType == "LinkedResources");
        Assert.Equal("Available", evidence.Status);
        Assert.Equal(1d, evidence.Completeness);
    }

    [Fact]
    public async Task ResolveAsync_GivenATargetInAnotherSubscription_DoesNotQueryItAndReportsItUnresolved()
    {
        var client = new RecordingResourceGraphClient();
        var snapshot = SnapshotWith(Link(InterfaceId, KeyVaultId));

        var resolved = await LinkedResourceResolver.ResolveAsync(snapshot, client, "token");

        // The Resource Graph client is scoped to one subscription, and reading another would need consent the
        // operator did not give by choosing this scope.
        Assert.Empty(client.Queries);

        var link = Assert.Single(resolved.Resources.LinkedResources!);
        Assert.Equal(LinkedResourceInfo.ResolutionUnresolved, link.Resolution);

        var evidence = Assert.Single(resolved.CollectionMetadata, item => item.ItemType == "LinkedResources");
        Assert.Equal("Partial", evidence.Status);
        Assert.Contains(evidence.Errors, error => error.Contains("outside the collected subscription", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_GivenMoreTargetsThanTheBatchCeiling_LeavesTheSurplusNamedFromTheId()
    {
        var client = new RecordingResourceGraphClient
        {
            Responder = _ => new ResourceGraphQueryResult([], IsTruncated: false, SkipToken: null, TotalRecords: 0),
        };
        var links = Enumerable.Range(0, 1001)
            .Select(index => Link(InterfaceId, $"{InScopeScope}/Microsoft.Compute/virtualMachines/vm-{index:D4}"))
            .ToArray();

        var resolved = await LinkedResourceResolver.ResolveAsync(SnapshotWith(links), client, "token");

        Assert.Equal(LinkedResourceResolver.MaxBatches, client.Queries.Count);

        var byResolution = resolved.Resources.LinkedResources!
            .GroupBy(link => link.Resolution)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(1000, byResolution[LinkedResourceInfo.ResolutionUnresolved]);
        Assert.Equal(1, byResolution[LinkedResourceInfo.ResolutionDerived]);

        var evidence = Assert.Single(resolved.CollectionMetadata, item => item.ItemType == "LinkedResources");
        Assert.Equal("Partial", evidence.Status);
        Assert.Contains(evidence.Errors, error => error.Contains("batch limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_GivenAFailingQuery_KeepsTheLinksAndRecordsTheFailure()
    {
        var client = new RecordingResourceGraphClient { Failure = new HttpRequestException("Resource Graph refused the query.") };
        var snapshot = SnapshotWith(Link(InterfaceId, SqlServerId));

        var resolved = await LinkedResourceResolver.ResolveAsync(snapshot, client, "token");

        var link = Assert.Single(resolved.Resources.LinkedResources!);
        Assert.Equal(LinkedResourceInfo.ResolutionUnresolved, link.Resolution);
        Assert.Equal("sql-prod", link.DisplayName);

        var evidence = Assert.Single(resolved.CollectionMetadata, item => item.ItemType == "LinkedResources");
        Assert.Equal("Unavailable", evidence.Status);
        Assert.Contains(evidence.Errors, error => error.Contains("refused the query", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAsync_GivenATargetInASecondCollectedSubscription_QueriesBothAndResolvesIt()
    {
        var client = new RecordingResourceGraphClient();
        var snapshot = SnapshotWith(Link(InterfaceId, KeyVaultId)) with
        {
            SubscriptionIds = [SubscriptionId, "sub-2"],
        };

        var resolved = await LinkedResourceResolver.ResolveAsync(snapshot, client, "token");

        Assert.Equal([SubscriptionId, "sub-2"], Assert.Single(client.Subscriptions));

        var link = Assert.Single(resolved.Resources.LinkedResources!);
        Assert.Equal(LinkedResourceInfo.ResolutionResolved, link.Resolution);
        Assert.Equal("kv-prod", link.DisplayName);
    }

    [Fact]
    public async Task ResolveAsync_GivenNoLinks_ReturnsTheSameSnapshot()
    {
        var snapshot = SnapshotWith();

        var resolved = await LinkedResourceResolver.ResolveAsync(snapshot, new RecordingResourceGraphClient(), "token");

        Assert.Same(snapshot, resolved);
    }

    [Fact]
    public async Task ResolveAsync_GivenLinksInAnotherOrder_SendsTheSameQueries()
    {
        var first = Link(InterfaceId, $"{InScopeScope}/Microsoft.Compute/virtualMachines/vm-b");
        var second = Link(InterfaceId, $"{InScopeScope}/Microsoft.Compute/virtualMachines/vm-a");

        var forward = new RecordingResourceGraphClient();
        var reversed = new RecordingResourceGraphClient();
        await LinkedResourceResolver.ResolveAsync(SnapshotWith(first, second), forward, "token");
        await LinkedResourceResolver.ResolveAsync(SnapshotWith(second, first), reversed, "token");

        Assert.Equal(forward.Queries, reversed.Queries);
        Assert.Contains("vm-a", forward.Queries[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_GivenACancelledToken_StopsBeforeQuerying()
    {
        var client = new RecordingResourceGraphClient();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LinkedResourceResolver.ResolveAsync(SnapshotWith(Link(InterfaceId, SqlServerId)), client, "token", cancellation.Token));

        Assert.Empty(client.Queries);
    }

    /// <summary>A snapshot whose only content is the links under test.</summary>
    private static NetworkSnapshot SnapshotWith(params LinkedResourceInfo[] links)
    {
        var snapshot = TestSnapshotFactory.Create();

        return snapshot with
        {
            Resources = snapshot.Resources with { LinkedResources = links.Length == 0 ? null : links },
        };
    }

    /// <summary>A link as the parser emits it: target identity derived from the id, nothing looked up yet.</summary>
    private static LinkedResourceInfo Link(string sourceResourceId, string targetResourceId)
    {
        var target = ArmResourceId.ParseOrNull(targetResourceId);

        return new LinkedResourceInfo(
            SourceResourceId: sourceResourceId,
            TargetResourceId: targetResourceId,
            LinkKind: LinkedResourceKinds.PrivateEndpointFor,
            Resolution: LinkedResourceInfo.ResolutionDerived,
            Metadata: Evidence(),
            DisplayName: target?.Name,
            AzureResourceType: target?.AzureResourceType,
            ResourceGroup: target?.ResourceGroup);
    }

    private static CollectionMetadata Evidence() => new(
        ItemType: "Resources",
        Source: "AzureResourceGraph",
        Timestamp: CollectedAt,
        Permission: "Reader",
        Status: "Available",
        Completeness: 1d,
        Errors: []);

    /// <summary>
    /// A Resource Graph client that records the query text and answers with a row per id it was asked about.
    /// </summary>
    private sealed class RecordingResourceGraphClient : IResourceGraphClient
    {
        public List<string> Queries { get; } = [];

        /// <summary>Subscription sets every query was scoped to.</summary>
        public List<IReadOnlyList<string>> Subscriptions { get; } = [];

        public Exception? Failure { get; init; }

        /// <summary>Answers a query; the default resolves every id in it.</summary>
        public Func<string, ResourceGraphQueryResult> Responder { get; init; } = ResolveEveryId;

        public Task<ResourceGraphQueryResult> QueryAsync(
            string accessToken,
            string query,
            IReadOnlyList<string> subscriptionIds,
            string? skipToken,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            Subscriptions.Add(subscriptionIds);

            return Failure is null
                ? Task.FromResult(Responder(query))
                : Task.FromException<ResourceGraphQueryResult>(Failure);
        }

        /// <summary>
        /// Answers with one row per quoted id in the query. Splitting on the quote character is enough because the
        /// resolver wraps every id in single quotes, so the ids sit at the odd indexes.
        /// </summary>
        private static ResourceGraphQueryResult ResolveEveryId(string query)
        {
            var ids = query
                .Split('\'')
                .Where((_, index) => index % 2 == 1)
                .ToList();

            var rows = ids
                .Select(id => JsonSerializer.SerializeToElement(new
                {
                    id,
                    name = id[(id.LastIndexOf('/') + 1)..],
                    type = "Microsoft.Sql/servers",
                    location = "westeurope",
                    resourceGroup = "rg-data",
                }))
                .ToList();

            return new ResourceGraphQueryResult(rows, IsTruncated: false, SkipToken: null, TotalRecords: rows.Count);
        }
    }
}
