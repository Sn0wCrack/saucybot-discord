using System.Net.Http;
using SaucyBot.Diagnostics;

namespace SaucyBot.Common;

public sealed class HttpResponseStream : Stream
{
    private readonly HttpResponseMessage _response;
    private readonly Stream _inner;
    private readonly SaucyBotMetrics? _metrics;
    private int _disposed;

    public HttpResponseStream(HttpResponseMessage response, Stream inner, SaucyBotMetrics? metrics = null)
    {
        _response = response;
        _inner = inner;
        _metrics = metrics;
        _metrics?.DownloadConcurrency.Add(1);
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => RecordRead(_inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => RecordRead(_inner.Read(buffer));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        RecordRead(await _inner.ReadAsync(buffer, cancellationToken));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        RecordRead(await _inner.ReadAsync(buffer, offset, count, cancellationToken));

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                _inner.Dispose();
            }
            finally
            {
                _response.Dispose();
                _metrics?.DownloadConcurrency.Add(-1);
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _inner.DisposeAsync();
        }
        finally
        {
            _response.Dispose();
            _metrics?.DownloadConcurrency.Add(-1);
        }

        GC.SuppressFinalize(this);
    }

    private int RecordRead(int bytes)
    {
        if (bytes > 0)
        {
            _metrics?.DownloadBytes.Add(bytes);
        }

        return bytes;
    }
}
