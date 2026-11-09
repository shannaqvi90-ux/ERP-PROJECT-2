using Erp.Modules.Attachments.Contracts;
using Microsoft.Extensions.Configuration;

namespace Erp.Modules.Attachments.Tests;

/// <summary>The local store keeps tenants apart on its own.</summary>
public sealed class StoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "erp-store-tests-" + Guid.NewGuid().ToString("N"));
    private readonly IAttachmentStore _store;

    public StoreTests() => _store = new LocalAttachmentStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp:Attachments:Root"] = _root }).Build());

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Stream Bytes(string text) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task A_saved_file_is_read_back_by_its_own_tenant_only()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var saved = await _store.SaveAsync(a, Bytes("tenant a"), 1000, TestContext.Current.CancellationToken);
        Assert.Equal(8, saved.Size);
        Assert.StartsWith(a.ToString("N") + "/", saved.Key);
        await using (var own = await _store.OpenAsync(a, saved.Key, TestContext.Current.CancellationToken))
        {
            Assert.NotNull(own);
            using var reader = new StreamReader(own!);
            Assert.Equal("tenant a", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
        Assert.Null(await _store.OpenAsync(b, saved.Key, TestContext.Current.CancellationToken));
        await _store.DeleteAsync(b, saved.Key, TestContext.Current.CancellationToken);
        Assert.NotNull(await _store.OpenAsync(a, saved.Key, TestContext.Current.CancellationToken));
        await _store.DeleteAsync(a, saved.Key, TestContext.Current.CancellationToken);
        Assert.Null(await _store.OpenAsync(a, saved.Key, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("00000000000000000000000000000000/../../etc/passwd")]
    [InlineData("zz")]
    public async Task A_key_that_is_not_the_stores_own_opens_nothing(string key)
    {
        var tenant = Guid.Empty;
        Assert.Null(await _store.OpenAsync(tenant, key, TestContext.Current.CancellationToken));
        await _store.DeleteAsync(tenant, key, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_file_over_the_limit_leaves_nothing_behind()
    {
        var tenant = Guid.NewGuid();
        await Assert.ThrowsAsync<BlobTooLargeException>(() => _store.SaveAsync(tenant, Bytes(new string('x', 500)), 100, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }
}
