namespace FrontPageFoundry.Sim;

/// <summary>Everything that can be bought from the classified ads (GDD §4, §5). Add new types at the end.</summary>
public enum BuildingType : byte
{
    BeltCanvas, BeltRubber, BeltSteel, Splitter, SortingSplitter, Trestle,
    MineHead, FreightDepot,
    // Extraction & trade
    LoggingCamp, PumpJack, WaterPump, ReceivingDock, ExpositionYard,
    // Mills & furnaces (T1)
    Smelter, CokeOven, OpenHearthFurnace, ReductionWorks, Glassworks, Kiln, CementWorks, Sawmill, Refinery, Vulcanizer, TextileMill, ChemicalWorks,
    // Shops (T2–T3)
    Press, RollingMill, DrawingMill, TubeMill, Lathe, Coiler, PrecisionShop, Foundry, BoringMill, DropForge, Joinery, DopingShed,
    WireCoater, RubberWorks, TireWorks, SheetMetalShop, ElectricalShop, Wheelwright, Upholstery, FrameShop, DrivetrainShop,
    // Works (T4–T5)
    EngineWorks, ChassisShop, BodyShop, AirframeWorks, FinalAssemblyLine, AircraftHangar,
    // Power
    Waterwheel, Boiler, SteamPipe, PowerStation, PowerPole, HydroDam,
    // Haulage (M9): roads, rail, river; the vehicles are bought from the same column
    Road, TruckDepot, Rail, RailStation, BargeLanding, RailSignal, MotorTruck, Locomotive, Barge,
}

/// <summary>Classified-ad column (GDD §10).</summary>
public enum AdColumn : byte { Transport, Extraction, MillsAndFurnaces, Shops, Works, Power, Haulage }

/// <summary>What a building needs to run (GDD §5): nothing, any power source (it has a firebox), or electricity only.</summary>
public enum PowerNeed : byte { None, Any, Elec }

/// <summary>
/// One catalog row. <paramref name="Width"/>×<paramref name="Height"/> is the footprint facing north;
/// facing east or west swaps them. <paramref name="Escalates"/>: machines get dearer per copy owned
/// (GDD §5); logistics stays flat. <paramref name="DrawKw"/>: power draw while working.
/// <paramref name="Available"/>: false until the milestone that implements it ships, so it is not sold yet.
/// </summary>
public sealed record BuildingDef(BuildingType Type, string Id, int Width, int Height, long BaseCostCents, bool Escalates, AdColumn Column,
    PowerNeed Power = PowerNeed.None, int DrawKw = 0, bool Available = true)
{
    public bool IsBelt => Type is BuildingType.BeltCanvas or BuildingType.BeltRubber or BuildingType.BeltSteel;
    public bool IsSplitter => Type is BuildingType.Splitter or BuildingType.SortingSplitter;
    public bool IsBridge => Type == BuildingType.Trestle;
    /// <summary>Road or rail: laid by the yard, undirected, never ticked.</summary>
    public bool IsTrack => Type is BuildingType.Road or BuildingType.Rail or BuildingType.RailSignal;
    /// <summary>Where vehicles call: truck depot, rail station, barge landing.</summary>
    public bool IsTerminal => Type is BuildingType.TruckDepot or BuildingType.RailStation or BuildingType.BargeLanding;
    /// <summary>Bought from the ads but never placed: given two terminals to run between.</summary>
    public bool IsVehicle => Type is BuildingType.MotorTruck or BuildingType.Locomotive or BuildingType.Barge;
    /// <summary>Runs recipes (see <see cref="Recipes"/>).</summary>
    public bool IsMachine => Recipes.Of(Type).Count > 0;
    public bool HasFirebox => Power == PowerNeed.Any;
}

public static class Catalog
{
    static BuildingDef B(BuildingType t, string id, int size, double dollars, AdColumn col, PowerNeed power = PowerNeed.None, int kw = 0, bool available = true, bool escalates = true) =>
        new(t, id, size, size, (long)Math.Round(dollars * 100), escalates, col, power, kw, available);

