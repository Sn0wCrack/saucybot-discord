namespace SaucyBot.Services.Cache;

public interface ICacheDriver
{
    public Task<T?> Get<T>(object key);

    public Task<bool> Delete(object key);

    public Task<T> Set<T>(object key, T value);

    public Task<T> Set<T>(object key, T value, TimeSpan expiry);
}
