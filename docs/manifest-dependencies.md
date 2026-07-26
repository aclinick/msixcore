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
| `PackageDependency` | `Framework` | A framework or resource package needed at runtime, e.g. `Microsoft.VCLibs.140.00`. |
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

Two consequences follow:

- `PackageDependency.MinVersion` is **nullable**, and is always `null` for a `MainPackage`. A
  modification package binds to its parent by name and publisher alone. It is not defaulted to
  `0.0.0.0`, which would erase the difference between "no version constraint exists" and "version
  zero was explicitly requested".
- `MaxMajorVersionTested` is a single `xs:unsignedShort` — **not** a four-part version quad like every
  other version attribute in the manifest. It is modelled as `ushort?`. Parsing it as a quad would
  reject every real manifest that declares it.

## Install-time resolution

[`DependencyResolver`](../src/MsixCore.PackageStore/DependencyResolver.cs) resolves each declared
dependency against an `IPackageStore`. `PackageManager.AddPackage` runs it **before extraction**, so
an unsatisfiable package fails fast without writing a staging tree, and reports
`MsixErrorCode.DependencyNotSatisfied` naming every unsatisfied dependency.

Resolution rules:

- A dependency names a package by `Name` + `Publisher`, which together determine the **package family
  name** — so this is a family lookup, not a full-name lookup.
- When a `MainPackageDependency` omits `Publisher`, the **dependent's own publisher** is used. A
  modification package and the package it modifies are required to share a publisher, which is
  exactly why the schema lets the attribute be omitted.
- Candidates are filtered by **architecture** before the version comparison. An installed x86
  framework does not satisfy an x64 app, because it cannot load into that app's process. A `neutral`
  package on either side matches anything, since neutral packages carry no architecture-specific code.
- Among the surviving candidates the **highest version** decides, since a store legitimately holds
  several versions of a framework family.
- `DeploymentOptions.SkipDependencyCheck` bypasses the whole check for staging scenarios where
  dependencies are added afterwards.

## Divergences from Windows

- Windows resolves framework dependencies against the machine-wide package graph and can acquire a
  missing framework from the Store. msixcore resolves only against the store it was given and never
  acquires anything.
- Windows enforces `MaxMajorVersionTested` when choosing among installed framework versions.
  msixcore parses and surfaces it but does not yet use it to *reject* a newer major version.
- The `uap17:PackageDependency` `DependencyType` distinction (install-time versus runtime) is not
  modelled, so such a dependency is treated as required at install time.

## Surfacing

`msixkit inspect` prints a `Dependencies` section when a package declares any, and `--json` emits a
`Dependencies` array of `{ Kind, Name, Publisher, MinVersion, MaxMajorVersionTested }`. The property
is additive to CLI schema version 1: consumers that predate it ignore it.
