# Manifest validation

Parsing and validating are separate operations in MSIX Core, on purpose.
`AppxManifestParser` is deliberately tolerant — it matches elements by local name
so a package built against a newer Windows SDK can still be inspected.
`ManifestValidator` is where a caller opts in to strictness.

```csharp
using MsixCore.Packaging;
using MsixCore.Packaging.Validation;

using MsixPackage package = MsixPackage.Open("App.msix");
ManifestValidationResult result = package.ValidateManifest();

if (!result.IsValid)
{
    foreach (ManifestValidationIssue issue in result.Errors)
    {
        Console.Error.WriteLine($"{issue.Rule} at {issue.Target}: {issue.Message}");
    }
}
```

The `msixkit validate` verb runs this automatically; a manifest error fails the
CI gate with exit code `1`.

## What this is not

This is a **semantic** validator, not an XSD validator. It does not check element
ordering, cardinality, or attribute presence — the schema covers those, and the
parser already enforces them where a missing value would make the result
meaningless. The Microsoft schema documents (~46 XSDs) are not vendored into this
repository, so no `XmlSchemaSet` validation is performed.

It also does not evaluate deployment policy. `runFullTrust`, for example, is a
restricted capability requiring Store approval, but it is a perfectly valid
declaration and is not flagged here.

## Severity

| Severity  | Meaning |
|-----------|---------|
| `Error`   | The manifest violates a rule Windows enforces. `IsValid` is `false`. |
| `Warning` | Advisory only. Never makes `IsValid` false. |

Only one rule produces warnings today: `UnknownNamespace`. A package targeting a
newer SDK than this library knows about is valid, and failing it would be wrong —
so the namespace is reported, and its content is simply not validated.

## Rules

| Rule | Severity | Checks |
|------|----------|--------|
| `IdentifierMalformed` | Error | `Identity/@Name`, `Identity/@ResourceId`, and every dependency `Name` contain only letters, digits, `.`, and `-`. |
| `IdentifierLength` | Error | `Identity/@Name` and `PackageDependency`/`MainPackageDependency` names are 3–50 characters; `Identity/@ResourceId` is 1–30. |
| `IdentifierReserved` | Error | A package identifier is not `.`/`..`, a DOS device name (`con`, `prn`, `aux`, `nul`, `com1`–`com9`, `lpt1`–`lpt9`), does not start with one of those followed by a period or with `xn--`, and does not end with a period. |
| `PublisherMalformed` | Error | `Identity/@Publisher` matches the schema's `ST_Publisher_2010_v2` pattern — a `", "`-separated list of `attribute=value` pairs drawn from a fixed attribute set (or a numeric `OID.n.n…`), each value either a quoted string or a non-empty run excluding `,+="<>#;` — and is 1–8192 characters. |
| `VersionRangeInverted` | Error | A `PackageDependency`'s `MinVersion` major does not exceed its `MaxMajorVersionTested`, and a `TargetDeviceFamily`'s `MinVersion` does not exceed its `MaxVersionTested`. |
| `ConflictingPackageType` | Error | A package is not both a framework and a resource package, and an optional package is neither. |
| `FrameworkContent` | Error | A framework package declares no `Applications` and no `Capabilities`. |
| `ResourcePackageContent` | Error | A resource package declares no `Applications`, `Capabilities`, package `Extensions`, `PackageDependency`, or `MainPackageDependency`, and no processor architecture — not even an explicit `neutral`. |
| `OptionalPackageContent` | Error | An optional package (one with a `MainPackageDependency`) declares no `Capabilities` and no `Properties/SupportedUsers`. |
| `ApplicationIdMalformed` | Error | Each `Application/@Id` is 1–64 characters and each dot-separated segment starts with a letter and contains only letters and digits. |
| `DuplicateApplicationId` | Error | No two applications share an `Id`. |
| `DuplicateCapability` | Error | No capability declares the same `Name` twice within its schema uniqueness scope. |
| `UnknownNamespace` | Warning | Every XML namespace actually used by an element or attribute is in the known schema registry. |

Identifier rules are stricter than a character-class check because a package
identifier becomes a directory name on disk, and Windows still reserves the DOS
device names at the filesystem level. A `HostRuntimeDependency/@Name` is exempt
from the length and reserved-name rules: the schema types it as
`ST_AsciiIdentifier`, not `ST_PackageName`, so it shares the character set but
has neither the 3–50 bound nor the reserved-name prohibition.

Capability uniqueness follows the foundation schema's `xs:unique` constraints
literally, and they are not per element type:

- `Capability_Name` is a **union** selector covering `f:Capability`,
  `uap:Capability`, `wincap:Capability`, and `rescap:Capability`. Those four
  share **one** scope, so a foundation `<Capability Name="x"/>` and a
  `<rescap:Capability Name="x"/>` *are* a duplicate.
- `DeviceCapability_Name` and `CustomCapability_Name` are separate scopes, so the
  same name may appear once in each.
- Only the **unnumbered** namespace revisions appear in the union selector.
  `uap2:`…`uap11:`, `mobile:`, and `iot:` capabilities are under no uniqueness
  constraint at all, and a repeat there is not reported.

## Rules that need the XML document

`ManifestValidator.Validate(AppxManifest)` checks everything the parsed model can
express. Two rules cannot be expressed there, because the parser normalises the
value away, so they run only on the overloads that also have the document —
`Validate(AppxManifest, XDocument)`, `Validate(Stream)`, and
`MsixPackage.ValidateManifest()`:

- A resource package declaring an **explicit** `ProcessorArchitecture="neutral"`.
  The parser maps both an absent attribute and an explicit `neutral` to
  `ProcessorArchitecture.Neutral`; the rule is about the attribute being present.
- An optional package declaring `Properties/SupportedUsers`, which the parser
  does not model.

## Known divergences from Windows

These are the places where this validator knowingly differs from what Windows
enforces. Each is a consequence of what the parser models, not an oversight.

- **No XSD validation.** Structural violations that the parser tolerates —
  unexpected child elements, out-of-order content — are not reported.
- **Namespaces are checked, schemas are not.** A known namespace means "this
  library recognises the schema revision", not "the content conforms to it".

## Namespace registry

`ManifestNamespaces` is the table of namespaces the MSIX manifest schemas define,
mapped to the schema document that defines each. It mirrors
`cmake/msix_resources.cmake` in Microsoft's `msix-packaging` repository: 46
package-manifest namespaces and 6 bundle-manifest namespaces.

```csharp
bool known = ManifestNamespaces.IsKnownPackageNamespace(
    "http://schemas.microsoft.com/appx/manifest/uap/windows10/13");   // true
```

Two details of the upstream registry are worth knowing, because both look like
bugs and are not:

- There is **no `.../uap/windows10/9`**. The sequence jumps from `/8` to `/10`,
  with no comment upstream explaining why. Only its absence is a fact; any
  explanation would be a guess.
- The **2014 bundle schema document declares the 2013 namespace**
  (`http://schemas.microsoft.com/appx/2013/bundle`). The mismatch is upstream's.

There is also no Xbox namespace, despite one being widely assumed to exist.
