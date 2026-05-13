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

// `tauri signer generate` and `tauri signer sign` are intentionally NOT typed in v0.1.0.
// Both verbs need to pass a key password to the spawned process, which requires either:
//   (a) Tamp.Tauri.V2 on Tamp.Core's InternalsVisibleTo list (to Reveal() the password
//       into an env var like TAURI_SIGNING_PRIVATE_KEY_PASSWORD), OR
//   (b) emitting the password on the CLI directly (visible to /proc; bad shape).
// DasBook (the v0.1.0 canary) doesn't sign updater artifacts so it's not blocking.
// Filed as TAM-190 for the 0.2.0 wave once the InternalsVisibleTo grant is in place.
// Adopters who need signing now can use Tauri.Raw(tool, "signer", "generate", ...) and
// manage TAURI_SIGNING_PRIVATE_KEY_PASSWORD on their own env.

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
