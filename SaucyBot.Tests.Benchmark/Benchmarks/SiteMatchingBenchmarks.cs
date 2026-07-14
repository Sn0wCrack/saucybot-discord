using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.ArtStation;
using SaucyBot.Library.Sites.BlueSky;
using SaucyBot.Library.Sites.DeviantArt;
using SaucyBot.Library.Sites.E621;
using SaucyBot.Library.Sites.ExHentai;
using SaucyBot.Library.Sites.FurAffinity;
using SaucyBot.Library.Sites.HentaiFoundry;
using SaucyBot.Library.Sites.Misskey;
using SaucyBot.Library.Sites.Newgrounds;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Library.Sites.Twitter;
using SaucyBot.Services;
using SaucyBot.Site;
using SaucyBot.Site.ArtStation;
using SaucyBot.Site.Bluesky;
using SaucyBot.Site.DeviantArt;
using SaucyBot.Site.E621;
using SaucyBot.Site.ExHentai;
using SaucyBot.Site.FurAffinity;
using SaucyBot.Site.HentaiFoundry;
using SaucyBot.Site.Instagram;
using SaucyBot.Site.Misskey;
using SaucyBot.Site.Newgrounds;
using SaucyBot.Site.Pixiv;
using SaucyBot.Site.Reddit;
using SaucyBot.Site.Twitter;

namespace SaucyBot.Tests.Benchmark.Benchmarks;

[MemoryDiagnoser]
[MinInvokeCount(3), InvocationCount(16)]
[MinWarmupCount(3), MaxWarmupCount(5)]
[MinIterationCount(3), MaxIterationCount(5)]
public class SiteMatchingBenchmarks
{
    private FxTwitterSite _fxTwitter = null!;
    private ArtStationSite _artStation = null!;
    private BlueskySite _bluesky = null!;
    private DeviantArtSite _deviantArt = null!;
    private E621Site _e621 = null!;
    private ExHentaiSite _exHentai = null!;
    private FurAffinitySite _furAffinity = null!;
    private HentaiFoundrySite _hentaiFoundry = null!;
    private VxInstagramSite _vxInstagram = null!;
    private MisskeySite _misskey = null!;
    private NewgroundsSite _newgrounds = null!;
    private PixivSite _pixiv = null!;
    private RxRedditSite _rxReddit = null!;
    private List<BaseSite> _allSites = null!;

    private const string TweetUrl = "https://twitter.com/username/status/1234567890123456789";
    private const string ArtStationUrl = "https://www.artstation.com/artwork/aBcDeFg";
    private const string BlueskyUrl = "https://bsky.app/profile/user.bsky.social/post/3a7b8c9d1e2f";
    private const string DeviantArtUrl = "https://www.deviantart.com/author/art/My-Art-Title-12345";
    private const string E621Url = "https://e621.net/posts/1234567";
    private const string ExHentaiUrl = "https://exhentai.org/g/1234567/aBcDeFgH01/";
    private const string EHentaiUrl = "https://e-hentai.org/g/1234567/aBcDeFgH01/";
    private const string FurAffinityUrl = "https://www.furaffinity.net/view/12345678/";
    private const string HentaiFoundryUrl = "https://www.hentai-foundry.com/pictures/user/someuser/123456/some-art-slug";
    private const string InstagramUrl = "https://www.instagram.com/p/ABC123def456/";
    private const string MisskeyUrl = "https://misskey.io/notes/abc123def456";
    private const string NewgroundsUrl = "https://www.newgrounds.com/art/view/someuser/some-art-slug";
    private const string PixivUrl = "https://www.pixiv.net/en/artworks/12345678";
    private const string RedditUrl = "https://www.reddit.com/media?url=https%3A%2F%2Fi.redd.it%2Fimage.png";
    private const string NonMatchingText = "Hello world this is just some plain text with no URLs at all";
    private const string MixedText =
        "Check out this tweet https://twitter.com/user/status/123456789 and this art https://www.artstation.com/artwork/aBcDeFg";

    [GlobalSetup]
    public void Setup()
    {
        var config = new ConfigurationBuilder().Build();

        _fxTwitter = CreateFxTwitter(config);
        _artStation = CreateArtStation(config);
        _bluesky = CreateBluesky(config);
        _deviantArt = CreateDeviantArt(config);
        _e621 = CreateE621(config);
        _exHentai = CreateExHentai(config);
        _furAffinity = CreateFurAffinity(config);
        _hentaiFoundry = CreateHentaiFoundry(config);
        _vxInstagram = CreateInstagram(config);
        _misskey = CreateMisskey(config);
        _newgrounds = CreateNewgrounds(config);
        _pixiv = CreatePixiv(config);
        _rxReddit = CreateReddit(config);

        _allSites =
        [
            _artStation, _bluesky, _deviantArt, _e621, _exHentai,
            _furAffinity, _fxTwitter, _hentaiFoundry, _vxInstagram,
            _misskey, _newgrounds, _pixiv, _rxReddit
        ];
    }

    [Benchmark]
    public MatchCollection FxTwitter_MatchingTweet() => _fxTwitter.Pattern.Matches(TweetUrl);

