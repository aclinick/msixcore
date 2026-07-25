# MSIX Core Studio UI

## Goals

MSIX Core Studio is a cross-platform desktop inspector for `.msix`/`.appx` files and loose package
folders. It uses the managed `MsixCore.Packaging` APIs directly, so identity, manifest, block-map
verification, and signature results are consistent with the CLI and library on Windows, Linux, and
macOS. Later releases can grow into a packager and a Windows-gated installer.

## Why Avalonia

Avalonia 11 supports .NET 10 and provides one XAML UI across Windows, Linux, and macOS. That matches
the cross-platform purpose of this port and avoids introducing a Windows-only dependency into the
inspection workflow.

## Structure

The app uses MVVM with CommunityToolkit.Mvvm:

- `MainWindow` supplies only the platform storage picker.
- `MainWindowViewModel` owns commands, loading state, errors, and presentation models.
- `MsixPackage.Open` / `OpenDirectory` are used on a worker thread, and the package is disposed after
  an immutable snapshot has been produced.
- Packaging and deployment projects are referenced directly; no library code is copied.

## Screens

After **Open package** or **Open folder**, the window provides:

1. **Overview** — identity name, publisher, version, architecture, family/full names, display names,
   framework status, and capabilities.
2. **Applications** — manifest application IDs, display names, executables, and entry points.
3. **Files & Block Map** — block-map file names, sizes, block counts, per-file verdicts, and the
   overall `VerifyBlockMap()` result.
4. **Signature** — signed/unsigned state plus signer subject, issuer, thumbprint, certificate validity,
   CMS integrity, and publisher match. Missing or malformed optional data is shown as an unavailable
   result instead of terminating the app.

## Deferred

- Installation and registration UI is Windows-only and must be runtime/compile-time gated.
- Package creation, editing, and signing UI.
- Native app packaging/distribution for Windows, macOS, and Linux.

## Run

From the repository root:

```text
dotnet run --project src/MsixCore.Studio/MsixCore.Studio.csproj
```

Choose a package file or a loose folder containing `AppxManifest.xml` and `AppxBlockMap.xml`.
