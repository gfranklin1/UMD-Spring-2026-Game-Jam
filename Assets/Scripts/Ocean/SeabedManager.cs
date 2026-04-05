using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using SysRandom = System.Random;

public enum DepthZone { Shallow, Mid, Deep }

public struct SeabedLootPoint
{
    public Vector3   WorldPosition;
    public DepthZone Zone;
    public float     Depth;      // metres below surface (positive)
    public int       SiteIndex;
    public int       PointIndex;
}

public struct DiveSite
{
    public Vector3           CenterWorldPos;
    public float             CenterDepth;     // metres below surface at site centre
    public DepthZone         CenterZone;
    public SeabedLootPoint[] LootPoints;
    public int               SiteIndex;
}

/// <summary>
/// Procedural ocean-floor system.
///  • Streams 40 m terrain chunks around the ship as it sails.
///  • Scatters dive sites (~500 m apart) across the infinite world.
///  • Registers loot-spawn points per site for future use by a LootSpawner.
///
/// Place this component on a root GameObject at world origin.
/// Assign <see cref="shipTransform"/> in the inspector (falls back to
/// GameObject.Find("Ship") at startup).
/// </summary>
public class SeabedManager : MonoBehaviour
{
    public static SeabedManager Instance { get; private set; }

    // ── Chunk settings ──────────────────────────────────────────────
    [Header("Chunks")]
    [Tooltip("Quads per side per chunk")]
    [SerializeField] private int       chunkResolution = 20;
    [Tooltip("World metres per chunk side")]
    [SerializeField] private float     chunkSize       = 40f;
    [Tooltip("Chunks loaded in each direction from ship")]
    [SerializeField] private int       loadRadius      = 4;
    [Tooltip("Transform used as the streaming center (usually the Ship)")]
    [SerializeField] private Transform shipTransform;

    // ── Noise ───────────────────────────────────────────────────────
    [Header("Noise")]
    [Tooltip("0 = random each session; set manually to fix terrain")]
    [SerializeField] private int   seed          = 0;
    [Tooltip("Primary large-scale biome frequency (lower = bigger features)")]
    [SerializeField] private float noiseScale    = 0.0018f;
    [Tooltip("Secondary detail frequency (keep well below noiseScale to avoid wrinkles)")]
    [SerializeField] private float noiseScale2   = 0.005f;
    [Tooltip("Weight of primary noise octave")]
    [SerializeField] private float octave1Weight = 0.88f;
    [Tooltip("Weight of secondary noise octave")]
    [SerializeField] private float octave2Weight = 0.12f;

    [Header("Domain Warp")]
    [Tooltip("Frequency of the warp noise (breaks up round dimple shapes)")]
    [SerializeField] private float warpScale    = 0.003f;
    [Tooltip("How far (metres) to distort sample coordinates — higher = more organic shapes")]
    [SerializeField] private float warpStrength = 50f;

    // ── Depth ───────────────────────────────────────────────────────
    [Header("Depth")]
    [Tooltip("Must match OceanWaves transform Y")]
    [SerializeField] private float surfaceY      = 0f;
    [Tooltip("Absolute shallowest floor (metres)")]
    [SerializeField] private float minFloorDepth = 8f;
    [Tooltip("Total depth range for base terrain (min to min+range before canyons)")]
    [SerializeField] private float shallowRange  = 45f;

    [Header("Terracing")]
    [Tooltip("Number of distinct depth plateaus across the shallow range")]
    [SerializeField] private int   terraceSteps = 3;
    [Tooltip("0 = smooth hills, 1 = hard stepped plateaus")]
    [SerializeField] private float terraceBlend = 0.4f;

    [Header("Canyons")]
    [Tooltip("Very low frequency for large trench features — match noiseScale for continent-scale rifts")]
    [SerializeField] private float canyonScale     = 0.003f;
    [Tooltip("Noise value above which canyon starts (lower = more common, wider rifts)")]
    [SerializeField] private float canyonThreshold = 0.55f;
    [Tooltip("Max extra metres in the deepest canyon centre (total depth = minFloor+shallowRange+this)")]
    [SerializeField] private float canyonMaxDepth  = 80f;

