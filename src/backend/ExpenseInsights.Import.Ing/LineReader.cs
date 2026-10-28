using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace ExpenseInsights.Import.Ing;

/// <summary>
/// Reads a stream line by line (LF or CRLF) without loading it whole. The encoding is UTF-8 when
/// there is a BOM or the first non-ASCII line is valid UTF-8, otherwise Windows-1250 (what ING
/// exports).
/// </summary>
internal sealed class LineReader(Stream stream) : IDisposable
{
    private static readonly Encoding Windows1250 = CodePagesEncodingProvider.Instance.GetEncoding(1250)
        ?? throw new InvalidOperationException("Windows-1250 is not available.");

    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
    private int _start;
    private int _end;
    private bool _eof;
    private bool _first = true;
    private Encoding? _encoding;

    public int LineNumber { get; private set; }

    /// <summary>The next line without its line ending, or <c>null</c> at the end of the stream.</summary>
    public async ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            var newline = _buffer.AsSpan(_start, _end - _start).IndexOf((byte)'\n');
            if (newline >= 0)
            {
                var line = Decode(_buffer.AsSpan(_start, newline));
                _start += newline + 1;
                return line;
            }

            if (_eof)
            {
                if (_start == _end)
                {
                    return null;
                }

                var last = Decode(_buffer.AsSpan(_start, _end - _start));
                _start = _end;
                return last;
            }

            await FillAsync(ct);
        }
    }

    private async ValueTask FillAsync(CancellationToken ct)
    {
        if (_start > 0)
        {
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
            _end -= _start;
            _start = 0;
        }

        if (_end == _buffer.Length)
        {
            var larger = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
            Buffer.BlockCopy(_buffer, 0, larger, 0, _end);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = larger;
        }

        var read = await stream.ReadAsync(_buffer.AsMemory(_end), ct);
        _end += read;
        _eof = read == 0;
    }

    private string Decode(ReadOnlySpan<byte> line)
    {
        LineNumber++;
        if (_first)
        {
            _first = false;
            if (line.StartsWith("﻿"u8))
            {
                _encoding = Encoding.UTF8;
                line = line[3..];
            }
        }

        if (line.EndsWith("\r"u8))
        {
            line = line[..^1];
        }

        _encoding ??= DetectEncoding(line);
        return (_encoding ?? Encoding.ASCII).GetString(line);
    }

    /// <summary><c>null</c> while the text is plain ASCII and either encoding would do.</summary>
    internal static Encoding? DetectEncoding(ReadOnlySpan<byte> text)
    {
        if (!text.ContainsAnyExceptInRange((byte)0, (byte)0x7F))
        {
            return null;
        }

        return Utf8.IsValid(text) ? Encoding.UTF8 : Windows1250;
    }

    internal static string DecodeHead(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("﻿"u8))
        {
            return Encoding.UTF8.GetString(head[3..]);
        }

        // The head may end mid-character; that only affects the last few bytes.
        var trimmed = head.Length > 4 ? head[..^4] : head;
        return (DetectEncoding(trimmed) ?? Encoding.ASCII).GetString(head);
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }
}