    [Benchmark]
    public MatchCollection ArtStation_MatchingArtwork() => _artStation.Pattern.Matches(ArtStationUrl);

    [Benchmark]
    public MatchCollection Bluesky_MatchingPost() => _bluesky.Pattern.Matches(BlueskyUrl);

    [Benchmark]
    public MatchCollection DeviantArt_MatchingArt() => _deviantArt.Pattern.Matches(DeviantArtUrl);

    [Benchmark]
    public MatchCollection E621_MatchingPost() => _e621.Pattern.Matches(E621Url);

    [Benchmark]
    public MatchCollection ExHentai_MatchingGallery() => _exHentai.Pattern.Matches(ExHentaiUrl);

    [Benchmark]
    public MatchCollection EHentai_MatchingGallery() => _exHentai.Pattern.Matches(EHentaiUrl);

    [Benchmark]
    public MatchCollection FurAffinity_MatchingSubmission() => _furAffinity.Pattern.Matches(FurAffinityUrl);

    [Benchmark]
    public MatchCollection HentaiFoundry_MatchingPicture() => _hentaiFoundry.Pattern.Matches(HentaiFoundryUrl);

    [Benchmark]
    public MatchCollection Instagram_MatchingPost() => _vxInstagram.Pattern.Matches(InstagramUrl);

    [Benchmark]
    public MatchCollection Misskey_MatchingNote() => _misskey.Pattern.Matches(MisskeyUrl);

    [Benchmark]
    public MatchCollection Newgrounds_MatchingArt() => _newgrounds.Pattern.Matches(NewgroundsUrl);

    [Benchmark]
    public MatchCollection Pixiv_MatchingArtwork() => _pixiv.Pattern.Matches(PixivUrl);

    [Benchmark]
    public MatchCollection Reddit_MatchingMedia() => _rxReddit.Pattern.Matches(RedditUrl);

    [Benchmark]
    public int AllSites_NonMatchingText()
    {
        var total = 0;
        foreach (var site in _allSites)
        {
            total += site.Pattern.Matches(NonMatchingText).Count;
        }
        return total;
    }

    [Benchmark]
    public int AllSites_MixedText()
    {
        var total = 0;
        foreach (var site in _allSites)
        {
            total += site.Pattern.Matches(MixedText).Count;
        }
        return total;
    }

    [Benchmark]
    public int AllSites_TweetUrl()
    {
        var total = 0;
        foreach (var site in _allSites)
        {
            total += site.Pattern.Matches(TweetUrl).Count;
        }
        return total;
    }

    private static FxTwitterSite CreateFxTwitter(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<FxTwitterSite>>();
        var client = Substitute.For<IFxTwitterClient>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        return new FxTwitterSite(logger, config, client, httpClientFactory);
    }

    private static ArtStationSite CreateArtStation(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<ArtStationSite>>();
        var client = Substitute.For<IArtStationClient>();
        return new ArtStationSite(logger, config, client);
    }

    private static BlueskySite CreateBluesky(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<BlueskySite>>();
        var client = Substitute.For<IVixBlueskyClient>();
        return new BlueskySite(logger, config, client);
    }

    private static DeviantArtSite CreateDeviantArt(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<DeviantArtSite>>();
        var client = Substitute.For<IDeviantArtClient>();
        var openEmbedClient = Substitute.For<IDeviantArtOpenEmbedClient>();
        return new DeviantArtSite(logger, config, client, openEmbedClient);
    }

    private static E621Site CreateE621(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<E621Site>>();
        var client = Substitute.For<IE621Client>();
        return new E621Site(logger, client);
    }

    private static ExHentaiSite CreateExHentai(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<ExHentaiSite>>();
        var client = Substitute.For<IExHentaiClient>();
        return new ExHentaiSite(logger, config, client);
    }

    private static FurAffinitySite CreateFurAffinity(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<FurAffinitySite>>();
        var client = Substitute.For<IFurAffinityClient>();
        return new FurAffinitySite(logger, client);
    }

    private static HentaiFoundrySite CreateHentaiFoundry(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<HentaiFoundrySite>>();
        var client = Substitute.For<IHentaiFoundryClient>();
        return new HentaiFoundrySite(logger, client);
    }

    private static VxInstagramSite CreateInstagram(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<VxInstagramSite>>();
        return new VxInstagramSite(logger);
    }

    private static MisskeySite CreateMisskey(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<MisskeySite>>();
        var client = Substitute.For<IMisskeyClient>();
        return new MisskeySite(logger, config, client);
    }

    private static NewgroundsSite CreateNewgrounds(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<NewgroundsSite>>();
        var client = Substitute.For<INewgroundsClient>();
        return new NewgroundsSite(logger, client);
    }

    private static PixivSite CreatePixiv(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<PixivSite>>();
        var client = Substitute.For<IPixivClient>();
        var guildConfigManager = Substitute.For<IGuildConfigurationManager>();
        return new PixivSite(logger, config, guildConfigManager, client);
    }

    private static RxRedditSite CreateReddit(IConfiguration config)
    {
        var logger = Substitute.For<ILogger<RxRedditSite>>();
        return new RxRedditSite(logger);
    }
}
