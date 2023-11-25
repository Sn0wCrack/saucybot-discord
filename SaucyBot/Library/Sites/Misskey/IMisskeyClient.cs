namespace SaucyBot.Library.Sites.Misskey;

public interface IMisskeyClient
{
    public void SetUrl(string url);

    public Task<ShowNoteResponse?> ShowNote(string id);
}
