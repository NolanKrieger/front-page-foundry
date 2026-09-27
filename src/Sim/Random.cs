namespace FrontPageFoundry.Sim;

/// <summary>PCG32 (O'Neill): small, fast, seedable, one stream per system so systems never steal each other's numbers (GDD §14).</summary>
public sealed partial class Pcg32
{
    ulong state;
    readonly ulong inc;

    public Pcg32(ulong seed, ulong stream)
    {
        inc = (stream << 1) | 1;
        state = 0;
        Next();
        state += seed;
        Next();
    }

    public uint Next()
    {
        ulong old = state;
        state = old * 6364136223846793005UL + inc;
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    /// <summary>Uniform integer in [0, n).</summary>
    public int NextInt(int n) => (int)(Next() % (uint)n);

    /// <summary>Uniform integer in [-range, range].</summary>
    public int NextSigned(int range) => NextInt(2 * range + 1) - range;

    /// <summary>True with probability num/den.</summary>
    public bool Chance(int num, int den) => NextInt(den) < num;

    internal ulong State => state;
}
