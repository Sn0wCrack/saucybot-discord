namespace SaucyBot.Services;

public interface ICacheManager
{
    public Task<T?> Get<T>(object key);

    public Task<T> Set<T>(object key, T value);

    public Task<T> Set<T>(object key, T value, TimeSpan expiry);

    public Task<bool> Delete(object key);
    
    public Task<T?> Remember<T>(object key, Func<Task<T?>> value);

    public Task<T?> Remember<T>(object key, TimeSpan expiry, Func<Task<T?>> value);
}
