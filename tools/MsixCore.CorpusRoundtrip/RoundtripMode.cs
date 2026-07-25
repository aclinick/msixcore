namespace MsixCore.CorpusRoundtrip;

/// <summary>Compression modes exercised by the corpus round-trip harness.</summary>
public enum RoundtripMode
{
    /// <summary>Stored/no-compression mode.</summary>
    Stored,

    /// <summary>Optimal/deflate mode.</summary>
    Optimal,
}
