namespace SaucyBot.Library.Sites.Misskey;

public interface IMisskeyClient
{
    public Task<ShowNoteResponse?> ShowNote(string url, string id);
}
