# Radivum OSS and Radivum Pro

Two editions of one collector. The difference is interpretation, not access.

| | Radivum OSS (this repository) | Radivum Pro |
|---|---|---|
| Collection | the collector | the **same** collector, same code |
| Snapshot, graph, Mermaid topology | yes | yes |
| Markdown report | yes | yes, plus a consultant report |
| Level 1 configuration findings | yes | yes |
| Effective routing reasoning, asymmetric-routing and firewall-bypass detection | no | yes |
| Effective NSG and exposure analysis | no | yes |
| Deterministic reachability between two endpoints | no | yes |
| Premium findings, each tagged with its origin | no | yes |
| Account or licence to use it | none | licence |
| Licence | Apache-2.0 | proprietary |
| Distribution | nuget.org, from this source | private |

## Why the collector is shared

Pro registers the same command classes as the OSS tool. That is a deliberate design decision, and it has a
consequence a reader should be able to check: **a Pro run cannot see more than an OSS run.** Collection quality
is not a differentiator; the analysis on top of it is. If you are ever told that Pro collects something OSS
cannot, the claim is wrong — and the code says so.

## What Pro adds, concretely

The evidence OSS produces is *configured* state: what is declared in the resources. Pro adds the reasoning that
turns configured state into an answer about behaviour — does this path work, is this exposure real, does the
route table actually send that traffic to the appliance — and it adds the report shape a consultant delivers.

That is why a Pro bundle can carry `routing-evidence.json` and `security-evidence.json` while an OSS bundle
declares those capabilities `unavailable` with the detail `requires the pro edition`. The bundle says what it
cannot answer instead of leaving the reader to guess.

## What is not in the OSS edition, on purpose

Nothing is withheld from the tool to make it inconvenient:

- No usage cap, no expiry, no account, no telemetry, no nag screen.
- The artifact formats are documented and versioned, so another tool can read them — and Pro reads the same
  files.
- A capability that ships in the OSS edition is not withdrawn or degraded to sell Pro. Deprecation follows
  semantic versioning with notice.

## The Workbench

The Radivum Workbench is a separate product: a signed-in browser application that renders these artifacts, hosts
analysis for a team, and shows both editions' evidence. It reads the same `network-graph.json` and bundle a CLI
run writes, so an OSS bundle opens there too. It is proprietary and needs an account; the CLI does not.
