namespace Tamp.Tauri.V2;

/// <summary>
/// Common knobs shared by every Tauri verb's settings class. Working directory + env overlay +
/// verbosity + an explicit override for the `tauri.conf.json` path the CLI consumes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tool resolution:</b> Tauri's CLI ships as <c>@tauri-apps/cli</c> via npm. Adopters typically
/// resolve via <c>[FromNodeModules("tauri")]</c> which finds <c>node_modules/.bin/tauri(.cmd)</c>.
/// Other valid paths are <c>npx tauri</c> (slower per-invocation), or a globally-installed
/// <c>tauri-cli</c> binary from cargo (<c>cargo install tauri-cli</c>) — the wrapper doesn't
/// opine on which.
/// </para>
/// <para>
/// <b>Working directory matters.</b> Tauri resolves <c>tauri.conf.json</c> from cwd. Set
/// <see cref="WorkingDirectory"/> to the directory containing <c>src-tauri/</c> (typically
/// the repo root for a one-app repo, or the per-app subdirectory in a monorepo).
/// </para>
/// </remarks>
public abstract class TauriSettingsBase
{
    /// <summary>Working directory for the spawned tauri process. Typically the directory containing <c>src-tauri/</c>.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables on top of the inherited environment.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>Override the path to <c>tauri.conf.json</c> (<c>--config</c>). Useful for env-specific configs.</summary>
    public string? ConfigPath { get; set; }

    /// <summary>Verbose output (<c>-v</c>). Set to 2 for <c>-vv</c>.</summary>
    public int Verbosity { get; set; }

    /// <summary>Suppress prompts and pretty output (<c>--ci</c>). Default true — most adopters use the wrapper from CI.</summary>
    public bool Ci { get; set; } = true;

    /// <summary>Subclasses produce the verb token(s) + verb-specific arguments.</summary>
    protected abstract IEnumerable<string> BuildVerbArguments();

    /// <summary>Subclasses extend the secret list (e.g. <c>Tauri.Signer</c>'s key password).</summary>
    protected virtual IEnumerable<Secret> CollectSecrets() => Array.Empty<Secret>();

    internal CommandPlan ToCommandPlan(Tool tool)
    {
        var args = new List<string>();
        args.AddRange(BuildVerbArguments());

        if (!string.IsNullOrEmpty(ConfigPath)) { args.Add("--config"); args.Add(ConfigPath!); }
        if (Verbosity >= 2) args.Add("-vv");
        else if (Verbosity == 1) args.Add("-v");
        if (Ci) args.Add("--ci");

        return new CommandPlan
        {
            Executable = tool.Executable.Value,
            Arguments = args,
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = WorkingDirectory ?? tool.WorkingDirectory,
            Secrets = CollectSecrets().ToList(),
        };
    }
}

/// <summary>Fluent setters for the common knobs.</summary>
public static class TauriSettingsBaseExtensions
{
    public static T SetWorkingDirectory<T>(this T s, string? cwd) where T : TauriSettingsBase { s.WorkingDirectory = cwd; return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : TauriSettingsBase { s.EnvironmentVariables[name] = value; return s; }
    public static T SetConfigPath<T>(this T s, string? path) where T : TauriSettingsBase { s.ConfigPath = path; return s; }
    public static T SetVerbosity<T>(this T s, int level) where T : TauriSettingsBase { s.Verbosity = level; return s; }
    public static T SetCi<T>(this T s, bool v = true) where T : TauriSettingsBase { s.Ci = v; return s; }
}

/// <summary>Settings for <c>tauri build</c> — produce desktop bundles.</summary>
public sealed class TauriBuildSettings : TauriSettingsBase
{
    /// <summary>Bundle types to produce (<c>--bundles</c>, comma-joined). Values: <c>app</c>, <c>dmg</c>, <c>deb</c>, <c>rpm</c>, <c>appimage</c>, <c>msi</c>, <c>nsis</c>, <c>updater</c>, <c>none</c>. Empty = Tauri picks per <c>tauri.conf.json</c>.</summary>
    public List<string> Bundles { get; } = new();

