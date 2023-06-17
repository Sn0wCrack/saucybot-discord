namespace SaucyBot.Library.Sites.Twitter;

public interface IFxTwitterClient
{
    public Task<FxTwitterResponse?> GetTweet(string name, string identifier);
}
