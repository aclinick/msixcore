# We rebuilt MSIX Core in C#. Here's why that matters.

## A confession

I was the GPM of MSIX at Microsoft, among other things.

Back then we built **MSIX Core** with a genuinely lofty goal: an open-source core for MSIX that would
run on every version of Windows, and then — cross-platform. And not just *run* cross-platform. We were
setting out to replace the **application package format on every operating system**. APK on Android.
IPA on iOS. macOS. Linux. All of it.

Looking back, that was hopelessly misguided. Heroic in scope, but misguided.

And yet — you have to admire it. That was a room full of people who genuinely believed they could
change the world, said so out loud, and then went and tried. Not a hedged roadmap with a tidy set of
exit criteria. An actual swing. We were wrong about the destination, but we were not wrong to care
that much, and I'd take that energy over cautious incrementalism any day of the week.

What it actually became was an open-source implementation that helped people understand MSIX more
deeply. That's it. That's the legacy. And since I left Microsoft it's bugged me that it just sat there,
languishing — most certainly not modern, while the official MSIX tooling hasn't kept up with the
realities of today's development lifecycle, let alone AI coding agents.

The irony is that **containment in Windows matters more now than it ever has**. MSIX is finally coming
into its own. And the tools are stuck in 2017.

Then, on a Friday, I got a message from my good friend Ryan Mangan.

Ryan has spent years in the trenches of application delivery and packaging — he's one of the people
the community actually goes to when MSIX doesn't behave. We've had some version of this conversation
many times: packaging keeps *almost* getting there.

It wasn't a pitch. There was no plan, no repo, no scope. It was just the kind of conversation where
you finally say the quiet part out loud — the tooling hasn't moved, the need has moved a long way,
and an entire industry has quietly built workarounds instead of asking for better.

I couldn't let it go over the weekend.

And this epic began.

## What this is NOT

Not an attempt to install MSIX on Linux. Not APK-replacement round two. I already made that mistake
once.