    /// <summary>Target triple (<c>--target</c>) — e.g. <c>x86_64-pc-windows-msvc</c>. Optional; defaults to host.</summary>
    public string? Target { get; set; }

    /// <summary>Skip the bundler step (<c>--no-bundle</c>) — just produce the binary. Useful for split build/bundle CI stages.</summary>
    public bool NoBundle { get; set; }

    /// <summary>Use debug profile (<c>--debug</c>). Default false (release).</summary>
    public bool Debug { get; set; }

    /// <summary>Cargo features forwarded to the underlying build (<c>--features</c>, comma-joined).</summary>
    public List<string> Features { get; } = new();

    /// <summary>Specific binary name to build (<c>--bin</c>). Useful for multi-bin workspaces.</summary>
    public string? Bin { get; set; }

    /// <summary>Custom build runner (<c>--runner</c>) — overrides the default cargo invocation.</summary>
    public string? Runner { get; set; }

    public TauriBuildSettings AddBundle(string type) { Bundles.Add(type); return this; }
    public TauriBuildSettings AddBundles(params string[] types) { Bundles.AddRange(types); return this; }
    public TauriBuildSettings SetTarget(string? triple) { Target = triple; return this; }
    public TauriBuildSettings SetNoBundle(bool v = true) { NoBundle = v; return this; }
    public TauriBuildSettings SetDebug(bool v = true) { Debug = v; return this; }
    public TauriBuildSettings AddFeature(string feature) { Features.Add(feature); return this; }
    public TauriBuildSettings AddFeatures(params string[] features) { Features.AddRange(features); return this; }

    /// <summary>
    /// Add the canonical Tauri custom-protocol cargo feature
    /// (<c>tauri/custom-protocol</c>). Without it, a release-built Tauri shell
    /// silently runs in dev mode at runtime — a notoriously expensive bug class
    /// to diagnose because everything compiles, signs, and packages fine.
    /// Idempotent — calling multiple times adds the feature only once.
    /// </summary>
    /// <remarks>
    /// Uses the workspace-qualified form <c>tauri/custom-protocol</c> which
    /// works regardless of whether the consuming crate is the workspace root
    /// or a workspace member. For non-workspace projects where the unqualified
    /// <c>custom-protocol</c> is sufficient, call <see cref="AddFeature"/>
    /// directly with that value.
    /// </remarks>
    public TauriBuildSettings EnableCustomProtocol()
    {
        const string Feature = "tauri/custom-protocol";
        if (!Features.Contains(Feature, StringComparer.Ordinal)) Features.Add(Feature);
        return this;
    }
    public TauriBuildSettings SetBin(string? name) { Bin = name; return this; }
    public TauriBuildSettings SetRunner(string? runner) { Runner = runner; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "build";
        if (Bundles.Count > 0) { yield return "--bundles"; yield return string.Join(",", Bundles); }
        if (!string.IsNullOrEmpty(Target)) { yield return "--target"; yield return Target!; }
        if (NoBundle) yield return "--no-bundle";
        if (Debug) yield return "--debug";
        if (Features.Count > 0) { yield return "--features"; yield return string.Join(",", Features); }
        if (!string.IsNullOrEmpty(Bin)) { yield return "--bin"; yield return Bin!; }
        if (!string.IsNullOrEmpty(Runner)) { yield return "--runner"; yield return Runner!; }
    }
}

/// <summary>Settings for <c>tauri info</c> — diagnostic snapshot of the local toolchain.</summary>
public sealed class TauriInfoSettings : TauriSettingsBase
{
    /// <summary>Skip the interactive section (<c>--interactive false</c>). Default true (we're typically in CI).</summary>
    public bool NonInteractive { get; set; } = true;

