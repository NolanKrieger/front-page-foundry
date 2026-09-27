using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FrontPageFoundry.Sim;
using Godot;

namespace FrontPageFoundry;

/// <summary>
/// The company folders under <c>user://companies</c> (GDD §13): one save per company, autosaved every
/// in-game day and on quit, two rolling backups behind it for crash recovery. Steam Cloud syncs this
/// folder between PCs once the app's cloud path is set on the partner site (M15).
/// </summary>
public sealed class Companies
{
    /// <summary>Where companies and settings live: <c>user://</c>, or a sandbox under it for the self-test and the playtest.</summary>
    public static string UserRoot { get; set; } = "user://";
    public static string Root => ProjectSettings.GlobalizePath(UserRoot + "companies");
    public const string DefaultName = "Carvell Falls Works";
    /// <summary>Longest name the founding page takes (and the room a free name keeps for its number).</summary>
    public const int MaxName = 40;

    /// <summary>The company being played, or null for a scratch world (demo, bench, self-test) that is never saved.</summary>
    public string? Name { get; private set; }
    public string? Dir { get; private set; }
    /// <summary>Set when the latest file was damaged and a backup was read instead.</summary>
    public string? RecoveredFrom { get; private set; }
    public long LastSavedTick { get; private set; } = -1;
    public int Saves { get; private set; }

    /// <summary>Every company on the books, newest save first; an unreadable folder tree reads as none.</summary>
    public static List<(string Dir, SaveHeader Header)> List()
    {
        try
        {
            return SaveStore.List(Root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"could not list the companies in {Root}: {e.Message}");
            return new();
        }
    }

    /// <summary>A name the founding page accepts: at least one letter or figure (so it makes a folder of its own).</summary>
    public static bool Valid(string name) => name.Trim().Any(char.IsLetterOrDigit);

    /// <summary>True when a company of this name, or of one that files under the same folder, is already on the books.</summary>
    public static bool Exists(string name) => Directory.Exists(SaveStore.CompanyDir(Root, name));

    /// <summary>The wanted name if no company files under it yet, else the same name with the first free number after it.</summary>
    public static string FreeName(string wanted)
    {
        wanted = wanted.Trim();
        if (!Exists(wanted))
            return wanted;
        // Leave room for the number, so a long name's folder never truncates back onto the taken one.
        string stem = wanted.Length > MaxName - 5 ? wanted[..(MaxName - 5)].TrimEnd() : wanted;
        for (int n = 2; n < 10_000; n++)
        {
            string candidate = $"{stem} {n}";
            if (!Exists(candidate))
                return candidate;
        }
        return $"{stem} {DateTime.UtcNow.Ticks % 100_000}";
    }

    /// <summary>The company's name as its books give it, or its folder's name when they cannot be read.</summary>
    public static string NameOf(string dir)
    {
        try
        {
            return SaveStore.Header(dir)?.Company ?? Path.GetFileName(dir);
        }
        catch (Exception)
        {
            return Path.GetFileName(dir);
        }
    }

    /// <summary>Plays (and saves) under this name from now on.</summary>
    public void Adopt(string name)
    {
        Name = name.Trim().Length == 0 ? DefaultName : name.Trim();
        Dir = SaveStore.CompanyDir(Root, Name);
        RecoveredFrom = null;
    }

    /// <summary>
    /// A new company: adopts the name, or the first free name after it, so a new company never files under a folder
    /// another company already keeps (its saves would roll that company's books away).
    /// </summary>
    public void AdoptNew(string name)
    {
        Adopt(FreeName(Valid(name) ? name : DefaultName));
        // Belt and braces: the store never hands a new company a folder another company saves in.
        Dir = SaveStore.NewCompanyDir(Root, Name!);
    }

    public void Forget()
    {
        Name = null;
        Dir = null;
        RecoveredFrom = null;
    }

    /// <summary>Loads a company folder, falling back to its backups; remembers where it came from.</summary>
    public World Load(string dir)
    {
        var (world, source) = SaveStore.Load(dir);
        Dir = dir;
        Name = NameOf(dir);
        RecoveredFrom = source == SaveStore.FileName ? null : source;
        LastSavedTick = world.TickCount;
        return world;
    }

    /// <summary>Writes the save now if this is a real company and something has happened since the last one.</summary>
    public bool Save(World world)
    {
        Flush();
        if (Dir == null || Name == null || world.TickCount == LastSavedTick)
            return false;
        SaveStore.Save(Dir, world, Name);
        LastSavedTick = world.TickCount;
        Saves++;
        return true;
    }

    /// <summary>
    /// The daily autosave: the world is captured now, and compressed and written on a worker thread so a large
    /// works never stalls a frame. One write at a time; <see cref="Save"/> and <see cref="Flush"/> wait for it.
    /// </summary>
    public bool SaveInBackground(World world)
    {
        if (Dir == null || Name == null || world.TickCount == LastSavedTick)
            return false;
        Flush();
        var snapshot = SaveGame.Capture(world, Name);
        string dir = Dir;
        writing = Task.Run(() => SaveStore.Save(dir, snapshot));
        LastSavedTick = world.TickCount;
        Saves++;
        return true;
    }

    Task? writing;

    /// <summary>Waits for a background write to land; a failed one is reported (and the next save tries again).</summary>
    public void Flush()
    {
        if (writing == null)
            return;
        try
        {
            writing.Wait();
        }
        catch (AggregateException e)
        {
            Failed?.Invoke(e.InnerException?.Message ?? e.Message);
            LastSavedTick = -1;
        }
        writing = null;
    }

    /// <summary>A save could not be written (disk full, folder gone): the reason, for the masthead.</summary>
    public event Action<string>? Failed;
}
