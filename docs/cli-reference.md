# Command-line reference

The tool installs as `radivum-oss`.

```bash
dotnet tool install --global radivum-oss     # install
dotnet tool update  --global radivum-oss     # update
radivum-oss --help                           # the command surface
```

## Global behaviour

- Every command writes its result to the output directory you name (default `./output`) and prints the absolute
  paths it wrote, so a script can read them.
- Errors go to standard error; the exit code states the class of failure (see the table at the end).
- Cancelling with Ctrl+C exits `130` and leaves the output directory without a manifest, because the manifest is
  written last.

## `login-status`

```bash
radivum-oss login-status
```

Shows the subscriptions the current login can see, with their names and states. No arguments.

Use it first: if a subscription is not listed here, `collect` cannot read it either.

## `collect`

```bash
radivum-oss collect --scope <scope> [--scope <scope> ...] [--out <dir>] [--resolve-links <true|false>]
```

| Argument | Meaning |
|---|---|
| `--scope` | A subscription id, `/subscriptions/<id>`, or `/subscriptions/<id>/resourceGroups/<rg>`. Repeatable up to **three** subscriptions of one tenant. A resource group form takes exactly one subscription |
| `--out` | Output directory (default `./output`), created if missing |
| `--resolve-links` | `true` looks up the resource a NIC or private endpoint points at, one extra read per link. Default `false`: the target is named from its resource id |

Writes the complete bundle: `network-snapshot.json`, `findings.json`, `network-report.md`, `network-graph.json`,
`network-topology.md`, and `analysis-manifest.json` last.

A subscription that cannot be read at all fails the run; a single resource type that cannot be read degrades that
capability and is stated in the report and the manifest.

## `report`

```bash
radivum-oss report --snapshot <file> [--out <dir>]
```

Reads an existing `network-snapshot.json` and regenerates its findings, report, graph, topology and manifest. No
Azure access is used, so this is the command for working offline and the one the sample uses.

## `graph`

```bash
radivum-oss graph --snapshot <file> [--out <dir-or-file>]
```

Re-renders `network-graph.json` from a snapshot. `--out` accepts a directory (the file is created inside it with
the standard name) or an explicit `.json` path. `collect` and `report` already write this artifact; this command
is for writing it somewhere else without regenerating everything.

## `diagram`

```bash
radivum-oss diagram --snapshot <file> [--out <dir-or-file>] [--format mermaid]
```

Re-renders the topology. `--format` currently accepts only `mermaid`. `--out` accepts a directory or an explicit
`.md` path.

## Exit codes

| Code | Name | Meaning |
|---|---|---|
| `0` | success | the command completed and wrote its artifacts |
| `1` | usage error | an argument is missing, unknown or malformed; nothing was written |
| `2` | authentication failed | the credential chain produced no usable identity, or the token was refused |
| `3` | Azure request failed | Azure rejected a request that was made with a valid identity |
| `4` | not reachable | a destination in a trace could not be reached (used by the tracing surface) |
| `5` | analysis stage failed | an analysis stage failed after collection; inspect the message and the artifacts on disk |
| `6` | connection failed | a connection to a paired host failed (used by the connected surface) |
| `130` | cancelled | the run was cancelled; no manifest was written |

## Environment

| Variable | Effect |
|---|---|
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` | honoured by `DefaultAzureCredential`; the tool never reads or stores them itself |
| `AZURE_CONFIG_DIR` | where the Azure CLI keeps its login, if you use the CLI credential |

Configuration is deliberately absent: there is no config file, no state directory and no cache. Everything a run
needs is an argument or the credential your environment already provides.
