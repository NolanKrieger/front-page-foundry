using FrontPageFoundry.Sim;

namespace Sim.Tests;

public class MapTests
{
    [Fact]
    public void RiversRunWestToEastInEveryCorridorOneToThreeTilesWide()
    {
        var map = new MapGen(3);
        for (int corridor = -1; corridor <= 1; corridor++)
            for (int x = -400; x <= 400; x += 7)
            {
                int centre = (int)Math.Floor(map.RiverCentre(corridor, x));
                int wet = 0;
                for (int y = centre - 6; y <= centre + 6; y++)
                    if (map.RiverAt(new Cell(x, y)))
                        wet++;
                Assert.InRange(wet, 1, 3);
                Assert.True(map.RiverAt(new Cell(x, centre)) || map.RiverAt(new Cell(x, centre - 1)), $"no water at the centre, x={x}");
            }
        // The main river passes by the works, south of the starter seams and the cleared start.
        Assert.InRange(map.RiverCentre(0, 0), 12, 40);
        Assert.False(map.RiverAt(MapGen.StarterPatch));
        Assert.False(map.RiverAt(MapGen.StarterCoal));
        Assert.False(map.RiverAt(MapGen.StarterLimestone));
    }

    [Fact]
    public void TheSameSeedGivesTheSameMapInAnyOrderAndSeedsDiffer()
    {
        var a = new MapGen(11);
        var b = new MapGen(11);
        var c = new MapGen(12);
        var cells = Enumerable.Range(-300, 600).Select(i => new Cell(i * 5, -i * 3 + 40)).ToArray();
        var forward = cells.Select(a.TileAt).ToArray();
        var backward = cells.Reverse().Select(b.TileAt).Reverse().ToArray();
        Assert.Equal(forward, backward);
        Assert.NotEqual(forward, cells.Select(c.TileAt).ToArray());
        Assert.Contains(forward, t => t.Kind == Terrain.Forest);
        Assert.Contains(forward, t => t.Hill);
        Assert.Contains(forward, t => t.Kind == Terrain.River);
        Assert.Contains(forward, t => t.IsSeam);
    }

    [Fact]
    public void PatchesGetRicherWithDistanceAndStayOffRiversAndTheStart()
    {
        var map = new MapGen(5);
        var near = new List<Patch>();
        var far = new List<Patch>();
        for (int cx = -8; cx <= 8; cx++)
            for (int cy = -8; cy <= 8; cy++)
                near.AddRange(map.PatchesIn(cx, cy));
        for (int cx = 60; cx <= 70; cx++)
            for (int cy = 60; cy <= 70; cy++)
                far.AddRange(map.PatchesIn(cx, cy));
        Assert.True(near.Count > 20, $"only {near.Count} patches near");
        Assert.True(far.Count > 20, $"only {far.Count} patches far");
        Assert.True(far.Average(p => p.Richness) > 2 * near.Average(p => p.Richness));
        Assert.All(near, p => Assert.False(Math.Abs(p.Centre.X) < 32 && Math.Abs(p.Centre.Y) < 24));
        Assert.All(near, p => Assert.False(map.RiverAt(p.Centre)));
        Assert.All(near.Concat(far), p => Assert.Equal(p.Kind, map.TileAt(p.Centre).Kind));
        Assert.True(near.Select(p => p.Kind).Distinct().Count() >= 5, "several kinds of seam");
        // Same patch list every time it is asked for.
        Assert.Equal(map.PatchesIn(3, 4).Select(p => p.Id), new MapGen(5).PatchesIn(3, 4).Select(p => p.Id));
    }

