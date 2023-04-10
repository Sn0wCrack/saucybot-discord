namespace SaucyBot.Library.Sites.E621;

public interface IE621Client
{
    public Task<E621PostResponse?> GetPost(string identifier);
}