    // ── Dive sites ──────────────────────────────────────────────────
    [Header("Dive Sites")]
    [Tooltip("Approx metres between sites")]
    [SerializeField] private float nominalSiteSpacing = 500f;
    [Tooltip("Number of loot points generated per site")]
    [SerializeField] private int   lootPointsPerSite  = 6;
    [Tooltip("XZ radius around site centre")]
    [SerializeField] private float lootPointRadius    = 15f;
    [Tooltip("Depth less than or equal to this is Shallow")]
    [SerializeField] private float shallowMaxDepth    = 12f;
    [Tooltip("Depth less than or equal to this is Mid; otherwise Deep (canyon)")]
    [SerializeField] private float midMaxDepth        = 30f;

    // ── Rendering ───────────────────────────────────────────────────
    [Header("Rendering")]
    [Tooltip("Material assigned to generated seabed chunk renderers")]
    [SerializeField] private Material seabedMaterial;

    // ── Runtime state ───────────────────────────────────────────────
    private readonly Dictionary<Vector2Int, GameObject> _loadedChunks = new();
    private readonly Dictionary<Vector2Int, DiveSite>   _knownSites   = new();
    private readonly List<DiveSite>                     _allSites     = new();
    private Vector2Int _lastShipChunk = new Vector2Int(int.MinValue, int.MinValue);
    private bool _generated; // true once chunks have been generated for the first time

    /// <summary>True once the seabed seed is resolved and the first chunk pass is complete.</summary>
    public bool IsReady { get; private set; }

    /// <summary>World metres per chunk side.</summary>
    public float ChunkSize => chunkSize;
    /// <summary>The resolved terrain seed — use for deterministic per-chunk RNG.</summary>
    public int Seed => seed;

    /// <summary>Fired each time a new terrain chunk is created.</summary>
    public event System.Action<Vector2Int, GameObject> OnChunkCreated;
    /// <summary>Fired just before a terrain chunk is destroyed.</summary>
    public event System.Action<Vector2Int> OnChunkUnloaded;
    /// <summary>Fired when the entire seabed is torn down and regenerated (seed change).</summary>
    public event System.Action OnSeabedReset;

    // ════════════════════════════════════════════════════════════════
    // Lifecycle
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (shipTransform == null)
        {
            var shipGO = GameObject.Find("Ship");
            if (shipGO != null) shipTransform = shipGO.transform;
            else Debug.LogWarning("[SeabedManager] shipTransform not assigned and 'Ship' not found.");
        }

        if (QuotaManager.Instance != null)
            QuotaManager.Instance.SeabedSeed.OnValueChanged += OnSeedChanged;

