using Microsoft.Extensions.Configuration;
using Erp.Kernel.Http;
using Erp.Kernel.Security;

namespace Erp.Kernel.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void Password_hashes_verify_only_the_right_password_and_use_unique_salts()
    {
        var a = PasswordHasher.Hash("Correct-Horse-1");
        var b = PasswordHasher.Hash("Correct-Horse-1");
        Assert.NotEqual(a, b);
        Assert.StartsWith("pbkdf2-sha512$210000$", a, StringComparison.Ordinal);
        Assert.True(PasswordHasher.Verify("Correct-Horse-1", a, out var rehash));
        Assert.False(rehash);
        Assert.False(PasswordHasher.Verify("correct-horse-1", a, out _));
        Assert.False(PasswordHasher.Verify("x", "garbage", out _));
        Assert.False(PasswordHasher.Verify("x", "pbkdf2-sha512$1$AAAA$AAAA", out _));
    }

    [Fact]
    public void Weaker_hashes_still_verify_and_ask_to_be_upgraded()
    {
        var weak = PasswordHasher.Hash("Old-Password-1", iterations: 50_000);
        Assert.True(PasswordHasher.Verify("Old-Password-1", weak, out var rehash));
        Assert.True(rehash);
    }

    [Fact]
    public void Session_tokens_are_256_bit_base64url_and_stored_as_sha256()
    {
        var token = SessionTokens.Generate();
        Assert.Equal(43, token.Length);
        Assert.True(SessionTokens.LooksValid(token));
        Assert.NotEqual(token, SessionTokens.Generate());
        Assert.Equal(32, SessionTokens.Hash(token).Length);
        Assert.False(SessionTokens.LooksValid(token + "x"));
        Assert.False(SessionTokens.LooksValid(new string('!', 43)));
        Assert.False(SessionTokens.LooksValid(null));
    }

    [Theory]
    [InlineData("identity.users.read", true)]
    [InlineData("identity.userRoles.assignAll", true)]
    [InlineData("identity.users", false)]
    [InlineData("Identity.users.read", false)]
    [InlineData("identity.users.read.extra", false)]
    [InlineData("identity..read", false)]
    public void Permission_keys_are_module_resource_action(string key, bool valid) =>
        Assert.Equal(valid, PermissionDefinition.IsValidKey(key));

    [Theory]
    [InlineData("a@b.example", true)]
    [InlineData("first.last+tag@sub.domain.ae", true)]
    [InlineData("no-at.example", false)]
    [InlineData("a@b", false)]
    [InlineData("a@@b.example", false)]
    [InlineData("a b@c.example", false)]
    [InlineData("a@.example", false)]
    public void Email_validation(string value, bool valid) => Assert.Equal(valid, Validator.IsEmail(value));

    [Fact]
    public void Forwarded_headers_are_trusted_only_from_configured_proxies()
    {
        IConfiguration Config(params (string Key, string Value)[] values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

        Assert.Null(Erp.Kernel.Hosting.ErpPlatform.ForwardedHeadersFrom(Config()));

        var options = Erp.Kernel.Hosting.ErpPlatform.ForwardedHeadersFrom(Config(("Erp:Http:KnownProxies", "10.0.0.5, 10.0.0.6"), ("Erp:Http:KnownNetworks", "172.18.0.0/16")))!;
        Assert.Equal(["10.0.0.5", "10.0.0.6"], options.KnownProxies.Select(p => p.ToString()));
        Assert.Equal(["172.18.0.0/16"], options.KnownIPNetworks.Select(n => n.ToString()));
        Assert.Equal(1, options.ForwardLimit);

        Assert.Throws<InvalidOperationException>(() => Erp.Kernel.Hosting.ErpPlatform.ForwardedHeadersFrom(Config(("Erp:Http:KnownProxies", "proxy.local"))));
    }
}
