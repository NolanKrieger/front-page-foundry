using FrontPageFoundry.Sim;

namespace Sim.Tests;

public class RecipeTableTests
{
    [Fact]
    public void NoRecipeInputSetIsASubsetOfAnotherOnTheSameMachine()
    {
        foreach (var machine in Catalog.All)
        {
            var recipes = Recipes.Of(machine);
            for (int a = 0; a < recipes.Count; a++)
                for (int b = 0; b < recipes.Count; b++)
                {
                    if (a == b)
                        continue;
                    var setA = recipes[a].Inputs.Select(i => i.Item).ToHashSet();
                    var setB = recipes[b].Inputs.Select(i => i.Item).ToHashSet();
                    Assert.False(setA.IsSubsetOf(setB), $"{machine}: {recipes[a].Id}'s inputs are a subset of {recipes[b].Id}'s");
                }
        }
    }

    [Fact]
    public void EveryMadeGoodHasARecipeOnASoldMachineAndRawsHaveNone()
    {
        foreach (var def in Items.Defs)
        {
            var recipe = Recipes.Producing(def.Item);
            if (def.Tier == Tier.Raw)
                Assert.Null(recipe);
            else
            {
                Assert.NotNull(recipe);
                Assert.True(Catalog.Of(recipe!.Machine).Available, $"{def.Id} is made by {recipe.Machine}, which is not on sale");
            }
        }
        Assert.Equal(90, Items.Defs.Length);
        foreach (var r in Recipes.All)
        {
            Assert.True(r.CraftTicks > 0);
            Assert.True(Catalog.Of(r.Machine).IsMachine);
            Assert.All(r.Inputs, i => Assert.True(i.Count > 0));
        }
    }

    [Fact]
    public void PortsSitOnTheFrontEdgeAndTurnWithTheBuilding()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Refinery, 0, 0, Dir.East);
        var refinery = (Machine)w.BuildingAt(new Cell(0, 0))!;
        Assert.Equal(new[] { new Cell(3, 0), new Cell(3, 1), new Cell(3, 2) }, refinery.OutputCells(3));
        w.Apply(new Rotate(new Cell(0, 0), Dir.South));
        Assert.Equal(new[] { new Cell(2, 3), new Cell(1, 3), new Cell(0, 3) }, refinery.OutputCells(3));
        w.Apply(new Rotate(new Cell(0, 0), Dir.West));
        Assert.Equal(new[] { new Cell(-1, 2), new Cell(-1, 1), new Cell(-1, 0) }, refinery.OutputCells(3));
        w.Apply(new Rotate(new Cell(0, 0), Dir.North));
        Assert.Equal(new[] { new Cell(0, -1), new Cell(1, -1), new Cell(2, -1) }, refinery.OutputCells(3));

        w.PlaceOk(BuildingType.Smelter, 10, 0, Dir.East);
        Assert.Equal(new Cell(12, 1), ((Machine)w.BuildingAt(new Cell(10, 0))!).PortCell(0));
        w.PlaceOk(BuildingType.Lathe, 20, 0, Dir.North);
        Assert.Equal(new Cell(20, -1), ((Machine)w.BuildingAt(new Cell(20, 0))!).PortCell(0));
    }
}

public class MachineTests
{
    static Machine Feed(World w, Machine m, Item item, int count)
    {
        for (int i = 0; i < count; i++)
            Assert.True(m.TryAccept(w, item, Dir.East, BeltTiers.Spacing / 2, m.Origin), $"{m.Type} refused {item} #{i + 1}");
        return m;
    }

