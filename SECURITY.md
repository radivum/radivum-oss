# Security policy

## Reporting a vulnerability

Please use GitHub's **private vulnerability reporting** on this repository: *Security* → *Advisories* →
*Report a vulnerability*. That channel is private, tracked, and reaches the maintainer directly.

Do not open a public issue for anything that could be exploited, and do not attach a real snapshot, a tenant
identifier or a subscription identifier. A minimal reproduction with synthetic ids is always enough.

Expect an acknowledgement within a few business days. If the report is confirmed, the fix ships in the next
release and the advisory is published after it, crediting you unless you prefer otherwise.

## Supported versions

The latest released `radivum-oss` version and the current `main` branch receive fixes. There is no long-term
support branch while the tool is pre-1.0.

## What is in scope

- The tool's own code: collection, snapshot, graph, checks, report and manifest.
- Package integrity: how the published package relates to this source (see the release workflow and its
  provenance attestation).
- Anything that would make the tool write, delete or transmit data it should not.

Out of scope: vulnerabilities in Azure itself, in the .NET runtime, or in a dependency (report those upstream;
tell us if the tool needs to move off a version). Also out of scope: the deployment of someone's own estate,
and the content of a snapshot a user chooses to analyse.

## What the tool does with your data

Collection and analysis run locally with your identity. Nothing is uploaded, there is no telemetry, and
credentials are never stored, logged or transmitted. The exact endpoints the tool contacts and the role it
needs are documented in [docs/permissions.md](docs/permissions.md) — that document is the one to hand to
whoever reviews the tool for use in a client environment.
