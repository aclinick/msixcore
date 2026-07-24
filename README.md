# MSIX Core (.NET)

A cross-platform C# (.NET 10) port and modernization of Microsoft's
[MSIX Core (`msixmgr`)](https://github.com/microsoft/msix-packaging/tree/master/MsixCore).

The original MSIX Core was a C++ downlevel installer that let older Windows
releases (Windows 7 SP1+, Server 2012+) install MSIX packages. It was intended
to become the literal *core* of MSIX but never did. This project re-imagines it
as a modern, memory-safe, **cross-platform** library and CLI:

- **`MsixCore.Packaging`** — cross-platform package reading: OPC/ZIP container,
  `AppxManifest.xml` parsing, block map, signature validation, identity
  (`PackageFullName` / `PackageFamilyName`).
- **`MsixCore.Deployment`** — install/uninstall engine with a handler pipeline.
  Extraction is cross-platform; OS integration (shortcuts, registry, file-type
  associations) is guarded to the relevant platform.
- **`msixmgr`** — command-line tool with parity to the original verbs plus
  modern additions.

## Status

Under active, phased development. Each phase lands as its own reviewed PR with
full test coverage. See the phase plan in the repository issues / project board.

## Requirements

- .NET 10 SDK (pinned in `global.json`).

## Build & test

```bash
dotnet build
dotnet test
```

## License

MIT — see [LICENSE](LICENSE).
