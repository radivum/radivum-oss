# Quickstart

Two paths. Path A needs no Azure access and takes about a minute; Path B documents your own estate in about
five minutes.

## Prerequisites

| Path | Needs |
|---|---|
| A | the **.NET 10 SDK** (`dotnet --version` reports 10.x). `dotnet tool install` is an SDK command — the runtime alone is not enough |
| B | the same, plus the **Azure CLI** (`az`) and **Reader** (or equivalent read access) at the scope you collect |

## Path A — analyse the committed sample

```bash
dotnet tool install --global radivum-oss
radivum-oss report --snapshot samples/network-snapshot.sample.v1.json --out ./sample-output
```

You now have in `sample-output/`:

```
network-report.md        the document to read first
network-topology.md      the Mermaid diagram, renders on GitHub and in VS Code
network-graph.json       the same graph, for your own tooling
findings.json            the machine-readable findings
network-snapshot.json    the input, re-emitted as the tool writes it
analysis-manifest.json   the index: digests and declared capability states
```

To point the tool at your own artifacts later, note what it reads: a **snapshot** for `collect`-less workflows,
a **graph** for the graph-only mode. Nothing else is required, and nothing leaves the machine.

## Path B — collect your own estate

```bash
az login
radivum-oss login-status
radivum-oss collect --scope <subscription-id> --out ./output
```

`login-status` lists the subscriptions your login can see. If your subscription is missing there, `collect` will
refuse it too, and the reason is a permission rather than a bug.

`collect` writes the complete bundle in one run. To treat several subscriptions of one tenant as a single
network, repeat the scope (up to three):

```bash
radivum-oss collect --scope <subscription-a> --scope <subscription-b> --out ./output
```

A resource group scope takes exactly one subscription:

```bash
radivum-oss collect --scope /subscriptions/<id>/resourceGroups/<rg> --out ./output
```

### Re-running without collecting again

```bash
radivum-oss report  --snapshot ./output/network-snapshot.json --out ./output
radivum-oss graph   --snapshot ./output/network-snapshot.json --out ./output/network-graph.json
radivum-oss diagram --snapshot ./output/network-snapshot.json --format mermaid --out ./output/network-topology.md
```

`report` regenerates the report, the graph and the topology together. `graph` and `diagram` write one of them
somewhere else.

## Reading the result

1. **`network-report.md`** — inventory, topology, collection coverage, findings.
2. **Collection coverage** — the section that says what could not be read and why. If it reports partial
   coverage, the findings are still correct for what was read; they just cannot see the rest.
3. **`analysis-manifest.json`** — the declared state of each capability (`available`, `limited`, `unavailable`).
   A reader that shows a section as unavailable is repeating this file, not guessing.

## Troubleshooting

| Symptom | Cause | What to do |
|---|---|---|
| `collect` exits `2` | the credential chain produced no usable identity | `az login`; the tool respects the Azure CLI credential |
| `collect` exits `3` | Azure refused the request | check the subscription is enabled and the identity is in its tenant |
| Coverage says `PARTIAL` for a resource type | the identity lacks read access at that scope, or the provider is not registered for the subscription | see [permissions.md](permissions.md); the reason is stated per resource type in the report |
| An "unexpected" finding about an NSG or a route | the checks read the *configured* state | the evidence on the finding names the resource and the rule; open it |
| `dotnet tool install` says the SDK is missing | the runtime is installed but not the SDK | install the .NET 10 SDK |
| `network-topology.md` shows no diagram in your viewer | that viewer does not render Mermaid | GitHub, VS Code (with Markdown preview), Azure DevOps wikis and most modern wikis do |