    [Fact]
    public void LandCostsMoreInTownAndForestMustBeCleared()
    {
        var w = Rig.Fresh();
        Assert.True(w.Town.Blocks >= 12, $"town has {w.Town.Blocks} blocks");
        long rural = w.LandPriceCents(new Cell(-200, -200), BuildingType.BeltCanvas);
        Assert.Equal(200, rural);
        var edge = w.Town.Tiles.OrderBy(t => t.Manhattan(Town.Centre)).First() + new Cell(0, -1);
        Assert.True(w.LandPriceCents(Town.Centre, BuildingType.BeltCanvas) > 320, $"land is dear in town: {w.LandPriceCents(Town.Centre, BuildingType.BeltCanvas)}");
        Assert.Equal(PlaceResult.Blocked, w.Place(BuildingType.BeltCanvas, w.Town.Tiles.First().X, w.Town.Tiles.First().Y));

        // Find a forest tile and build on it: the belt plus land plus $20 of clearing, and the wood is gone.
        Cell? forest = null;
        for (int r = 20; r < 200 && forest == null; r++)
            for (int y = -r; y <= r && forest == null; y += 3)
                for (int x = -r; x <= r; x += 3)
                    if (w.TerrainAt(new Cell(x, y)) == Terrain.Forest && !w.Map.TileAt(new Cell(x, y)).Hill)
                    {
                        forest = new Cell(x, y);
                        break;
                    }
        Assert.NotNull(forest);
        long cash = w.CashCents;
        w.PlaceOk(BuildingType.BeltCanvas, forest!.Value.X, forest.Value.Y);
        Assert.Equal(500 + 200 + World.ClearingCents, cash - w.CashCents);
        Assert.Equal(Terrain.Ground, w.TerrainAt(forest.Value));
        w.Apply(new Remove(forest.Value));
        Assert.Equal(Terrain.Ground, w.TerrainAt(forest.Value));

        // Hills cost machines a quarter more in land, belts nothing extra.
        Cell? hill = null;
        for (int y = -300; y < 300 && hill == null; y += 2)
            for (int x = -300; x < 300; x += 2)
                if (w.Map.TileAt(new Cell(x, y)) is { Hill: true, Kind: Terrain.Ground })
                {
                    hill = new Cell(x, y);
                    break;
                }
        Assert.NotNull(hill);
        Assert.Equal(250, w.LandPriceCents(hill!.Value, BuildingType.Smelter));
        Assert.Equal(200, w.LandPriceCents(hill.Value, BuildingType.BeltCanvas));
    }

    [Fact]
    public void RiversBlockBuildingButATrestleSpansThem()
    {
        var w = Rig.Fresh();
        int x = 40;
        int centre = (int)Math.Floor(w.Map.RiverCentre(0, x));
        int top = centre;
        while (w.Map.RiverAt(new Cell(x, top - 1)))
            top--;
        int bottom = centre;
        while (w.Map.RiverAt(new Cell(x, bottom + 1)))
            bottom++;
        Assert.True(w.Map.RiverAt(new Cell(x, top)) && w.Map.RiverAt(new Cell(x, bottom)));
        Assert.Equal(PlaceResult.Blocked, w.Place(BuildingType.BeltCanvas, x, top));
        Assert.Equal(PlaceResult.Blocked, w.Place(BuildingType.Smelter, x, top));
        int span = bottom - top + 1;
        Assert.InRange(span, 1, 3);
        w.PlaceOk(BuildingType.Trestle, x, top - 1, Dir.South, span);
        Assert.IsType<Belt>(w.BuildingAt(new Cell(x, bottom + 1)));
    }

    [Fact]
    public void TheTownGrowsWithProfitAndNeverOverBuildings()
    {
        var w = Rig.Fresh();
        int blocks = w.Town.Blocks;
        // Ring the town's northern edge with belts so growth must go round them.
        var north = w.Town.Tiles.Min(t => t.Y) - 1;
        var xs = w.Town.Tiles.Select(t => t.X);
        for (int x = xs.Min() - 2; x <= xs.Max() + 2; x++)
            if (w.TerrainAt(new Cell(x, north)) == Terrain.Ground)
                w.Place(BuildingType.BeltCanvas, x, north);
        w.Run(30 * 120);
        Assert.True(w.Town.Blocks > blocks, $"grew from {blocks} to {w.Town.Blocks}");
        foreach (var b in w.Buildings)
            foreach (var c in b.Cells)
                Assert.DoesNotContain(c, w.Town.Tiles);
        Assert.All(w.Town.Tiles, t => Assert.NotEqual(Terrain.River, w.Map.TerrainAt(t)));
    }

    [Fact]
    public void TwoMinesOnOnePatchShareItsDepletion()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.MineHead, 5, -5);
        w.PlaceOk(BuildingType.MineHead, 5, -3);
        w.PlaceOk(BuildingType.FreightDepot, 7, -5);
        w.PlaceOk(BuildingType.FreightDepot, 7, -3);
        var a = (Mine)w.BuildingAt(new Cell(5, -5))!;
        var b = (Mine)w.BuildingAt(new Cell(5, -3))!;
        Assert.Same(a.Patch, b.Patch);
        w.Run(600);
        long shared = w.PatchExtracted(a.Patch!.Id);
        Assert.Equal(a.Extracted + b.Extracted, shared);
        Assert.True(shared > 1000);
        Assert.InRange(a.OutputRate, Mine.YieldFactor(shared, MapGen.StarterRichness) - 0.02, Mine.YieldFactor(shared - 64, MapGen.StarterRichness) + 0.02);
    }
}
