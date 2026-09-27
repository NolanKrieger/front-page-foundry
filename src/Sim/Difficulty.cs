namespace FrontPageFoundry.Sim;

/// <summary>The three presets (GDD §13). Values are proposals until the M14 balance pass.</summary>
public sealed record Difficulty(string Id, long StartingCashCents, long BaseCreditCents, int WeeklyInterestPermille,
    int TrendSigmaMilli, int EventChancePercentPerDay, int RichnessPercent)
{
    public static readonly Difficulty BoomTimes = new("boom_times", 800_000, 1_500_000, 5, 4, 7, 150);
    public static readonly Difficulty SteadyTrade = new("steady_trade", 500_000, 1_000_000, 10, 8, 10, 100);
    public static readonly Difficulty HardTimes = new("hard_times", 300_000, 500_000, 15, 12, 14, 70);
    public static readonly Difficulty[] All = { BoomTimes, SteadyTrade, HardTimes };
}
