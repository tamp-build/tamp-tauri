# Changelog

All notable changes to **Tamp.Tauri.V2** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

## [0.2.1] — pending — `CargoBuildSettings.AsTauriShell()` cargo-side helper (TAM-205)

### Added

- **`CargoBuildSettings.AsTauriShell()`** — cross-package extension method
  (defined in `Tamp.Tauri.V2`, extends `Tamp.Cargo.CargoBuildSettings`) that
  idempotently adds the workspace-qualified `tauri/custom-protocol` cargo
  feature. Closes the loop for adopters who bypass `Tauri.Build(...)` to
  access cargo-only knobs (custom profiles, linker overrides) that
  tauri-cli doesn't surface to its inner cargo invocation.

  ```csharp
  Cargo.Build(CargoBin, s => s
      .SetWorkingDirectory(SrcTauri)
      .SetProfile("fast-release")     // tauri-cli doesn't expose --profile
      .AsTauriShell()                  // adds tauri/custom-protocol idempotently
      .SetLocked());
  ```

  Without `tauri/custom-protocol`, the release-built Tauri shell silently
  runs in dev mode at runtime — a notoriously expensive bug class because
  compile / sign / package all succeed but the distributed binary fails
  to launch correctly. `AsTauriShell` is the cargo-side mirror of
  `TauriBuildSettings.EnableCustomProtocol()` (from 0.2.0).

### Dependencies

- `Tamp.Tauri.V2` now depends on `Tamp.Cargo` (>= 0.2.0). Adopters using
  `Tamp.Tauri.V2` will pick up `Tamp.Cargo` transitively. The cross-package
  reference is justified — adopters using Tauri almost always pair it with
  Cargo, and the `AsTauriShell` extension is intrinsically Tauri-flavored
  even though it operates on a Cargo type.

### Why

DasBook canary friction batch #3 #8 (2026-05-13). DasBook needed
`--profile fast-release` because of an MSVC 14.50 fat-LTO crash; tauri-cli
doesn't surface `--profile` for its inner cargo invocation, so they
bypassed `Tauri.Build()` and went straight to `Cargo.Build(CargoBin, s =>
...)`. The bypass lost access to the `EnableCustomProtocol()` we shipped
in 0.2.0 specifically for them.

### Tests

- 7 new tests in `TauriCargoExtensionsTests` covering: feature added,
  idempotency (multiple calls = single feature entry), composition with
  adopter-added features (order preserved), no duplication when feature
  was added manually first, return-same-instance for chaining, null guard,
  full DasBook-style pipeline composition.

## [0.2.0] — pending — typed `tauri signer` verbs (TAM-190) + `EnableCustomProtocol()` (TAM-201)

### Added — `EnableCustomProtocol()` convenience (TAM-201)

- **`TauriBuildSettings.EnableCustomProtocol()`** — adds the canonical
  workspace-qualified Tauri cargo feature `tauri/custom-protocol`. Idempotent
  (re-calling does not duplicate). Without this feature, a release-built Tauri
  shell silently runs in dev mode at runtime — an expensive bug class to
  diagnose because compile/sign/package all succeed.

  ```csharp
  Tauri.Build(TauriCli, s => s
      .SetTarget("x86_64-pc-windows-msvc")
      .AddBundles("msi", "nsis")
      .EnableCustomProtocol());        // formerly: .AddFeature("tauri/custom-protocol")
  ```

  Filed from the DasBook canary (2026-05-13). The functional surface always
  existed via `AddFeature(...)` / `AddFeatures(...)`; this method is
  ergonomics + typo-avoidance + documents the bug class in xmldoc.

  4 new unit tests in `TauriTests`: feature added correctly, idempotence,
  composition with other features, `Same`-instance return for chaining.

### Added — typed `tauri signer` verbs (TAM-190)

- **`Tauri.Signer.Generate(...)`** — `tauri signer generate -w <path>`. Produces
  a minisign key pair for use with Tauri's updater. `SetWriteKeysPath(path)`
  required. `SetForce()` for overwrite. `SetPassword(Secret)` routes the key
  password via the `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` environment variable —
  never via CLI flag, never on the process arg table. `SetNoPassword()` for
  unprotected key flows; mutually exclusive with `SetPassword`.

