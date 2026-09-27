using System.Text;
using FrontPageFoundry.Sim;
using Xunit.Abstractions;

namespace Sim.Tests;

/// <summary>Audit (haulage-saves): the company folders survive damaged, truncated and foreign files.</summary>
public class AuditStoreTests : IDisposable
{
    readonly ITestOutputHelper output;
    readonly string root = Path.Combine(Path.GetTempPath(), "fpf-audit-store-" + Guid.NewGuid().ToString("N"));

    public AuditStoreTests(ITestOutputHelper output) => this.output = output;

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    /// <summary>A company folder with two saves: the latest and one backup.</summary>
    (string Dir, long FirstTick, long SecondTick) TwoSaves(string company = "Audit Works")
    {
        var w = Rig.StarterLine(out _);
        string dir = SaveStore.CompanyDir(root, company);
        w.Run(5);
        SaveStore.Save(dir, w, company);
        long first = w.TickCount;
        w.Run(5);
        SaveStore.Save(dir, w, company);
        return (dir, first, w.TickCount);
    }

    static byte[] SaveBytes(World w, string company = "Audit Works")
    {
        using var ms = new MemoryStream();
        SaveGame.Write(w, ms, company);
        return ms.ToArray();
    }

    public static IEnumerable<object[]> Damage()
    {
        // (name, how the latest file is damaged)
        yield return new object[] { "empty" };
        yield return new object[] { "magic only" };
        yield return new object[] { "cut in the header" };
        yield return new object[] { "cut in the body" };
        yield return new object[] { "negative header length" };
        yield return new object[] { "huge header length" };
        yield return new object[] { "header not json" };
        yield return new object[] { "header with a strange map mode" };
        yield return new object[] { "negative body length" };
    }

    static byte[] Damaged(byte[] good, string how)
    {
        int headerLen = BitConverter.ToInt32(good, 8);
        switch (how)
        {
            case "empty": return Array.Empty<byte>();
            case "magic only": return good[..4];
            case "cut in the header": return good[..(12 + headerLen / 2)];
            case "cut in the body": return good[..(good.Length - 40)];
            case "negative header length":
            {
                var b = (byte[])good.Clone();
                BitConverter.GetBytes(-7).CopyTo(b, 8);
                return b;
            }
            case "huge header length":
            {
                var b = (byte[])good.Clone();
                BitConverter.GetBytes(int.MaxValue).CopyTo(b, 8);
                return b;
            }
            case "header not json":
            {
                var b = (byte[])good.Clone();
                for (int i = 12; i < 12 + headerLen; i++)
                    b[i] = (byte)'#';
                return b;
            }
            case "header with a strange map mode":
            {
                string json = Encoding.UTF8.GetString(good, 12, headerLen);
                string bad = json.Replace("\"Mode\":\"Full\"", "\"Mode\":\"Moon\"");
                Assert.NotEqual(json, bad);
                var head = Encoding.UTF8.GetBytes(bad);
                var ms = new MemoryStream();
                ms.Write(good, 0, 8);
                ms.Write(BitConverter.GetBytes(head.Length));
                ms.Write(head);
                ms.Write(good, 12 + headerLen, good.Length - 12 - headerLen);
                return ms.ToArray();
            }
            case "negative body length":
            {
                var b = (byte[])good.Clone();
                BitConverter.GetBytes(-3).CopyTo(b, 12 + headerLen);
                return b;
            }
        }
        throw new ArgumentException(how);
    }

    [Theory]
    [MemberData(nameof(Damage))]
    public void ADamagedLatestFallsBackToTheBackupAndTheListSurvives(string how)
    {
        var (dir, first, _) = TwoSaves();
        string latest = Path.Combine(dir, SaveStore.FileName);
        File.WriteAllBytes(latest, Damaged(File.ReadAllBytes(latest), how));
        var (world, source) = SaveStore.Load(dir);
        Assert.Equal(SaveStore.Backup1, source);
        Assert.Equal(first, world.TickCount);
        var list = SaveStore.List(root);
        Assert.Single(list);
        Assert.Equal("Audit Works", list[0].Header.Company);
    }

    [Fact]
    public void FoundingACompanyNeverWritesIntoAnotherCompanysFolder()
    {
        // Nolan's company, under the name the founding page offers by default.
        var first = Rig.StarterLine(out _);
        first.Run(5);
        string firstDir = SaveStore.NewCompanyDir(root, "Carvell Falls Works");
        SaveStore.Save(firstDir, first, "Carvell Falls Works");
        long firstTick = first.TickCount;
        // A second company founded under the same name, and one whose name shares its folder name.
        foreach (var name in new[] { "Carvell Falls Works", "carvell falls works!" })
        {
            var second = Rig.Fresh(9);
            string dir = SaveStore.NewCompanyDir(root, name);
            Assert.NotEqual(firstDir, dir);
            for (int k = 0; k < 3; k++)
            {
                second.Run(1);
                SaveStore.Save(dir, second, name);
            }
        }
        var (loaded, source) = SaveStore.Load(firstDir);
        Assert.Equal(SaveStore.FileName, source);
        Assert.Equal(firstTick, loaded.TickCount);
        Assert.Equal(3, SaveStore.List(root).Count);

        // What game/Companies.Adopt does today (CompanyDir): the two names share a folder and three
        // autosaves of the new company leave nothing of the old one, backups included.
        string old = Path.Combine(root, "old-way");
        SaveStore.Save(SaveStore.CompanyDir(old, "Carvell Falls Works"), first, "Carvell Falls Works");
        Assert.Equal(SaveStore.CompanyDir(old, "Carvell Falls Works"), SaveStore.CompanyDir(old, "carvell falls works!"));
        var intruder = Rig.Fresh(9);
        for (int k = 0; k < 3; k++)
        {
            intruder.Run(1);
            SaveStore.Save(SaveStore.CompanyDir(old, "carvell falls works!"), intruder, "carvell falls works!");
        }
        string shared = SaveStore.CompanyDir(old, "Carvell Falls Works");
        foreach (var file in new[] { SaveStore.FileName, SaveStore.Backup1, SaveStore.Backup2 })
        {
            using var f = File.OpenRead(Path.Combine(shared, file));
            Assert.Equal("carvell falls works!", SaveGame.ReadHeader(f).Company);
        }
        // Punctuation alone and a Windows device name still make distinct, usable folders.
        Assert.Equal("company", SaveStore.Slug("?!?"));
        Assert.NotEqual(SaveStore.NewCompanyDir(root, "!!!"), firstDir);
        Assert.Equal("con-co", SaveStore.Slug("Con"));
        Assert.Equal("ünïcode-wörks", SaveStore.Slug("Ünïcode Wörks"));
    }
}
