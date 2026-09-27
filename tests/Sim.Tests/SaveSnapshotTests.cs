using FrontPageFoundry.Sim;

namespace Sim.Tests;

/// <summary>The autosave captures the world on the ticking thread and packs it on a worker (audit, 2026-09-23).</summary>
public class SaveSnapshotTests
{
    static ulong Loaded(byte[] file)
    {
        using var ms = new MemoryStream(file);
        return SaveGame.Read(ms).StateHash();
    }

    [Fact]
    public void ASnapshotWrittenLaterOnAnotherThreadHoldsTheWorldAsItWasWhenTaken()
    {
        var w = SaveTests.BusyWorld();
        ulong atCapture = w.StateHash();
        var snapshot = SaveGame.Capture(w, "Snapshot Works");
        w.Run(5);                                   // the works carries on while the file is packed
        Assert.NotEqual(atCapture, w.StateHash());
        byte[] file = Task.Run(() =>
        {
            using var ms = new MemoryStream();
            SaveGame.Write(snapshot, ms);
            return ms.ToArray();
        }).Result;
        Assert.Equal(atCapture, Loaded(file));
        Assert.Equal(snapshot.Tick, w.TickCount - 5 * World.TicksPerSecond);
    }

    [Fact]
    public void TheStoreWritesASnapshotLikeADirectSave()
    {
        var w = SaveTests.BusyWorld();
        string dir = Path.Combine(Path.GetTempPath(), "fpf-snap-" + Guid.NewGuid().ToString("N"));
        try
        {
            SaveStore.Save(dir, SaveGame.Capture(w, "Snapshot Works"));
            var (back, source) = SaveStore.Load(dir);
            Assert.Equal(SaveStore.FileName, source);
            Assert.Equal(w.StateHash(), back.StateHash());
            Assert.Equal("Snapshot Works", SaveStore.Header(dir)!.Company);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
