namespace SyntheticEnterprise.Core.Plugins;

internal sealed class BoundedPluginPayloadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;

    public BoundedPluginPayloadStream(Stream inner, long maxBytes)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _maxBytes = Math.Max(0, maxBytes);
    }

    public long BytesWritten { get; private set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken)
        => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var remaining = _maxBytes - BytesWritten;
        if (buffer.Length <= remaining)
        {
            _inner.Write(buffer);
            BytesWritten += buffer.Length;
            return;
        }

        if (remaining > 0)
        {
            _inner.Write(buffer[..(int)remaining]);
            BytesWritten += remaining;
        }

        throw new PluginInputPayloadLimitExceededException();
    }

    public override void WriteByte(byte value)
    {
        Span<byte> buffer = stackalloc byte[1] { value };
        Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
}

internal sealed class PluginInputPayloadLimitExceededException : Exception
{
}
