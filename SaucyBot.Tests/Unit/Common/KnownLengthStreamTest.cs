using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using SaucyBot.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Common;

public sealed class KnownLengthStreamTest
{
    [Fact]
    public async Task DisposeAsyncDisposesHttpResponseAndContentStream()
    {
        var contentStream = new TrackingStream();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var content = new TrackingContent();
        response.Content = content;
        var stream = new HttpResponseStream(response, contentStream);
        var knownLengthStream = new KnownLengthStream(stream, 12);

        await knownLengthStream.DisposeAsync();

        Assert.Equal(1, contentStream.DisposeCount);
        Assert.Equal(1, content.DisposeCount);
        response.Dispose();
        Assert.Equal(1, contentStream.DisposeCount);
        Assert.Equal(1, content.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsyncDisposesHttpResponseAndContentStreamOnce()
    {
        var contentStream = new TrackingStream();
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var stream = new HttpResponseStream(response, contentStream);

        await stream.DisposeAsync();
        await stream.DisposeAsync();

        Assert.Equal(1, contentStream.DisposeCount);
        response.Dispose();
        Assert.Equal(1, contentStream.DisposeCount);
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

    private sealed class TrackingContent : HttpContent
    {
        public int DisposeCount { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.CompletedTask;

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
}
