namespace FrontPageFoundry.Sim;

/// <summary>Every good in the game (GDD §6), in tier order. Stored as one byte on belts; add new goods at the end.</summary>
public enum Item : byte
{
    // T0 raw
    IronOre, CopperOre, Coal, Limestone, Sand, Clay, Sulfur, Bauxite, Timber, CrudeOil, RawRubber, Cotton,
    // T1 materials
    IronIngot, CopperIngot, Coke, SteelIngot, AluminumIngot, Glass, Lumber, Porcelain, Cement, Rubber, Cloth, Gasoline, Lubricant, Tar,
    // T2 parts
    IronPlate, SteelPlate, CopperSheet, AluminumSheet, SteelBeam, SteelRod, SteelWire, CopperWire, SteelTube, AluminumTube,
    Bolts, Rivets, Springs, Gears, Bearings, IronCasting, AluminumCasting, Varnish, JapanEnamel, Plywood, DopedFabric,
    PlateGlass, InsulatedWire, RubberHose, Gasket, Tire,
    // T3 components
    EngineBlock, Crankcase, Piston, Crankshaft, Axle, SparkPlug, Magneto, Headlamp, WiringHarness, Carburetor, Gauge,
    SteeringGear, Radiator, BodyPanel, Transmission, Wheel, Seat, ChassisFrame, FuselageFrame, WingSpar, Undercarriage,
    WingRib, Propeller, InstrumentPanel,
    // T4 assemblies
    CarEngine, RadialAeroEngine, RollingChassis, CarBody, Wing, Fuselage, TailAssembly,
    // T5 flagships
    Automobile, MotorTruck, Aeroplane,
    // T6 prestige parts
    AluminumGirder, GasCell, Gondola, StressedSkinPanel,
}

public enum Tier : byte { Raw, Materials, Parts, Components, Assemblies, Flagships, Prestige }

/// <param name="Id">Stable string id: the key for text and art.</param>
/// <param name="BaseCents">Market price when nothing has been sold lately; placeholders until the M14 balance pass (GDD §6).</param>
/// <param name="MarketDepth">K: units sold in a glut that halve the price (GDD §8).</param>
public sealed record ItemDef(Item Item, string Id, Tier Tier, long BaseCents, double MarketDepth);

public static class Items
{
    static ItemDef D(Item item, string id, Tier tier, double dollars, double depth) => new(item, id, tier, (long)Math.Round(dollars * 100), depth);