    public static readonly BuildingDef[] Defs =
    {
        new(BuildingType.BeltCanvas, "belt_canvas", 1, 1, 500, false, AdColumn.Transport),
        new(BuildingType.BeltRubber, "belt_rubber", 1, 1, 1_200, false, AdColumn.Transport),
        new(BuildingType.BeltSteel, "belt_steel", 1, 1, 3_000, false, AdColumn.Transport),
        new(BuildingType.Splitter, "splitter", 2, 1, 15_000, false, AdColumn.Transport),
        new(BuildingType.SortingSplitter, "sorting_splitter", 2, 1, 40_000, false, AdColumn.Transport),
        new(BuildingType.Trestle, "trestle", 1, 1, 8_000, false, AdColumn.Transport),
        B(BuildingType.MineHead, "mine_head", 2, 400, AdColumn.Extraction, PowerNeed.Any, 5),
        B(BuildingType.FreightDepot, "freight_depot", 2, 600, AdColumn.Extraction),

        B(BuildingType.LoggingCamp, "logging_camp", 2, 350, AdColumn.Extraction, PowerNeed.Any, 4),
        B(BuildingType.PumpJack, "pump_jack", 2, 1_500, AdColumn.Extraction, PowerNeed.Any, 6),
        B(BuildingType.WaterPump, "water_pump", 1, 250, AdColumn.Power),
        B(BuildingType.ReceivingDock, "receiving_dock", 2, 800, AdColumn.Extraction),
        B(BuildingType.ExpositionYard, "exposition_yard", 3, 25_000, AdColumn.Extraction, escalates: false),

        B(BuildingType.Smelter, "smelter", 2, 600, AdColumn.MillsAndFurnaces, PowerNeed.Any, 8),
        B(BuildingType.CokeOven, "coke_oven", 2, 700, AdColumn.MillsAndFurnaces, PowerNeed.Any, 6),
        B(BuildingType.OpenHearthFurnace, "open_hearth_furnace", 3, 12000, AdColumn.MillsAndFurnaces, PowerNeed.Any, 30),
        B(BuildingType.ReductionWorks, "reduction_works", 3, 120000, AdColumn.MillsAndFurnaces, PowerNeed.Elec, 60),
        B(BuildingType.Glassworks, "glassworks", 2, 1800, AdColumn.MillsAndFurnaces, PowerNeed.Any, 10),
        B(BuildingType.Kiln, "kiln", 2, 500, AdColumn.MillsAndFurnaces, PowerNeed.Any, 8),
        B(BuildingType.CementWorks, "cement_works", 2, 2400, AdColumn.MillsAndFurnaces, PowerNeed.Any, 10),
        B(BuildingType.Sawmill, "sawmill", 2, 450, AdColumn.MillsAndFurnaces, PowerNeed.Any, 6),
        B(BuildingType.Refinery, "refinery", 3, 24000, AdColumn.MillsAndFurnaces, PowerNeed.Any, 25),
        B(BuildingType.Vulcanizer, "vulcanizer", 2, 3600, AdColumn.MillsAndFurnaces, PowerNeed.Any, 8),
        B(BuildingType.TextileMill, "textile_mill", 2, 3000, AdColumn.MillsAndFurnaces, PowerNeed.Any, 6),
        B(BuildingType.ChemicalWorks, "chemical_works", 2, 5000, AdColumn.MillsAndFurnaces, PowerNeed.Any, 8),

        B(BuildingType.Press, "press", 2, 3000, AdColumn.Shops, PowerNeed.Any, 10),
        B(BuildingType.RollingMill, "rolling_mill", 3, 24000, AdColumn.Shops, PowerNeed.Any, 30),
        B(BuildingType.DrawingMill, "drawing_mill", 2, 4200, AdColumn.Shops, PowerNeed.Any, 10),
        B(BuildingType.TubeMill, "tube_mill", 2, 6600, AdColumn.Shops, PowerNeed.Any, 12),
        B(BuildingType.Lathe, "lathe", 1, 2100, AdColumn.Shops, PowerNeed.Any, 4),
        B(BuildingType.Coiler, "coiler", 1, 1500, AdColumn.Shops, PowerNeed.Any, 3),
        B(BuildingType.PrecisionShop, "precision_shop", 2, 12000, AdColumn.Shops, PowerNeed.Any, 8),
        B(BuildingType.Foundry, "foundry", 2, 9000, AdColumn.Shops, PowerNeed.Any, 15),
        B(BuildingType.BoringMill, "boring_mill", 2, 15000, AdColumn.Shops, PowerNeed.Any, 12),
        B(BuildingType.DropForge, "drop_forge", 2, 13500, AdColumn.Shops, PowerNeed.Any, 20),
        B(BuildingType.Joinery, "joinery", 2, 3600, AdColumn.Shops, PowerNeed.Any, 5),
        B(BuildingType.DopingShed, "doping_shed", 2, 6000, AdColumn.Shops, PowerNeed.Any, 4),
        B(BuildingType.WireCoater, "wire_coater", 1, 2700, AdColumn.Shops, PowerNeed.Any, 4),
        B(BuildingType.RubberWorks, "rubber_works", 2, 7200, AdColumn.Shops, PowerNeed.Any, 8),
        B(BuildingType.TireWorks, "tire_works", 2, 10500, AdColumn.Shops, PowerNeed.Any, 10),
        B(BuildingType.SheetMetalShop, "sheet_metal_shop", 2, 10500, AdColumn.Shops, PowerNeed.Any, 10),
        B(BuildingType.ElectricalShop, "electrical_shop", 2, 27000, AdColumn.Shops, PowerNeed.Elec, 10),
        B(BuildingType.Wheelwright, "wheelwright", 1, 2400, AdColumn.Shops, PowerNeed.Any, 3),
        B(BuildingType.Upholstery, "upholstery", 1, 2100, AdColumn.Shops, PowerNeed.Any, 2),
        B(BuildingType.FrameShop, "frame_shop", 3, 30000, AdColumn.Shops, PowerNeed.Any, 20),
        B(BuildingType.DrivetrainShop, "drivetrain_shop", 2, 18000, AdColumn.Shops, PowerNeed.Any, 12),

        B(BuildingType.EngineWorks, "engine_works", 3, 150000, AdColumn.Works, PowerNeed.Any, 30),
        B(BuildingType.ChassisShop, "chassis_shop", 3, 108000, AdColumn.Works, PowerNeed.Any, 25),
        B(BuildingType.BodyShop, "body_shop", 3, 108000, AdColumn.Works, PowerNeed.Any, 25),
        B(BuildingType.AirframeWorks, "airframe_works", 3, 270000, AdColumn.Works, PowerNeed.Elec, 30),
        B(BuildingType.FinalAssemblyLine, "final_assembly_line", 3, 180000, AdColumn.Works, PowerNeed.Elec, 40),
        B(BuildingType.AircraftHangar, "aircraft_hangar", 3, 480000, AdColumn.Works, PowerNeed.Elec, 50),

        new(BuildingType.Waterwheel, "waterwheel", 1, 2, 30_000, true, AdColumn.Power),
        B(BuildingType.Boiler, "boiler", 2, 2_500, AdColumn.Power),
        new(BuildingType.SteamPipe, "steam_pipe", 1, 1, 1_000, false, AdColumn.Power),
        B(BuildingType.PowerStation, "power_station", 3, 40000, AdColumn.Power),
        new(BuildingType.PowerPole, "power_pole", 1, 1, 4_000, false, AdColumn.Power),
        B(BuildingType.HydroDam, "hydro_dam", 3, 120000, AdColumn.Power),

        new(BuildingType.Road, "road", 1, 1, 800, false, AdColumn.Haulage),
        B(BuildingType.TruckDepot, "truck_depot", 2, 1_500, AdColumn.Haulage, escalates: false),
        new(BuildingType.Rail, "rail", 1, 1, 2_500, false, AdColumn.Haulage),
        B(BuildingType.RailStation, "rail_station", 3, 5_000, AdColumn.Haulage, escalates: false),
        B(BuildingType.BargeLanding, "barge_landing", 2, 2_000, AdColumn.Haulage, escalates: false),
        new(BuildingType.RailSignal, "rail_signal", 1, 1, 6_500, false, AdColumn.Haulage),
        new(BuildingType.MotorTruck, "motor_truck", 1, 1, 360_000, false, AdColumn.Haulage),
        new(BuildingType.Locomotive, "locomotive", 1, 1, 1_500_000, false, AdColumn.Haulage),
        new(BuildingType.Barge, "barge", 1, 1, 200_000, false, AdColumn.Haulage),
    };

