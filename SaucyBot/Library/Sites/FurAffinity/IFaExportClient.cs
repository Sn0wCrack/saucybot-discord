namespace SaucyBot.Library.Sites.FurAffinity;

public interface IFaExportClient
{
    public Task<FaExportSubmission?> GetSubmission(string identifier);
}