    public static readonly ItemDef[] Defs =
    {
        D(Item.IronOre, "iron_ore", Tier.Raw, 2, 5000),
        D(Item.CopperOre, "copper_ore", Tier.Raw, 3, 5000),
        D(Item.Coal, "coal", Tier.Raw, 1.5, 5000),
        D(Item.Limestone, "limestone", Tier.Raw, 1, 5000),
        D(Item.Sand, "sand", Tier.Raw, 0.5, 5000),
        D(Item.Clay, "clay", Tier.Raw, 0.8, 5000),
        D(Item.Sulfur, "sulfur", Tier.Raw, 3, 5000),
        D(Item.Bauxite, "bauxite", Tier.Raw, 4, 5000),
        D(Item.Timber, "timber", Tier.Raw, 2, 5000),
        D(Item.CrudeOil, "crude_oil", Tier.Raw, 3, 5000),
        D(Item.RawRubber, "raw_rubber", Tier.Raw, 8, 5000),
        D(Item.Cotton, "cotton", Tier.Raw, 5, 5000),

        D(Item.IronIngot, "iron_ingot", Tier.Materials, 3.5, 2000),
        D(Item.CopperIngot, "copper_ingot", Tier.Materials, 5, 2000),
        D(Item.Coke, "coke", Tier.Materials, 4.5, 2000),
        D(Item.SteelIngot, "steel_ingot", Tier.Materials, 18, 2000),
        D(Item.AluminumIngot, "aluminum_ingot", Tier.Materials, 28, 2000),
        D(Item.Glass, "glass", Tier.Materials, 4, 2000),
        D(Item.Lumber, "lumber", Tier.Materials, 1.6, 2000),
        D(Item.Porcelain, "porcelain", Tier.Materials, 3, 2000),
        D(Item.Cement, "cement", Tier.Materials, 2.2, 2000),
        D(Item.Rubber, "rubber", Tier.Materials, 13, 2000),
        D(Item.Cloth, "cloth", Tier.Materials, 14, 2000),
        D(Item.Gasoline, "gasoline", Tier.Materials, 5, 2000),
        D(Item.Lubricant, "lubricant", Tier.Materials, 6, 2000),
        D(Item.Tar, "tar", Tier.Materials, 3, 2000),

        D(Item.IronPlate, "iron_plate", Tier.Parts, 5, 500),
        D(Item.SteelPlate, "steel_plate", Tier.Parts, 24, 500),
        D(Item.CopperSheet, "copper_sheet", Tier.Parts, 7, 500),
        D(Item.AluminumSheet, "aluminum_sheet", Tier.Parts, 36, 500),
        D(Item.SteelBeam, "steel_beam", Tier.Parts, 75, 500),
        D(Item.SteelRod, "steel_rod", Tier.Parts, 12, 500),
        D(Item.SteelWire, "steel_wire", Tier.Parts, 8, 500),
        D(Item.CopperWire, "copper_wire", Tier.Parts, 3.5, 500),
        D(Item.SteelTube, "steel_tube", Tier.Parts, 32, 500),
        D(Item.AluminumTube, "aluminum_tube", Tier.Parts, 46, 500),
        D(Item.Bolts, "bolts", Tier.Parts, 4, 500),
        D(Item.Rivets, "rivets", Tier.Parts, 1.5, 500),
        D(Item.Springs, "springs", Tier.Parts, 21, 500),
        D(Item.Gears, "gears", Tier.Parts, 16, 500),
        D(Item.Bearings, "bearings", Tier.Parts, 12, 500),
        D(Item.IronCasting, "iron_casting", Tier.Parts, 16, 500),
        D(Item.AluminumCasting, "aluminum_casting", Tier.Parts, 72, 500),
        D(Item.Varnish, "varnish", Tier.Parts, 5.5, 500),
        D(Item.JapanEnamel, "japan_enamel", Tier.Parts, 6, 500),
        D(Item.Plywood, "plywood", Tier.Parts, 14, 500),
        D(Item.DopedFabric, "doped_fabric", Tier.Parts, 44, 500),
        D(Item.PlateGlass, "plate_glass", Tier.Parts, 11, 500),
        D(Item.InsulatedWire, "insulated_wire", Tier.Parts, 13, 500),
        D(Item.RubberHose, "rubber_hose", Tier.Parts, 44, 500),
        D(Item.Gasket, "gasket", Tier.Parts, 7, 500),
        D(Item.Tire, "tire", Tier.Parts, 90, 500),

        D(Item.EngineBlock, "engine_block", Tier.Components, 45, 200),
        D(Item.Crankcase, "crankcase", Tier.Components, 95, 200),
        D(Item.Piston, "piston", Tier.Components, 11, 200),
        D(Item.Crankshaft, "crankshaft", Tier.Components, 50, 200),
        D(Item.Axle, "axle", Tier.Components, 32, 200),
        D(Item.SparkPlug, "spark_plug", Tier.Components, 20, 200),
        D(Item.Magneto, "magneto", Tier.Components, 60, 200),
        D(Item.Headlamp, "headlamp", Tier.Components, 24, 200),
        D(Item.WiringHarness, "wiring_harness", Tier.Components, 70, 200),
        D(Item.Carburetor, "carburetor", Tier.Components, 48, 200),
        D(Item.Gauge, "gauge", Tier.Components, 55, 200),
        D(Item.SteeringGear, "steering_gear", Tier.Components, 60, 200),
        D(Item.Radiator, "radiator", Tier.Components, 85, 200),
        D(Item.BodyPanel, "body_panel", Tier.Components, 70, 200),
        D(Item.Transmission, "transmission", Tier.Components, 140, 200),
        D(Item.Wheel, "wheel", Tier.Components, 140, 200),
        D(Item.Seat, "seat", Tier.Components, 95, 200),
        D(Item.ChassisFrame, "chassis_frame", Tier.Components, 240, 200),
        D(Item.FuselageFrame, "fuselage_frame", Tier.Components, 300, 200),
        D(Item.WingSpar, "wing_spar", Tier.Components, 125, 200),
        D(Item.Undercarriage, "undercarriage", Tier.Components, 500, 200),
        D(Item.WingRib, "wing_rib", Tier.Components, 15, 200),
        D(Item.Propeller, "propeller", Tier.Components, 75, 200),
        D(Item.InstrumentPanel, "instrument_panel", Tier.Components, 300, 200),

        D(Item.CarEngine, "car_engine", Tier.Assemblies, 480, 40),
        D(Item.RadialAeroEngine, "radial_aero_engine", Tier.Assemblies, 1050, 40),
        D(Item.RollingChassis, "rolling_chassis", Tier.Assemblies, 1400, 40),
        D(Item.CarBody, "car_body", Tier.Assemblies, 1300, 40),
        D(Item.Wing, "wing", Tier.Assemblies, 1100, 40),
        D(Item.Fuselage, "fuselage", Tier.Assemblies, 1600, 40),
        D(Item.TailAssembly, "tail_assembly", Tier.Assemblies, 340, 40),

        D(Item.Automobile, "automobile", Tier.Flagships, 4200, 30),
        D(Item.MotorTruck, "motor_truck", Tier.Flagships, 3100, 30),
        D(Item.Aeroplane, "aeroplane", Tier.Flagships, 11000, 6),

        D(Item.AluminumGirder, "aluminum_girder", Tier.Prestige, 200, 60),
        D(Item.GasCell, "gas_cell", Tier.Prestige, 150, 60),
        D(Item.Gondola, "gondola", Tier.Prestige, 950, 60),
        D(Item.StressedSkinPanel, "stressed_skin_panel", Tier.Prestige, 130, 60),
    };

    public static readonly Item[] All = Enum.GetValues<Item>();

    public static ItemDef Of(Item item) => Defs[(int)item];
    public static string Id(Item item) => Defs[(int)item].Id;
    public static long BasePriceCents(Item item) => Defs[(int)item].BaseCents;
    public static double MarketDepth(Item item) => Defs[(int)item].MarketDepth;

    /// <summary>The raw good a seam yields, or null for plain ground.</summary>
    public static Item? OfTerrain(Terrain terrain) => terrain switch
    {
        Terrain.IronOre => Item.IronOre,
        Terrain.CopperOre => Item.CopperOre,
        Terrain.Coal => Item.Coal,
        Terrain.Limestone => Item.Limestone,
        Terrain.Sand => Item.Sand,
        Terrain.Clay => Item.Clay,
        Terrain.Sulfur => Item.Sulfur,
        Terrain.Bauxite => Item.Bauxite,
        _ => null,
    };

    static Items()
    {
        for (int i = 0; i < Defs.Length; i++)
            if ((int)Defs[i].Item != i)
                throw new InvalidOperationException($"Items.Defs[{i}] is {Defs[i].Item}; the table must follow the enum order.");
        if (Defs.Length != All.Length)
            throw new InvalidOperationException("Every Item needs a row in Items.Defs.");
    }
}
