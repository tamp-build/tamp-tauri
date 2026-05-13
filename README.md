# Tamp.Tauri.V2

> Wrapper for the Tauri 2.x CLI (`@tauri-apps/cli`) plus typed path helpers for the externalBin sidecar contract. Pairs with [`Tamp.Cargo`](https://github.com/tamp-build/tamp-cargo) for the canonical Rust + Tauri build chain.

| Package | Status |
|---|---|
| `Tamp.Tauri.V2` | 0.1.0 (initial) |

## Why this exists

Tauri's `externalBin` contract requires sidecar binaries to land at a specific path: `<src-tauri>/binaries/<name>-<target-triple>[.exe]`. Many shipped Tauri apps have a class of bug where the sidecar is in the wrong directory, the wrong target-triple, or missing the `.exe` suffix — the user installs the app and gets a "service not running" dialog because Tauri's runtime sidecar resolver can't find the file.

The pattern is universal: the file-copy step that puts the Rust binary at the externalBin path lives in human memory (or a memo, or a bash script comment), not the build graph. `Tamp.Tauri.V2.ExternalBinPath(...)` makes that path a typed value flowing through the build script — so the step is part of the dependency graph, not a tribal-knowledge step that drifts from reality.

## Install

```bash
dotnet add package Tamp.Tauri.V2
```

Multi-targets net8 / net9 / net10. Requires Tauri 2.x CLI on PATH or in `node_modules/.bin/` (the typical install path).

## Quick start — Rust + Tauri chain end-to-end

```csharp
using Tamp;
using Tamp.Cargo;
using Tamp.Tauri.V2;

class Build : TampBuild
{
    public static int Main(string[] args) => Execute<Build>(args);

    [FromPath("cargo")] readonly Tool CargoBin = null!;
    [FromNodeModules("tauri")] readonly Tool TauriCli = null!;

    AbsolutePath ServiceCrate => RootDirectory / "dasbook-service";
    AbsolutePath SrcTauri => RootDirectory / "src-tauri";

    const string TargetTriple = "x86_64-pc-windows-msvc";

    Target BuildService => _ => _
        .Description("[Build] Compile dasbook-service for the Tauri sidecar slot")
        .Executes(() => Cargo.Build(CargoBin, s => s
            .SetWorkingDirectory(ServiceCrate)
            .SetRelease()
            .SetTarget(TargetTriple)
            .SetLocked()));

    Target StageSidecar => _ => _
        .DependsOn(nameof(BuildService))
        .Description("[Build] Stage the compiled service into Tauri's externalBin slot")
        .Executes(() =>
        {
            var built = ServiceCrate / "target" / TargetTriple / "release" / "dasbook-service.exe";
            var sidecar = Tauri.ExternalBinPath(SrcTauri, "dasbook-service", TargetTriple);
            Directory.CreateDirectory(sidecar.Parent!.Value);
            File.Copy(built.Value, sidecar.Value, overwrite: true);
            // The path that used to live in a memo is now a typed expression in the build graph.
        });

    Target BuildDesktop => _ => _
        .DependsOn(nameof(StageSidecar))
        .Description("[Build] Bundle the Tauri desktop app (msi + nsis)")
        .Executes(() => Tauri.Build(TauriCli, s => s
            .SetWorkingDirectory(RootDirectory)
            .AddBundles("msi", "nsis")
            .SetTarget(TargetTriple)));
}
```

The sidecar staging step that was previously documented in `BUILD_NOTES.md` and lived in human memory is now part of the dependency graph: `BuildDesktop ← StageSidecar ← BuildService`. Drift between docs and reality stops being possible because there are no docs to drift from.

## Verb surface

| Tamp method | tauri command | Notes |
|---|---|---|
| `Tauri.Build(...)` | `tauri build` | `--bundles msi,nsis,...`, `--target`, `--no-bundle`, `--debug`, `--features`, `--bin`, `--runner`. |
| `Tauri.Info(...)` | `tauri info` | Diagnostic snapshot of toolchain — useful in CI logs. |
| `Tauri.Icon(...)` | `tauri icon <png> [--output dir]` | Generate platform icon sets from a source PNG. |
| `Tauri.Migrate(...)` | `tauri migrate` | v1 → v2 project migration. |
| `Tauri.Raw(...)` | `tauri <anything>` | Escape hatch — covers `plugin`, `init`, `signer`, etc. |

`--ci` defaults to ON for every verb (turn off via `.SetCi(false)` for interactive runs).

### What's deferred

`tauri signer generate` and `tauri signer sign` are not yet typed in 0.1.0 — filed as **TAM-190** for a 0.2.0 wave when an adopter actually needs to sign updater artifacts. (Originally this needed `Tamp.Tauri.V2` on `Tamp.Core`'s `InternalsVisibleTo` list to safely `Reveal()` the password into `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`; as of Tamp.Core **1.6.0** `Secret.Reveal()` is public and TAMP004-gated, so the deferral now is just about the unwritten verb surface.) Until then, `Tauri.Raw(tool, "signer", "generate", ...)` with adopter-managed env covers the case.

## `ExternalBinPath` — the load-bearing helper

```csharp
public static AbsolutePath ExternalBinPath(
    AbsolutePath srcTauriDir,
    string name,
    string targetTriple,
    bool? isWindows = null);
```

Computes the absolute path Tauri expects for an external binary:

```
<srcTauriDir>/binaries/<name>-<target-triple>[.exe]
```

Examples:
- `ExternalBinPath(srcTauri, "dasbook-service", "x86_64-pc-windows-msvc")` → `…/src-tauri/binaries/dasbook-service-x86_64-pc-windows-msvc.exe`
- `ExternalBinPath(srcTauri, "dasbook-service", "x86_64-unknown-linux-gnu")` → `…/src-tauri/binaries/dasbook-service-x86_64-unknown-linux-gnu`

The `.exe` suffix is inferred from the target-triple (any triple containing `windows`). Override explicitly via the `isWindows` parameter for unusual cases.

Empty `name` or `targetTriple` is rejected at call time — the triple is required by Tauri's contract regardless of host platform; defaulting to host would produce silently-wrong sidecar names in cross-compile scenarios.

## `HostTargetTriple` — for the "build for the current platform" case

```csharp
var triple = Tauri.HostTargetTriple();
// e.g. "x86_64-pc-windows-msvc" on Windows x64
//      "x86_64-unknown-linux-gnu" on Linux x64
//      "aarch64-pc-windows-msvc" on Windows arm64
```

Useful when the build script targets the host architecture and doesn't want to hard-code the triple. For cross-compile, set the triple explicitly via `Cargo.Build(...)`'s `SetTarget(...)` and pass the same value to `ExternalBinPath`.

## Tool resolution

Tauri's CLI ships as `@tauri-apps/cli` via npm. Adopters typically resolve via:

```csharp
[FromNodeModules("tauri")] readonly Tool TauriCli = null!;
```

This finds `node_modules/.bin/tauri(.cmd)` for npm-installed projects. Alternative resolutions:

- `[FromPath("tauri")]` — globally-installed via `cargo install tauri-cli` (less common)
- `[FromPath("npx")]` + `Tauri.Raw(tool, "tauri", "build", ...)` — npx-mediated invocation (slower)

The wrapper doesn't opine on which path you choose; it just invokes the resolved Tool.

## Sibling packages

- [`Tamp.Cargo`](https://github.com/tamp-build/tamp-cargo) — Rust toolchain. The canonical paired wrapper for Tauri's Rust side.
- [`Tamp.Npm.V10`](https://github.com/tamp-build/tamp-npm) — for the frontend stage of a Tauri app (`vite dev` / `vite build` orchestration via npm scripts).
- [`Tamp.Msix`](https://github.com/tamp-build/tamp-msix) — wraps `makeappx` / `signtool` for Microsoft Store packaging downstream of `tauri build`.

## Releasing

Releases follow the [Tamp dogfood pattern](MAINTAINERS.md): bump `<Version>` in `Directory.Build.props`, tag `v<X.Y.Z>`, GitHub Actions runs `dotnet tamp Ci` then `dotnet tamp Push`.

## License

MIT. See [LICENSE](LICENSE).
