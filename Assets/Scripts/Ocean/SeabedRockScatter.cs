using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SysRandom = System.Random;

/// <summary>
/// Scatters rock props on each terrain chunk as SeabedManager generates it.
/// Rocks are parented to the chunk GameObject so they are automatically destroyed
/// when the chunk unloads as the ship sails away.
///
/// Setup:
///  1. Add this component to any GameObject in SampleScene.
///  2. Drag all 11 SM_Rocks_XX prefabs from Assets/PolyOne/Rocks Stylized/Prefabs/
///     into the Rock Prefabs array in the Inspector.
///  3. (Optional) Enable GPU Instancing on Assets/PolyOne/Rocks Stylized/Materials/Rocks_Stylized_M.mat
///     for best draw-call performance.
/// </summary>
public class SeabedRockScatter : MonoBehaviour
{
    [Header("Rock Prefabs")]
    [Tooltip("Assign the SM_Rocks_XX prefabs from Assets/PolyOne/Rocks Stylized/Prefabs/.")]
    [SerializeField] private GameObject[] rockPrefabs;

    [Header("Density")]
    [Tooltip("Maximum rocks placed per 40 m chunk. Actual count is lower in sparse (Perlin) areas.")]
    [SerializeField] private int rocksPerChunk = 20;
    [Tooltip("Perlin noise threshold for clustering. 0 = rocks everywhere; 0.7 = tight dense clusters with wide empty gaps.")]
    [SerializeField][Range(0f, 1f)] private float clusterThreshold = 0.45f;
    [Tooltip("Frequency of the cluster noise. Lower = wider rocky patches; higher = small speckled patches.")]
    [SerializeField] private float clusterFrequency = 0.035f;

    [Header("Scale")]
    [SerializeField] private float minScale = 0.5f;
    [SerializeField] private float maxScale = 1.8f;

    [Header("Orientation")]
    [Tooltip("0 = rocks always upright; 1 = rocks fully tilt to match the terrain slope.")]
    [SerializeField][Range(0f, 1f)] private float normalAlignStrength = 0.65f;
    [Tooltip("Finite-difference step used to estimate the terrain normal (metres). Keep below chunk quad size (~2 m).")]
    [SerializeField] private float normalSampleStep = 0.8f;
    [Tooltip("Small Y offset above the floor to prevent z-fighting.")]
    [SerializeField] private float verticalBias = 0.05f;

    // Chunk coords we've already scattered rocks on — avoids double-spawning.
    private readonly HashSet<Vector2Int> _processed = new();

    // ─────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (rockPrefabs == null || rockPrefabs.Length == 0)
        {
            Debug.LogWarning("[SeabedRockScatter] No rock prefabs assigned — rocks won't appear. " +
                             "Assign SM_Rocks_XX prefabs in the Inspector.", this);
            return;
        }

