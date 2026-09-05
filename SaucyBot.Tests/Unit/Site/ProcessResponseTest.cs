using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using SaucyBot.Site;
using Xunit;

namespace SaucyBot.Tests.Unit.Site;

public sealed class ProcessResponseTest
{
    [Fact]
    public async Task DisposeAsyncDisposesEveryAttachmentStream()
    {
        var first = new TrackingStream();
        var second = new TrackingStream();
        var response = new ProcessResponse(files:
        [
            new FileAttachment(first, "first"),
            new FileAttachment(second, "second"),
        ]);

        await response.DisposeAsync();

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        var stream = new TrackingStream();
        var response = new ProcessResponse(files: [new FileAttachment(stream, "file")]);

        await response.DisposeAsync();
        await response.DisposeAsync();

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsyncDisposesResponseWithoutFiles()
    {
        var response = new ProcessResponse(nsfw: true);

        await response.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncRunsAfterAFailedSend()
    {
        var stream = new TrackingStream();
        var response = new ProcessResponse(files: [new FileAttachment(stream, "file")]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            try
            {
                throw new InvalidOperationException("send failed");
            }
            finally
            {
                await response.DisposeAsync();
            }
        });

        Assert.Equal(1, stream.DisposeCount);
    }

    private sealed class TrackingStream : MemoryStream
    {
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
}
