using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SaucyBot.Library.Sites.Pixiv;
using SaucyBot.Services;
using SaucyBot.Site;
using SaucyBot.Site.Pixiv;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public class PixivTest
{
    [Fact]
    public async Task UgoiraDisposesTheArchiveSourceAfterProcessing()
    {
        var source = new TrackingStream(CreateUgoiraArchive());
        var client = CreateUgoiraClient(source);
        var renderer = CreateUgoiraDependencies();
        var site = CreateUgoiraSite(client, renderer);

        var response = await site.Process(CreateUgoiraRequest(TestContext.Current.CancellationToken));

        Assert.NotNull(response);
        Assert.True(source.DisposeCount > 0);
        await response.DisposeAsync();
    }

    [Fact]
    public async Task UgoiraExtractsFromStagedArchiveBeforeRendering()
    {
        var source = new NonSeekableTrackingStream(CreateUgoiraArchive());
        var client = CreateUgoiraClient(source);
        string? stagedArchivePath = null;
        var renderer = Substitute.For<IUgoiraVideoRenderer>();
        renderer.RenderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var videoPath = callInfo.ArgAt<string>(1);
                var requestPath = Directory.GetParent(Path.GetDirectoryName(videoPath)!)!.FullName;
                stagedArchivePath = Path.Combine(requestPath, "ugoira.zip");
                Assert.True(File.Exists(stagedArchivePath));
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(videoPath)!, "frame.jpg")));
                File.WriteAllBytes(videoPath, [1]);
                return Task.CompletedTask;
            });
        var site = CreateUgoiraSite(client, renderer);

        var response = await site.Process(CreateUgoiraRequest(TestContext.Current.CancellationToken));

        Assert.NotNull(response);
        Assert.NotNull(stagedArchivePath);
        Assert.True(source.Disposed);
        await response.DisposeAsync();
    }

    [Fact]
    public async Task UgoiraExtractsArchiveMemberThatMatchesTheStagingFileName()
    {
        var source = new NonSeekableTrackingStream(CreateUgoiraArchiveWithStagingNameCollision());
        var client = CreateUgoiraClient(source);
        var renderer = Substitute.For<IUgoiraVideoRenderer>();
        renderer.RenderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var workspace = Path.GetDirectoryName(callInfo.ArgAt<string>(1))!;
                Assert.Equal([0x5A], File.ReadAllBytes(Path.Combine(workspace, "ugoira.zip")));
                File.WriteAllBytes(callInfo.ArgAt<string>(1), [1]);
                return Task.CompletedTask;
            });
        var site = CreateUgoiraSite(client, renderer);

        var response = await site.Process(CreateUgoiraRequest(TestContext.Current.CancellationToken));

        Assert.NotNull(response);
        await response.DisposeAsync();
    }

    [Fact]
    public async Task UgoiraCleansWorkspaceWhenStagingIsCancelled()
    {
        var source = new BlockingNonSeekableStream();
        var client = CreateUgoiraClient(source);
        var site = CreateUgoiraSite(client, Substitute.For<IUgoiraVideoRenderer>());
        using var cancellation = new CancellationTokenSource();
        var workspacesBefore = GetUgoiraWorkspaces();

        var processing = site.Process(CreateUgoiraRequest(cancellation.Token));
        await source.FirstChunkRead.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);

        Assert.True(source.Disposed);
        Assert.Empty(GetUgoiraWorkspaces().Except(workspacesBefore));
    }

    [Fact]
    public async Task UgoiraCleansWorkspaceWhenArchiveExtractionFails()
    {
        var source = new NonSeekableTrackingStream([0x01, 0x02, 0x03]);
        var client = CreateUgoiraClient(source);
        var site = CreateUgoiraSite(client, Substitute.For<IUgoiraVideoRenderer>());
        var workspacesBefore = GetUgoiraWorkspaces();

        await Assert.ThrowsAsync<InvalidDataException>(() => site.Process(CreateUgoiraRequest(TestContext.Current.CancellationToken)));

        Assert.True(source.Disposed);
        Assert.Empty(GetUgoiraWorkspaces().Except(workspacesBefore));
    }

    [Fact]
    public async Task UgoiraCleansWorkspaceWhenArchiveDownloadFails()
    {
        var source = new FailingNonSeekableStream();
        var site = CreateUgoiraSite(
            CreateUgoiraClient(source),
            Substitute.For<IUgoiraVideoRenderer>());
        var workspacesBefore = GetUgoiraWorkspaces();

        var exception = await Assert.ThrowsAsync<IOException>(() => site.Process(
            CreateUgoiraRequest(TestContext.Current.CancellationToken)));

        Assert.Equal("source read failed", exception.Message);
        Assert.True(source.Disposed);
        Assert.Empty(GetUgoiraWorkspaces().Except(workspacesBefore));
    }

    [Fact]
    public async Task UgoiraPreservesFailureAndCleansUpWhenTheRenderedFileCannotBeOpened()
    {
        var source = new TrackingStream(CreateUgoiraArchive());
        var client = CreateUgoiraClient(source);
        string? renderedPath = null;
        var renderer = Substitute.For<IUgoiraVideoRenderer>();
        renderer.RenderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                renderedPath = callInfo.ArgAt<string>(1);
                return Task.CompletedTask;
            });
        var site = new PixivSite(
            Substitute.For<ILogger<PixivSite>>(),
            new ConfigurationBuilder().Build().PixivOptions(),
            client,
            renderer);

        await Assert.ThrowsAsync<FileNotFoundException>(() => site.Process(CreateUgoiraRequest(TestContext.Current.CancellationToken)));

        Assert.NotNull(renderedPath);
        Assert.False(Directory.Exists(Path.GetDirectoryName(renderedPath)));
    }

    [Fact]
    public async Task UgoiraPreservesRenderFailureWhenWorkspaceWasAlreadyRemoved()
    {
        var logger = new RecordingLogger<PixivSite>();
        var renderer = Substitute.For<IUgoiraVideoRenderer>();
        renderer.RenderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var requestPath = Directory.GetParent(Path.GetDirectoryName(callInfo.ArgAt<string>(1))!)!.FullName;
                Directory.Delete(requestPath, true);
                throw new InvalidOperationException("render failed");
            });
        var site = new PixivSite(
            logger,
            new ConfigurationBuilder().Build().PixivOptions(),
            CreateUgoiraClient(new TrackingStream(CreateUgoiraArchive())),
            renderer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => site.Process(CreateUgoiraRequest(TestContext.Current.CancellationToken)));

        Assert.Equal("render failed", exception.Message);
        Assert.Empty(logger.Exceptions);
    }

    [Fact]
    public async Task GetFileRejectsUnsuccessfulResponsesBeforeReturningAStream()
    {
        var content = new TrackingContent();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
        {
            Content = content,
        });
        var client = new PixivClient(
            Substitute.For<ILogger<PixivClient>>(),
            new ConfigurationBuilder().Build(),
            Substitute.For<ICacheManager>(),
            new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetFile("https://example.test/file.jpg", TestContext.Current.CancellationToken));

        Assert.Equal(1, content.DisposeCount);
    }

    [Fact]
    public async Task GetFilePassesCancellationToTheHttpRequest()
    {
        var cancellation = new CancellationTokenSource();
        var handler = new CancellationHandler();
        var client = new PixivClient(
            Substitute.For<ILogger<PixivClient>>(),
            new ConfigurationBuilder().Build(),
            Substitute.For<ICacheManager>(),
            new HttpClient(handler));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetFile("https://example.test/file.jpg", cancellation.Token));

        Assert.True(handler.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task GetFilePassesCancellationDuringContentStreaming()
    {
        var content = new StreamingContent();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        });
        var client = new PixivClient(
            Substitute.For<ILogger<PixivClient>>(),
            new ConfigurationBuilder().Build(),
            Substitute.For<ICacheManager>(),
            new HttpClient(handler));

        await using var stream = await client.GetFile("https://example.test/file.jpg", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ReadAsync(new byte[1], cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, content.Stream.CancellationToken);
    }

    [Fact]
    public async Task PokeFileRejectsUnsuccessfulResponsesBeforeReturningAResponse()
    {
        var content = new TrackingContent();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
        {
            Content = content,
        });
        var client = new PixivClient(
            Substitute.For<ILogger<PixivClient>>(),
            new ConfigurationBuilder().Build(),
            Substitute.For<ICacheManager>(),
            new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PokeFile("https://example.test/file.jpg", TestContext.Current.CancellationToken));

        Assert.Equal(1, content.DisposeCount);
    }

    [Fact]
    public async Task AFileIsCreatedForEachImageWithinMultiImagePost()
    {
        // Post: https://www.pixiv.net/en/artworks/106848609

        var logger = Substitute.For<ILogger<PixivSite>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:Pixiv:PostLimit", "5"}
            })
            .Build();

        var client = Substitute.For<IPixivClient>();

        var illustrationDetails = new IllustrationDetails(
            "106848609",
            "vs鬼",
            "",
            IllustrationType.Illustration,
            AiType.None,
            new IllustrationDetailsUrls("", "", "", "", ""),
            1,
            1,
            1,
            4,
            "12345",
            "testuser",
            "testaccount",
            ContentRestrictionType.General,
            DateTimeOffset.Now,
            DateTimeOffset.Now
        );

        var illustrationDetailsResponse = new IllustrationDetailsResponse(
            false,
            "Test Response",
            illustrationDetails
        );

        var illustrationPages = new List<IllustrationPages>
        {
            new(
                new IllustrationPagesUrls(
                    "https://i.pximg.net/c/128x128/img-master/img/2023/04/04/06/00/11/106848609_p0_square1200.jpg",
                    "https://i.pximg.net/c/540x540_70/img-master/img/2023/04/04/06/00/11/106848609_p0_master1200.jpg",
                    "https://i.pximg.net/img-master/img/2023/04/04/06/00/11/106848609_p0_master1200.jpg",
                    "https://i.pximg.net/img-original/img/2023/04/04/06/00/11/106848609_p0.jpg"
                ),
                1296,
                2366
            ),
            new(
                new IllustrationPagesUrls(
                    "https://i.pximg.net/c/128x128/img-master/img/2023/04/04/06/00/11/106848609_p1_square1200.jpg",
                    "https://i.pximg.net/c/540x540_70/img-master/img/2023/04/04/06/00/11/106848609_p1_master1200.jpg",
                    "https://i.pximg.net/img-master/img/2023/04/04/06/00/11/106848609_p1_master1200.jpg",
                    "https://i.pximg.net/img-original/img/2023/04/04/06/00/11/106848609_p1.jpg"
                ),
                1296,
                2366
            ),
            new(
                new IllustrationPagesUrls(
                    "https://i.pximg.net/c/128x128/img-master/img/2023/04/04/06/00/11/106848609_p2_square1200.jpg",
                    "https://i.pximg.net/c/540x540_70/img-master/img/2023/04/04/06/00/11/106848609_p2_master1200.jpg",
                    "https://i.pximg.net/img-master/img/2023/04/04/06/00/11/106848609_p2_master1200.jpg",
                    "https://i.pximg.net/img-original/img/2023/04/04/06/00/11/106848609_p2.jpg"
                ),
                972,
                1775
            ),
            new(
                new IllustrationPagesUrls(
                    "https://i.pximg.net/c/128x128/img-master/img/2023/04/04/06/00/11/106848609_p3_square1200.jpg",
                    "https://i.pximg.net/c/540x540_70/img-master/img/2023/04/04/06/00/11/106848609_p3_master1200.jpg",
                    "https://i.pximg.net/img-master/img/2023/04/04/06/00/11/106848609_p3_master1200.jpg",
                    "https://i.pximg.net/img-original/img/2023/04/04/06/00/11/106848609_p3.jpg"
                ),
                1134,
                2037
            ),
        };

        var illustrationPagesResponse = new IllustrationPagesResponse(
            false,
            "Test Response",
            illustrationPages
        );

        client
            .Login()
            .Returns(true);

        client
            .IllustrationDetails(Arg.Any<string>())
            .Returns((IllustrationDetailsResponse?)illustrationDetailsResponse);

        client
            .IllustrationPages(Arg.Any<string>())
            .Returns((IllustrationPagesResponse?)illustrationPagesResponse);

        client
            .GetFile(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryStream() as Stream);

        var site = new PixivSite(
            logger,
            config.PixivOptions(),
            client,
            Substitute.For<IUgoiraVideoRenderer>()
        );

        var match = site.Pattern.Matches("https://www.pixiv.net/en/artworks/106848609").First();

        var cancellationToken = new CancellationTokenSource().Token;
        var response = await site.Process(new ProcessRequest(
            match,
            Context: new ProcessingContext(NsfwAllowed: true, CancellationToken: cancellationToken)));

        Assert.NotNull(response);
        Assert.NotEmpty(response.Files);
        Assert.Equal(4, response.Files.Count);
        _ = client.Received(4).GetFile(Arg.Any<string>(), cancellationToken);
    }

    [Fact]
    public async Task NothingIsReturnedWhenTheApiClientReturnsUnsuccessfully()
    {
        var logger = Substitute.For<ILogger<PixivSite>>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Sites:Pixiv:PostLimit", "5"}
            })
            .Build();

        var client = Substitute.For<IPixivClient>();

        client
            .IllustrationDetails(Arg.Any<string>())
            .Returns((IllustrationDetailsResponse?)null);


        var site = new PixivSite(
            logger,
            config.PixivOptions(),
            client,
            Substitute.For<IUgoiraVideoRenderer>()
        );

        var match = site.Pattern.Matches("https://www.pixiv.net/en/artworks/79124301").First();

        var response = await site.Process(new ProcessRequest(match));

        Assert.Null(response);
    }

    [Fact]
    public void ArtworksOnSeparateLinesBeforeAndAfterSameLineArtworksAreAllMatched()
    {
        var logger = Substitute.For<ILogger<PixivSite>>();
        var config = new ConfigurationBuilder().Build();
        var client = Substitute.For<IPixivClient>();

        var site = new PixivSite(
            logger,
            config.PixivOptions(),
            client,
            Substitute.For<IUgoiraVideoRenderer>()
        );

        var content =
            "https://www.pixiv.net/en/artworks/1\n" +
            "https://www.pixiv.net/en/artworks/2 https://www.pixiv.net/artworks/3\n" +
            "https://www.pixiv.net/en/artworks/4";

        var matches = site.Pattern.Matches(content);

        Assert.Equal(4, matches.Count);

        Assert.Equal("1", matches[0].Groups["id"].Value);
        Assert.Equal("2", matches[1].Groups["id"].Value);
        Assert.Equal("3", matches[2].Groups["id"].Value);
        Assert.Equal("4", matches[3].Groups["id"].Value);
    }

    [Fact]
    public void ArtworksSurroundedByTextOnASingleLineAreAllMatched()
    {
        var logger = Substitute.For<ILogger<PixivSite>>();
        var config = new ConfigurationBuilder().Build();
        var client = Substitute.For<IPixivClient>();

        var site = new PixivSite(
            logger,
            config.PixivOptions(),
            client,
            Substitute.For<IUgoiraVideoRenderer>()
        );

        var content = "wow https://www.pixiv.net/en/artworks/1 and https://www.pixiv.net/artworks/2 cool";

        var matches = site.Pattern.Matches(content);

        Assert.Equal(2, matches.Count);

        Assert.Equal("1", matches[0].Groups["id"].Value);
        Assert.Equal("2", matches[1].Groups["id"].Value);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        public CancellationToken CancellationToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }

    private sealed class TrackingContent : HttpContent
    {
        public int DisposeCount { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class StreamingContent : HttpContent
    {
        public CancellationTrackingStream Stream { get; } = new();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(Stream);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }
    }

    private sealed class CancellationTrackingStream : MemoryStream
    {
        public CancellationToken CancellationToken { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
            return ValueTask.FromCanceled<int>(cancellationToken);
        }
    }

    private static PixivSite CreateUgoiraSite(
        IPixivClient client,
        IUgoiraVideoRenderer renderer) => new(
        Substitute.For<ILogger<PixivSite>>(),
        new ConfigurationBuilder().Build().PixivOptions(),
        client,
        renderer);

    private static IUgoiraVideoRenderer CreateUgoiraDependencies()
    {
        var renderer = Substitute.For<IUgoiraVideoRenderer>();
        renderer.RenderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                File.WriteAllBytes(callInfo.ArgAt<string>(1), [1]);
                return Task.CompletedTask;
            });

        return renderer;
    }

    private static IPixivClient CreateUgoiraClient(Stream source)
    {
        var client = Substitute.For<IPixivClient>();
        client.Login().Returns(true);
        client.IllustrationDetails(Arg.Any<string>()).Returns(CreateUgoiraDetails());
        client.UgoiraMetadata(Arg.Any<string>()).Returns(new UgoiraMetadataResponse(
            false,
            "",
            new UgoiraMetadata(
                [new UgoiraFrame("frame.jpg", 100)],
                "video/mp4",
                "https://example.test/archive.zip",
                "https://example.test/archive.zip")));
        client.GetFile(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(source);
        return client;
    }

    private static ProcessRequest CreateUgoiraRequest(CancellationToken cancellationToken = default) => new(
    new PixivSite(
            Substitute.For<ILogger<PixivSite>>(),
            new ConfigurationBuilder().Build().PixivOptions(),
            Substitute.For<IPixivClient>(),
            Substitute.For<IUgoiraVideoRenderer>()).Pattern.Match("https://www.pixiv.net/en/artworks/123"),
        Context: new ProcessingContext(NsfwAllowed: true, CancellationToken: cancellationToken));

    private static HashSet<string> GetUgoiraWorkspaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "pixiv");
        return Directory.Exists(root)
            ? Directory.GetDirectories(root).ToHashSet(StringComparer.Ordinal)
            : [];
    }

    private static IllustrationDetailsResponse CreateUgoiraDetails() => new(
        false,
        "",
        new IllustrationDetails(
            "123",
            "Ugoira",
            "",
            IllustrationType.Ugoira,
            AiType.None,
            new IllustrationDetailsUrls("", "", "", "", ""),
            0,
            0,
            0,
            1,
            "1",
            "user",
            "user",
            ContentRestrictionType.General,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

    private static byte[] CreateUgoiraArchive()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var entry = archive.CreateEntry("frame.jpg").Open())
        {
            entry.WriteByte(1);
        }

        return stream.ToArray();
    }

    private static byte[] CreateUgoiraArchiveWithStagingNameCollision()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var frame = archive.CreateEntry("frame.jpg").Open())
            {
                frame.WriteByte(1);
            }

            using (var collision = archive.CreateEntry("ugoira.zip").Open())
            {
                collision.WriteByte(0x5A);
            }
        }

        return stream.ToArray();
    }

    private sealed class TrackingStream : MemoryStream
    {
        public TrackingStream(byte[] buffer) : base(buffer)
        {
        }

        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class NonSeekableTrackingStream : Stream
    {
        private readonly MemoryStream _inner;

        public bool Disposed { get; private set; }

        public NonSeekableTrackingStream(byte[] buffer) => _inner = new MemoryStream(buffer);

        public override bool CanRead => !Disposed && _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !Disposed)
            {
                Disposed = true;
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class BlockingNonSeekableStream : Stream
    {
        private bool _firstRead = true;

        public TaskCompletionSource FirstChunkRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }

        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_firstRead)
            {
                _firstRead = false;
                buffer.Span[0] = 0x2a;
                FirstChunkRead.TrySetResult();
                return 1;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class FailingNonSeekableStream : Stream
    {
        private bool _firstRead = true;

        public bool Disposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_firstRead)
            {
                _firstRead = false;
                buffer.Span[0] = 0x2a;
                return ValueTask.FromResult(1);
            }

            return ValueTask.FromException<int>(new IOException("source read failed"));
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<Exception> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                Exceptions.Add(exception);
            }
        }
    }
}