    public TauriInfoSettings SetNonInteractive(bool v = true) { NonInteractive = v; return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        yield return "info";
        // The --ci flag from the base already covers the CI case for `tauri info`; nothing
        // verb-specific to add here.
    }
}

/// <summary>Settings for <c>tauri icon</c> — generate platform icon sets from a source PNG.</summary>
public sealed class TauriIconSettings : TauriSettingsBase
{
    /// <summary>Source PNG path (positional arg). Required.</summary>
    public string? SourcePng { get; set; }

    /// <summary>Output directory (<c>--output</c>). Default: <c>src-tauri/icons/</c>.</summary>
    public string? Output { get; set; }

    /// <summary>Icon types to generate (<c>--ios-color</c>, <c>--png</c>, etc.). Most adopters use defaults.</summary>
    public List<string> ExtraFlags { get; } = new();

    public TauriIconSettings SetSourcePng(string path) { SourcePng = path; return this; }
    public TauriIconSettings SetOutput(string? path) { Output = path; return this; }
    public TauriIconSettings AddFlag(string flag) { ExtraFlags.Add(flag); return this; }

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (string.IsNullOrEmpty(SourcePng))
            throw new InvalidOperationException("SourcePng is required for tauri icon (set via SetSourcePng).");
        yield return "icon";
        yield return SourcePng!;
        if (!string.IsNullOrEmpty(Output)) { yield return "--output"; yield return Output!; }
        foreach (var f in ExtraFlags) yield return f;
    }
}

// ────────────────────────────────────────────────────────────────────────────
//  Signer (TAM-190 — landed in 0.2.0)
//
//  Tauri's updater signing relies on a minisign-style key pair. Two verbs:
//    `tauri signer generate -w <path>`  produces <path> (private) + <path>.pub
//    `tauri signer sign -k <key> <file>` writes <file>.sig alongside the artifact
//
//  Both verbs read the key password from the TAURI_SIGNING_PRIVATE_KEY_PASSWORD
//  env var. We route the Secret-typed password through Environment, NEVER the
//  CLI flag — keeping it off the process arg table is the whole point of the
//  Secret type, and Tauri's own docs prefer the env path.
//
//  Originally deferred from 0.1.0 because Secret.Reveal() was internal and
//  required InternalsVisibleTo. Tamp.Core 1.6.0 made Reveal() public + TAMP004-
//  analyzer-gated; the *Settings class-name suffix here is the canonical approved
//  context, so no IVT entry is needed.
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Settings for <c>tauri signer generate</c> — produce a minisign key pair for use with
/// Tauri's updater. Writes the private key to <see cref="WriteKeysPath"/> and the public
/// key to <c>&lt;path&gt;.pub</c>.
/// </summary>
public sealed class TauriSignerGenerateSettings : TauriSettingsBase
{
    /// <summary>Path to write the private key (<c>-w / --write-keys</c>). Public key goes to <c>&lt;path&gt;.pub</c>.</summary>
    public string? WriteKeysPath { get; set; }

    /// <summary>Force-overwrite an existing key file (<c>-f / --force</c>).</summary>
    public bool Force { get; set; }

    /// <summary>
    /// Password protecting the private key. Routed via the <c>TAURI_SIGNING_PRIVATE_KEY_PASSWORD</c>
    /// environment variable, not via a CLI flag. <see cref="Secret"/>-typed so the value is masked
    /// in Tamp's process trace and never appears in argument lists.
    /// </summary>
    public Secret? Password { get; set; }

    /// <summary>No-password mode (<c>--no-password</c>). Mutually exclusive with <see cref="Password"/>.</summary>
    public bool NoPassword { get; set; }

    public TauriSignerGenerateSettings SetWriteKeysPath(string path) { WriteKeysPath = path; return this; }
    public TauriSignerGenerateSettings SetForce(bool v = true) { Force = v; return this; }
    public TauriSignerGenerateSettings SetPassword(Secret secret) { Password = secret; return this; }
    public TauriSignerGenerateSettings SetNoPassword(bool v = true) { NoPassword = v; return this; }

