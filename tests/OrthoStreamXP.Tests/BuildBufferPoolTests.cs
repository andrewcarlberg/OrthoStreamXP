using OrthoStreamXP.Textures;

namespace OrthoStreamXP.Tests;

/// <summary>The build pictures are reused at their exact size, and the pool never holds more than its capacity.</summary>
public sealed class BuildBufferPoolTests
{
    [Fact]
    public void A_picture_has_its_exact_length_and_is_reused()
    {
        var pool = new BuildBufferPool(1000);

        byte[] first = pool.Rent(300);
        pool.Return(first);
        byte[] second = pool.Rent(300);

        Assert.Equal(300, first.Length);
        Assert.Same(first, second);
        Assert.Equal(300, pool.RentedBytes);
        Assert.Equal(0, pool.KeptBytes);
    }

    [Fact]
    public void A_picture_of_another_length_is_new()
    {
        var pool = new BuildBufferPool(1000);
        byte[] small = pool.Rent(100);
        pool.Return(small);

        Assert.NotSame(small, pool.Rent(200));
        Assert.Equal(100, pool.KeptBytes);
    }

    [Fact]
    public void Kept_pictures_stay_within_the_capacity_beside_those_in_use()
    {
        var pool = new BuildBufferPool(1000);
        byte[] a = pool.Rent(400), b = pool.Rent(400), c = pool.Rent(400); // more in use than the capacity: allowed

        pool.Return(a);
        Assert.Equal(0, pool.KeptBytes); // 400 kept + 800 in use would be 1200
        pool.Return(b);
        Assert.Equal(400, pool.KeptBytes);
        pool.Return(c);
        Assert.Equal(800, pool.KeptBytes);
        Assert.Equal(0, pool.RentedBytes);
    }

    [Fact]
    public void A_new_picture_makes_room_by_letting_the_oldest_kept_ones_go()
    {
        var pool = new BuildBufferPool(1000);
        byte[] a = pool.Rent(400), b = pool.Rent(300);
        pool.Return(a);
        pool.Return(b);

        byte[] c = pool.Rent(500);

        Assert.Equal(300, pool.KeptBytes); // the 400 went, the newer 300 fits beside the 500
        Assert.Same(b, pool.Rent(300));
        GC.KeepAlive(c);
    }

    [Fact]
    public void A_picture_not_asked_for_again_is_let_go()
    {
        var time = new ManualTime();
        var pool = new BuildBufferPool(1000, time);
        byte[] old = pool.Rent(100);
        pool.Return(old);

        time.Advance(TimeSpan.FromMilliseconds(BuildBufferPool.IdleMilliseconds + 1));

        Assert.NotSame(old, pool.Rent(100));
        Assert.Equal(0, pool.KeptBytes);
    }

    /// <summary>Time that moves only when told.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
