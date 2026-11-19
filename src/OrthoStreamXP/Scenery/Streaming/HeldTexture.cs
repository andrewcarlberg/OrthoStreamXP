namespace OrthoStreamXP.Scenery;

/// <summary>
/// A texture X-Plane holds: the pixels across of the finest level it read (what it takes of the graphics memory), and
/// whether it is an airport's (ZL18 or finer), whose size a resolution may set apart from the wide areas'.
/// </summary>
internal readonly record struct HeldTexture(int Detail, bool Airport);