    [Fact]
    public void SmelterTurnsOreIntoIngotsAndPushesThemOutOfItsPort()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Smelter, 0, 0, Dir.East);
        var smelter = (Machine)w.BuildingAt(new Cell(0, 0))!;
        w.BeltRow(BuildingType.BeltCanvas, 2, 4, 1);
        w.PlaceOk(BuildingType.FreightDepot, 5, 0);
        var depot = (Depot)w.BuildingAt(new Cell(5, 1))!;
        Assert.Equal(MachineState.Starved, smelter.State);
        for (int t = 0; t < 30 * World.TicksPerSecond; t++)
        {
            smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin);
            w.Tick();
            if (t == 30)
                Assert.Equal(MachineState.Working, smelter.State);
        }
        // Two seconds a run, three tiles of transit.
        Assert.InRange(depot.ItemsSold, 11, 15);
        Assert.True(w.Market.Glut(Item.IronIngot) > 10);
        Assert.Equal(0, (int)w.Market.Glut(Item.IronOre));
        Assert.True(smelter.Runs >= 12);
    }

    [Fact]
    public void InputsAreCappedAndOnlyWantedGoodsAreTaken()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Smelter, 0, 0, Dir.East);
        var smelter = (Machine)w.BuildingAt(new Cell(0, 0))!;
        Assert.False(smelter.TryAccept(w, Item.Sand, Dir.East, 60, smelter.Origin));
        int taken = 0;
        while (smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin))
            taken++;
        Assert.Equal(Recipes.InputCap(BuildingType.Smelter, Item.IronOre), taken);
        Assert.Equal(2, taken);
    }

    [Fact]
    public void BlockedWhenNothingTakesTheOutput()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Smelter, 0, 0, Dir.East);
        var smelter = (Machine)w.BuildingAt(new Cell(0, 0))!;
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin);
            w.Tick();
        }
        Assert.Equal(MachineState.Blocked, smelter.State);
        Assert.Equal(Recipes.OutputCap(BuildingType.Smelter), smelter.Waiting.Count);
        long runs = smelter.Runs;
        w.Run(5);
        Assert.Equal(runs, smelter.Runs);
        // Give it somewhere to go and it clears.
        w.PlaceOk(BuildingType.FreightDepot, 2, 0);
        w.Run(1);
        Assert.Empty(smelter.Waiting);
    }

    [Fact]
    public void FireboxRunsOutAndCoalRelightsIt()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Smelter, 0, 0, Dir.East);
        w.PlaceOk(BuildingType.FreightDepot, 2, 0);
        var smelter = (Machine)w.BuildingAt(new Cell(0, 0))!;
        Assert.Equal(Firebox.StarterCoal, smelter.Firebox!.Coal);
        // 8 kW: 15 s per lump; the starter bag lasts 450 s of work.
        Assert.Equal(15 * World.TicksPerSecond, smelter.Firebox.TicksPerCoal);
        int worked = 0;
        for (int t = 0; t < 500 * World.TicksPerSecond; t++)
        {
            smelter.TryAccept(w, Item.IronOre, Dir.East, 60, smelter.Origin);
            w.Tick();
            if (smelter.State == MachineState.Working)
                worked++;
        }
        Assert.Equal(MachineState.Unpowered, smelter.State);
        Assert.InRange(worked, 450 * World.TicksPerSecond - 60, 450 * World.TicksPerSecond + 60);
        Assert.NotNull(smelter.Current);
        Assert.True(smelter.Progress < 1, "the run in hand waits for the fire");

        Assert.True(smelter.TryAccept(w, Item.Coal, Dir.East, 60, smelter.Origin));
        w.Tick();
        Assert.Equal(MachineState.Working, smelter.State);
    }

    [Fact]
    public void CoalIsARecipeInputFirstAndFuelSecond()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.CokeOven, 0, 0, Dir.East);
        var oven = (Machine)w.BuildingAt(new Cell(0, 0))!;
        int cap = Recipes.InputCap(BuildingType.CokeOven, Item.Coal);
        // The starter bag is over the firebox cap, so the first lumps all go to the recipe.
        for (int i = 0; i < cap; i++)
            Assert.True(oven.TryAccept(w, Item.Coal, Dir.East, 60, oven.Origin));
        Assert.Equal(cap, oven.Stored(Item.Coal));
        Assert.False(oven.TryAccept(w, Item.Coal, Dir.East, 60, oven.Origin));
    }

    [Fact]
    public void ElectricOnlyMachinesWaitForPower()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.ElectricalShop, 0, 0, Dir.East);
        var shop = (Machine)w.BuildingAt(new Cell(0, 0))!;
        Assert.Null(shop.Firebox);
        Feed(w, shop, Item.InsulatedWire, 4);
        w.Run(2);
        Assert.Equal(MachineState.Unpowered, shop.State);
        Assert.Equal(0, shop.Runs);
    }

    [Fact]
    public void AShopFedTwoJobsAlternates()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Lathe, 0, 0, Dir.East);
        var lathe = (Machine)w.BuildingAt(new Cell(0, 0))!;
        w.PlaceOk(BuildingType.BeltSteel, 1, 0);
        w.PlaceOk(BuildingType.BeltSteel, 2, 0);
        w.PlaceOk(BuildingType.FreightDepot, 3, -1);
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            lathe.TryAccept(w, Item.SteelRod, Dir.East, 60, lathe.Origin);
            lathe.TryAccept(w, Item.SteelWire, Dir.East, 60, lathe.Origin);
            w.Tick();
        }
        Assert.True(w.Market.Glut(Item.Bolts) > 8, "bolts were made");
        Assert.True(w.Market.Glut(Item.Rivets) > 16, "rivets were made");
    }

    [Fact]
    public void RefinerySendsEachProductOutItsOwnPort()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.Refinery, 0, 0, Dir.East);
        var refinery = (Machine)w.BuildingAt(new Cell(0, 0))!;
        for (int y = 0; y < 3; y++)
            w.BeltRow(BuildingType.BeltCanvas, 3, 6, y);
        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
        {
            refinery.TryAccept(w, Item.CrudeOil, Dir.East, 60, refinery.Origin);
            w.Tick();
        }
        Assert.All(w.BeltAt(3, 0).Line!.Goods, g => Assert.Equal(Item.Gasoline, g.Item));
        Assert.All(w.BeltAt(3, 1).Line!.Goods, g => Assert.Equal(Item.Lubricant, g.Item));
        Assert.All(w.BeltAt(3, 2).Line!.Goods, g => Assert.Equal(Item.Tar, g.Item));
        Assert.True(w.BeltAt(3, 2).Line!.Count > 0);
    }

    /// <summary>Iron ingots + coke + limestone → steel → plate, rod, wire: the M3 exit criterion.</summary>
    [Fact]
    public void SteelChainMakesPlatesRodsAndWire()
    {
        var w = Rig.Fresh();
        w.PlaceOk(BuildingType.OpenHearthFurnace, 0, 0, Dir.East);
        var hearth = (Machine)w.BuildingAt(new Cell(0, 0))!;
        Assert.Equal(new Cell(3, 1), hearth.PortCell(0));
        // Steel leaves at (3,1); a splitter deals it to a press (top lane) and a drawing mill (bottom lane).
        w.PlaceOk(BuildingType.BeltCanvas, 3, 1);
        w.PlaceOk(BuildingType.Splitter, 4, 1, Dir.East);
        w.PlaceOk(BuildingType.BeltCanvas, 5, 1);
        w.PlaceOk(BuildingType.BeltCanvas, 5, 2);
        w.PlaceOk(BuildingType.Press, 6, 0, Dir.East);          // fed at (6,1); port (8,1)
        w.PlaceOk(BuildingType.DrawingMill, 6, 2, Dir.East);    // fed at (6,2); rods out at (8,3)
        w.PlaceOk(BuildingType.BeltCanvas, 8, 1);
        w.PlaceOk(BuildingType.FreightDepot, 9, 0);             // plates sold
        w.BeltRow(BuildingType.BeltCanvas, 8, 9, 3);
        w.PlaceOk(BuildingType.DrawingMill, 10, 2, Dir.East);   // rods in at (10,3); wire out at (12,3)
        w.PlaceOk(BuildingType.BeltCanvas, 12, 3);
        w.PlaceOk(BuildingType.FreightDepot, 13, 2);            // wire sold
        var press = (Machine)w.BuildingAt(new Cell(6, 0))!;
        var mill1 = (Machine)w.BuildingAt(new Cell(6, 2))!;
        var mill2 = (Machine)w.BuildingAt(new Cell(10, 2))!;

        for (int t = 0; t < 240 * World.TicksPerSecond; t++)
        {
            hearth.TryAccept(w, Item.IronIngot, Dir.East, 60, hearth.Origin);
            hearth.TryAccept(w, Item.Coke, Dir.East, 60, hearth.Origin);
            hearth.TryAccept(w, Item.Limestone, Dir.East, 60, hearth.Origin);
            // 30 kW eats a lump every 4 s: keep its firebox fed too.
            hearth.TryAccept(w, Item.Coal, Dir.East, 60, hearth.Origin);
            w.Tick();
        }
        // One steel every 8 s, dealt evenly: about 15 plates and 15 ingots' worth of rods → wire.
        Assert.InRange(hearth.Runs, 26, 30);
        Assert.InRange(press.Runs, 10, 15);
        Assert.InRange(mill1.Runs, 10, 15);
        Assert.True(mill2.Runs >= 16, $"second mill drew wire {mill2.Runs} times");
        Assert.True(w.Market.Glut(Item.SteelPlate) >= 9, "steel plates reached the depot");
        Assert.True(w.Market.Glut(Item.SteelWire) >= 24, "steel wire reached the depot");
        Assert.Equal(0, (int)w.Market.Glut(Item.SteelRod));
    }

    [Fact]
    public void MineYieldsTheSeamUnderItAndBurnsCoal()
    {
        var w = Rig.Fresh();
        Assert.Equal(Terrain.Coal, w.Map.TerrainAt(MapGen.StarterCoal));
        w.PlaceOk(BuildingType.MineHead, MapGen.StarterCoal.X - 1, MapGen.StarterCoal.Y - 1);
        var mine = (Mine)w.BuildingAt(MapGen.StarterCoal)!;
        Assert.Equal(Item.Coal, mine.Yields);
        Assert.Equal(4, mine.OreTiles);
        w.PlaceOk(BuildingType.MineHead, MapGen.StarterLimestone.X - 1, MapGen.StarterLimestone.Y - 1);
        Assert.Equal(Item.Limestone, ((Mine)w.BuildingAt(MapGen.StarterLimestone)!).Yields);

        // Its coal goes through a splitter: one lane loops back into the mine, the other sells.
        // When the firebox is full the loop backs up and the splitter sends everything to the depot.
        Assert.Equal(new Cell(-8, 4), mine.OutputCell);
        w.PlaceOk(BuildingType.BeltCanvas, -8, 4, Dir.East);
        w.PlaceOk(BuildingType.Splitter, -7, 4, Dir.East);
        w.PlaceOk(BuildingType.BeltCanvas, -6, 4, Dir.North);
        w.PlaceOk(BuildingType.BeltCanvas, -6, 3, Dir.West);
        w.PlaceOk(BuildingType.BeltCanvas, -7, 3, Dir.West);
        w.PlaceOk(BuildingType.BeltCanvas, -8, 3, Dir.West);   // into the mine's east edge at (-9,3)
        w.PlaceOk(BuildingType.BeltCanvas, -6, 5, Dir.East);
        w.PlaceOk(BuildingType.FreightDepot, -5, 4);
        var depot = (Depot)w.BuildingAt(new Cell(-5, 4))!;
        // 5 kW: a lump every 24 s, so the starter bag alone would die at 720 s.
        w.Run(1000);
        Assert.True(mine.Lit, "the mine feeds its own firebox");
        Assert.True(mine.Extracted > 900, $"extracted {mine.Extracted}");
        Assert.True(depot.ItemsSold > 500, $"the rest was sold ({depot.ItemsSold})");
    }
}
