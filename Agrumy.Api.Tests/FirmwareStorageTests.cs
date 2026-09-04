using Xunit;

namespace Agrumy.Api.Tests;

/// <summary>SaveAsync must not leave a .tmp file behind when the write is interrupted partway through.</summary>
public class FirmwareStorageTests
{
    private sealed class ExplodingStream : System.IO.Stream
    {
        private readonly byte[] _firstChunk;
        private bool _served;

        public ExplodingStream(byte[] firstChunk) => _firstChunk = firstChunk;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_served)
            {
                throw new IOException("simulated mid-write failure");
            }
            _served = true;
            Array.Copy(_firstChunk, 0, buffer, offset, _firstChunk.Length);
            return _firstChunk.Length;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task SaveAsync_ExceptionMidWrite_DeletesTmpFile_NotJustTheFinalOne()
    {
        var storage = FirmwareTestSupport.NewStorage(out string root);
        const string fileName = "agrumy-esp32dev-v1.2.3.bin";
        string tmpPath = Path.Combine(root, fileName + ".tmp");
        string finalPath = Path.Combine(root, fileName);

        await Assert.ThrowsAsync<IOException>(
            () => storage.SaveAsync(fileName, new ExplodingStream([1, 2, 3, 4])));

        Assert.False(File.Exists(tmpPath), ".tmp file must be cleaned up after a failed write");
        Assert.False(File.Exists(finalPath), "final file must never appear when the write never completed");
    }

    [Fact]
    public async Task SaveAsync_Success_WritesFinalFileAndLeavesNoTmpBehind()
    {
        var storage = FirmwareTestSupport.NewStorage(out string root);
        const string fileName = "agrumy-esp32dev-v1.2.3.bin";
        byte[] payload = [1, 2, 3, 4, 5];

        var (size, sha) = await storage.SaveAsync(fileName, new MemoryStream(payload));

        Assert.Equal(payload.Length, size);
        Assert.False(string.IsNullOrEmpty(sha));
        Assert.True(File.Exists(Path.Combine(root, fileName)));
        Assert.False(File.Exists(Path.Combine(root, fileName + ".tmp")));
    }
}
