namespace FrontPageFoundry.Sim;

/// <summary>
/// One save per company, always the latest (GDD §13). Writes are atomic (temp file → flush to disk →
/// rename) and two rolling backups exist only so a damaged latest file recovers by itself; the player
/// never picks an older state.
/// </summary>
public static class SaveStore
{
    public const string FileName = "save.bin";
    public const string Backup1 = "save.bak1";
    public const string Backup2 = "save.bak2";
    static readonly string[] Candidates = { FileName, Backup1, Backup2 };

    /// <summary>A folder name from a company name: lower-case letters, digits and dashes.</summary>
    public static string Slug(string company)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in company.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if ((ch == ' ' || ch == '-' || ch == '_') && sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        string slug = sb.ToString().Trim('-');
        if (slug.Length > 40)
            slug = slug[..40].Trim('-');
        if (slug.Length == 0)
            return "company";
        // Windows keeps these names for devices: a folder called "con" or "lpt1" cannot be made there.
        return Reserved.Contains(slug) ? slug + "-co" : slug;
    }

    static readonly HashSet<string> Reserved = new(new[] { "con", "prn", "aux", "nul" }
        .Concat(Enumerable.Range(1, 9).SelectMany(n => new[] { $"com{n}", $"lpt{n}" })));

    public static string CompanyDir(string root, string company) => Path.Combine(root, Slug(company));

    /// <summary>
    /// The folder for a newly founded company: its name's folder, or that name with -2, -3 … when another
    /// company already saves there. Two names can share a folder ("Acme Co." and "acme co", or any two
    /// names of punctuation alone), and the founding page offers the default name again, so founding must
    /// never write into an existing company's folder: its save would roll to the backups and be gone.
    /// </summary>
    public static string NewCompanyDir(string root, string company)
    {
        string slug = Slug(company);
        for (int n = 1; ; n++)
        {
            string dir = Path.Combine(root, n == 1 ? slug : $"{slug}-{n}");
            if (!Candidates.Any(name => File.Exists(Path.Combine(dir, name))))
                return dir;
        }
    }

    /// <summary>A file that cannot be read as a save: damaged, cut short, foreign, or not ours to open.</summary>
    static bool Unreadable(Exception e) => e is InvalidDataException or IOException or UnauthorizedAccessException;

    /// <summary>Saves the world under the company's folder, keeping the two previous files as backups.</summary>
    public static void Save(string dir, World world, string company) => Save(dir, SaveGame.Capture(world, company));

    /// <summary>Writes a captured world under the company's folder (any thread; never two at once for one folder).</summary>
    public static void Save(string dir, SaveSnapshot snapshot)
    {
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, "save.tmp");
        using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            SaveGame.Write(snapshot, f);
            f.Flush(true);
        }
        string latest = Path.Combine(dir, FileName), bak1 = Path.Combine(dir, Backup1), bak2 = Path.Combine(dir, Backup2);
        if (File.Exists(bak1))
            File.Move(bak1, bak2, overwrite: true);
        if (File.Exists(latest))
            File.Move(latest, bak1, overwrite: true);
        File.Move(tmp, latest, overwrite: true);
    }

    /// <summary>The latest readable save: the current file, else the newest backup that still reads. Says which.</summary>
    public static (World World, string Source) Load(string dir)
    {
        Exception? last = null;
        foreach (var name in Candidates)
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path))
                continue;
            try
            {
                using var f = File.OpenRead(path);
                return (SaveGame.Read(f), name);
            }
            catch (Exception e) when (Unreadable(e))
            {
                last = e;
            }
        }
        throw new InvalidDataException("no readable save in " + dir, last);
    }

    /// <summary>The header of the latest readable file in a folder, or null.</summary>
    public static SaveHeader? Header(string dir)
    {
        foreach (var name in Candidates)
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path))
                continue;
            try
            {
                using var f = File.OpenRead(path);
                return SaveGame.ReadHeader(f);
            }
            catch (Exception e) when (Unreadable(e))
            {
            }
        }
        return null;
    }

    /// <summary>Every company folder under the root with a readable header, newest save first.</summary>
    public static List<(string Dir, SaveHeader Header)> List(string root)
    {
        var found = new List<(string, SaveHeader)>();
        if (!Directory.Exists(root))
            return found;
        foreach (var dir in Directory.GetDirectories(root))
            if (Header(dir) is { } header)
                found.Add((dir, header));
        found.Sort((a, b) => string.CompareOrdinal(b.Item2.SavedAt, a.Item2.SavedAt));
        return found;
    }
}
