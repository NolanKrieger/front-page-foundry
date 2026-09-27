using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>Where an unlock is reported beyond the local file: Steam once the app has an id (M15); until then, nothing.</summary>
public interface IAchievementSink
{
    void Unlock(string id);
}

/// <summary>Stands in for Steamworks until Nolan registers the app; the local file is the record meanwhile.</summary>
public sealed class NoSteam : IAchievementSink
{
    public void Unlock(string id) { }
}

/// <summary>
/// The achievements of <c>docs/ACHIEVEMENTS.md</c>, checked against the sim once a second and framed as
/// clippings. Unlocks are per company (a file in its folder) and never revoked; a Steam sink is told too.
/// </summary>
public sealed class Achievements
{
    public static readonly string[] All =
    {
        "FIRST_POUR", "HORSELESS", "HAULAGE", "BARNSTORMER", "LIGHTS_ON", "THE_DAM_HELD", "A_CAR_FOR_EVERY_GARAGE",
        "DIVERSIFIED", "DEBT_FREE", "CAPTAIN_OF_INDUSTRY", "COMMISSION_1", "COMMISSION_2", "COMMISSION_3",
        "COMMISSION_4", "COMMISSION_5", "COMMISSION_6", "CLOSE_CALL", "OLD_HAND",
    };
    public const long DebtFreeFromCents = 5_000_000;
    public const long CaptainShareCents = 10_000;
    public const int DiversifiedGoods = 10;

    sealed class State
    {
        public List<string> Unlocked { get; set; } = new();
        public long MaxDebtCents { get; set; }
        public bool Warned { get; set; }
        public bool DroughtSeen { get; set; }
        public long WeekSeen { get; set; } = -1;
        /// <summary>Goods sold of each kind when the week opened, so a week that spans a load still counts for Diversified.</summary>
        public long[] WeekStartSold { get; set; } = Array.Empty<long>();
    }

    readonly State state = new();
    readonly HashSet<string> unlocked = new();
    readonly IAchievementSink sink;
    long[] weekStartSold = Array.Empty<long>();
    string? path;

    public event Action<string>? Unlocked;

    public Achievements(IAchievementSink? sink = null) => this.sink = sink ?? new NoSteam();

    public IReadOnlySet<string> Done => unlocked;
    public bool Has(string id) => unlocked.Contains(id);

    /// <summary>Reads the company's record (a missing file is a fresh company).</summary>
    public void Attach(string? companyDir)
    {
        path = companyDir == null ? null : Path.Combine(companyDir, "achievements.json");
        unlocked.Clear();
        state.Unlocked.Clear();
        state.MaxDebtCents = 0;
        state.Warned = state.DroughtSeen = false;
        state.WeekSeen = -1;
        weekStartSold = Array.Empty<long>();
        if (path == null || !File.Exists(path))
            return;
        try
        {
            var saved = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
            if (saved == null)
                return;
            foreach (var id in saved.Unlocked)
                unlocked.Add(id);
            state.Unlocked.AddRange(saved.Unlocked);
            state.MaxDebtCents = saved.MaxDebtCents;
            state.Warned = saved.Warned;
            state.DroughtSeen = saved.DroughtSeen;
            state.WeekSeen = saved.WeekSeen;
            weekStartSold = saved.WeekStartSold ?? Array.Empty<long>();
        }
        catch (JsonException)
        {
        }
    }

    void Write()
    {
        if (path == null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Looks at the world; unlocks whatever has been earned. Cheap enough to call every second.</summary>
    public void Poll(World w)
    {
        state.MaxDebtCents = Math.Max(state.MaxDebtCents, w.DebtCents);
        if (w.BankersWarning)
            state.Warned = true;
        bool drought = w.Market.Events.Any(e => e.Event.Id == "drought");
        if (drought && w.Owned(BuildingType.HydroDam) > 0)
            state.DroughtSeen = true;
        bool changed = false;

        if (w.Paper.Made(Item.SteelIngot))
            changed |= Earn("FIRST_POUR");
        if (w.Paper.Made(Item.Automobile))
            changed |= Earn("HORSELESS");
        if (w.Paper.Made(Item.MotorTruck))
            changed |= Earn("HAULAGE");
        if (w.Paper.Made(Item.Aeroplane))
            changed |= Earn("BARNSTORMER");
        if (w.Owned(BuildingType.PowerStation) > 0)
            changed |= Earn("LIGHTS_ON");
        if (state.DroughtSeen && !drought && w.Owned(BuildingType.HydroDam) > 0)
            changed |= Earn("THE_DAM_HELD");
        if (w.Paper.Printed("CARS_1000"))
            changed |= Earn("A_CAR_FOR_EVERY_GARAGE");
        if (state.MaxDebtCents >= DebtFreeFromCents && w.DebtCents == 0)
            changed |= Earn("DEBT_FREE");
        if (state.Warned && !w.BankersWarning && !w.Defaulted && w.Week > 0)
            changed |= Earn("CLOSE_CALL");
        if (w.Paper.Editions.Count >= 100)
            changed |= Earn("OLD_HAND");
        for (int n = 1; n <= 6; n++)
            if (w.Prestige.Completed >= n)
                changed |= Earn("COMMISSION_" + n);

        // Once a week: the share price at the close, and how many different goods sold.
        if (w.Week != state.WeekSeen)
        {
            if (state.WeekSeen >= 0 && weekStartSold.Length == Items.All.Length)
            {
                int kinds = 0;
                foreach (var item in Items.All)
                    if (w.Paper.Sold(item) > weekStartSold[(int)item])
                        kinds++;
                if (kinds >= DiversifiedGoods)
                    changed |= Earn("DIVERSIFIED");
                if (w.SharePriceCents >= CaptainShareCents)
                    changed |= Earn("CAPTAIN_OF_INDUSTRY");
            }
            state.WeekSeen = w.Week;
            weekStartSold = Items.All.Select(i => w.Paper.Sold(i)).ToArray();
            state.WeekStartSold = weekStartSold;
            changed = true;
        }
        if (changed)
            Write();
    }

    bool Earn(string id)
    {
        if (!unlocked.Add(id))
            return false;
        state.Unlocked.Add(id);
        sink.Unlock(id);
        Unlocked?.Invoke(id);
        return true;
    }
}