This is a **rich tool layer with real validation**: modern (.NET 10, C#), faster, smaller, and
deliberately balanced between human use and AI toolchain use.

## Principles

We wrote these down first, and they did a lot of work:

- **Security is the OS domain.** We verify integrity. We don't pretend to be a sandbox.
- **Code signing is 100% not our job.** Leave it to codesign/signtool. We *verify* signatures. We
  don't issue them.
- **We will never play the "do we trust this cert" game.** More on this below — it follows directly
  from the first principle.
- **CLI is the new API.** Every tool must be useful to a human.
- **Every tool must be AI/LLM-friendly.** `--json` on everything, stable exit codes, no interactive
  prompts.
- **GitHub CI/CD is the goal**, not a nice-to-have.
- **Windows and Linux are ship criteria.** Not aspirations.
- **No COM. No C++. No shortcuts.**

## CLI is the new API

Here's the part that genuinely delighted me.

I spent decades crafting APIs. Sweating interface shape. Head-scratching over seemingly inane naming
conventions. Arguing about whether it's `Get` or `Read`. And it all comes back to **command-line tools
with switches**.

You know what? It feels great.

Because an LLM agent doesn't consume your beautifully layered `IAppxFactory`. It runs a command and
reads stdout. If your output is a stable JSON object and your exit code means something, you have an
API. If it's a hex HRESULT printed to the console, you have a support ticket.

Which brings us to the comparison.

### What the original command line looks like

`makemsix` is the cross-platform CLI from `microsoft/msix-packaging`. Its entire grammar:

```text
makemsix unpack   -p <package>   -d <directory> [-pfn] [-ac] [-ss] [-pfn-flat]
makemsix unbundle -p <bundle>    -d <directory> [-pfn] [-ac] [-ss] [-sl] [-sp]
                                                [-extract-all] [-pfn-flat]
makemsix pack     -d <directory> -p <package>
makemsix bundle   -p <outputBundle> [-d] [-f] [-bv] [-mo] [-fb] [-o] [-no] [-v]
```

Note what's missing: **there is no `inspect` and no `validate`**. There is no way to ask "is this
package intact?" without extracting it. And `pack`/`bundle` only exist if the library was compiled
with `MSIX_PACK` defined — on a default build they're not in the help text at all.

On failure, `main()` returns the HRESULT directly and prints:

```text
Error: 0x8bad0002
```

That's the API surface. Good luck parsing that in a pipeline.

The Windows-only `msixkit` from MsixCore is a different shape but the same era:

```text
msixkit -AddPackage <path> [-sourceApplicationId] [-correlationId]
msixkit -RemovePackage <fullName>
msixkit -FindPackage <fullName>
msixkit -Unpack -packagePath <p> -destination <d> [-applyACLs] [-validateSignature]
               [-create] [-rootDirectory] [-fileType] [-vhdSize]
msixkit -ApplyACLs -packagePath <p>
msixkit -MountImage -imagePath <p>
```

Integrity checking is `-validateSignature` — **a suboption of `-Unpack`**. Verification isn't a thing
you can do; it's a flag on the thing you do.

And it was never going to run anywhere else. The source includes `TraceLoggingProvider.h`, returns
HRESULTs, pulls help text from Win32 string resources, and contains this include:

```cpp
#include "..\msixkitLib\GeneralUtil.hpp"
```

A backslash. That file will not compile on Linux, and it never intended to.

### What ours looks like

```text
msixkit inspect  <path> [--json]
msixkit validate <path> [--json]
msixkit unpack   <path> -Destination <dir>
msixkit pack     <dir>  -o <package> [--compress] [--overwrite]
msixkit bundle   ...
```

Five verbs. About seven flags total. **`--json` on every verb.** Exit codes are `0` valid, `1`
invalid, `2` usage error — always, on every command. `<path>` is a `.msix`, an `.appx`, or a loose
directory; the tool figures it out.

`validate` is the verb that didn't exist before:

```console
$ msixkit validate .\App.msix
INTEGRITY OK      Contoso.App_1.2.3.0_x64__abcd1234efgh5
  Block map : ok (42 files)
  Signature : CMS envelope ok, binding verified
```

One line in a workflow:

```yaml
- run: msixkit validate ./artifact/App.msix
```

That's the whole pitch for "CLI is the new API." Same information, one round trip, parseable by a
human at 11pm or by an agent at scale.

## On never playing the trust game

`makemsix unpack` has two flags I want to put side by side:

| Flag | Official description |
|---|---|
| `-ac` | "Allows any certificate. By default the signature origin must be known." |
| `-ss` | "Skips enforcement of signed packages. By default packages must be signed." |

That is a tool making a **trust policy decision** and then, inevitably, shipping the escape hatch. And
you know what everybody types. Everybody types `-ss`.

We don't have those flags because we don't make that call. Ever. This isn't "not yet implemented" — it
is a **permanent non-goal**.

Certificate trust is the operating system's job. The OS owns the root store, the OS owns revocation,
the OS owns the enterprise policy that says which publishers are acceptable in your organisation. A
packaging tool that re-litigates that question is either duplicating the OS badly or teaching users to
bypass it.

What we *do* own is integrity, and we own it completely: does the payload match the block map, does
the block map match what the signature actually signed, and is the thing we verified the same bytes as
the thing we reported? Those are answerable questions with cryptographic answers. We answer them, and
we fail closed when we can't.

Integrity is our job. Authenticity is the OS's. Clean line, no flags.

## Tested, and reproducible

I went in expecting to find nothing, and I want to be honest about what's actually there — some of
this was my watch, after all.

There *is* a suite: `src/test/msixtest`, thirteen Catch2 source files covering the manifest reader,
block map reader, bundle readers, writer and unpack/unbundle round-trips. And there *are* real tamper
tests — `SignedTamperedBlockMap-TRUST_E_BAD_DIGEST.appx`, `SignedTamperedCD`,
`SignedTamperedContentTypes`, `SignedUntrustedCert-CERT_E_CHAINING` — which assert that a corrupted
package fails. Credit where it's due.

Two things are missing, and they're the two that matter.

**Nothing tests a hostile archive.** Every negative test is a *corrupted* package. There is no
zip-bomb test, no decompression-ratio limit, no path-traversal-on-read test, no duplicate
central-directory-name test, no overlapping-range or ZIP64 integer-overflow test. The difference
between "this file got damaged" and "someone built this file specifically to hurt you" is the entire
threat model, and only the first one is covered. Several tamper fixtures even sit in `testData` with no
active test case referencing them.

**And the security tests don't gate.** CI is Azure Pipelines. The SDK pipelines do run on PRs with
`failTaskOnFailedTests: true` — fine. But the MsixCore pipeline runs only on `master`, and its test
task is marked `continueOnError: true`. A test failure there stops nothing. There is no code-coverage
collection or threshold anywhere in the templates.

That's the real lesson, and it's not "they didn't write tests." It's that a suite which only models
accidents, and a gate that can't fail the build, add up to a tool you can't actually trust with a
hostile input.

So we built that half first, and it changed the design.

- **527 tests**, green.
- **6,961 lines of test code against 7,219 lines of source** — near 1:1.
- **Warnings-as-errors** on `net10.0`, nullable enabled.
- **Ten consecutive adversarial security reviews** on the signature-binding work alone.

That last number deserves a note, because it's the most useful thing I learned in this whole project.

We built the real attack: take a *genuine* SignTool signature off a real package and staple it onto
tampered content. Before the fix, our own tool said `INTEGRITY OK`, exit 0. Nine of the ten review

Worth noting where the reference implementation sits on this. The C++ SDK parses the AXPC (file
records) and AXCD (central directory) digests out of the signature and then simply never compares
them to anything — the source carries an open `// TODO: unnamed stream for central directory?` at
`src/msix/unpack/AppxSignature.cpp`. Its tampered-central-directory test still passes, but it passes
because Windows platform trust validation rejects the package, not because the portable code checked
the digest. On a non-Windows host that safety net isn't there.

I'm not going to claim what `signtool` or `makeappx` do or don't catch — I haven't tested them, and I'm
not going to assert it from reading someone else's source. What I can say is that the open-source
reference does not verify that binding, and we now do. Nine of the ten review
rounds found a **fail-open** — a code path that skipped verification. And the pattern was identical
every single time: a fix scoped to the path under discussion got bypassed by a path that wasn't.

The fixes that finally held all did the same thing — they removed the *possibility* of a second path.
One shared enumeration routine. Enforcement pushed down to the single lowest choke point. And the one
that ended the streak: making the drift check a **required interface member with no default
implementation**, so a type that forgets to verify is a *compile error* rather than a silent runtime
pass.

You cannot get there with a test suite that only tests the happy path.

## And it's faster

Benchmarked against MakeAppx 10.0.26100.8249 on an idle Arm64 Windows box, 21 reps after warmup,
stored/uncompressed.

| Operation | Package size | MSIX Core (.NET) | MakeAppx | Speedup |
|---|---|---|---|---|
| pack | 1 MB | 93.6 ms | 86.2 ms | 0.92× |
| pack | 10 MB | 151.5 ms | 283.5 ms | **1.87×** |
| pack | 64 MB | 493.2 ms | 875.3 ms | **1.77×** |
| unpack | 1 MB | 71.4 ms | 79.7 ms | 1.12× |
| unpack | 10 MB | 78.1 ms | 106.2 ms | 1.36× |
| unpack | 64 MB | 129.0 ms | 216.0 ms | **1.67×** |
| validate | 64 MB | 116.3 ms | *no equivalent* | — |

Memory is the bigger story:

| Metric | MSIX Core | MakeAppx | Advantage |
|---|---|---|---|
| Peak working set, 64 MB pack | 37.07 MB | 83.15 MB | **2.24× less** |
| Private bytes, 64 MB pack | 14.52 MB | 70.75 MB | **4.87× less** |
| Private bytes, unpack | — | — | **~1.9× less** |
| Peak working set, unpack | ~24 MB flat | — | flat regardless of package size |

Unpack memory is **flat at around 24 MB whether the package is 1 MB or 64 MB**, because nothing
buffers the container. That's not an accident — during the security work we explicitly refused a fix
that would have bought integrity by loading the whole package into memory, and found a narrower one
instead. Five small security-critical parts are cached; payload stays streaming.

Two honest caveats. MakeAppx wins the 1 MB pack — JIT warmup is real. And the managed tool is more
sensitive to CPU contention; a run on a busy machine flipped two rows to losses. Benchmark on an idle
box or don't quote the numbers.

For scale: the MakeAppx SDK footprint is **6.39 MB** across `AppxPackaging.dll`, `makeappx.exe`, and
`OpcServices.DLL` — plus a COM registration you inherit whether you wanted it or not.

## Where it is

**Merged.** 32 pull requests. 527 tests. Zero warnings.

- `MsixCore.Packaging` — open a package file *or* a loose folder; identity, manifest, block map,
  signature, integrity.
- `MsixCore.PackageStore` — extraction with path-traversal and symlink-escape containment, plus a
  cross-platform filesystem package store.
- `msixkit` — the CLI above. `inspect` / `validate` / `unpack` / `pack` / `bundle` run on Windows,
  Linux, and macOS.

Cross-tool compatibility is verified both directions: MakeAppx reads what we write, we read what
MakeAppx writes.

Still to come: bundle applicability and flattening, and actual Windows OS registration. Certificate
trust chains are not on that list, and never will be.

MSIX Core was supposed to be the core of MSIX. It didn't happen. Eight years later, on .NET 10, with a
test suite that actively tries to break it — maybe it can be something better: the layer that tells
you the truth about a package, fast enough to run on every commit, and legible to both the human and
the agent reading the output.
