namespace SaucyBot.Services.Cache;

public interface ICacheDriver
{
    public Task<object?> Get(object key);

    public Task<bool> Delete(object key);

    public Task<bool> Set(object key, object value, TimeSpan? expiry);
}
