# Contributing to Radivum OSS

Thank you for taking the time. This document states the two things a contributor needs to know — how your
contribution is licensed, and how it reaches the published package — and then how to build and test.

## Sign-off (Developer Certificate of Origin 1.1)

Every commit must carry a `Signed-off-by` line:

```
Signed-off-by: Your Name <your.email@example.com>
```

Add it with `git commit --signoff` (or `git commit -s`). The line certifies the
[Developer Certificate of Origin 1.1](https://developercertificate.org/): that you wrote the change, or have the
right to submit it, and that it may be distributed under this project's licence. A pull-request check fails a
commit without it.

There is **no CLA**. You keep your copyright.

## What your contribution is licensed under

Contributions are licensed to this project under **Apache-2.0**, the same licence as the project (inbound equals
outbound). Two consequences worth stating plainly:

1. Apache-2.0 permits the maintainer to combine contributions into other products, including the proprietary
   Radivum Pro edition. By contributing you agree that your contribution **may be incorporated into Radivum's
   proprietary products** and that you keep your copyright.
2. The open-source edition's licence cannot change under you: the code you contributed stays available under
   Apache-2.0.

## How a change reaches the published package

This repository is the public source of record. The maintainer imports accepted changes into the maintaining
repository by hand, runs the full internal suite there, and publishes the next snapshot of that tree through the
pipeline. So a merged pull request appears here immediately and in the next release package — not in an
intermediate build.

Pull requests are reviewed for correctness, tests and licence provenance. If a change needs a design decision,
it is easier to open an issue first.

## Building and testing

Prerequisite: the **.NET 10 SDK**.

```bash
dotnet build Radivum.Oss.sln
dotnet test Radivum.Oss.sln
```

The build treats warnings as errors, and the analyzers run with `AnalysisLevel latest` — a warning is a failing
build, deliberately. Tests must pass on Linux, Windows and macOS; CI runs all three.

Useful conventions in this codebase:

- Async all the way, with a `CancellationToken` on anything that touches Azure or the filesystem.
- Determinism matters: graphs, reports and manifests are expected to be byte-stable for the same input, because
  the manifest records digests.
- Anything that can be unavailable in Azure is modelled as a declared capability state rather than an exception.
  See [docs/artifacts.md](docs/artifacts.md).

## Reporting a bug

Open an issue with the version (`dotnet tool list --global` shows it), the command you ran, and what you expected. Do not
paste a real snapshot: it describes someone's estate. See [SECURITY.md](SECURITY.md) if the report is a
vulnerability rather than a defect, and [SUPPORT.md](SUPPORT.md) if you are not sure it is either.
