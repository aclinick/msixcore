# Manifest extensions

`<Extensions>` is how a package declares its integration points with the OS: the file types it
opens, the URI schemes it handles, the aliases it can be launched by, the COM classes it registers.
msixcore **parses and surfaces** these declarations so tooling can report what a package would do to
a machine. It does **not** register any of them with the OS — see [Divergences](#divergences-from-windows).

Schema reference:
[uap:Extension](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap-extension),
[desktop extensions](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions).

## Where extensions live

There are two containers, and msixcore keeps them separate because a package-level extension has no
owning application:

| Container | Model |
| --- | --- |
| `Package/Applications/Application/Extensions` | [`ManifestApplication.Extensions`](../src/MsixCore.Packaging/Manifest/ManifestApplication.cs) |
| `Package/Extensions` | [`AppxManifest.Extensions`](../src/MsixCore.Packaging/Manifest/AppxManifest.cs) |

Both are optional and both are lists preserving manifest order.

## The `Extension` element

Every variant (`uap:`, `uap3:`, `uap5:`, `desktop:`, `desktop7:`, `com:` …) shares one shape, so all
of them parse into a single [`AppExtension`](../src/MsixCore.Packaging/Manifest/AppExtension.cs):

| Attribute | Required | Property |
| --- | --- | --- |
| `Category` | **yes** | `Category` |
| `Executable` | no | `Executable` |
| `EntryPoint` | no | `EntryPoint` |
| `StartPage` | no | `StartPage` |
| `ResourceGroup` | no | `ResourceGroup` |
| `RuntimeType` | no | `RuntimeType` |

`Category` is modelled as a **plain string, not an enum**, even though the schema declares a closed
enumeration per namespace. The enumeration grows with every schema revision; rejecting an unfamiliar
category would make this library fail on packages that are valid against a newer schema than it was
built against. Schema conformance belongs to manifest validation, not to the object model.

## Recognised categories

`AppExtension.Payload` holds the category's child element for the categories below, and is `null`
for every other category. It is also `null` when a recognised category declares no child element —
the schema makes the child choice `minOccurs="0"`, and a bare
`<desktop:Extension Category="windows.fullTrustProcess" Executable="app.exe" />` is both valid and
common.

| Category | Child element | Payload type |
| --- | --- | --- |
| `windows.fileTypeAssociation` | `uap:FileTypeAssociation` | [`FileTypeAssociationExtension`](../src/MsixCore.Packaging/Manifest/FileTypeAssociationExtension.cs) |
| `windows.protocol` | `uap:Protocol` / `uap3:Protocol` | [`ProtocolExtension`](../src/MsixCore.Packaging/Manifest/ProtocolExtension.cs) |
| `windows.appExecutionAlias` | `uap3:` / `uap5:AppExecutionAlias` | [`AppExecutionAliasExtension`](../src/MsixCore.Packaging/Manifest/AppExecutionAliasExtension.cs) |
| `windows.startupTask` | `desktop:` / `uap5:StartupTask` | [`StartupTaskExtension`](../src/MsixCore.Packaging/Manifest/StartupTaskExtension.cs) |
| `windows.fullTrustProcess` | `desktop:FullTrustProcess` | [`FullTrustProcessExtension`](../src/MsixCore.Packaging/Manifest/FullTrustProcessExtension.cs) |
| `windows.comServer` | `com:ComServer` | [`ComServerExtension`](../src/MsixCore.Packaging/Manifest/ComServerExtension.cs) |
| `windows.shortcut` | `desktop7:Shortcut` | [`ShortcutExtension`](../src/MsixCore.Packaging/Manifest/ShortcutExtension.cs) |

### Namespace variants collapse onto one model

Consistent with the rest of the parser, elements are matched by **local name**. That is not merely a
convenience here — the same logical extension is spelled differently across schema revisions:

- `uap:Protocol` and `uap3:Protocol` differ only by the added `Parameters` attribute, which is
  simply `null` for the `uap` form.
- `uap3:AppExecutionAlias` nests **`desktop:ExecutionAlias`**, while `uap5:AppExecutionAlias` nests
  `uap5:ExecutionAlias`. Both yield the same `Aliases` list.
- `desktop:StartupTask` and `uap5:StartupTask` declare the same three attributes.

### Fidelity choices

- **File extensions are preserved verbatim**, including the leading dot the schema requires and the
  case as written. Normalising would hide a manifest defect from tooling whose job is to report what
  the package actually declares.
- **`StartupTask.IsEnabled` and `Shortcut.PinToStartMenu` are `bool?`**. The schema declares no
  default for either attribute, so "unstated" stays distinguishable from "stated false".
- **CLSIDs are kept as strings**, not parsed to `Guid`, so a manifest with a malformed CLSID can be
  reported rather than rejected outright.
- **`FullTrustProcess` carries only the parameter groups.** The executable is an attribute of the
  enclosing `Extension`, not of the child element, and is read from `AppExtension.Executable`.

### What is rejected

A missing **required** attribute is a semantic error (`MsixErrorCode.ManifestSemantics`): an
`Extension` without `Category`, a `FileTypeAssociation` without `Name`, a `Protocol` without `Name`,
an `ExecutionAlias` without `Alias`, a `StartupTask` without `TaskId`, a `ParameterGroup` missing
`GroupId`/`Parameters`, a `Class`/`ProgId` without `Id`, a `Shortcut` without `File`/`Icon`, and an
empty `FileType`. So is a non-boolean `Enabled` or `PinToStartMenu`.

## Surfacing

`msixkit inspect` lists every extension from both containers, tagged with the declaring application
(or `package`):

```console
$ msixkit inspect .\App.msix
...
Extensions      :
  [App] windows.fileTypeAssociation contoso-doc: .cdoc .cdx
  [App] windows.protocol myscheme:
  [App] windows.appExecutionAlias contoso.exe
  [package] windows.shortcut Contoso.lnk
```

`--json` emits an `Extensions` array of `{ ApplicationId?, Category, Executable?, Details? }`.
`ApplicationId` is **omitted** for a package-level extension (the CLI omits null properties), and
`Details` is a one-line human summary of the payload — the JSON contract intentionally does not
mirror the full payload shape, which callers should get from the library.

## Divergences from Windows

- **No OS registration.** Windows creates the shell associations, protocol handlers, aliases,
  startup entries and COM registrations described here. msixcore only reports them; registration is
  the Windows-integration work tracked separately.
- **Not every category is modelled.** App services, background tasks, share targets, File Explorer
  context menus and the app-extension (add-in) host/guest model are reported by category string with
  no payload. See [`msix-spec-coverage.md`](msix-spec-coverage.md) §3.
- **Category strings are not validated** against the schema's closed enumerations, deliberately —
  see above.
- **COM detail is partial.** `ExeServer`, `SurrogateServer`, `Class` and `ProgId` are modelled;
  `TreatAsClass`, interface registrations, and the per-class OLE detail (`ImplementedCategories`,
  `DataFormats`, `Verbs`, `MiscStatus`, …) are not.
