using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Tamp.Tauri.V2;

/// <summary>Top-level facade for Tauri 2.x CLI verbs + path helpers for the externalBin sidecar contract.</summary>
public static class Tauri
{
    /// <summary><c>tauri build</c> — produce desktop bundles.</summary>
    public static CommandPlan Build(Tool tool, Action<TauriBuildSettings>? configure = null)
        => Run<TauriBuildSettings>(tool, configure);

    /// <summary><c>tauri info</c> — diagnostic snapshot of the local Tauri / Rust / Node toolchain.</summary>
    public static CommandPlan Info(Tool tool, Action<TauriInfoSettings>? configure = null)
        => Run<TauriInfoSettings>(tool, configure);

    /// <summary><c>tauri icon</c> — generate platform icon sets from a source PNG.</summary>
    public static CommandPlan Icon(Tool tool, Action<TauriIconSettings> configure)
        => Run<TauriIconSettings>(tool, configure);

    /// <summary>Nested verbs under <c>tauri signer</c> — minisign key generation + artifact signing for Tauri's updater (TAM-190, 0.2.0).</summary>
    public static class Signer
    {
        /// <summary><c>tauri signer generate -w &lt;path&gt;</c> — produce a minisign key pair. Password is routed via the <c>TAURI_SIGNING_PRIVATE_KEY_PASSWORD</c> env var.</summary>
        public static CommandPlan Generate(Tool tool, Action<TauriSignerGenerateSettings> configure)
            => Run<TauriSignerGenerateSettings>(tool, configure);

        /// <summary><c>tauri signer sign -k &lt;key&gt; &lt;file&gt;</c> — sign an artifact and write <c>&lt;file&gt;.sig</c> next to it. Password is routed via the <c>TAURI_SIGNING_PRIVATE_KEY_PASSWORD</c> env var.</summary>
        public static CommandPlan Sign(Tool tool, Action<TauriSignerSignSettings> configure)
            => Run<TauriSignerSignSettings>(tool, configure);

        // Object-init overloads (Tamp 1.2.0+ pattern).
        public static CommandPlan Generate(Tool tool, TauriSignerGenerateSettings settings) => Plan(tool, settings);
        public static CommandPlan Sign(Tool tool, TauriSignerSignSettings settings) => Plan(tool, settings);
    }

    /// <summary><c>tauri migrate</c> — migrate a Tauri v1 project to v2.</summary>
    public static CommandPlan Migrate(Tool tool, Action<TauriMigrateSettings>? configure = null)
        => Run<TauriMigrateSettings>(tool, configure);

    /// <summary>Raw escape hatch for verbs not yet typed (e.g. <c>tauri plugin</c>, <c>tauri init</c>).</summary>
    public static CommandPlan Raw(Tool tool, params string[] arguments)
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (arguments is null || arguments.Length == 0)
            throw new ArgumentException("Raw requires at least one argument.", nameof(arguments));
        var s = new TauriRawSettings();
        s.AddArgs(arguments);
        return s.ToCommandPlan(tool);
    }

    private static CommandPlan Run<T>(Tool tool, Action<T>? configure) where T : TauriSettingsBase, new()
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        var s = new T();
        configure?.Invoke(s);
        return s.ToCommandPlan(tool);
    }

    // ---- Object-init overloads ----

    public static CommandPlan Build(Tool tool, TauriBuildSettings settings) => Plan(tool, settings);
    public static CommandPlan Info(Tool tool, TauriInfoSettings settings) => Plan(tool, settings);
    public static CommandPlan Icon(Tool tool, TauriIconSettings settings) => Plan(tool, settings);
    public static CommandPlan Migrate(Tool tool, TauriMigrateSettings settings) => Plan(tool, settings);

    private static CommandPlan Plan<T>(Tool tool, T settings) where T : TauriSettingsBase
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan(tool);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ExternalBin path helpers — the load-bearing piece for sidecar contracts.
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute the absolute path Tauri expects for an external binary (sidecar). The contract
    /// is <c>&lt;srcTauriDir&gt;/binaries/&lt;name&gt;-&lt;target-triple&gt;[.exe]</c> — Tauri's
    /// bundler resolves <c>app.shell().sidecar("name")</c> against this layout at build time.
    /// </summary>
    /// <param name="srcTauriDir">The <c>src-tauri/</c> directory of the Tauri app.</param>
    /// <param name="name">The sidecar logical name (e.g. <c>dasbook-service</c>) — matches the <c>externalBin</c> entry in <c>tauri.conf.json</c> minus any target-triple suffix.</param>
    /// <param name="targetTriple">Rust target triple, e.g. <c>x86_64-pc-windows-msvc</c>. Must NOT be empty — Tauri's contract requires the suffix.</param>
    /// <param name="isWindows">When true, <c>.exe</c> is appended. When null (default), inferred from <paramref name="targetTriple"/> containing <c>windows</c>.</param>
    /// <returns>Absolute path where the sidecar binary must be placed before <c>tauri build</c> runs.</returns>
    /// <example>
    /// <code>
    /// var sidecar = Tauri.ExternalBinPath(
    ///     RootDirectory / "src-tauri",
    ///     "dasbook-service",
    ///     "x86_64-pc-windows-msvc");
    /// // → /Users/scott/repos/DasBook2/src-tauri/binaries/dasbook-service-x86_64-pc-windows-msvc.exe
    /// File.Copy(cargoReleasePath, sidecar.Value, overwrite: true);
    /// </code>
    /// </example>
    public static AbsolutePath ExternalBinPath(AbsolutePath srcTauriDir, string name, string targetTriple, bool? isWindows = null)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required.", nameof(name));
        if (string.IsNullOrEmpty(targetTriple))
            throw new ArgumentException(
                "targetTriple required — Tauri's externalBin contract demands the suffix. Use the same triple you pass to cargo build --target.",
                nameof(targetTriple));

        var isWin = isWindows ?? targetTriple.Contains("windows", StringComparison.OrdinalIgnoreCase);
        var ext = isWin ? ".exe" : "";
        var fileName = $"{name}-{targetTriple}{ext}";
        return srcTauriDir / "binaries" / fileName;
    }

    /// <summary>
    /// Compute the target-triple suffix for the current host platform — useful when an adopter
    /// wants the "native" sidecar path without hard-coding the triple.
    /// </summary>
    public static string HostTargetTriple()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            Architecture.X86 => "i686",
            Architecture.Arm => "arm",
            _ => "unknown",
        };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"{arch}-pc-windows-msvc";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return $"{arch}-unknown-linux-gnu";
        return $"{arch}-unknown-unknown";
    }
}
