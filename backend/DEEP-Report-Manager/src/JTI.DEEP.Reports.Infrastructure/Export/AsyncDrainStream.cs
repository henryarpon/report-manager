namespace JTI.DEEP.Reports.Infrastructure.Export;

/// <summary>
/// Write-only stream that collects synchronous writes in memory and pushes them to the real output
/// only when <see cref="DrainAsync"/> is awaited.
/// </summary>
/// <remarks>
/// <see cref="System.IO.Compression.ZipArchive"/> still performs some synchronous writes (entry and
/// archive disposal), which Kestrel rejects on the response body. The caller drains regularly, so
/// memory stays bounded to roughly one drain interval of compressed output.
/// </remarks>
internal sealed class AsyncDrainStream(Stream inner) : Stream
{
    private readonly MemoryStream _buffer = new();
    private long _position;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    /// <summary>Total bytes written so far (ZipArchive uses it to record entry offsets).</summary>
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public long BufferedBytes => _buffer.Length;

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _buffer.Write(buffer);
        _position += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        if (_buffer.Length > 0)
        {
            await inner.WriteAsync(_buffer.GetBuffer().AsMemory(0, (int)_buffer.Length), cancellationToken);
            _buffer.SetLength(0);
        }

        await inner.FlushAsync(cancellationToken);
    }

    // Data reaches the output only through DrainAsync.
    public override void Flush() { }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer.Dispose();
        }

        base.Dispose(disposing);
    }
}
