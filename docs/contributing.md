# Contributing

## Prerequisites

- **.NET 10 SDK** — pinned in [`global.json`](../global.json) to `10.0.100` with
  `rollForward: latestMajor` and `allowPrerelease: false`. Install a matching or
  newer .NET 10 SDK; `dotnet --version` should resolve to 10.x.

No other tooling is required — the reader and integrity code are pure managed
and build/run on Linux, macOS, and Windows.

## Build & test

```bash
dotnet build -c Release
dotnet test --configuration Release
```

- `dotnet build -c Release` builds all `src/` and `tests/` projects.
- `dotnet test --configuration Release` runs the full suite (currently
  **152 tests** across `MsixCore.Packaging.Tests`, `MsixCore.Deployment.Tests`,
  and `msixmgr.Tests`).

To run a single project's tests:

```bash
dotnet test tests/MsixCore.Packaging.Tests --configuration Release
```

CI ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) runs
`restore → build --configuration Release → test --configuration Release` on both
`ubuntu-latest` and `windows-latest` for every push to `main` and every pull
request, proving the cross-platform guarantee (notably Linux validation).

## Solution format (`.slnx`)

The solution is [`MsixCore.slnx`](../MsixCore.slnx), the modern **XML-based**
solution format (not the legacy `.sln`). It simply groups the `src/` and
`tests/` projects into two solution folders. Add a new project by adding a
`<Project Path="..." />` element under the appropriate `<Folder>`; the SDK's
`dotnet` commands understand `.slnx` natively.

## Conventions

Shared build settings live in [`Directory.Build.props`](../Directory.Build.props)
and apply to every project:

- **Target framework:** `net10.0`; `LangVersion` `latest`.
- **Nullable reference types:** enabled (`<Nullable>enable</Nullable>`).
- **Implicit usings:** enabled.
- **Warnings as errors:** `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
  Keep the build clean — a warning fails the build. `EnforceCodeStyleInBuild` and
  `AnalysisLevel=latest-recommended` mean analyzer/style violations are errors
  too.
- **XML doc comments:** `GenerateDocumentationFile` is on. Public members should
  carry `///` doc comments (the missing-doc warning `CS1591` is suppressed, but
  the public surface is documented as a matter of course — see the existing
  code).
- **Deterministic builds** are enabled.

Follow the existing code style: file-scoped namespaces, expression-bodied
members where natural, `record`/`sealed` types for immutable models, guard
clauses with `ArgumentNullException.ThrowIfNull` /
`ArgumentException.ThrowIfNullOrEmpty`, and comments only where they add
clarification (not to restate the code).

### Security-sensitive code

When touching parsers or the OPC/loose-layout readers, preserve the existing
invariants: XXE-hardened `XmlReaderSettings` (`DtdProcessing.Prohibit`, no
resolver), part-name validation, and path-traversal/symlink defenses. See
[architecture.md](architecture.md#security-invariants).

## Workflow: branch → PR → review → merge

Development is **phased**: each phase is a self-contained, reviewed unit of work.

1. **Branch.** Create a topic/phase branch off `main` (e.g. `phase/6-deployment-engine`,
   `docs/architecture`). Do not commit directly to `main`.
2. **Implement with tests.** Every phase lands with full test coverage; keep the
   `Release` build warning-free and all tests green locally before pushing.
3. **Open a PR** targeting `main`. CI must pass on both Linux and Windows.
4. **GPT-5.6 review.** Each PR is reviewed (by the GPT-5.6 reviewer) before
   merge; address review feedback on the branch.
5. **Merge** once approved and green. The parent/integrator merges — individual
   phase branches do not self-merge.

### Commit messages

Commit with an explicit identity and the project's trailers, for example:

```bash
git -c user.name="aclinick" -c user.email="80841394+aclinick@users.noreply.github.com" \
  commit -m "<subject>" \
  -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Reporting bugs

This project keeps a full issue history of defects. If you find a genuine bug
(e.g. a verb crashes or produces wrong output), file a GitHub issue against
`aclinick/msixcore` with a clear, minimal repro and the `bug` label, and
reference it from the fixing PR.
