using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Erp.Kernel.Tests;

/// <summary>
/// The cost of new password hashes is the production cost everywhere; only the test processes of
/// ./erp verify (the Testing host environment) may lower it with Erp:Auth:PasswordIterations, and
/// no shipped configuration sets it.
/// </summary>
public sealed class PasswordHashingTests
{
    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Erp.Host";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static PasswordHashing Make(string environment, string? iterations) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [PasswordHashing.IterationsSetting] = iterations }).Build(),
            new Environment(environment));

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Without_the_setting_new_hashes_use_the_production_cost(string environment)
    {
        var hashing = Make(environment, null);
        Assert.Equal(210_000, hashing.Iterations);
        Assert.StartsWith("pbkdf2-sha512$210000$", hashing.Hash("Correct-Horse-1"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Outside_the_Testing_environment_a_lowered_cost_is_refused(string environment)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => Make(environment, "10000"));
        Assert.Contains("honoured only in the Testing environment", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void In_the_Testing_environment_the_cost_is_set_within_bounds_and_hashes_still_verify()
    {
        var hashing = Make("Testing", "10000");
        var hash = hashing.Hash("Correct-Horse-1");
        Assert.StartsWith("pbkdf2-sha512$10000$", hash, StringComparison.Ordinal);
        Assert.True(PasswordHasher.Verify("Correct-Horse-1", hash, out var production));
        // Measured against the production cost it is outdated; against the process's own it is not.
        Assert.True(production);
        Assert.False(hashing.IsOutdated(PasswordHasher.ChallengeOf(hash)!));
        Assert.True(Make("Testing", "50000").IsOutdated(PasswordHasher.ChallengeOf(hash)!));
        foreach (var bad in new[] { "9999", "210001", "-1", "1e5", " 10000", "ten" })
        {
            Assert.Throws<InvalidOperationException>(() => Make("Testing", bad));
        }
    }

    [Fact]
    public void No_shipped_configuration_lowers_the_cost()
    {
        // Every file a deployment or the demo is built from; only the .NET test stage of ./erp verify
        // (build/verify-inside.sh) sets it, for its test processes.
        var shipped = new[] { "compose.yaml", "Dockerfile", "erp", "src/Host/Erp.Host/appsettings.json" }
            .Concat(Directory.EnumerateFiles(Repo.PathOf("src"), "appsettings*.json", SearchOption.AllDirectories)
                .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal))
                .Select(f => Path.GetRelativePath(Repo.Root, f).Replace('\\', '/')))
            .Distinct().ToList();
        foreach (var file in shipped)
        {
            var text = File.ReadAllText(Repo.PathOf(file.Split('/')));
            Assert.False(text.Contains("PasswordIterations", StringComparison.OrdinalIgnoreCase), $"{file} sets the password hashing cost");
        }
        var verify = File.ReadAllText(Repo.PathOf("build", "verify-inside.sh"));
        // Set in the .NET test stage only, never in the timing stage (which measures the product as it runs).
        var dotnetStage = verify[verify.IndexOf("  dotnet)", StringComparison.Ordinal)..verify.IndexOf("  timing)", StringComparison.Ordinal)];
        Assert.Contains($"{ErpAppFactory.PasswordIterationsVariable}=", dotnetStage, StringComparison.Ordinal);
        Assert.Equal(1, verify.Split($"{ErpAppFactory.PasswordIterationsVariable}=").Length - 1);
        Assert.DoesNotContain("Erp__Auth__PasswordIterations", verify, StringComparison.Ordinal);
    }
}
