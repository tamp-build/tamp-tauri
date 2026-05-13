# Changelog

All notable changes to **Tamp.Tauri.V2** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

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
