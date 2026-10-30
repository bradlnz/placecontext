using System.Formats.Tar;
using System.IO.Compression;

namespace PlaceContext.Infrastructure.Workload;

internal static class QemuProfileArchive
{
    internal const int MaxBytes = 8 * 1024 * 1024;
    internal const string GuestProfile = "/home/workflow/.local/share/placecontext-browser";

    private sealed class BoundedStream(Stream inner) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = inner.Read(buffer);
            if ((_read += count) > 128 * 1024 * 1024) throw new ArgumentException("Expanded browser session exceeds 128 MiB.");
            return count;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    internal static void Validate(byte[] archive)
    {
        if (archive.Length is < 1 or > MaxBytes) throw new ArgumentException("Saved browser sessions must be smaller than 8 MiB.");
        using var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress);
        using var bounded = new BoundedStream(gzip);
        using var reader = new TarReader(bounded);
        long bytes = 0;
        var count = 0;
        while (reader.GetNextEntry() is { } entry)
        {
            var path = entry.Name;
            if (++count > 20_000 || (bytes += entry.Length) > 128 * 1024 * 1024
                || path.StartsWith('/') || path.Contains('\\') || path.Split('/').Any(part => part == "..")
                || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory))
                throw new ArgumentException("Invalid or oversized browser session archive.");
        }
        if (count == 0) throw new ArgumentException("The browser session is empty.");
    }
}
