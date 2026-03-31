/// <summary>
/// Seeded 2D Simplex noise utility.
/// Based on Stefan Gustavson's public-domain implementation.
///
/// Usage:
///   SimplexNoise.SetSeed(42);
///   float v = SimplexNoise.Noise(x, y);  // returns [0, 1]
/// </summary>
public static class SimplexNoise
{
    // Gradient vectors for 2D (12 directions, z ignored)
    private static readonly int[] _grad3 =
    {
         1, 1, 0,  -1, 1, 0,   1,-1, 0,  -1,-1, 0,
         1, 0, 1,  -1, 0, 1,   1, 0,-1,  -1, 0,-1,
         0, 1, 1,   0,-1, 1,   0, 1,-1,   0,-1,-1
    };

    private static readonly int[] _perm      = new int[512];
    private static readonly int[] _permMod12 = new int[512];

    static SimplexNoise() => SetSeed(0);

    /// <summary>Re-seeds the noise. Call once before generating terrain.</summary>
    public static void SetSeed(int seed)
    {
        // Build a shuffled permutation table from the seed.
        var p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;

        var rng = new System.Random(seed);
        for (int i = 255; i > 0; i--)
        {
            int j   = rng.Next(i + 1);
            int tmp = p[i]; p[i] = p[j]; p[j] = tmp;
        }

        for (int i = 0; i < 512; i++)
        {
            _perm[i]      = p[i & 255];
            _permMod12[i] = _perm[i] % 12;
        }
    }

    /// <summary>
    /// Returns a Simplex noise value for the given 2D coordinates.
    /// Output is remapped to approximately [0, 1].
    /// </summary>
    public static float Noise(float xin, float yin)
    {
        const float F2 = 0.36602540378f; // (sqrt(3) - 1) / 2
        const float G2 = 0.21132486541f; // (3 - sqrt(3)) / 6

        // Skew input to find simplex cell
        float s = (xin + yin) * F2;
        int   i = FastFloor(xin + s);
        int   j = FastFloor(yin + s);

        float t  = (i + j) * G2;
        float x0 = xin - (i - t);
        float y0 = yin - (j - t);

        // Determine which simplex triangle we're in
        int i1 = x0 > y0 ? 1 : 0;
        int j1 = x0 > y0 ? 0 : 1;

        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + 2f * G2;
        float y2 = y0 - 1f + 2f * G2;

        int ii = i & 255;
        int jj = j & 255;

        int gi0 = _permMod12[ii      + _perm[jj     ]];
        int gi1 = _permMod12[ii + i1 + _perm[jj + j1]];
        int gi2 = _permMod12[ii +  1 + _perm[jj +  1]];

        // Noise contributions from each corner
        float n0 = 0f, n1 = 0f, n2 = 0f;

        float t0 = 0.5f - x0 * x0 - y0 * y0;
        if (t0 >= 0f) { t0 *= t0; n0 = t0 * t0 * Dot(gi0, x0, y0); }

        float t1 = 0.5f - x1 * x1 - y1 * y1;
        if (t1 >= 0f) { t1 *= t1; n1 = t1 * t1 * Dot(gi1, x1, y1); }

        float t2 = 0.5f - x2 * x2 - y2 * y2;
        if (t2 >= 0f) { t2 *= t2; n2 = t2 * t2 * Dot(gi2, x2, y2); }

        // Scale to [0, 1]  (raw range is roughly [-1, 1], factor 70 normalises it)
        return 0.5f + 35f * (n0 + n1 + n2);
    }

    private static float Dot(int g, float x, float y)
        => _grad3[g * 3] * x + _grad3[g * 3 + 1] * y;

    private static int FastFloor(float x)
        => x > 0f ? (int)x : (int)x - 1;
}
