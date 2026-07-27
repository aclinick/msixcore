# Manifest dependencies

A package declares what else must be present for it to run, under the manifest's `<Dependencies>`
element. msixcore parses three package-to-package relationships into
[`PackageDependency`](../src/MsixCore.Packaging/Manifest/PackageDependency.cs) and resolves them at
install time.

`TargetDeviceFamily` also lives under `<Dependencies>` but constrains the *OS*, not another package,
so it keeps its own model ([`TargetDeviceFamily`](../src/MsixCore.Packaging/Manifest/TargetDeviceFamily.cs)).

## What is parsed

| Element | `PackageDependencyKind` | Meaning |
|---|---|---|
| `PackageDependency` | `Framework` | A framework package needed at runtime, e.g. `Microsoft.VCLibs.140.00`. |
| `uap3:MainPackageDependency`, `uap4:MainPackageDependency` | `MainPackage` | The package this modification package modifies. |
| `uap10:HostRuntimeDependency`, `uap13:HostRuntimeDependency` | `HostRuntime` | The host runtime that executes a hosted app's code. |

Elements are matched by **local name**, as everywhere else in the parser, so the revisioned forms of
each element collapse onto one kind. The revisions differ in which attributes they permit, not in
what the relationship means; a consumer that needs the revision can read the namespace from the DOM.

`uap17:PackageDependency` also matches by local name and is parsed as `Framework`. Its extra
`DependencyType` attribute (`install` vs `installAndRuntime`) is not yet modelled.

### Not parsed

`uap5:DriverDependency` and `uap7:OSPackageDependency` are not package-to-package relationships and
are deliberately ignored, as is any unrecognised child, so a future schema revision does not break
reading a manifest.

## Required attributes

Which attributes the schema requires differs per element, so the parser enforces different rules per
kind rather than one blanket rule:

| Attribute | `PackageDependency` | `MainPackageDependency` | `HostRuntimeDependency` |
|---|---|---|---|
| `Name` | required | required | required |
| `Publisher` | required | optional (absent from the `uap3` form) | required |
| `MinVersion` | required | **not present** | required |
| `MaxMajorVersionTested` | optional | not present | not present |
| `uap6:Optional` | optional | not present | not present |

Three consequences follow:

- `PackageDependency.MinVersion` is **nullable**, and is always `null` for a `MainPackage`. A
  modification package binds to its parent by name and publisher alone. It is not defaulted to
  `0.0.0.0`, which would erase the difference between "no version constraint exists" and "version
  zero was explicitly requested".
- `MaxMajorVersionTested` is a single `xs:unsignedShort` — **not** a four-part version quad like every
  other version attribute in the manifest. It is modelled as `ushort?`. Parsing it as a quad would
  reject every real manifest that declares it.
- `uap6:Optional="true"` marks a framework the package can run without. Such a dependency is still
  resolved and reported, but its absence does not block deployment. The attribute is **rejected**
  on `MainPackageDependency` and `HostRuntimeDependency`, whose schemas do not declare it: a
  modification package without its main package cannot run at all, so silently honouring the
  attribute there would let a malformed manifest opt out of a mandatory dependency.

## Dependency resolution

[`DependencyResolver`](../src/MsixCore.PackageStore/DependencyResolver.cs) resolves each declared
dependency against a set of installed packages the caller supplies. It is a pure function over that
set rather than a query against a store, because the authority on what is installed differs by host:
on Windows it is the OS deployment stack, in CI a directory of staged packages, in a deployment tool
that tool's own inventory. A caller gates on `CanDeploy` and reports every entry in `Blocking`.

Resolution rules:

- A dependency names a package by `Name` + `Publisher`, which together determine the **package family
  name** — so this is a family lookup, not a full-name lookup.
- When a `MainPackageDependency` omits `Publisher`, the **dependent's own publisher** is used. A
  modification package and the package it modifies are required to share a publisher, which is
  exactly why the schema lets the attribute be omitted.
- Candidates are filtered by **architecture** before the version comparison. An installed x86
  framework does not satisfy an x64 app, because it cannot load into that app's process. A `neutral`
  package on either side matches anything, since neutral packages carry no architecture-specific code.
- A foundation `PackageDependency` is satisfied only by a package whose manifest declares
  `<Properties><Framework>true</Framework>`. An ordinary app package that happens to share the family
  name reports `NotAFramework`, not `Resolved` — it is not loadable as a framework. The other two
  kinds carry no such role constraint: the package a modification package modifies, and a host
  runtime, are both ordinary packages.
- Among the surviving candidates the **highest version** decides, since a machine legitimately holds
  several versions of a framework family.
- `MinVersion` is a **floor, not a pin**: a newer installed framework satisfies it.
- A dependency marked `uap6:Optional="true"` is reported in `Unsatisfied` when absent but is excluded
  from `Blocking`, so it never fails a deployment. `CanDeploy` — not `IsSatisfied` — is the gate.

The installed sequence is materialised once, and is not enumerated at all when the manifest declares
no dependencies, so a caller whose inventory is expensive to produce pays nothing for a package that
depends on nothing.

### What a package family holds

Resolution assumes a family can hold several installed packages at once, which is how MSIX behaves:

- **Framework architecture variants coexist.** An x86 app and an x64 app on one machine each need
  their own build of a framework.
- **Framework versions coexist.** Each app binds to the specific `MinVersion` it declared, so a newer
  framework must not evict the older one an already-installed app resolved against.
- **Resource packages coexist** with the main package, since they differ by resource id.

A caller assembling the installed set must therefore pass **every** installed package in a family,
not just the newest — collapsing a family to one entry would make architecture-variant resolution
fail.

## Divergences from Windows

- Windows resolves framework dependencies against the machine-wide package graph and can acquire a
  missing framework from the Store. msixcore resolves only against the set it was given and never
  acquires anything.
- Windows enforces `MaxMajorVersionTested` when choosing among installed framework versions.
  msixcore parses and surfaces it but does not yet use it to *reject* a newer major version.
- The `uap17:PackageDependency` `DependencyType` distinction (install-time versus runtime) is not
  modelled, so such a dependency is treated as required at install time.

## Surfacing

`msixkit inspect` prints a `Dependencies` section when a package declares any, and `--json` emits a
`Dependencies` array of `{ Kind, Name, Publisher, MinVersion, MaxMajorVersionTested }`. The property
is additive to CLI schema version 1: consumers that predate it ignore it.
