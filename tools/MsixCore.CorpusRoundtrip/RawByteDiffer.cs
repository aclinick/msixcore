namespace MsixCore.CorpusRoundtrip;

/// <summary>Finds the first raw byte difference between two files.</summary>
public static class RawByteDiffer
{
    /// <summary>Returns the first differing offset, or <see langword="null"/> when the files are byte-identical.</summary>
    public static long? FindFirstDifference(string leftPath, string rightPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(leftPath);
        ArgumentException.ThrowIfNullOrEmpty(rightPath);

        const int BufferSize = 81920;
        byte[] leftBuffer = new byte[BufferSize];
        byte[] rightBuffer = new byte[BufferSize];
        long offset = 0;

        using FileStream left = File.OpenRead(leftPath);
        using FileStream right = File.OpenRead(rightPath);
        while (true)
        {
            int leftRead = left.Read(leftBuffer, 0, leftBuffer.Length);
            int rightRead = right.Read(rightBuffer, 0, rightBuffer.Length);
            int shared = Math.Min(leftRead, rightRead);
            for (int i = 0; i < shared; i++)
            {
                if (leftBuffer[i] != rightBuffer[i])
                {
                    return offset + i;
                }
            }

            if (leftRead != rightRead)
            {
                return offset + shared;
            }

            if (leftRead == 0)
            {
                return null;
            }

            offset += leftRead;
        }
    }
}
