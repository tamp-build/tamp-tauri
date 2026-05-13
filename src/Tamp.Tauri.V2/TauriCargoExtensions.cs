using Tamp.Cargo;

namespace Tamp.Tauri.V2;

/// <summary>
/// Cross-package extensions on <see cref="CargoBuildSettings"/> for the
/// Tauri shell crate build path. Lives in <c>Tamp.Tauri.V2</c> rather than
/// <c>Tamp.Cargo</c> because the surface is conceptually Tauri-specific —
/// adopters reach for these when invoking <c>cargo build</c> against the
/// <c>src-tauri</c> crate directly (typically to expose cargo flags like
/// <c>--profile</c> that <c>tauri build</c> doesn't surface).
/// </summary>
/// <remarks>
/// Filed under TAM-205. DasBook canary friction #8 — adopters with custom
/// cargo profiles (e.g. an LTO-tuned <c>fast-release</c>) need to bypass
/// <see cref="Tauri.Build(Tamp.Tool, System.Action{TauriBuildSettings}?)"/>
/// to access <see cref="CargoBuildLikeSettingsBase.Profile"/>, which leaves
/// the canonical <c>tauri/custom-protocol</c> feature flag unreachable
/// through <see cref="TauriBuildSettings.EnableCustomProtocol"/>.
/// <see cref="AsTauriShell"/> closes the loop on the cargo-side build path.
/// </remarks>
public static class TauriCargoExtensions
{
    /// <summary>
    /// Configure a <see cref="CargoBuildSettings"/> instance as the Tauri shell
    /// crate's cargo build: idempotently adds the workspace-qualified
    /// <c>tauri/custom-protocol</c> feature so the release-built binary runs
    /// as a packaged Tauri shell, not in dev mode at runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without the <c>tauri/custom-protocol</c> feature, a release-built Tauri
    /// shell silently runs in dev mode at runtime — a notoriously expensive
    /// bug class because compile / sign / package all succeed, but the
    /// distributed binary attempts to talk to a dev server that doesn't exist.
    /// </para>
    /// <para>
    /// This extension is the cargo-side mirror of
    /// <see cref="TauriBuildSettings.EnableCustomProtocol"/>. Use when the
    /// adopter needs cargo-only knobs (custom profile, additional features,
    /// linker overrides) that <see cref="Tauri.Build(Tamp.Tool, System.Action{TauriBuildSettings}?)"/>
    /// doesn't surface:
    /// </para>
    /// <code>
    /// Cargo.Build(CargoBin, s => s
    ///     .SetWorkingDirectory(SrcTauri)
    ///     .SetProfile("fast-release")    // custom profile for the LTO-crash workaround
    ///     .AsTauriShell()                // adds tauri/custom-protocol idempotently
    ///     .SetLocked())
    /// </code>
    /// <para>
    /// Idempotent — re-applying does not duplicate the feature in the
    /// <see cref="CargoBuildSettings.Features"/> list.
    /// </para>
    /// </remarks>
    public static CargoBuildSettings AsTauriShell(this CargoBuildSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        const string Feature = "tauri/custom-protocol";
        if (!settings.Features.Contains(Feature, StringComparer.Ordinal))
            settings.Features.Add(Feature);
        return settings;
    }
}
