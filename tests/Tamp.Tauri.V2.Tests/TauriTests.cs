using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Tamp;
using Tamp.Tauri.V2;
using Xunit;

namespace Tamp.Tauri.V2.Tests;

public sealed class TauriTests
{
    private static Tool FakeTool() => new(AbsolutePath.Create("/fake/tauri"));

    private static int IndexOf(IReadOnlyList<string> args, string token)
    {
        for (var i = 0; i < args.Count; i++) if (args[i] == token) return i;
        return -1;
    }

    // ---- Build ----

    [Fact]
    public void Build_Bare_Has_Verb_And_Ci_Default()
    {
        var plan = Tauri.Build(FakeTool());
        Assert.Equal("build", plan.Arguments[0]);
        Assert.Contains("--ci", plan.Arguments);   // CI flag default-on
    }

    [Fact]
    public void Build_With_Bundles_And_Target()
    {
        var plan = Tauri.Build(FakeTool(), s => s
            .AddBundles("msi", "nsis")
            .SetTarget("x86_64-pc-windows-msvc"));
        Assert.Equal("msi,nsis", plan.Arguments[IndexOf(plan.Arguments, "--bundles") + 1]);
        Assert.Equal("x86_64-pc-windows-msvc", plan.Arguments[IndexOf(plan.Arguments, "--target") + 1]);
    }

    [Fact]
    public void Build_NoBundle_Plus_Debug_Plus_Features()
    {
        var plan = Tauri.Build(FakeTool(), s => s
            .SetNoBundle()
            .SetDebug()
            .AddFeatures("custom-protocol", "updater"));
        Assert.Contains("--no-bundle", plan.Arguments);
        Assert.Contains("--debug", plan.Arguments);
        Assert.Equal("custom-protocol,updater",
            plan.Arguments[IndexOf(plan.Arguments, "--features") + 1]);
    }

    [Fact]
    public void EnableCustomProtocol_Adds_Qualified_Feature()
    {
        var plan = Tauri.Build(FakeTool(), s => s.EnableCustomProtocol());
        Assert.Equal("tauri/custom-protocol",
            plan.Arguments[IndexOf(plan.Arguments, "--features") + 1]);
    }

    [Fact]
    public void EnableCustomProtocol_Is_Idempotent()
    {
        var plan = Tauri.Build(FakeTool(), s => s
            .EnableCustomProtocol()
            .EnableCustomProtocol()
            .EnableCustomProtocol());
        // Single feature, not "tauri/custom-protocol,tauri/custom-protocol,..."
        Assert.Equal("tauri/custom-protocol",
            plan.Arguments[IndexOf(plan.Arguments, "--features") + 1]);
    }

    [Fact]
    public void EnableCustomProtocol_Composes_With_Other_Features()
    {
        var plan = Tauri.Build(FakeTool(), s => s
            .AddFeature("updater")
            .EnableCustomProtocol()
            .AddFeature("devtools"));
        // Order is preserved; the qualified feature lands in the middle.
        Assert.Equal("updater,tauri/custom-protocol,devtools",
            plan.Arguments[IndexOf(plan.Arguments, "--features") + 1]);
    }

    [Fact]
    public void EnableCustomProtocol_Returns_Same_Instance_For_Chaining()
    {
        var s = new TauriBuildSettings();
        Assert.Same(s, s.EnableCustomProtocol());
    }

    [Fact]
    public void Build_Bin_Plus_Runner()
    {
        var plan = Tauri.Build(FakeTool(), s => s.SetBin("dasbook2").SetRunner("cargo-tauri"));
        Assert.Equal("dasbook2", plan.Arguments[IndexOf(plan.Arguments, "--bin") + 1]);
        Assert.Equal("cargo-tauri", plan.Arguments[IndexOf(plan.Arguments, "--runner") + 1]);
    }

    [Fact]
    public void Build_Disable_Ci_Mode()
    {
        var plan = Tauri.Build(FakeTool(), s => s.SetCi(false));
        Assert.DoesNotContain("--ci", plan.Arguments);
    }

    // ---- Info / Icon / Migrate / Raw ----

    [Fact]
    public void Info_Has_Verb()
    {
        var plan = Tauri.Info(FakeTool());
        Assert.Equal("info", plan.Arguments[0]);
    }

