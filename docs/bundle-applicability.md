# Bundle applicability

A `.msixbundle` carries several child packages: one application package per processor architecture,
plus resource packages qualified by language, display scale, and DirectX feature level. *Applicability*
is the question "given this device, which of those child packages should actually be installed?"

```csharp
using MsixBundle bundle = MsixBundle.Open("App.msixbundle");

BundleApplicabilityResult applicable = bundle.SelectApplicable(new BundleTarget
{
    Architecture = ProcessorArchitecture.Arm64,
    Languages = ["fr-FR", "en-US"],
    Scale = 200,
});

// The one package to install, plus its resource packages.
Console.WriteLine(applicable.ApplicationPackage.FileName);
foreach (var resource in applicable.ResourcePackages)
{
    Console.WriteLine(resource.FileName);
}
```

`bundle.SelectApplicable()` with no argument resolves against the current device via
`BundleTarget.Current()`.

## We deliberately do not port the upstream algorithm

This is the one area of the port where copying `microsoft/msix-packaging` would have been actively
wrong, so the divergence is worth stating plainly.

The upstream open-source SDK's applicability engine:

- **never reads a package's `Architecture`.** It is parsed into the child package identity and then
  never consulted. There is no target-architecture query, no WoW64 rule, no emulation rule, and no
  "no applicable architecture" error, because architecture is never tested at all.
- **never compares a `Scale` value.** Its only scale rule rejects resource packages that have a scale
  but no language; a package with both is selected on language alone and its scale is ignored.
- **never parses `DXFeatureLevel`.** `GetDXFeatureLevel` returns `E_NOTIMPL`, so a package qualified
  only by DX feature level looks unqualified and is therefore always applicable.
- has its **platform filtering commented out**, which makes the public
  `MSIX_APPLICABILITY_OPTION_SKIPPLATFORM` flag a no-op.

Porting that faithfully would mean implementing almost nothing, and would fail the acceptance
criteria this feature exists to satisfy (an x64 target must select the x64 package). So this
implementation follows the **documented Windows behaviour** instead. Where the two differ, upstream's
behaviour is treated as an incomplete implementation rather than a specification.

The one part of upstream worth keeping is its BCP-47 comparison, which is a reasonable, well-defined
subset. `Bcp47Tag` mirrors it, including strict script equality and the `zh-CN`/`zh-TW`/`zh-HK` script
normalization without which `zh-CN` would not match a `zh-Hans` resource.

## Architecture

Exactly **one** application package is selected, because installing two app packages from a single
bundle is never correct. Candidates are ranked by this table, best first, and everything absent from
a row cannot run on that target:

| Target | Preference order |
| --- | --- |
| `X86` | `X86`, `Neutral` |
| `X64` | `X64`, `X86` (WoW64), `Neutral` |
| `Arm` | `Arm`, `X86`, `Neutral` |
| `Arm64` | `Arm64`, `X64`, `X86OnArm64`, `X86`, `Arm`, `Neutral` |

`Neutral` is always last: it is genuinely runnable, but a native package is always the better
choice when the bundle carries one.

`Arm` ranks below the emulated x64/x86 entries on `Arm64` because Windows 11 dropped ARM32
application support. This ordering is a judgement call rather than something the documentation
states outright, and it is pinned by tests so that changing it is a deliberate act.

When no application package can run on the target, resolution throws `InvalidDataException` tagged
[`no_applicable_package`](error-codes.md), and the message lists the architectures the bundle does
carry — the useful thing to know at that moment.

## Languages

Each resource package language is compared against the requested languages and classified:

| Requested | Offered | Result |
| --- | --- | --- |
| `fr-FR` | `fr-FR` | Exact |
| `fr-FR` | `fr` | Neutral parent — directly usable |
| `fr` | `fr-FR` | Variant — fallback only |
| `fr-FR` | `fr-CA` | Variant — fallback only |
| `fr-FR` | `de-DE` | No match |
| `zh-Hans` | `zh-Hant` | No match (script mismatch) |
| `sr` | `sr-Latn` | No match (script mismatch) |
| anything | `und` | Matches |

Exact and neutral-parent matches are selected. **Variants are included only when nothing matched
directly anywhere in the bundle**, so a `fr-FR` user does not also receive `fr-CA` payload, but a
`fr-FR` user of a bundle containing only `fr-CA` still gets French.

An `und` package is always selected but does **not** count as a direct match, because it carries
language-neutral payload. Otherwise a bundle that happened to ship an `und` package would leave a
`fr-FR` user with no French at all.

Scripts must match **exactly, including absent against present**: `sr` does not match `sr-Latn`,
because handing a reader a script they cannot read is worse than handing them a fallback language.
Resolving a bare `sr` to a default script would require suppress-script inference, which is not
implemented. Tags whose primary subtag is not 2-8 letters — private-use (`x-private`) and
grandfathered (`i-klingon`) forms — are rejected outright rather than being read as a language `x`
or `i`, which would make every such tag compare equal to every other.

## Scale

The requested scale is resolved against the scales carried by the resource packages that **survive
language and DirectX filtering**, not by the bundle as a whole: the exact scale when present,
otherwise the **next largest** (downscaling an image degrades better than upscaling), otherwise the
largest available.

Resolving it bundle-wide would let an unselected package eliminate a selected one. A bundle of
(`en`, scale-100) and (`fr`, scale-200) resolved for `en` at scale 150 would globally round up to
200, drop the English package on scale and the French one on language, and return nothing.

## Unspecified qualifiers do not filter

`Languages` empty, `Scale` null, or `DXFeatureLevel` null all mean *do not filter on this*, and select
every package rather than none. A partially-specified target should not silently discard payload.

This is why `BundleTarget.Current()` leaves `Scale` and `DXFeatureLevel` unset: neither is
discoverable from a cross-platform runtime API, and guessing would drop resource packages.

## Options

`BundleApplicabilityOptions` ignores individual qualifiers; `All` ignores every qualifier, so no
resource package is filtered out and any application package counts as runnable. It still yields
exactly one application package: a bundle's application packages are alternatives to each other, so
"install them all" is never meaningful. `SkipArchitecture` keeps unrunnable application packages as
candidates rather than dropping them, but the preference ranking above still decides which one is
chosen.

These flags intentionally do **not** reuse upstream's `MSIX_APPLICABILITY_OPTIONS` numeric values.
Upstream defines `SKIPPLATFORM = 1` and `SKIPLANGUAGE = 2`, but its platform filtering is disabled,
so `SKIPPLATFORM` does nothing there and has no counterpart here. Matching the numbers would imply a
compatibility that does not exist.

## Not implemented

The full Windows Resource Management System does considerably more than this, and none of the
following is implemented:

- macro-regions, preferred regions, sibling scoring, and orthographic affinity
- language-list *position* weighting (a match is a match regardless of which preferred language it
  hit)
- suppress-script inference
- contrast, and other resource qualifiers beyond language/scale/DXFL
- PRI-level resource selection *inside* a package (this operates on whole packages only)
