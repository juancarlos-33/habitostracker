using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

public class MemoryCacheTicketStore : ITicketStore
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        _cache.Set(key, ticket);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket> RetrieveAsync(string key)
    {
        _cache.TryGetValue(key, out AuthenticationTicket ticket);
        return Task.FromResult(ticket);
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString();
        _cache.Set(key, ticket);
        return Task.FromResult(key);
    }
}