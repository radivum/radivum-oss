# How a collection is performed

One page on what `collect` actually does, so the behaviour is not a mystery when coverage says something is
missing.

## The shape of a run

```
scope validation  ->  Resource Graph query  ->  per-type normalisation  ->  findings  ->  artifacts
```

1. **Scope validation.** The scope is parsed and the identity is asked what it can see. Several subscriptions
   must belong to one tenant; a resource group scope must name exactly one subscription.
2. **Resource Graph query.** One query fetches the network resources of the scope. This is the efficient path:
   Resource Graph answers across subscriptions and resource groups in one call, and it is what makes a
   three-subscription collection a single network rather than three.
3. **Normalisation.** Each resource type is projected into its snapshot shape: subnets and peerings out of a
   virtual network, rules out of an NSG, routes out of a route table, and references such as a NIC's subnet or a
   private endpoint's target kept as declared ids.
4. **Checks.** The Level 1 checks run over the snapshot, not over the ARM responses, so a finding can be
   reproduced from the snapshot alone.
5. **Writing.** The snapshot, findings, report, graph and topology are written, then the manifest — last, as the
   completion marker.

## Paging and bounds

Resource Graph pages its results with a skip token. The client follows the token and refuses to follow it
indefinitely: a page ceiling stops a pathological scope from running forever. A partial page run is reported as
partial coverage rather than as a shorter inventory.

## Degradation instead of failure

Two failure shapes are distinguished, and the distinction is the useful part:

- **The scope cannot be read at all** — no identity, wrong tenant, a disabled subscription. The run fails with
  an exit code that says which.
- **A resource type cannot be read** — a provider is not registered, or the identity lacks the read action for
  that type. The type is recorded in `collectionMetadata` with a status, a completeness and a reason, the run
  continues, and the report and manifest say so.

A collection is therefore usually *partial* in a well-understood way rather than either perfect or failed. The
coverage section of the report is the list of what to fix, in order.

## Linked resources

A network interface references a subnet, and a private endpoint references a target. By default the tool records
the declared id and names the target by parsing that id — no extra read, and the reference is still visible.
`--resolve-links true` performs the extra read so the target also carries its name, type and region. The choice
is recorded per link (`resolved`, `derived` or not confirmed) rather than hidden.

## Determinism

The graph is built from the snapshot with an injected clock, node ids are derived from resource ids, and the
serializer is fixed. The same snapshot therefore produces the same graph on any machine, which is what makes the
manifest's digests meaningful and a diff between two runs readable.

## What this design does not do

- No data-plane reads: no key vault secret, no storage blob, no guest-level inspection of a VM.
- No active probing: nothing is sent to a customer endpoint to see whether it answers. Reachability reasoning is
  a computation over collected configuration, not a scan.
- No writes of any kind. There is no code path in the collector that issues a state-changing request.
