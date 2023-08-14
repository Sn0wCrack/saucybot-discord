namespace SaucyBot.Library.Sites.Twitter;

public interface IVxTwitterClient
{
    public Task<VxTwitterResponse?> GetTweet(string name, string identifier);
}
