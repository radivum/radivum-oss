# Artifacts and contracts

A run writes six files. Three of them are contracts — a consumer may rely on their shape — and three are
renderings of those contracts for a human or a diagram tool.

## The contracts

### `network-snapshot.json` — schema `0.1`

The inventory, decoupled from the raw ARM responses.

```
scope            subscriptionId, subscriptionIds[], subscriptions[], subscriptionName?, scopeDescription
collectedAt      when the collection ran
toolVersion      the version that produced it
collectionMetadata[]   per resource type: itemType, status, completeness, errors, scopeId?
resources        vNets, subnets, peerings, networkSecurityGroups, routeTables, networkInterfaces,
                 publicIpAddresses, privateEndpoints, privateDnsZones, azureFirewalls, virtualWans,
                 virtualHubs, expressRouteCircuits, virtualNetworkGateways, linkedResources
```

`collectionMetadata` is the part that makes the file honest: a type that could not be read is listed with a
status and a reason rather than being absent and indistinguishable from "there is none".

### `network-graph.json` — graph schema `1.0`

A deterministic, renderer-neutral projection of the snapshot.

```
graphSchemaVersion, sourceSnapshotSchemaVersion, sourceSnapshotToolVersion, generatedAt
scopeDescription
nodes[]   id, resourceId, nodeType, displayName, azureResourceType, subscriptionId, resourceGroup,
          region, parentNodeId, metadata
edges[]   id, sourceId, targetId, relationship, metadata
groups[]  id, displayName, nodeIds
metadata
```

Two rules are worth stating because they are contract:

- **Containment is ownership only.** `parentNodeId` is the owning resource (a subnet's virtual network, a
  resource's resource group). Where a resource *sits* on the network is derived from attachment edges by the
  consumer, so the graph never carries two authorities for one answer.
- **Node ids are derived from the resource id**, so the same estate produces the same ids on every run and on
  every machine.

### `analysis-manifest.json` — schema `0.1`

The index of the run, and the file to read when something looks incomplete.

```
analysisId            a stable hash of the scope, collectedAt and tool version
generatedAt, edition, toolVersion, sourceKind
metadata              scope, subscription, collectedAt
artifacts[]           role, relativePath, sha256, byteLength
capabilities[]        id, status (available | limited | unavailable), detail
coverage              overall status, missing type count, resource count
```

It is written **last**. Its presence means the run completed; its absence means it did not, whatever else is on
disk. Each capability states its own availability, and a detail string when it is not available — `missing
network-graph.json`, `requires the pro edition`, `collection coverage 83%`. A reader that displays a section as
unavailable is repeating this file, not inventing a reason.

## The renderings

| File | From | For |
|---|---|---|
| `network-report.md` | snapshot + findings | the document a human reads: inventory, topology, coverage, findings |
| `network-topology.md` | graph | a Mermaid diagram, rendered by GitHub, VS Code, wikis and documentation sites |
| `findings.json` | checks | the machine-readable findings, each with evidence and the resources it concerns |

Renderings are not indexed by the manifest, because they are produced from the bundle rather than part of it.
`report`, `graph` and `diagram` regenerate them without touching the contracts.

## Versioning

- A **contract version** is the `schemaVersion` (or `graphSchemaVersion`) member. It changes only when a consumer
  would have to change: a removed or renamed member, a changed meaning, a new required member.
- Additive members do not change the version. A consumer must ignore what it does not know — that is how this
  tool reads a snapshot written by an older or newer build.
- The tool version and the contract version are separate promises. A new tool that writes the same contract does
  not force a consumer to change.

## Determinism

The same snapshot produces the same graph, report and topology, byte for byte, apart from `generatedAt`. The
manifest records SHA-256 digests so a consumer can verify that what it reads is what was written. This is why the
graph builder takes its clock as an argument and the serializer is fixed.

## Reading them from your own tool

The three files are JSON with a published shape. Parse them, ignore unknown members, and treat a capability
state as data rather than as an error. If you build something against them, an issue describing what you needed
is the most useful bug report you can file.
