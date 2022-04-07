namespace SaucyBot.Services.Cache;

public class RedisCacheDriver : ICacheDriver
{

    public Task<T?> Get<T>(object key)
    {
        throw new NotImplementedException();
    }

    public Task<bool> Delete(object key)
    {
        throw new NotImplementedException();
    }

    public Task<bool> Set<T>(object key, T value, TimeSpan? expiry)
    {
        throw new NotImplementedException();
    }
}