    protected override IEnumerable<Secret> CollectSecrets() =>
        Password is null ? Array.Empty<Secret>() : new[] { Password };

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (string.IsNullOrEmpty(WriteKeysPath))
            throw new InvalidOperationException(
                "WriteKeysPath is required for `tauri signer generate` — set via SetWriteKeysPath.");
        if (Password is not null && NoPassword)
            throw new InvalidOperationException(
                "Password and NoPassword are mutually exclusive — pick one.");

        // Route the password into the env vars dictionary BEFORE the base materializes the plan.
        // ToCommandPlan() copies EnvironmentVariables into the returned CommandPlan; mutating
        // the dict here is the seam.
        if (Password is not null)
            EnvironmentVariables["TAURI_SIGNING_PRIVATE_KEY_PASSWORD"] = Password.Reveal();

        yield return "signer";
        yield return "generate";
        yield return "-w";
        yield return WriteKeysPath!;
        if (Force) yield return "-f";
        if (NoPassword) yield return "--no-password";
    }
}

/// <summary>
/// Settings for <c>tauri signer sign</c> — sign an artifact with a previously-generated minisign
/// private key, producing <c>&lt;file&gt;.sig</c> next to it.
/// </summary>
public sealed class TauriSignerSignSettings : TauriSettingsBase
{
    /// <summary>The file to sign (positional argument).</summary>
    public string? File { get; set; }

    /// <summary>Path to the private key, or the inline base64 key body (<c>-k / --private-key</c>).</summary>
    public string? PrivateKey { get; set; }

    /// <summary>
    /// Password protecting the private key. Routed via <c>TAURI_SIGNING_PRIVATE_KEY_PASSWORD</c>
    /// — never via the CLI flag, even though <c>tauri signer sign</c> accepts <c>-p</c>; the env
    /// path keeps the value off the process arg table.
    /// </summary>
    public Secret? Password { get; set; }

    /// <summary>Write a fresh signature even if one already exists (<c>-f / --force</c>).</summary>
    public bool Force { get; set; }

    public TauriSignerSignSettings SetFile(string path) { File = path; return this; }
    public TauriSignerSignSettings SetPrivateKey(string keyOrPath) { PrivateKey = keyOrPath; return this; }
    public TauriSignerSignSettings SetPassword(Secret secret) { Password = secret; return this; }
    public TauriSignerSignSettings SetForce(bool v = true) { Force = v; return this; }

    protected override IEnumerable<Secret> CollectSecrets() =>
        Password is null ? Array.Empty<Secret>() : new[] { Password };

    protected override IEnumerable<string> BuildVerbArguments()
    {
        if (string.IsNullOrEmpty(File))
            throw new InvalidOperationException(
                "File is required for `tauri signer sign` — set via SetFile.");
        if (string.IsNullOrEmpty(PrivateKey))
            throw new InvalidOperationException(
                "PrivateKey is required for `tauri signer sign` — set via SetPrivateKey (file path or inline base64).");

        if (Password is not null)
            EnvironmentVariables["TAURI_SIGNING_PRIVATE_KEY_PASSWORD"] = Password.Reveal();

        yield return "signer";
        yield return "sign";
        yield return "-k";
        yield return PrivateKey!;
        if (Force) yield return "-f";
        yield return File!;
    }
}

/// <summary>Settings for <c>tauri migrate</c> — migrate a Tauri v1 project to v2.</summary>
public sealed class TauriMigrateSettings : TauriSettingsBase
{
    protected override IEnumerable<string> BuildVerbArguments() { yield return "migrate"; }
}

/// <summary>Raw escape hatch for verbs not yet typed (e.g. <c>tauri plugin</c>, <c>tauri init</c>).</summary>
public sealed class TauriRawSettings : TauriSettingsBase
{
    private readonly List<string> _args = new();
    public void AddArgs(IEnumerable<string> args) => _args.AddRange(args);
    protected override IEnumerable<string> BuildVerbArguments() => _args;
}
