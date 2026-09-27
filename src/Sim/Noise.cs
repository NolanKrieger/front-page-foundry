namespace FrontPageFoundry.Sim;

/// <summary>2D simplex noise (Gustavson's public-domain formulation) with a seeded permutation. Deterministic per seed.</summary>
public sealed class Simplex
{
    static readonly int[][] Grad =
    {
        new[] { 1, 1 }, new[] { -1, 1 }, new[] { 1, -1 }, new[] { -1, -1 },
        new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 },
    };
    const double F2 = 0.36602540378;   // (sqrt(3)-1)/2
    const double G2 = 0.21132486540;   // (3-sqrt(3))/6

    readonly int[] perm = new int[512];

    public Simplex(int seed)
    {
        var p = new int[256];
        for (int i = 0; i < 256; i++)
            p[i] = i;
        var rng = new Pcg32((ulong)seed, 5);
        for (int i = 255; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++)
            perm[i] = p[i & 255];
    }

    /// <summary>Noise in roughly [-1, 1].</summary>
    public double At(double x, double y)
    {
        double s = (x + y) * F2;
        int i = (int)Math.Floor(x + s), j = (int)Math.Floor(y + s);
        double t = (i + j) * G2;
        double x0 = x - (i - t), y0 = y - (j - t);
        int i1 = x0 > y0 ? 1 : 0, j1 = x0 > y0 ? 0 : 1;
        double x1 = x0 - i1 + G2, y1 = y0 - j1 + G2;
        double x2 = x0 - 1 + 2 * G2, y2 = y0 - 1 + 2 * G2;
        int ii = i & 255, jj = j & 255;
        int g0 = perm[ii + perm[jj]] & 7, g1 = perm[ii + i1 + perm[jj + j1]] & 7, g2 = perm[ii + 1 + perm[jj + 1]] & 7;
        double n = 0;
        double t0 = 0.5 - x0 * x0 - y0 * y0;
        if (t0 > 0) { t0 *= t0; n += t0 * t0 * (Grad[g0][0] * x0 + Grad[g0][1] * y0); }
        double t1 = 0.5 - x1 * x1 - y1 * y1;
        if (t1 > 0) { t1 *= t1; n += t1 * t1 * (Grad[g1][0] * x1 + Grad[g1][1] * y1); }
        double t2 = 0.5 - x2 * x2 - y2 * y2;
        if (t2 > 0) { t2 *= t2; n += t2 * t2 * (Grad[g2][0] * x2 + Grad[g2][1] * y2); }
        return 70.0 * n;
    }

    /// <summary>Two octaves, the second at half amplitude and double frequency.</summary>
    public double Fbm2(double x, double y) => (At(x, y) + 0.5 * At(x * 2 + 31.7, y * 2 - 17.3)) / 1.5;
}
