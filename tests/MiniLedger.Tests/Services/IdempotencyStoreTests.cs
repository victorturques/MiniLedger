using MiniLedger.Application.Services;
using MiniLedger.Tests.Infrastructure;

namespace MiniLedger.Tests.Services;

public class IdempotencyStoreTests
{
    private static IdempotencyEntry NewEntry(string body = "body", int statusCode = 200) => new()
    {
        StatusCode = statusCode,
        Body = body
    };

    [Fact]
    public void TryGet_Should_Return_False_When_Key_Was_Never_Saved()
    {
        var store = new IdempotencyStore();

        Assert.False(store.TryGet("chave-inexistente", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGet_Should_Return_False_For_Empty_Key(string key)
    {
        var store = new IdempotencyStore();
        Assert.False(store.TryGet(key, out _));
    }

    [Fact]
    public void Save_And_TryGet_Should_RoundTrip_Entry()
    {
        var store = new IdempotencyStore();
        var entry = NewEntry("{\"accountNumber\":\"00000001\"}", 201);

        store.Save("chave-1", entry);

        Assert.True(store.TryGet("chave-1", out var stored));
        Assert.Equal(201, stored.StatusCode);
        Assert.Equal("{\"accountNumber\":\"00000001\"}", stored.Body);
        Assert.NotEqual(default, stored.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Save_Should_Ignore_Empty_Key(string key)
    {
        var store = new IdempotencyStore();

        store.Save(key, NewEntry());

        Assert.False(store.TryGet(key, out _));
    }

    [Fact]
    public void Save_Should_Overwrite_Entry_With_Same_Key()
    {
        var store = new IdempotencyStore();

        store.Save("chave-1", NewEntry("primeiro"));
        store.Save("chave-1", NewEntry("segundo"));

        Assert.True(store.TryGet("chave-1", out var stored));
        Assert.Equal("segundo", stored.Body);
    }

    [Fact]
    public void Entry_Should_Be_Available_Before_Ttl()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new IdempotencyStore(time, TimeSpan.FromMinutes(10));

        store.Save("chave-1", NewEntry());
        time.Advance(TimeSpan.FromMinutes(9));

        Assert.True(store.TryGet("chave-1", out _));
    }

    [Fact]
    public void Entry_Should_Expire_After_Ttl()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new IdempotencyStore(time, TimeSpan.FromMinutes(10));

        store.Save("chave-1", NewEntry());
        time.Advance(TimeSpan.FromMinutes(10));

        Assert.False(store.TryGet("chave-1", out _));
    }

    [Fact]
    public void Save_Should_Evict_Expired_Entries()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new IdempotencyStore(time, TimeSpan.FromMinutes(10));

        store.Save("antiga", NewEntry("antiga"));
        time.Advance(TimeSpan.FromMinutes(11));
        store.Save("nova", NewEntry("nova"));

        Assert.False(store.TryGet("antiga", out _));
        Assert.True(store.TryGet("nova", out _));
    }

    [Fact]
    public void Store_Instances_Should_Not_Share_State()
    {
        var first = new IdempotencyStore();
        var second = new IdempotencyStore();

        first.Save("chave-1", NewEntry());

        Assert.True(first.TryGet("chave-1", out _));
        Assert.False(second.TryGet("chave-1", out _));
    }
}
