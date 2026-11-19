namespace OrthoStreamXP.Textures;

/// <summary>
/// No provider has the tile, for good: each answered "not here" (404 or another client error, no imagery, outside its
/// area), so asking again will not change it. A network failure is a plain <see cref="IOException"/> instead.
/// </summary>
internal sealed class TileNotAvailableException : IOException
{
    public TileNotAvailableException()
    {
    }

    public TileNotAvailableException(string message) : base(message)
    {
    }

    public TileNotAvailableException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
