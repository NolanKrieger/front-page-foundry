namespace FrontPageFoundry.Sim;

/// <summary>Goods in, goods out, and how long one run takes (GDD §6). Craft times are placeholders for the M14 balance pass.</summary>
public sealed record Recipe(BuildingType Machine, (Item Item, int Count)[] Inputs, (Item Item, int Count)[] Outputs, int CraftTicks)
{
    public string Id => Items.Id(Outputs[0].Item);
    public bool Needs(Item item) => Array.Exists(Inputs, i => i.Item == item);
    public int Need(Item item) => Array.Find(Inputs, i => i.Item == item).Count;
    public int OutputCount => Outputs.Sum(o => o.Count);
}

/// <summary>One job per machine, no menus: what arrives selects the recipe (GDD §5). The subset rule is unit-tested.</summary>
public static class Recipes
{
    static (Item, int)[] In(params (Item, int)[] x) => x;
    static (Item, int)[] Out(params (Item, int)[] x) => x;
    static Recipe R(BuildingType m, (Item, int)[] inputs, (Item, int)[] outputs, double seconds) =>
        new(m, inputs, outputs, (int)Math.Round(seconds * World.TicksPerSecond));

    public static readonly Recipe[] All =
    {
        // T1 — materials
        R(BuildingType.Smelter, In((Item.IronOre, 1)), Out((Item.IronIngot, 1)), 2),
        R(BuildingType.Smelter, In((Item.CopperOre, 1)), Out((Item.CopperIngot, 1)), 2),
        R(BuildingType.CokeOven, In((Item.Coal, 2)), Out((Item.Coke, 1)), 4),
        R(BuildingType.OpenHearthFurnace, In((Item.IronIngot, 2), (Item.Coke, 1), (Item.Limestone, 1)), Out((Item.SteelIngot, 1)), 8),
        R(BuildingType.ReductionWorks, In((Item.Bauxite, 2), (Item.Coke, 1)), Out((Item.AluminumIngot, 1)), 10),
        R(BuildingType.Glassworks, In((Item.Sand, 2), (Item.Limestone, 1)), Out((Item.Glass, 1)), 3),
        R(BuildingType.Sawmill, In((Item.Timber, 1)), Out((Item.Lumber, 2)), 2),
        R(BuildingType.Kiln, In((Item.Clay, 2)), Out((Item.Porcelain, 1)), 4),
        R(BuildingType.CementWorks, In((Item.Limestone, 2), (Item.Clay, 1)), Out((Item.Cement, 2)), 4),
        R(BuildingType.Vulcanizer, In((Item.RawRubber, 2), (Item.Sulfur, 1)), Out((Item.Rubber, 2)), 4),
        R(BuildingType.TextileMill, In((Item.Cotton, 2)), Out((Item.Cloth, 1)), 4),
        R(BuildingType.Refinery, In((Item.CrudeOil, 4)), Out((Item.Gasoline, 2), (Item.Lubricant, 1), (Item.Tar, 1)), 8),
        // T2 — parts
        R(BuildingType.Press, In((Item.IronIngot, 1)), Out((Item.IronPlate, 1)), 2),
        R(BuildingType.Press, In((Item.SteelIngot, 1)), Out((Item.SteelPlate, 1)), 2),
        R(BuildingType.Press, In((Item.CopperIngot, 1)), Out((Item.CopperSheet, 1)), 2),
        R(BuildingType.Press, In((Item.AluminumIngot, 1)), Out((Item.AluminumSheet, 1)), 2),
        R(BuildingType.RollingMill, In((Item.SteelIngot, 3)), Out((Item.SteelBeam, 1)), 6),
        R(BuildingType.DrawingMill, In((Item.SteelIngot, 1)), Out((Item.SteelRod, 2)), 2),
        R(BuildingType.DrawingMill, In((Item.SteelRod, 1)), Out((Item.SteelWire, 2)), 2),
        R(BuildingType.DrawingMill, In((Item.CopperIngot, 1)), Out((Item.CopperWire, 2)), 2),
        R(BuildingType.TubeMill, In((Item.SteelPlate, 1)), Out((Item.SteelTube, 1)), 3),
        R(BuildingType.TubeMill, In((Item.AluminumSheet, 1)), Out((Item.AluminumTube, 1)), 3),
        R(BuildingType.Lathe, In((Item.SteelRod, 1)), Out((Item.Bolts, 4)), 2),
        R(BuildingType.Lathe, In((Item.SteelWire, 1)), Out((Item.Rivets, 8)), 2),
        R(BuildingType.Coiler, In((Item.SteelWire, 2)), Out((Item.Springs, 1)), 3),
        R(BuildingType.PrecisionShop, In((Item.SteelPlate, 1)), Out((Item.Gears, 2)), 3),
        R(BuildingType.PrecisionShop, In((Item.SteelRod, 1), (Item.Lubricant, 1)), Out((Item.Bearings, 2)), 3),
        R(BuildingType.Foundry, In((Item.IronIngot, 3), (Item.Sand, 1)), Out((Item.IronCasting, 1)), 5),
        R(BuildingType.Foundry, In((Item.AluminumIngot, 2), (Item.Sand, 1)), Out((Item.AluminumCasting, 1)), 5),
        R(BuildingType.ChemicalWorks, In((Item.Tar, 1), (Item.Gasoline, 1)), Out((Item.Varnish, 2)), 3),
        R(BuildingType.ChemicalWorks, In((Item.Tar, 1), (Item.Varnish, 1)), Out((Item.JapanEnamel, 2)), 3),
        R(BuildingType.Joinery, In((Item.Lumber, 3), (Item.Varnish, 1)), Out((Item.Plywood, 1)), 4),
        R(BuildingType.DopingShed, In((Item.Cloth, 2), (Item.Varnish, 1)), Out((Item.DopedFabric, 1)), 4),
        R(BuildingType.Glassworks, In((Item.Glass, 2)), Out((Item.PlateGlass, 1)), 4),
        R(BuildingType.WireCoater, In((Item.CopperWire, 2), (Item.Rubber, 1)), Out((Item.InsulatedWire, 2)), 3),
        R(BuildingType.RubberWorks, In((Item.Rubber, 2), (Item.SteelWire, 1)), Out((Item.RubberHose, 1)), 4),
        R(BuildingType.RubberWorks, In((Item.Rubber, 1), (Item.CopperSheet, 1)), Out((Item.Gasket, 4)), 4),
        R(BuildingType.TireWorks, In((Item.Rubber, 3), (Item.SteelWire, 2), (Item.Cloth, 1)), Out((Item.Tire, 1)), 6),
        // T3 — components
        R(BuildingType.BoringMill, In((Item.IronCasting, 2)), Out((Item.EngineBlock, 1)), 6),
        R(BuildingType.BoringMill, In((Item.AluminumCasting, 1)), Out((Item.Crankcase, 1)), 6),
        R(BuildingType.Lathe, In((Item.IronCasting, 1)), Out((Item.Piston, 2)), 3),
        R(BuildingType.DropForge, In((Item.SteelIngot, 2)), Out((Item.Crankshaft, 1)), 5),
        R(BuildingType.DropForge, In((Item.SteelRod, 2)), Out((Item.Axle, 1)), 4),
        R(BuildingType.ElectricalShop, In((Item.Porcelain, 1), (Item.CopperWire, 1), (Item.SteelWire, 1)), Out((Item.SparkPlug, 1)), 3),
        R(BuildingType.ElectricalShop, In((Item.CopperWire, 4), (Item.IronPlate, 1), (Item.Bearings, 2)), Out((Item.Magneto, 1)), 6),
        R(BuildingType.ElectricalShop, In((Item.Glass, 1), (Item.CopperWire, 2), (Item.IronPlate, 1)), Out((Item.Headlamp, 1)), 4),
        R(BuildingType.ElectricalShop, In((Item.InsulatedWire, 4)), Out((Item.WiringHarness, 1)), 5),
        R(BuildingType.PrecisionShop, In((Item.CopperSheet, 2), (Item.Springs, 1)), Out((Item.Carburetor, 1)), 5),
        R(BuildingType.PrecisionShop, In((Item.Glass, 1), (Item.Gears, 1), (Item.Springs, 1)), Out((Item.Gauge, 1)), 5),
        R(BuildingType.PrecisionShop, In((Item.Gears, 2), (Item.SteelRod, 1)), Out((Item.SteeringGear, 1)), 5),
        R(BuildingType.SheetMetalShop, In((Item.CopperSheet, 3), (Item.RubberHose, 1)), Out((Item.Radiator, 1)), 6),
        R(BuildingType.SheetMetalShop, In((Item.SteelPlate, 2), (Item.JapanEnamel, 1)), Out((Item.BodyPanel, 1)), 5),
        R(BuildingType.DrivetrainShop, In((Item.Gears, 4), (Item.Bearings, 2), (Item.IronCasting, 1)), Out((Item.Transmission, 1)), 8),
        R(BuildingType.Wheelwright, In((Item.Tire, 1), (Item.Lumber, 2), (Item.Bolts, 4)), Out((Item.Wheel, 1)), 6),
        R(BuildingType.Upholstery, In((Item.Cloth, 2), (Item.Springs, 2), (Item.Lumber, 2)), Out((Item.Seat, 1)), 6),
        R(BuildingType.FrameShop, In((Item.SteelBeam, 2), (Item.Bolts, 8)), Out((Item.ChassisFrame, 1)), 10),
        R(BuildingType.FrameShop, In((Item.SteelTube, 6), (Item.SteelWire, 4)), Out((Item.FuselageFrame, 1)), 12),
        R(BuildingType.FrameShop, In((Item.AluminumTube, 2), (Item.Lumber, 2)), Out((Item.WingSpar, 1)), 8),
        R(BuildingType.FrameShop, In((Item.SteelTube, 2), (Item.Springs, 2), (Item.Wheel, 2)), Out((Item.Undercarriage, 1)), 10),
        R(BuildingType.Joinery, In((Item.Plywood, 1), (Item.Bolts, 2)), Out((Item.WingRib, 2)), 4),
        R(BuildingType.Joinery, In((Item.Plywood, 3), (Item.Varnish, 2)), Out((Item.Propeller, 1)), 8),
        R(BuildingType.Joinery, In((Item.Plywood, 1), (Item.Gauge, 4)), Out((Item.InstrumentPanel, 1)), 8),
        // T4 — assemblies
        R(BuildingType.EngineWorks, In((Item.EngineBlock, 1), (Item.Piston, 4), (Item.Crankshaft, 1), (Item.Carburetor, 1), (Item.SparkPlug, 4), (Item.Radiator, 1)), Out((Item.CarEngine, 1)), 30),
        R(BuildingType.EngineWorks, In((Item.Crankcase, 1), (Item.Piston, 9), (Item.Crankshaft, 1), (Item.Magneto, 2), (Item.Carburetor, 1), (Item.SparkPlug, 18)), Out((Item.RadialAeroEngine, 1)), 45),
        R(BuildingType.ChassisShop, In((Item.ChassisFrame, 1), (Item.Axle, 2), (Item.Wheel, 4), (Item.Transmission, 1), (Item.SteeringGear, 1)), Out((Item.RollingChassis, 1)), 30),
        R(BuildingType.BodyShop, In((Item.BodyPanel, 6), (Item.PlateGlass, 2), (Item.Seat, 2), (Item.Headlamp, 2), (Item.InstrumentPanel, 1)), Out((Item.CarBody, 1)), 30),
        R(BuildingType.AirframeWorks, In((Item.WingSpar, 2), (Item.WingRib, 12), (Item.DopedFabric, 8), (Item.SteelWire, 6)), Out((Item.Wing, 1)), 40),
        R(BuildingType.AirframeWorks, In((Item.FuselageFrame, 1), (Item.DopedFabric, 10), (Item.Seat, 2), (Item.InstrumentPanel, 1)), Out((Item.Fuselage, 1)), 40),
        R(BuildingType.AirframeWorks, In((Item.SteelTube, 2), (Item.WingRib, 4), (Item.DopedFabric, 3)), Out((Item.TailAssembly, 1)), 20),
        // T5 — flagships
        R(BuildingType.FinalAssemblyLine, In((Item.CarEngine, 1), (Item.RollingChassis, 1), (Item.CarBody, 1), (Item.WiringHarness, 1), (Item.Gasoline, 1)), Out((Item.Automobile, 1)), 60),
        R(BuildingType.FinalAssemblyLine, In((Item.CarEngine, 1), (Item.RollingChassis, 1), (Item.SteelBeam, 2), (Item.BodyPanel, 4), (Item.WiringHarness, 1)), Out((Item.MotorTruck, 1)), 60),
        R(BuildingType.AircraftHangar, In((Item.RadialAeroEngine, 1), (Item.Wing, 2), (Item.Fuselage, 1), (Item.TailAssembly, 1), (Item.Undercarriage, 1), (Item.Propeller, 1)), Out((Item.Aeroplane, 1)), 120),
        // T6 — prestige parts
        R(BuildingType.FrameShop, In((Item.AluminumTube, 3), (Item.Rivets, 12)), Out((Item.AluminumGirder, 1)), 8),
        R(BuildingType.RubberWorks, In((Item.Cloth, 6), (Item.Rubber, 2)), Out((Item.GasCell, 1)), 8),
        R(BuildingType.AirframeWorks, In((Item.AluminumSheet, 8), (Item.PlateGlass, 6), (Item.Seat, 4)), Out((Item.Gondola, 1)), 40),
        R(BuildingType.SheetMetalShop, In((Item.AluminumSheet, 2), (Item.Rivets, 16)), Out((Item.StressedSkinPanel, 1)), 6),
    };

