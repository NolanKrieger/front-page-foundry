namespace FrontPageFoundry.Sim;

/// <summary>
/// Every player action. The same seed and command log always reproduce the same world (GDD §14).
/// Money fields carry the exact amount when a command reverses an earlier one, so undo is exact.
/// </summary>
public abstract record Command;

/// <param name="Span">Trestle bridge only: cells the deck crosses (1–4).</param>
/// <param name="PriceCents">Charge exactly this instead of the catalog price (undo/redo).</param>
/// <param name="Setting">A sorting filter or dock order to set as it goes down (undo of a demolition puts it back).</param>
/// <param name="Id">Undo of a demolition: bring the building back under its old number, so the undo step that
/// would take it down again (see <see cref="Remove.Id"/>) still recognises it.</param>
/// <param name="Fire">Undo of a demolition (or redo of a build): the fire as it was, instead of a fresh starter bag.</param>
public sealed record Place(BuildingType Type, Cell Origin, Dir Facing, int Span = 0, long? PriceCents = null, Item? Setting = null, int? Id = null,
    FireState? Fire = null) : Command;

/// <param name="RefundCents">Refund exactly this instead of 75% of the current price (undo/redo).</param>
/// <param name="Id">Undo of a build: remove only the building that build put up. Anything built on the cell since
/// (a plan, say) is left alone, so its price can never be refunded at another building's price.</param>
public sealed record Remove(Cell Cell, long? RefundCents = null, int? Id = null) : Command;

public sealed record Rotate(Cell Cell, Dir Facing) : Command;

public sealed record SetFilter(Cell Cell, Item? Filter) : Command;

/// <summary>Receiving dock: which raw good to buy (only raws can be ordered).</summary>
public sealed record SetOrder(Cell Cell, Item? Item) : Command;

/// <summary>Buys a vehicle and sets it running between two terminals (A == B parks it there).</summary>
/// <param name="Id">Undo of a scrap: bring the vehicle back under its old number so later commands still find it.</param>
public sealed record Assign(BuildingType Type, Cell A, Cell B, long? PriceCents = null, int? Id = null) : Command;

/// <summary>Gives a vehicle of the fleet two ends to run between, or parks it (nulls).</summary>
public sealed record Route(int Id, Cell? A, Cell? B) : Command;

/// <param name="RefundCents">Refund exactly this instead of 75% of the current price (undo/redo).</param>
public sealed record Scrap(int Id, long? RefundCents = null) : Command;

/// <summary>Takes the signal off a rail tile, leaving plain rail.</summary>
public sealed record ClearSignal(Cell Cell, long? RefundCents = null) : Command;

/// <summary>Planning mode: a pencil ghost of a building, for nothing, built later from cash (GDD §3).</summary>
public sealed record Draft(BuildingType Type, Cell Origin, Dir Facing, int Span = 0, Item? Setting = null) : Command;

/// <summary>Rubs out the plan covering a cell.</summary>
public sealed record Undraft(Cell Cell) : Command;

/// <summary>Planning mode on or off. While it is on, plans are drawn and none are built.</summary>
public sealed record SetPlanning(bool On) : Command;

public enum PlaceResult { Ok, Blocked, NeedsOre, TooExpensive, BadSpan, Nothing, Ended, NeedsBank, NeedsRiver, FloodBlocked, NeedsForest, NeedsOil, NeedsTerminal, NoRoute }

/// <summary>Something for the paper: an event headline key, a Banker's Warning, the default, the fold.</summary>
public sealed record Notice(long Tick, string Key, long Amount);
