namespace SaucyBot.Library.Sites.FurAffinity;

public interface IFurAffinityClient
{
    public Task<FaExportSubmission?> GetSubmission(string identifier);
}
