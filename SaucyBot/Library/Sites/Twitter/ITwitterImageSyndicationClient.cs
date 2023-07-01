namespace SaucyBot.Library.Sites.Twitter;

public interface ITwitterImageSyndicationClient
{
    public Task<TwitterImageSyndicationTweet?> GetTweet(string identifier);
}
