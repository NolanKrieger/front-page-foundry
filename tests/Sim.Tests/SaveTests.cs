using FrontPageFoundry.Sim;

namespace Sim.Tests;

/// <summary>M13: the save format round-trips the whole world, and the store survives a damaged file.</summary>
sealed class StateHasherBox
{
    public StateHasher H = StateHasher.Start();
    public ulong Value => H.Value;
}

public class SaveTests
{
    /// <summary>A company with a bit of everything: belts with goods, a bridge, splitters, a machine mid-run, a dock, a truck under way, plans, a yard, an event, three days of paper.</summary>
    internal static World BusyWorld() => Busy();

    static World Busy()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.BeltRow(BuildingType.BeltCanvas, 7, 9, -4);
        w.PlaceOk(BuildingType.TruckDepot, 10, -5);
        for (int x = 10; x <= 40; x++)
            w.PlaceOk(BuildingType.Road, x, -3);
        w.PlaceOk(BuildingType.TruckDepot, 40, -5);
        w.PlaceOk(BuildingType.BeltCanvas, 42, -4);
        w.PlaceOk(BuildingType.FreightDepot, 43, -5);
        w.Apply(new Assign(BuildingType.MotorTruck, new Cell(10, -5), new Cell(40, -5)));
        w.BeltRow(BuildingType.BeltSteel, -30, -26, -20);
        w.PlaceOk(BuildingType.SortingSplitter, -25, -20);
        w.Apply(new SetFilter(new Cell(-25, -20), Item.Coal));
        w.PlaceOk(BuildingType.Trestle, -24, -20, Dir.East, 2);     // exit at (-21,-20)
        w.PlaceOk(BuildingType.BeltSteel, -20, -20);
        w.PlaceOk(BuildingType.Smelter, -19, -21, Dir.East);         // fed at (-19,-20)
        for (int k = 0; k < 6; k++)
            w.Drop(-30 + k % 5, -20, item: k % 2 == 0 ? Item.Coal : Item.IronOre);
        w.PlaceOk(BuildingType.ReceivingDock, -20, -16);
        w.Apply(new SetOrder(new Cell(-20, -16), Item.Sand));
        w.PlaceOk(BuildingType.ExpositionYard, 20, -30);
        var yard = (Yard)w.BuildingAt(new Cell(20, -30))!;
        for (int k = 0; k < 3; k++)
            yard.TryAccept(w, Item.Automobile, Dir.East, 60, yard.Origin);
        w.Apply(new SetPlanning(true));
        w.Apply(new Draft(BuildingType.Press, new Cell(-10, -10), Dir.South, 0, null));
        w.Apply(new Draft(BuildingType.SortingSplitter, new Cell(-6, -10), Dir.East, 0, Item.Coal));   // the pencil stays up: they remain plans
        w.StartEvent(MarketEvent.All[2]);
        w.Run(3 * 120 + 7);
        return w;
    }

    static World RoundTrip(World w, out long bytes)
    {
        using var ms = new MemoryStream();
        SaveGame.Write(w, ms, "Carvell Test Works");
        bytes = ms.Length;
        ms.Position = 0;
        return SaveGame.Read(ms);
    }

    [Fact]
    public void ARoundTripKeepsTheStateHashAndTheFuture()
    {
        var w = Busy();
        Assert.True(w.Haulage.Fleet[0].Delivered > 0 || w.Haulage.Fleet[0].Underway, "the truck is at work");
        Assert.True(w.Paper.Editions.Count >= 3 && w.Market.Events.Count > 0 && w.Plans.Count == 2);
        var back = RoundTrip(w, out long bytes);
        if (w.StateHash() != back.StateHash())
        {
            static ulong Part(Action<StateHasherBox> mix) { var box = new StateHasherBox(); mix(box); return box.Value; }
            var parts = new (string, Func<World, ulong>)[]
            {
                ("market", x => Part(h => x.Market.Hash(ref h.H))), ("paper", x => Part(h => x.Paper.Hash(ref h.H))), ("town", x => Part(h => x.Town.Hash(ref h.H))),
                ("haulage", x => Part(h => x.Haulage.Hash(ref h.H))), ("prestige", x => Part(h => x.Prestige.Hash(ref h.H))),
                ("buildings", x => Part(h => { foreach (var b in x.Buildings) { h.H.Mix(b.Id); b.Hash(ref h.H); } })),
                ("lines", x => Part(h => { foreach (var l in x.Lines) l.Hash(ref h.H); })),
            };
            var differing = parts.Where(p => p.Item2(w) != p.Item2(back)).Select(p => p.Item1).ToList();
            foreach (var (bw, bb) in w.Buildings.Zip(back.Buildings))
                if (Part(h => bw.Hash(ref h.H)) != Part(h => bb.Hash(ref h.H)))
                    differing.Add($"building {bw.Type}#{bw.Id}");
            Assert.Fail("state differs after a round trip in: " + string.Join(", ", differing));
        }
        Assert.Equal(w.StateHash(), back.StateHash());
        Assert.Equal(w.Buildings.Count, back.Buildings.Count);
        Assert.Equal(w.Lines.Count, back.Lines.Count);
        Assert.Equal(w.Log.Count, back.Log.Count);
        Assert.Equal(w.Notices.Count, back.Notices.Count);
        Assert.Equal(w.Plans.Count, back.Plans.Count);
        Assert.Equal(w.Owned(BuildingType.BeltSteel), back.Owned(BuildingType.BeltSteel));
        Assert.Equal(w.Owned(BuildingType.MotorTruck), back.Owned(BuildingType.MotorTruck));
        Assert.Equal(w.Town.Blocks, back.Town.Blocks);
        Assert.Equal(w.Prestige.Current!.Bill[0].Delivered, back.Prestige.Current!.Bill[0].Delivered);
        Assert.Equal(w.Paper.TutorialStep, back.Paper.TutorialStep);
        Assert.Equal(w.Buildings.OfType<Machine>().Select(m => (m.State, m.Runs)), back.Buildings.OfType<Machine>().Select(m => (m.State, m.Runs)));
        Assert.Equal(((Splitter)w.BuildingAt(new Cell(-25, -20))!).Filter, ((Splitter)back.BuildingAt(new Cell(-25, -20))!).Filter);
        Assert.Equal(((Dock)w.BuildingAt(new Cell(-20, -16))!).Order, ((Dock)back.BuildingAt(new Cell(-20, -16))!).Order);
        Assert.Equal(((Belt)w.BuildingAt(new Cell(-24, -20))!).Partner!.Origin, ((Belt)back.BuildingAt(new Cell(-24, -20))!).Partner!.Origin);
        Assert.True(bytes < 200_000, $"save is {bytes} bytes");
        Assert.False(back.CanUndo, "a loaded company starts with no undo history");
        // The two worlds go on identically.
        w.Run(600);
        back.Run(600);
        Assert.Equal(w.StateHash(), back.StateHash());
        Assert.Equal(w.CashCents, back.CashCents);
        Assert.Equal(w.Haulage.Fleet[0].Delivered, back.Haulage.Fleet[0].Delivered);
        // And take commands the same way afterwards.
        w.PlaceOk(BuildingType.BeltCanvas, 0, -40);
        back.PlaceOk(BuildingType.BeltCanvas, 0, -40);
        Assert.True(w.Undo() && back.Undo());
        w.Run(5);
        back.Run(5);
        Assert.Equal(w.StateHash(), back.StateHash());
    }

    [Fact]
    public void TheHeaderReadsWithoutTheBody()
    {
        var w = Busy();
        using var ms = new MemoryStream();
        SaveGame.Write(w, ms, "Carvell Test Works");
        ms.Position = 0;
        var h = SaveGame.ReadHeader(ms);
        Assert.Equal("Carvell Test Works", h.Company);
        Assert.Equal(w.TickCount, h.Tick);
        Assert.Equal(w.Day, h.Day);
        Assert.Equal(w.SharePriceCents, h.ShareCents);
        Assert.Equal("steady_trade", h.Difficulty);
        Assert.Equal(SaveGame.Version, h.Version);
    }

    [Fact]
    public void ADamagedBodyIsRefused()
    {
        var w = Rig.StarterLine(out _);
        using var ms = new MemoryStream();
        SaveGame.Write(w, ms, "x");
        var bytes = ms.ToArray();
        bytes[^20] ^= 0x55;
        Assert.Throws<InvalidDataException>(() => SaveGame.Read(new MemoryStream(bytes)));
        Assert.Throws<InvalidDataException>(() => SaveGame.Read(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })));
    }

    [Fact]
    public void TheStoreRotatesBackupsAndRecoversFromADamagedLatest()
    {
        string root = Path.Combine(Path.GetTempPath(), "fpf-save-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var w = Rig.StarterLine(out _);
            string dir = SaveStore.CompanyDir(root, "The Carvell Falls Works!");
            Assert.EndsWith("the-carvell-falls-works", dir);
            w.Run(10);
            SaveStore.Save(dir, w, "The Carvell Falls Works!");
            long firstTick = w.TickCount;
            w.Run(10);
            SaveStore.Save(dir, w, "The Carvell Falls Works!");
            Assert.True(File.Exists(Path.Combine(dir, SaveStore.FileName)) && File.Exists(Path.Combine(dir, SaveStore.Backup1)));
            Assert.False(File.Exists(Path.Combine(dir, "save.tmp")));
            var (loaded, source) = SaveStore.Load(dir);
            Assert.Equal(SaveStore.FileName, source);
            Assert.Equal(w.TickCount, loaded.TickCount);
            // The latest file is damaged (a crash mid-write, a bad sector): the backup steps in.
            File.WriteAllBytes(Path.Combine(dir, SaveStore.FileName), new byte[] { 0, 1, 2 });
            var (recovered, from) = SaveStore.Load(dir);
            Assert.Equal(SaveStore.Backup1, from);
            Assert.Equal(firstTick, recovered.TickCount);
            var list = SaveStore.List(root);
            Assert.Single(list);
            Assert.Equal("The Carvell Falls Works!", list[0].Header.Company);
            Assert.Equal(firstTick, list[0].Header.Tick);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SlugsAreSafeFolderNames()
    {
        Assert.Equal("acme-foundry-co", SaveStore.Slug("  Acme Foundry & Co. "));
        Assert.Equal("company", SaveStore.Slug("!!!"));
        Assert.Equal(40, SaveStore.Slug(new string('a', 60)).Length);
    }
}