    static readonly Dictionary<BuildingType, Recipe[]> byMachine =
        All.GroupBy(r => r.Machine).ToDictionary(g => g.Key, g => g.ToArray());
    static readonly Recipe[] none = Array.Empty<Recipe>();

    public static IReadOnlyList<Recipe> Of(BuildingType machine) => byMachine.GetValueOrDefault(machine, none);

    /// <summary>The recipe that makes a good, if any (the refinery's three share one).</summary>
    public static Recipe? Producing(Item item) => Array.Find(All, r => Array.Exists(r.Outputs, o => o.Item == item));

    /// <summary>Most goods of one kind a machine keeps waiting: two runs of the hungriest recipe, at least two.</summary>
    public static int InputCap(BuildingType machine, Item item)
    {
        int max = 0;
        foreach (var r in Of(machine))
            max = Math.Max(max, r.Need(item));
        return Math.Max(2, max * 2);
    }

    /// <summary>Most finished goods a machine holds before it stops starting new runs: two runs of the fullest recipe.</summary>
    public static int OutputCap(BuildingType machine)
    {
        int max = 1;
        foreach (var r in Of(machine))
            max = Math.Max(max, r.OutputCount);
        return max * 2;
    }

    /// <summary>Output port a good leaves by: its index in the recipe's outputs (the refinery's three take three ports).</summary>
    public static int PortCount(BuildingType machine)
    {
        int max = 1;
        foreach (var r in Of(machine))
            max = Math.Max(max, r.Outputs.Length);
        return max;
    }
}
