# Changelog

All notable changes to **Tamp.Tauri.V2** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [SemVer](https://semver.org/spec/v2.0.0.html).

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