        // Try to initialise immediately if QuotaManager already has the seed.
        // If not yet available (client connecting before QuotaManager spawns), Update() polls.
        TryInitialize();
    }

    private void OnDestroy()
    {
        if (QuotaManager.Instance != null)
            QuotaManager.Instance.SeabedSeed.OnValueChanged -= OnSeedChanged;
    }

    private void OnSeedChanged(int oldSeed, int newSeed)
    {
        if (newSeed == 0 || newSeed == oldSeed) return;

        IsReady = false;

        // Notify subscribers that the seabed is being fully reset
        OnSeabedReset?.Invoke();

        // Destroy all existing chunks and sites, then re-generate with the new seed
        foreach (var chunk in _loadedChunks.Values)
            if (chunk != null) Destroy(chunk);
        _loadedChunks.Clear();
        _knownSites.Clear();
        _allSites.Clear();
        _lastShipChunk = new Vector2Int(int.MinValue, int.MinValue);

        seed       = 0; // clear inspector override so TryInitialize reads the new NetworkVariable seed
        _generated = false;
        // Update() will call TryInitialize() next frame to regenerate
    }

    private void Update()
    {
        if (!_generated)
        {
            TryInitialize();
            return;
        }
        if (shipTransform == null) return;
        Vector2Int current = WorldToChunkCoord(shipTransform.position);
        if (current != _lastShipChunk)
        {
            _lastShipChunk = current;
            UpdateChunks();
        }
    }

    private void TryInitialize()
    {
        int resolvedSeed = 0;

        if (seed != 0)
        {
            // Inspector override — always wins (useful for reproducible testing).
            resolvedSeed = seed;
        }
        else if (QuotaManager.Instance != null && QuotaManager.Instance.SeabedSeed.Value != 0)
        {
            // Normal networked path: server set SeabedSeed, now clients can read it.
            resolvedSeed = QuotaManager.Instance.SeabedSeed.Value;
        }
        else
        {
            // Offline / editor fallback: if NetworkManager isn't even listening,
            // pick a local random seed immediately so the floor always generates.
            bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (!networkActive)
                resolvedSeed = UnityEngine.Random.Range(1, int.MaxValue);
        }

        if (resolvedSeed == 0) return; // still waiting for networked seed

        seed = resolvedSeed;
        SimplexNoise.SetSeed(seed);
        _generated = true;
        UpdateChunks();
        IsReady = true;
    }

    // ════════════════════════════════════════════════════════════════
    // Chunk management
    // ════════════════════════════════════════════════════════════════

    private void UpdateChunks()
    {
        if (shipTransform == null) return;
        _lastShipChunk = WorldToChunkCoord(shipTransform.position);

        // Build the desired set of chunk coords
        var desired = new HashSet<Vector2Int>();
        for (int dx = -loadRadius; dx <= loadRadius; dx++)
        for (int dz = -loadRadius; dz <= loadRadius; dz++)
            desired.Add(new Vector2Int(_lastShipChunk.x + dx, _lastShipChunk.y + dz));

        // Unload chunks that left the radius
        foreach (var key in _loadedChunks.Keys.ToList())
        {
            if (!desired.Contains(key))
            {
                OnChunkUnloaded?.Invoke(key);
                Destroy(_loadedChunks[key]);
                _loadedChunks.Remove(key);
            }
        }

        // Load new chunks
        foreach (var coord in desired)
        {
            if (!_loadedChunks.ContainsKey(coord))
            {
                var chunk = GenerateChunk(coord);
                _loadedChunks[coord] = chunk;
                OnChunkCreated?.Invoke(coord, chunk);
            }
        }

        UpdateDiveSites();
    }

    private Vector2Int WorldToChunkCoord(Vector3 pos)
        => new Vector2Int(Mathf.FloorToInt(pos.x / chunkSize), Mathf.FloorToInt(pos.z / chunkSize));

    // ════════════════════════════════════════════════════════════════
    // Mesh generation
    // ════════════════════════════════════════════════════════════════

    private GameObject GenerateChunk(Vector2Int coord)
    {
        var go = new GameObject($"Chunk({coord.x},{coord.y})");
        go.transform.SetParent(transform);
        // Place the chunk GO at its world-space origin so vertices can be in local space
        go.transform.position = new Vector3(coord.x * chunkSize, 0f, coord.y * chunkSize);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = seabedMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        var mesh = BuildChunkMesh(coord);
        mf.mesh = mesh;

        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;

        return go;
    }

    private Mesh BuildChunkMesh(Vector2Int coord)
    {
        int   vertCount = chunkResolution * chunkResolution * 6;
        var   verts     = new Vector3[vertCount];
        var   tris      = new int[vertCount];
        var   uvs       = new Vector2[vertCount];
        var   colors    = new Color[vertCount];
        float step      = chunkSize / chunkResolution;
        float worldOX   = coord.x * chunkSize;  // world-space chunk origin X
        float worldOZ   = coord.y * chunkSize;  // world-space chunk origin Z
        int   vi        = 0;

        for (int z = 0; z < chunkResolution; z++)
        for (int x = 0; x < chunkResolution; x++)
        {
            // Local positions within the chunk
            float lx0 = x * step,       lx1 = lx0 + step;
            float lz0 = z * step,       lz1 = lz0 + step;

            // World positions (for noise sampling)
            float wx0 = worldOX + lx0, wx1 = worldOX + lx1;
            float wz0 = worldOZ + lz0, wz1 = worldOZ + lz1;

            float y00 = SampleFloorY(wx0, wz0);
            float y10 = SampleFloorY(wx1, wz0);
            float y01 = SampleFloorY(wx0, wz1);
            float y11 = SampleFloorY(wx1, wz1);

            // Triangle A: BL, TL, TR
            verts[vi] = new Vector3(lx0, y00, lz0); uvs[vi] = new Vector2(lx0 / chunkSize, lz0 / chunkSize); tris[vi] = vi; colors[vi] = DepthColor(wx0, wz0, y00); vi++;
            verts[vi] = new Vector3(lx0, y01, lz1); uvs[vi] = new Vector2(lx0 / chunkSize, lz1 / chunkSize); tris[vi] = vi; colors[vi] = DepthColor(wx0, wz1, y01); vi++;
            verts[vi] = new Vector3(lx1, y11, lz1); uvs[vi] = new Vector2(lx1 / chunkSize, lz1 / chunkSize); tris[vi] = vi; colors[vi] = DepthColor(wx1, wz1, y11); vi++;

            // Triangle B: BL, TR, BR
            verts[vi] = new Vector3(lx0, y00, lz0); uvs[vi] = new Vector2(lx0 / chunkSize, lz0 / chunkSize); tris[vi] = vi; colors[vi] = DepthColor(wx0, wz0, y00); vi++;
            verts[vi] = new Vector3(lx1, y11, lz1); uvs[vi] = new Vector2(lx1 / chunkSize, lz1 / chunkSize); tris[vi] = vi; colors[vi] = DepthColor(wx1, wz1, y11); vi++;
            verts[vi] = new Vector3(lx1, y10, lz0); uvs[vi] = new Vector2(lx1 / chunkSize, lz0 / chunkSize); tris[vi] = vi; colors[vi] = DepthColor(wx1, wz0, y10); vi++;
        }

        var mesh = new Mesh
        {
            name        = $"Seabed({coord.x},{coord.y})",
            indexFormat = IndexFormat.UInt32
        };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();   // flat normals because mesh is non-indexed
        mesh.RecalculateBounds();
        return mesh;
    }

    // Depth-to-colour gradient: shallow warm sand → deep dark rock/mud.
    // High-frequency noise breaks up the gradient so it reads as real sediment variation.
    private Color DepthColor(float wx, float wz, float worldY)
    {
        float depth    = surfaceY - worldY;   // positive = deeper
        float maxDepth = minFloorDepth + shallowRange + canyonMaxDepth;
        float t        = Mathf.Clamp01(depth / maxDepth);

        // Colour stops
        var sand     = new Color(0.83f, 0.73f, 0.52f);   // warm light sand (shallow)
        var sediment = new Color(0.60f, 0.50f, 0.34f);   // darker sandy sediment
        var mud      = new Color(0.34f, 0.28f, 0.20f);   // dark silt / mud
        var rock     = new Color(0.16f, 0.14f, 0.12f);   // near-black deep rock

        Color baseColor;
        if      (t < 0.25f) baseColor = Color.Lerp(sand,     sediment, t / 0.25f);
        else if (t < 0.55f) baseColor = Color.Lerp(sediment, mud,      (t - 0.25f) / 0.30f);
        else                baseColor = Color.Lerp(mud,       rock,     (t - 0.55f) / 0.45f);

        // Medium-frequency noise — patchy sand texture (distinct from terrain noise)
        float n1 = SimplexNoise.Noise(wx * 0.035f + 300f, wz * 0.035f + 300f);
        // Fine-frequency noise — grain-level variation
        float n2 = SimplexNoise.Noise(wx * 0.12f  + 600f, wz * 0.12f  + 600f);

        float variation = (n1 - 0.5f) * 0.14f + (n2 - 0.5f) * 0.06f;

        return new Color(
            Mathf.Clamp01(baseColor.r + variation),
            Mathf.Clamp01(baseColor.g + variation * 0.85f),
            Mathf.Clamp01(baseColor.b + variation * 0.65f),
            1f
        );
    }

    // ════════════════════════════════════════════════════════════════
    // Noise / height sampling
    // ════════════════════════════════════════════════════════════════

    /// <summary>Returns the world-space Y of the seabed floor at (wx, wz).</summary>
    public float GetFloorY(float wx, float wz) => SampleFloorY(wx, wz);

    private float SampleFloorY(float wx, float wz)
    {
        // ── Domain warp: distort sample coordinates for organic, elongated shapes ──
        // Two noise samples offset from each other give independent X and Z warp vectors.
        float dwx = (SimplexNoise.Noise(wx * warpScale + 100f, wz * warpScale       ) - 0.5f) * 2f;
        float dwz = (SimplexNoise.Noise(wx * warpScale,        wz * warpScale + 100f) - 0.5f) * 2f;
        float wwx = wx + dwx * warpStrength;
        float wwz = wz + dwz * warpStrength;

        // ── Base terrain: two low-frequency octaves on warped coordinates ──
        float n1  = SimplexNoise.Noise(wwx * noiseScale,  wwz * noiseScale);
        float n2  = SimplexNoise.Noise(wwx * noiseScale2, wwz * noiseScale2);
        float raw = Mathf.Clamp01(n1 * octave1Weight + n2 * octave2Weight);

        // ── Terracing: creates distinct plateau levels (Subnautica biome feel) ──
        // Quantise to N steps, then blend between raw and stepped.
        float steps   = Mathf.Max(1f, terraceSteps);
        float stepped = Mathf.Round(raw * steps) / steps;
        float terrain = Mathf.Lerp(raw, stepped, terraceBlend);

        float baseDepth = minFloorDepth + terrain * shallowRange;

        // ── Canyon layer: large trench features, decorrelated from base ──
        // Quadratic falloff (vs cubic) gives wider canyon slopes — more area is actually deep.
        float nc           = SimplexNoise.Noise(wwx * canyonScale + 500f, wwz * canyonScale + 500f);
        float canyonFactor = Mathf.Max(0f, (nc - canyonThreshold) / (1f - canyonThreshold));
        float extraDepth   = canyonFactor * canyonFactor * canyonMaxDepth;

        return surfaceY - (baseDepth + extraDepth);
    }

    // ════════════════════════════════════════════════════════════════
    // Dive site discovery
    // ════════════════════════════════════════════════════════════════

    private void UpdateDiveSites()
    {
        float discoverRange  = loadRadius * chunkSize + nominalSiteSpacing;
        int   siteLoadRadius = Mathf.CeilToInt(discoverRange / nominalSiteSpacing);

        var shipSiteCell = new Vector2Int(
            Mathf.FloorToInt(shipTransform.position.x / nominalSiteSpacing),
            Mathf.FloorToInt(shipTransform.position.z / nominalSiteSpacing));

        for (int dx = -siteLoadRadius; dx <= siteLoadRadius; dx++)
        for (int dz = -siteLoadRadius; dz <= siteLoadRadius; dz++)
        {
            var cell = new Vector2Int(shipSiteCell.x + dx, shipSiteCell.y + dz);
            if (!_knownSites.ContainsKey(cell))
                DiscoverSite(cell);
        }
    }

    private void DiscoverSite(Vector2Int cell)
    {
        var   rng = new SysRandom(CellHash(seed, cell.x, cell.y));
        float wx  = (cell.x + NextFloat(rng, 0.15f, 0.85f)) * nominalSiteSpacing;
        float wz  = (cell.y + NextFloat(rng, 0.15f, 0.85f)) * nominalSiteSpacing;
        float wy  = SampleFloorY(wx, wz);
        float dep = surfaceY - wy;

        int siteIdx = _allSites.Count;
        var site = new DiveSite
        {
            CenterWorldPos = new Vector3(wx, wy, wz),
            CenterDepth    = dep,
            CenterZone     = ClassifyDepth(dep),
            LootPoints     = GenerateLootPoints(wx, wz, rng, siteIdx),
            SiteIndex      = siteIdx
        };

        _knownSites[cell] = site;
        _allSites.Add(site);
    }

    private SeabedLootPoint[] GenerateLootPoints(float cx, float cz, SysRandom rng, int siteIdx)
    {
        var points      = new List<SeabedLootPoint>(lootPointsPerSite);
        int attempts    = 0;
        int maxAttempts = lootPointsPerSite * 30;

        while (points.Count < lootPointsPerSite && attempts++ < maxAttempts)
        {
            float px = cx + NextFloat(rng, -lootPointRadius, lootPointRadius);
            float pz = cz + NextFloat(rng, -lootPointRadius, lootPointRadius);

            // Must be within circular radius
            if (new Vector2(px - cx, pz - cz).magnitude > lootPointRadius) continue;
            // Must not be too close to another point in this site
            if (TooClose2D(points, px, pz, 3f)) continue;

            float py  = SampleFloorY(px, pz);
            float dep = surfaceY - py;

            points.Add(new SeabedLootPoint
            {
                WorldPosition = new Vector3(px, py, pz),
                Zone          = ClassifyDepth(dep),
                Depth         = dep,
                SiteIndex     = siteIdx,
                PointIndex    = points.Count
            });
        }

        return points.ToArray();
    }

    // ════════════════════════════════════════════════════════════════
    // Public query API
    // ════════════════════════════════════════════════════════════════

    /// <summary>All dive sites discovered so far this session.</summary>
    public IReadOnlyList<DiveSite> GetAllSites() => _allSites;

    /// <summary>All sites whose centre falls in the given depth zone.</summary>
    public IEnumerable<DiveSite> GetSitesByZone(DepthZone zone)
        => _allSites.Where(s => s.CenterZone == zone);

    /// <summary>
    /// All individual loot spawn points across every discovered site,
    /// optionally filtered by zone.
    /// </summary>
    public IEnumerable<SeabedLootPoint> GetLootPoints(DepthZone? zone = null)
    {
        foreach (var site in _allSites)
            foreach (var p in site.LootPoints)
                if (zone == null || p.Zone == zone)
                    yield return p;
    }

    /// <summary>Nearest discovered site to <paramref name="worldPos"/> (XZ only).</summary>
    public bool TryGetNearestSite(Vector3 worldPos, out DiveSite site)
    {
        site = default;
        if (_allSites.Count == 0) return false;

        float bestSq = float.MaxValue;
        foreach (var s in _allSites)
        {
            float dx = worldPos.x - s.CenterWorldPos.x;
            float dz = worldPos.z - s.CenterWorldPos.z;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq) { bestSq = sq; site = s; }
        }
        return true;
    }

    /// <summary>Look up a site by its stable <see cref="DiveSite.SiteIndex"/>.</summary>
    public bool TryGetSite(int siteIndex, out DiveSite site)
    {
        if (siteIndex >= 0 && siteIndex < _allSites.Count)
        {
            site = _allSites[siteIndex];
            return true;
        }
        site = default;
        return false;
    }

    // ════════════════════════════════════════════════════════════════
    // Private helpers
    // ════════════════════════════════════════════════════════════════

    private DepthZone ClassifyDepth(float depth)
    {
        if (depth <= shallowMaxDepth) return DepthZone.Shallow;
        if (depth <= midMaxDepth)     return DepthZone.Mid;
        return DepthZone.Deep;
    }

    private static bool TooClose2D(List<SeabedLootPoint> points, float x, float z, float minDist)
    {
        float minSq = minDist * minDist;
        foreach (var p in points)
        {
            float dx = x - p.WorldPosition.x;
            float dz = z - p.WorldPosition.z;
            if (dx * dx + dz * dz < minSq) return true;
        }
        return false;
    }

    /// <summary>Deterministic integer hash of a seed + 2D cell coordinate.</summary>
    private static int CellHash(int s, int x, int z)
    {
        unchecked
        {
            int h = s;
            h = h * 1664525 + x * 1013904223;
            h = h * 1664525 + z * 1013904223;
            return h;
        }
    }

    private static float NextFloat(SysRandom r, float min, float max)
        => (float)(r.NextDouble() * (max - min) + min);
}
