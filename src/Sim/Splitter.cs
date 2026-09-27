namespace FrontPageFoundry.Sim;

/// <summary>
/// A 1×2 piece across the flow with two internal lanes. A plain splitter hands goods from either lane
/// to its two outputs in turn, so it splits, merges, or both. A sorting splitter sends the filter good
/// out of its left lane and everything else out of its right (GDD §4).
/// </summary>
public sealed partial class Splitter : Building
{
    internal readonly TransportLine[] Lanes = new TransportLine[2];
    int nextOut, laneToggle;

    internal Splitter(int id, BuildingType type, Cell origin, Dir facing, Func<ILineOutput, TransportLine> makeLane)
        : base(id, type, origin, facing)
    {
        for (int lane = 0; lane < 2; lane++)
        {
            Lanes[lane] = makeLane(new LaneOutput(this, lane));
            Lanes[lane].SetLength(BeltTiers.TileLength);
        }
    }

    public bool Sorting => Type == BuildingType.SortingSplitter;
    /// <summary>Sorting splitter: the good sent left. Null sends everything right.</summary>
    public Item? Filter { get; internal set; }

    /// <summary>Lane 0 is on the left looking along the flow; lane 1 on the right.</summary>
    public Cell LaneCell(int lane)
    {
        var right = Facing.Clockwise().Offset();
        bool flipped = right.X < 0 || right.Y < 0;
        return lane == (flipped ? 1 : 0) ? Origin : Origin + (flipped ? right * -1 : right);
    }

    public Cell OutCell(int lane) => LaneCell(lane) + Facing.Offset();
    public Cell InCell(int lane) => LaneCell(lane) - Facing.Offset();
    public int LaneOf(Cell cell) => cell == LaneCell(0) ? 0 : 1;
    public TransportLine Lane(int lane) => Lanes[lane];

    /// <summary>Each lane runs at the speed of the belt feeding it (canvas if nothing does).</summary>
    internal void RefreshLaneTiers(World world)
    {
        for (int lane = 0; lane < 2; lane++)
            Lanes[lane].Tier = world.BuildingAt(InCell(lane)) is Belt b && b.Facing == Facing && b.Role != BeltRole.BridgeEntry
                ? b.Tier
                : BeltTier.Canvas;
    }

    internal override bool TryAccept(World world, Item item, Dir travel, int entryPos, Cell into) =>
        travel == Facing && Lanes[LaneOf(into)].TryInsert(world, entryPos, item);

    internal override void Update(World world)
    {
        // Alternate which lane goes first so a merger treats both inputs fairly.
        int first = laneToggle;
        Lanes[first].Tick(world);
        Lanes[first ^ 1].Tick(world);
    }

    /// <summary>Sorting splitter: which output a good takes.</summary>
    int OutFor(Item item) => item == Filter ? 0 : 1;

    bool Route(World world, int lane, Item item, int entryPos)
    {
        // Merging fairly: a good parked at the other lane's end has waited longer, so when it is that
        // lane's turn and it wants the same output, this good stands aside for a tick. Without this a
        // good that is still arriving fits a gap the parked one cannot and would win every slot.
        int other = lane ^ 1;
        if (laneToggle == other && Lanes[other].FrontParked
            && (!Sorting || OutFor(Lanes[other].Front!.Value) == OutFor(item)))
            return false;

        bool ok;
        if (Sorting)
        {
            ok = world.Offer(item, OutCell(OutFor(item)), Facing, entryPos);
        }
        else
        {
            ok = false;
            for (int k = 0; k < 2 && !ok; k++)
            {
                int t = (nextOut + k) & 1;
                if (world.Offer(item, OutCell(t), Facing, entryPos))
                {
                    nextOut = t ^ 1;
                    ok = true;
                }
            }
        }
        if (ok)
            laneToggle = other;
        return ok;
    }

    internal override void Hash(ref StateHasher h)
    {
        h.Mix(nextOut);
        h.Mix(laneToggle);
        h.Mix(Filter.HasValue ? (long)Filter.Value : -1);
        Lanes[0].Hash(ref h);
        Lanes[1].Hash(ref h);
    }

    sealed class LaneOutput : ILineOutput
    {
        readonly Splitter owner;
        readonly int lane;

        public LaneOutput(Splitter owner, int lane)
        {
            this.owner = owner;
            this.lane = lane;
        }

        public bool TryPush(World world, TransportLine line, Item item, int entryPos) => owner.Route(world, lane, item, entryPos);
    }
}
