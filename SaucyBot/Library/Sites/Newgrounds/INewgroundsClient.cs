namespace SaucyBot.Library.Sites.Newgrounds;

public interface INewgroundsClient
{
    public Task<NewgroundsArt?> GetArt(string user, string slug);
}