        StartCoroutine(WaitAndSubscribe());
    }

    private IEnumerator WaitAndSubscribe()
    {
        // Wait until SeabedManager has generated its first chunk batch.
        while (SeabedManager.Instance == null || !SeabedManager.Instance.IsReady)
            yield return new WaitForSeconds(0.25f);

        var sm = SeabedManager.Instance;
        sm.OnChunkCreated  += HandleChunkCreated;
        sm.OnChunkUnloaded += HandleChunkUnloaded;
        sm.OnSeabedReset   += HandleSeabedReset;

        // Process chunks that were already created before we subscribed.
        // SeabedManager parents all chunk GOs directly under its transform,
        // naming them "Chunk(x,y)" — we parse those names to get the coords.
        foreach (Transform child in sm.transform)
        {
            if (!TryParseChunkCoord(child.name, out var coord)) continue;
            if (_processed.Contains(coord)) continue;
            PopulateChunk(coord, child.gameObject, sm);
        }
    }

    private void OnDestroy()
    {
        if (SeabedManager.Instance == null) return;
        SeabedManager.Instance.OnChunkCreated  -= HandleChunkCreated;
        SeabedManager.Instance.OnChunkUnloaded -= HandleChunkUnloaded;
        SeabedManager.Instance.OnSeabedReset   -= HandleSeabedReset;
    }

    // ─────────────────────────────────────────────────────────────────
    // Event handlers
    // ─────────────────────────────────────────────────────────────────

    private void HandleChunkCreated(Vector2Int coord, GameObject chunkGO)
    {
        if (_processed.Contains(coord)) return;
        PopulateChunk(coord, chunkGO, SeabedManager.Instance);
    }

    private void HandleChunkUnloaded(Vector2Int coord)
    {
        // The chunk GO (and all rock children) are about to be Destroy()'d.
        // We just need to forget the coord so it can be re-populated if the
        // chunk re-enters the load radius.
        _processed.Remove(coord);
    }

    private void HandleSeabedReset()
    {
        // Entire seabed is being torn down and regenerated with a new seed.
        // Rock GOs are gone (destroyed with their parent chunks); clear tracking.
        _processed.Clear();
    }

    // ─────────────────────────────────────────────────────────────────
    // Core scatter logic
    // ─────────────────────────────────────────────────────────────────

    private void PopulateChunk(Vector2Int coord, GameObject chunkGO, SeabedManager sm)
    {
        _processed.Add(coord);

        float chunkSize = sm.ChunkSize;
        float originX   = coord.x * chunkSize;
        float originZ   = coord.y * chunkSize;

        // Deterministic RNG derived from the shared seabed seed + chunk coordinates.
        // This ensures every client sees identical rock placement.
        int chunkSeed;
        unchecked { chunkSeed = sm.Seed + coord.x * 1664525 + coord.y * 1013904223; }
        var rng = new SysRandom(chunkSeed);

        int placed = 0;
        int maxAttempts = rocksPerChunk * 4; // extra attempts to fill clusters

        for (int attempt = 0; attempt < maxAttempts && placed < rocksPerChunk; attempt++)
        {
            float rx = (float)rng.NextDouble() * chunkSize;
            float rz = (float)rng.NextDouble() * chunkSize;

            float wx = originX + rx;
            float wz = originZ + rz;

            // Perlin noise clustering: skip positions that fall in sparse zones.
            // Offsets (97.3, 31.7) decouple this noise from the terrain noise.
            float cluster = Mathf.PerlinNoise(wx * clusterFrequency + 97.3f,
                                              wz * clusterFrequency + 31.7f);
            if (cluster < clusterThreshold) continue;

            float floorY = sm.GetFloorY(wx, wz);

            // Build orientation: align up-axis to terrain slope, then spin randomly on that axis.
            Quaternion rot = BuildRotation(wx, wz, floorY, rng, sm);

            float scale  = Mathf.Lerp(minScale, maxScale, (float)rng.NextDouble());
            var   prefab = rockPrefabs[rng.Next(0, rockPrefabs.Length)];

            var pos  = new Vector3(wx, floorY + verticalBias, wz);
            var rock = Instantiate(prefab, pos, rot, chunkGO.transform);
            rock.transform.localScale = Vector3.one * scale;

            placed++;
        }
    }

    private Quaternion BuildRotation(float wx, float wz, float floorY, SysRandom rng, SeabedManager sm)
    {
        // Estimate terrain surface normal via finite differences of GetFloorY.
        // This avoids raycasting and uses the same height function as the mesh.
        float d   = normalSampleStep;
        float yPX = sm.GetFloorY(wx + d, wz);
        float yPZ = sm.GetFloorY(wx,     wz + d);

        Vector3 tangentX   = new Vector3(d,  yPX - floorY, 0f).normalized;
        Vector3 tangentZ   = new Vector3(0f, yPZ - floorY, d ).normalized;
        Vector3 surfNormal = Vector3.Cross(tangentZ, tangentX).normalized;

        // Ensure the normal points upward (Cross order can flip on very steep slopes).
        if (surfNormal.y < 0f) surfNormal = -surfNormal;

        // Random yaw around the surface normal for full 360° variety.
        float yaw    = (float)(rng.NextDouble() * 360.0);
        var   yawRot = Quaternion.AngleAxis(yaw, surfNormal);

        // Blend between upright and fully surface-aligned, then apply the yaw.
        var normalRot   = Quaternion.FromToRotation(Vector3.up, surfNormal);
        var blendedBase = Quaternion.Slerp(Quaternion.identity, normalRot, normalAlignStrength);

        return yawRot * blendedBase;
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Parses the "Chunk(x,y)" naming convention used by SeabedManager.GenerateChunk.</summary>
    private static bool TryParseChunkCoord(string name, out Vector2Int coord)
    {
        coord = default;
        if (!name.StartsWith("Chunk(") || !name.EndsWith(")")) return false;
        string inner = name.Substring(6, name.Length - 7);
        string[] parts = inner.Split(',');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out int cx) || !int.TryParse(parts[1], out int cy)) return false;
        coord = new Vector2Int(cx, cy);
        return true;
    }

    private void OnDrawGizmos()
    {
        // Optional: visualise the cluster noise pattern in the scene view at Y=0.
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
        Gizmos.color = new Color(0.8f, 0.6f, 0.2f, 0.25f);
        Gizmos.DrawWireCube(transform.position, new Vector3(80f, 0.1f, 80f));
#endif
    }
}
