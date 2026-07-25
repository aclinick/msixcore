using MsixCore.Packaging.Authoring;

namespace MsixCore.Packaging.Tests;

/// <summary>
/// Unit tests for <see cref="GatedCountingStream"/>.  This stream is the mechanism
/// that blocks DeflateStream finalization bytes from reaching the output, so its
/// correctness is critical to package integrity.
/// </summary>
public sealed class GatedCountingStreamTests
{
    [Fact]
    public void Write_ForwardsToInner_AndCountsBytes()
    {
        using var inner = new MemoryStream();
        var gate = new GatedCountingStream(inner);

        gate.Write(new byte[] { 1, 2, 3 });

        Assert.Equal(3, gate.BytesWritten);
        Assert.Equal(3, inner.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, inner.ToArray());
    }

    [Fact]
    public void Write_Span_ForwardsToInner()
    {
        using var inner = new MemoryStream();
        var gate = new GatedCountingStream(inner);

        gate.Write([10, 20, 30, 40]);

        Assert.Equal(4, gate.BytesWritten);
        Assert.Equal(new byte[] { 10, 20, 30, 40 }, inner.ToArray());
    }

    [Fact]
    public void WriteByte_ForwardsToInner()
    {
        using var inner = new MemoryStream();
        var gate = new GatedCountingStream(inner);

        gate.WriteByte(0xAB);

        Assert.Equal(1, gate.BytesWritten);
        Assert.Equal(new byte[] { 0xAB }, inner.ToArray());
    }

    [Fact]
    public void Close_BlocksSubsequentWrites()
    {
        using var inner = new MemoryStream();
        var gate = new GatedCountingStream(inner);

        gate.Write(new byte[] { 1, 2 });
        Assert.Equal(2, gate.BytesWritten);

        gate.Close();

        // These writes should be silently discarded.
        gate.Write(new byte[] { 99, 100 });
        gate.WriteByte(0xFF);
        gate.Write([42]);

        Assert.Equal(2, gate.BytesWritten);
        Assert.Equal(2, inner.Length);
        Assert.Equal(new byte[] { 1, 2 }, inner.ToArray());
    }

    [Fact]
    public void Reset_ReopensGate_AndResetsCounter()
    {
        using var inner = new MemoryStream();
        var gate = new GatedCountingStream(inner);

        gate.Write(new byte[] { 1 });
        gate.Close();
        gate.Write(new byte[] { 99 }); // Discarded.

        gate.Reset();

        gate.Write(new byte[] { 2, 3 });
        Assert.Equal(2, gate.BytesWritten);
        Assert.Equal(3, inner.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, inner.ToArray());
    }

    [Fact]
    public void MultipleBlocks_SimulatesRealUsage()
    {
        using var inner = new MemoryStream();
        var gate = new GatedCountingStream(inner);

        // Simulate two blocks: write → close (block finalization) → reset → write.
        gate.Write(new byte[] { 0xAA, 0xBB });
        int block1 = gate.BytesWritten;
        gate.Close();
        gate.Write(new byte[] { 0xFF }); // Finalization byte — discarded.

        gate.Reset();
        gate.Write(new byte[] { 0xCC });
        int block2 = gate.BytesWritten;
        gate.Close();
        gate.Write(new byte[] { 0xFE }); // Discarded.

        Assert.Equal(2, block1);
        Assert.Equal(1, block2);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, inner.ToArray());
    }

    [Fact]
    public void CanWrite_IsTrue()
    {
        using var inner = new MemoryStream();
        var gate = new GatedCountingStream(inner);
        Assert.True(gate.CanWrite);
    }

    [Fact]
    public void CanRead_CanSeek_AreFalse()
    {
        using var inner = new MemoryStream();
        var gate = new GatedCountingStream(inner);
        Assert.False(gate.CanRead);
        Assert.False(gate.CanSeek);
    }

    [Fact]
    public void Read_Seek_SetLength_Throw()
    {
        using var inner = new MemoryStream();
        var gate = new GatedCountingStream(inner);
        Assert.Throws<NotSupportedException>(() => gate.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => gate.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => gate.SetLength(0));
        Assert.Throws<NotSupportedException>(() => _ = gate.Length);
        Assert.Throws<NotSupportedException>(() => _ = gate.Position);
        Assert.Throws<NotSupportedException>(() => gate.Position = 0);
    }
}
