using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Erp.Kernel.Security;

/// <summary>
/// The work factor new password hashes are made with: <see cref="PasswordHasher.Iterations"/>
/// (210,000 rounds of PBKDF2-HMAC-SHA512, the OWASP figure) wherever the product runs. A stored
/// hash keeps its own cost, so a sign-in derives the typed password with exactly the cost the hash
/// was made with, and a hash made with less than the current cost is renewed at the next sign-in.
///
/// One exception, for the test processes of <c>./erp verify</c> only: in the <c>Testing</c> host
/// environment the setting <c>Erp:Auth:PasswordIterations</c> (10,000 to 210,000) sets a lower cost
/// for the hashes those processes make (the test library's hosts get it from
/// <c>ERP_TEST_PASSWORD_ITERATIONS</c>, which the .NET test stage sets). The test suites sign in thousands of times, and each
/// derivation at the production cost was the largest single use of processor time in them (round
/// 9, measured: 2,119 derivations, about 233 processor seconds, in one gate process). Every code
/// path still runs, every check still judges the same answers; only the number of rounds differs.
/// Outside the Testing environment the setting is refused at start-up, so no deployment can lower
/// the cost by configuration (tests: <c>PasswordHashingTests</c>, which also keep the setting out of every
/// shipped configuration).
/// </summary>
public sealed class PasswordHashing
{
    public const string IterationsSetting = "Erp:Auth:PasswordIterations";

    /// <summary>The environment in which <see cref="IterationsSetting"/> is honoured.</summary>
    public const string TestingEnvironment = "Testing";

    /// <summary>The lowest cost <see cref="PasswordHasher.Verify"/> accepts.</summary>
    public const int LowestIterations = 10_000;

    public PasswordHashing(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration[IterationsSetting];
        if (string.IsNullOrWhiteSpace(configured))
        {
            Iterations = PasswordHasher.Iterations;
            return;
        }
        if (!environment.IsEnvironment(TestingEnvironment))
        {
            throw new InvalidOperationException(
                $"{IterationsSetting} is set to '{configured}' in the {environment.EnvironmentName} environment; it is honoured only in the {TestingEnvironment} environment of the test suites. Remove it: passwords are hashed with {PasswordHasher.Iterations} iterations.");
        }
        if (!int.TryParse(configured, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var iterations)
            || iterations is < LowestIterations or > PasswordHasher.Iterations)
        {
            throw new InvalidOperationException($"{IterationsSetting} must be a whole number from {LowestIterations} to {PasswordHasher.Iterations}; it is '{configured}'.");
        }
        Iterations = iterations;
    }

    /// <summary>The cost of the hashes this process makes.</summary>
    public int Iterations { get; }

    /// <summary>A new hash of the password at the current cost.</summary>
    public string Hash(string password) => PasswordHasher.Hash(password, Iterations);

    /// <summary>True when a challenge's cost is below the current one (renew the hash at the next sign-in).</summary>
    public bool IsOutdated(string challenge) => PasswordHasher.IsOutdated(challenge, Iterations);
}
