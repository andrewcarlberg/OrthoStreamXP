namespace OrthoStreamXP.Textures;

/// <summary>
/// The large pictures a build works in (the stitched texture and its mip-level buffers), reused from build to build at
/// their exact length, with all of them, rented or kept, held within <see cref="CapacityBytes"/>. Unlike
/// <see cref="System.Buffers.ArrayPool{T}.Shared"/>, nothing is rounded up to a power of two (a 192 MB picture took a
/// 256 MB array there), and it keeps no more than its capacity however many builds ran at once: a picture that would
/// not fit is left to the garbage collector, and one not asked for again within <see cref="IdleMilliseconds"/> is let go.
/// </summary>
internal sealed class BuildBufferPool(long capacityBytes, TimeProvider? time = null)
{
    /// <summary>The capacity when none is given: the default build memory (build-memory).</summary>
    public const long DefaultCapacityBytes = 512L << 20;

    /// <summary>How long a kept picture waits to be asked for again.</summary>
    public const long IdleMilliseconds = 60_000;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _lock = new();
    // Kept pictures in the order they came back: the oldest are let go first.
    private readonly List<(byte[] Array, long ReturnedAt)> _kept = [];
    private long _keptBytes, _rentedBytes;

    public long CapacityBytes { get; } = capacityBytes > 0 ? capacityBytes : throw new ArgumentOutOfRangeException(nameof(capacityBytes));

    /// <summary>Bytes of the pictures kept for the next builds.</summary>
    public long KeptBytes
    {
        get { lock (_lock) return _keptBytes; }
    }

    /// <summary>Bytes of the pictures builds hold now.</summary>
    public long RentedBytes
    {
        get { lock (_lock) return _rentedBytes; }
    }

    /// <summary>
    /// An array of exactly <paramref name="length"/> bytes, with whatever an earlier build left in it; give it back with
    /// <see cref="Return"/>.
    /// </summary>
    public byte[] Rent(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        lock (_lock)
        {
            LetIdleGo();
            _rentedBytes += length;
            for (int i = _kept.Count - 1; i >= 0; i--)
            {
                if (_kept[i].Array.Length != length) continue;
                byte[] array = _kept[i].Array;
                _kept.RemoveAt(i);
                _keptBytes -= length;
                return array;
            }
            // A new one: the kept pictures of other sizes make room for it, oldest first.
            while (_kept.Count > 0 && _keptBytes + _rentedBytes > CapacityBytes)
                LetGo(0);
        }
        // Every byte is written by the build before it is read.
        return GC.AllocateUninitializedArray<byte>(length);
    }

    /// <summary>Gives back an array from <see cref="Rent"/>; it is kept when it fits beside what builds hold now.</summary>
    public void Return(byte[] array)
    {
        ArgumentNullException.ThrowIfNull(array);
        lock (_lock)
        {
            _rentedBytes -= array.Length;
            LetIdleGo();
            if (_keptBytes + _rentedBytes + array.Length > CapacityBytes) return;
            _kept.Add((array, _time.GetTimestamp()));
            _keptBytes += array.Length;
        }
    }

    private void LetIdleGo()
    {
        long now = _time.GetTimestamp();
        while (_kept.Count > 0 && _time.GetElapsedTime(_kept[0].ReturnedAt, now).TotalMilliseconds > IdleMilliseconds)
            LetGo(0);
    }

    private void LetGo(int index)
    {
        _keptBytes -= _kept[index].Array.Length;
        _kept.RemoveAt(index);
    }
}
