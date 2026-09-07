using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SaucyBot.Common;
using Xunit;

namespace SaucyBot.Tests.Unit.Common;

public sealed class HttpResponseStreamTest
{
    [Fact]
    public async Task ReadAsyncPassesCancellationToTheContentStream()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var content = new CancellationTrackingStream();
        await using var stream = new HttpResponseStream(response, content);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.ReadAsync(new byte[1], cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, content.CancellationToken);
    }

    [Fact]
    public async Task DisposeAsyncDisposesTheResponseAfterTheStreamIsDisposed()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var content = new TrackingContent();
        response.Content = content;
        await using var stream = new HttpResponseStream(response, new MemoryStream());

        await stream.DisposeAsync();

        Assert.Equal(1, content.DisposeCount);
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

    private sealed class TrackingContent : HttpContent
    {
        public int DisposeCount { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;

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