- **`Tauri.Signer.Sign(...)`** — `tauri signer sign -k <key> <file>`. Signs an
  artifact and writes `<file>.sig` next to it. `SetFile(path)` and
  `SetPrivateKey(keyOrPath)` required. `SetPassword(Secret)` routed via env var
  the same way. `SetForce()` to overwrite an existing signature.

Both verbs originally deferred from 0.1.0 because `Secret.Reveal()` required
`InternalsVisibleTo` on Tamp.Core. Tamp.Core 1.6.0 made `Reveal()` public +
TAMP004-analyzer-gated; the `*Settings` class-name suffix here is the canonical
approved context, so no IVT entry was ever needed. The deferral became a no-op
once 1.6.0 shipped — TAM-190 picked it up cleanly.

### Tests

- 11 new unit tests in `TauriTests`: WriteKeysPath required, force flag,
  password → env routing (and never argv), Password ⊕ NoPassword mutual
  exclusion, NoPassword standalone, sign requires File + PrivateKey, sign
  emits `-k` + positional file, sign force flag, sign password → env routing.

## [0.1.0] - 2026-05-13

### Added

- Initial release. Wraps the Tauri 2.x CLI (`@tauri-apps/cli`). Verb surface:
  `Build`, `Info`, `Icon`, `Migrate`, `Raw`. Filed under TAM-188.

- **`Tauri.ExternalBinPath(srcTauriDir, name, targetTriple, isWindows?)`** — load-bearing
  helper that computes the absolute path Tauri expects for an external binary sidecar:
  `<srcTauri>/binaries/<name>-<target-triple>[.exe]`. Makes the sidecar staging path a
  typed value flowing through the build graph instead of a memo. Addresses the live class
  of bug DasBook's brief flagged where a Rust binary lands at the wrong staging directory
  because the file-copy step lives in human memory.

- **`Tauri.HostTargetTriple()`** — convenience for the "build for current platform" case.
  Returns `x86_64-pc-windows-msvc` / `x86_64-unknown-linux-gnu` / `aarch64-pc-windows-msvc`
  etc. based on `RuntimeInformation`.

- `--ci` flag defaults to ON for every verb (turn off via `.SetCi(false)` for interactive
  invocations).

- `TauriBuildSettings` covers `--bundles` (comma-joined), `--target`, `--no-bundle`,
  `--debug`, `--features` (comma-joined), `--bin`, `--runner`.

- `Raw` escape hatch covers verbs not yet typed (`plugin`, `init`, `signer`, etc.).

### Deferred

- **`tauri signer generate` / `tauri signer sign`** intentionally NOT typed in 0.1.0.
  Both pass a signing-key password to the spawned process, which needs `Tamp.Tauri.V2` on
  `Tamp.Core`'s `InternalsVisibleTo` list to `Reveal()` the password safely into
  `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`. Filed as **TAM-190** for the 0.2.0 wave.
  DasBook (the 0.1.0 canary) doesn't sign updater artifacts so this isn't blocking.
  Adopters who need signing now can use `Tauri.Raw(tool, "signer", "generate", ...)` and
  manage `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` on their own env.

### Notes

- Second non-.NET satellite after `Tamp.Cargo`. Continues the toolchain-wrapper pattern
  established there (settings classes derive from `TauriSettingsBase`, fluent setters
  return `this`, object-init overloads parallel each fluent overload, `Raw` escape hatch).

- Tool resolution typically via `[FromNodeModules("tauri")]` since adopters install via
  npm's `@tauri-apps/cli`. Alternative paths (`[FromPath("tauri")]` for global installs,
  `npx tauri` for npx-mediated invocations) work with the same wrapper.

- 19 unit tests cover positive + negative cases including the `ExternalBinPath` contract
  (Windows `.exe` suffix inference, Linux/macOS no-suffix, explicit `isWindows` override,
  empty-arg rejection) and the `HostTargetTriple` runtime detection.