    [Fact]
    public void Icon_Requires_SourcePng_And_Emits_Positional()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Tauri.Icon(FakeTool(), _ => { }).Arguments.ToList());
        var plan = Tauri.Icon(FakeTool(), s => s.SetSourcePng("app-icon.png").SetOutput("src-tauri/icons"));
        Assert.Equal(new[] { "icon", "app-icon.png" }, plan.Arguments.Take(2));
        Assert.Equal("src-tauri/icons", plan.Arguments[IndexOf(plan.Arguments, "--output") + 1]);
    }

    [Fact]
    public void Migrate_Has_Verb()
    {
        var plan = Tauri.Migrate(FakeTool());
        Assert.Equal("migrate", plan.Arguments[0]);
    }

    [Fact]
    public void Raw_Allows_Arbitrary_Verb()
    {
        var plan = Tauri.Raw(FakeTool(), "plugin", "android", "init");
        Assert.Equal(new[] { "plugin", "android", "init" }, plan.Arguments.Take(3));
    }

    [Fact]
    public void Raw_Rejects_Empty_Args()
    {
        Assert.Throws<ArgumentException>(() => Tauri.Raw(FakeTool()));
    }

    // ---- Signer (TAM-190) ----

    [Fact]
    public void Signer_Generate_Requires_WriteKeysPath()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Tauri.Signer.Generate(FakeTool(), s => { }).Arguments.ToList());
    }

    [Fact]
    public void Signer_Generate_Emits_W_Flag()
    {
        var plan = Tauri.Signer.Generate(FakeTool(), s => s
            .SetWriteKeysPath("./keys/updater.key"));
        Assert.Equal(new[] { "signer", "generate", "-w", "./keys/updater.key" }, plan.Arguments.Take(4));
    }

    [Fact]
    public void Signer_Generate_Force_Flag()
    {
        var plan = Tauri.Signer.Generate(FakeTool(), s => s
            .SetWriteKeysPath("./keys/updater.key").SetForce());
        Assert.Contains("-f", plan.Arguments);
    }

    [Fact]
    public void Signer_Generate_Password_Routes_To_Env_Var_Not_Argv()
    {
        var pwd = new Secret("updater-key-pwd", "s3cret-key-pwd");
        var plan = Tauri.Signer.Generate(FakeTool(), s => s
            .SetWriteKeysPath("./keys/updater.key").SetPassword(pwd));
        Assert.Equal("s3cret-key-pwd", plan.Environment["TAURI_SIGNING_PRIVATE_KEY_PASSWORD"]);
        Assert.Contains(pwd, plan.Secrets);
        // Critically: the password value must NEVER appear in the arg list.
        Assert.DoesNotContain("s3cret-key-pwd", plan.Arguments);
        Assert.DoesNotContain("-p", plan.Arguments);
    }

    [Fact]
    public void Signer_Generate_Password_And_NoPassword_Mutually_Exclusive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Tauri.Signer.Generate(FakeTool(), s => s
                .SetWriteKeysPath("./k.key")
                .SetPassword(new Secret("p", "x"))
                .SetNoPassword()).Arguments.ToList());
    }

    [Fact]
    public void Signer_Generate_NoPassword_Standalone()
    {
        var plan = Tauri.Signer.Generate(FakeTool(), s => s
            .SetWriteKeysPath("./k.key").SetNoPassword());
        Assert.Contains("--no-password", plan.Arguments);
        // And no env var routed since no Secret was set.
        Assert.False(plan.Environment.ContainsKey("TAURI_SIGNING_PRIVATE_KEY_PASSWORD"));
    }

    [Fact]
    public void Signer_Sign_Requires_File()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Tauri.Signer.Sign(FakeTool(), s => s.SetPrivateKey("./k.key")).Arguments.ToList());
    }

    [Fact]
    public void Signer_Sign_Requires_PrivateKey()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Tauri.Signer.Sign(FakeTool(), s => s.SetFile("DasBook.msi")).Arguments.ToList());
    }

    [Fact]
    public void Signer_Sign_Emits_K_And_Positional_File()
    {
        var plan = Tauri.Signer.Sign(FakeTool(), s => s
            .SetPrivateKey("./keys/updater.key")
            .SetFile("DasBook.msi"));
        Assert.Equal(new[] { "signer", "sign", "-k", "./keys/updater.key" }, plan.Arguments.Take(4));
        // File is positional — comes after any flags, before --ci from base.
        Assert.Contains("DasBook.msi", plan.Arguments);
    }

    [Fact]
    public void Signer_Sign_Force_Flag()
    {
        var plan = Tauri.Signer.Sign(FakeTool(), s => s
            .SetPrivateKey("k").SetFile("DasBook.msi").SetForce());
        Assert.Contains("-f", plan.Arguments);
    }

    [Fact]
    public void Signer_Sign_Password_Routes_To_Env_Not_Argv()
    {
        var pwd = new Secret("updater-key-pwd", "s3cret-key-pwd");
        var plan = Tauri.Signer.Sign(FakeTool(), s => s
            .SetPrivateKey("./keys/updater.key")
            .SetFile("DasBook.msi")
            .SetPassword(pwd));
        Assert.Equal("s3cret-key-pwd", plan.Environment["TAURI_SIGNING_PRIVATE_KEY_PASSWORD"]);
        Assert.Contains(pwd, plan.Secrets);
        Assert.DoesNotContain("s3cret-key-pwd", plan.Arguments);
        Assert.DoesNotContain("-p", plan.Arguments);
    }

    // ---- Common knobs ----

    [Fact]
    public void ConfigPath_Override()
    {
        var plan = Tauri.Build(FakeTool(), s => s.SetConfigPath("tauri.prod.conf.json"));
        Assert.Equal("tauri.prod.conf.json", plan.Arguments[IndexOf(plan.Arguments, "--config") + 1]);
    }

    [Fact]
    public void Verbosity_Maps_To_V_Flags()
    {
        var v1 = Tauri.Build(FakeTool(), s => s.SetVerbosity(1));
        var v2 = Tauri.Build(FakeTool(), s => s.SetVerbosity(2));
        Assert.Contains("-v", v1.Arguments);
        Assert.Contains("-vv", v2.Arguments);
    }

    [Fact]
    public void WorkingDirectory_Propagates()
    {
        var plan = Tauri.Build(FakeTool(), s => s.SetWorkingDirectory("/repo"));
        Assert.Equal("/repo", plan.WorkingDirectory);
    }

    // ---- ExternalBinPath helper ----

    [Fact]
    public void ExternalBinPath_Windows_Has_Exe_Suffix()
    {
        var p = Tauri.ExternalBinPath(
            AbsolutePath.Create("/repo/src-tauri"),
            "dasbook-service",
            "x86_64-pc-windows-msvc");
        Assert.EndsWith("dasbook-service-x86_64-pc-windows-msvc.exe",
            p.Value.Replace(System.IO.Path.DirectorySeparatorChar, '/'));
        Assert.Contains("binaries",
            p.Value.Replace(System.IO.Path.DirectorySeparatorChar, '/'));
    }

    [Fact]
    public void ExternalBinPath_Linux_No_Suffix()
    {
        var p = Tauri.ExternalBinPath(
            AbsolutePath.Create("/repo/src-tauri"),
            "dasbook-service",
            "x86_64-unknown-linux-gnu");
        Assert.EndsWith("dasbook-service-x86_64-unknown-linux-gnu",
            p.Value.Replace(System.IO.Path.DirectorySeparatorChar, '/'));
        Assert.False(p.Value.EndsWith(".exe"));
    }

    [Fact]
    public void ExternalBinPath_IsWindows_Explicit_Override()
    {
        // Force .exe on a non-Windows triple (rare but supported)
        var p = Tauri.ExternalBinPath(
            AbsolutePath.Create("/repo/src-tauri"),
            "sidecar",
            "custom-target",
            isWindows: true);
        Assert.EndsWith(".exe", p.Value);
    }

    [Fact]
    public void ExternalBinPath_Empty_Name_Rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Tauri.ExternalBinPath(AbsolutePath.Create("/repo/src-tauri"), "", "x86_64-pc-windows-msvc"));
    }

    [Fact]
    public void ExternalBinPath_Empty_Target_Rejected_With_Helpful_Message()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Tauri.ExternalBinPath(AbsolutePath.Create("/repo/src-tauri"), "dasbook-service", ""));
        Assert.Contains("target", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostTargetTriple_Returns_Reasonable_Value()
    {
        var triple = Tauri.HostTargetTriple();
        Assert.NotEmpty(triple);
        // Should contain a recognizable arch token
        var hasArch = triple.Contains("x86_64") || triple.Contains("aarch64")
            || triple.Contains("i686") || triple.Contains("arm");
        Assert.True(hasArch, $"Expected arch token in '{triple}'");
        // And a platform marker for the runtime host
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.Contains("windows", triple);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Assert.Contains("linux", triple);
    }
}
