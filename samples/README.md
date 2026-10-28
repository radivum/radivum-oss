# Samples

Two files, both sanitised, both safe to read.

| File | What it is |
|---|---|
| `network-snapshot.sample.v1.json` | A snapshot of a synthetic estate: a hub virtual network peered with two spokes, each spoke workload protected by its own NSG. Tenant, subscription and account identifiers are zeroed or absent |
| `network-graph.sample.v1.json` | A `network-graph.json` against graph schema `1.0`, for consumers that want a contract-shaped example without running the tool |

## Producing the rest

The snapshot is the input; the other artifacts are one command away, which is better than committing copies that
would drift from the renderer:

```bash
radivum-oss report --snapshot samples/network-snapshot.sample.v1.json --out ./sample-output
```

That writes the report, the findings, the graph, the topology and the manifest.

## Where the identifiers went

The estate was captured from a test deployment, then sanitised: the tenant, the subscription and the account are
either the all-zero identifier or absent, and no resource carries a real customer name. The structure is
unchanged, which is the point — the topology, the NSGs and the coverage metadata are what the examples are for.

If you contribute a fixture, keep the same rule: one repeated hex digit for synthetic identifiers (`1111…`,
`2222…`), so it is obvious at a glance that nothing real is in it.
