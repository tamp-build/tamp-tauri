using Tamp.Cargo;
using Xunit;

namespace Tamp.Tauri.V2.Tests;

/// <summary>
/// Tests for <see cref="TauriCargoExtensions.AsTauriShell"/> — the cargo-side
/// shell-feature helper that adopters reach for when bypassing <c>tauri build</c>
/// to access cargo-only knobs (custom profiles, linker overrides).
/// Filed under TAM-205.
/// </summary>
public sealed class TauriCargoExtensionsTests
{
    [Fact]
    public void AsTauriShell_Adds_Workspace_Qualified_Feature()
    {
        var s = new CargoBuildSettings().AsTauriShell();
        Assert.Contains("tauri/custom-protocol", s.Features);
    }

    [Fact]
    public void AsTauriShell_Is_Idempotent_Across_Repeated_Calls()
    {
        var s = new CargoBuildSettings();
        s.AsTauriShell();
        s.AsTauriShell();
        s.AsTauriShell();
        Assert.Single(s.Features);
        Assert.Equal("tauri/custom-protocol", s.Features[0]);
    }

    [Fact]
    public void AsTauriShell_Composes_With_Adopter_Added_Features()
    {
        var s = new CargoBuildSettings();
        s.AddFeature("ssl");
        s.AsTauriShell();
        s.AddFeature("telemetry");
        // Order preserved — the helper just appends if missing.
        Assert.Equal(new[] { "ssl", "tauri/custom-protocol", "telemetry" }, s.Features);
    }

    [Fact]
    public void AsTauriShell_Does_Not_Duplicate_When_Feature_Was_Manually_Added()
    {
        var s = new CargoBuildSettings();
        s.AddFeature("tauri/custom-protocol");   // adopter added manually
        s.AsTauriShell();                         // helper called later
        // No duplication — the helper checks existing list.
        Assert.Single(s.Features);
    }

    [Fact]
    public void AsTauriShell_Returns_Same_Instance_For_Chaining()
    {
        var s = new CargoBuildSettings();
        Assert.Same(s, s.AsTauriShell());
    }

    [Fact]
    public void AsTauriShell_Throws_On_Null_Settings()
    {
        CargoBuildSettings? s = null;
        Assert.Throws<ArgumentNullException>(() => s!.AsTauriShell());
    }

    [Fact]
    public void AsTauriShell_Composes_Cleanly_In_Full_DasBook_Style_Pipeline()
    {
        // Worked example from the canary friction (TAM-205) — adopter needs
        // a custom profile that tauri-cli doesn't surface, so they bypass
        // Tauri.Build and configure cargo directly. AsTauriShell preserves
        // the canonical feature flag the bypass would otherwise drop.
        var s = new CargoBuildSettings()
            .SetWorkingDirectory(AbsolutePath.Create(Path.GetTempPath()) / "fake-src-tauri")
            .SetProfile("fast-release")
            .AsTauriShell()
            .SetLocked();

        Assert.Equal("fast-release", s.Profile);
        Assert.True(s.Locked);
        Assert.Contains("tauri/custom-protocol", s.Features);
    }
}
