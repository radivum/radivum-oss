## What this changes

<!-- One or two sentences. If it changes an artifact contract, say which one and whether the change is additive. -->

## Why

<!-- The problem, not the patch. A link to an issue is fine. -->

## Checklist

- [ ] Every commit carries a `Signed-off-by` line (`git commit --signoff`) — see [CONTRIBUTING.md](../CONTRIBUTING.md)
- [ ] `dotnet build Radivum.Oss.sln` is warning-free and `dotnet test Radivum.Oss.sln` passes
- [ ] A change to behaviour comes with a test that fails without it
- [ ] No real tenant, subscription or resource identifiers appear in code, tests, fixtures or this description
- [ ] No proprietary code, and no reference to the Pro edition's namespaces or commands

## Contract impact

<!-- Delete the lines that do not apply. -->

- No published artifact changes.
- `network-snapshot.json` changes (schema `0.1`): additive / versioned.
- `network-graph.json` changes (graph schema `1.0`): additive / versioned.
- `analysis-manifest.json` changes (schema `0.1`): additive / versioned.