    public static readonly BuildingType[] All = Enum.GetValues<BuildingType>();
    public static readonly AdColumn[] Columns = Enum.GetValues<AdColumn>();

    public static BuildingDef Of(BuildingType type) => Defs[(int)type];

    /// <summary>The types on sale in a column, in catalog order.</summary>
    public static IEnumerable<BuildingType> InColumn(AdColumn column) =>
        All.Where(t => Of(t).Column == column && Of(t).Available);

    public static BeltTier TierOf(BuildingType type) => type switch
    {
        BuildingType.BeltCanvas => BeltTier.Canvas,
        BuildingType.BeltRubber => BeltTier.Rubber,
        BuildingType.BeltSteel => BeltTier.Steel,
        _ => throw new ArgumentException($"{type} is not a belt"),
    };

    public static BuildingType BeltOf(BeltTier tier) => tier switch
    {
        BeltTier.Canvas => BuildingType.BeltCanvas,
        BeltTier.Rubber => BuildingType.BeltRubber,
        _ => BuildingType.BeltSteel,
    };

    /// <summary>Footprint of a type turned to face <paramref name="facing"/>.</summary>
    public static (int Width, int Height) Footprint(BuildingType type, Dir facing)
    {
        var def = Of(type);
        return facing.IsHorizontal() ? (def.Height, def.Width) : (def.Width, def.Height);
    }

    static Catalog()
    {
        for (int i = 0; i < Defs.Length; i++)
            if ((int)Defs[i].Type != i)
                throw new InvalidOperationException($"Catalog.Defs[{i}] is {Defs[i].Type}; the table must follow the enum order.");
        if (Defs.Length != All.Length)
            throw new InvalidOperationException("Every BuildingType needs a row in Catalog.Defs.");
    }
}
