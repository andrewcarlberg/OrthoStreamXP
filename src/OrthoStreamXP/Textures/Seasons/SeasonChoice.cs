using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace OrthoStreamXP.Textures;

/// <summary>
/// How the season is chosen: by X-Plane's date (<see cref="Auto"/>), by X-Plane's date with each place's grass and fields
/// as its satellite record shows them (<see cref="Satellite"/>), or by hand, the same everywhere.
/// </summary>
internal enum SeasonChoice
{
    Auto,
    Satellite,
    EarlySpring,
    Spring,
    Summer,
    EarlyAutumn,
    Autumn,
    LateAutumn,
    Winter,
}
