using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Low-poly flat-shaded ocean using Gerstner waves.
///
/// Key features:
///   • Non-indexed mesh  → each triangle owns its own vertices → flat / faceted look.
///   • Vertex colour RED = foam amount; rises automatically as waveIntensity increases.
///   • GetWaveHeight() / GetWaveSurfaceData() for ship buoyancy.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class OceanWaves : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    [Serializable]
    public struct WaveLayer
    {
        [Tooltip("Crest height in world units")]
        public float amplitude;
        [Tooltip("Higher = more wave crests per unit distance")]
        public float frequency;
        [Tooltip("How fast the wave travels")]
        public float speed;
        [Range(0f, 1f), Tooltip("Gerstner sharpness: 0 = sine, 1 = pointy crest")]
        public float steepness;
        [Tooltip("Travel direction in degrees (0 = +X, 90 = +Z)")]
        public float direction;
    }

    // ─────────────────────────────────────────────────────────────
    [Header("Ship Following")]
    [Tooltip("Ocean grid follows this transform so it stays under the ship as it sails")]
    public Transform shipTransform;

    // ─────────────────────────────────────────────────────────────
    [Header("Ocean Mesh")]
    [Tooltip("Grid cells per side — 30-50 gives a good low-poly look")]
    public int   gridSize = 40;
    [Tooltip("World-unit size of each cell (larger = bigger ocean)")]
    public float tileSize = 5f;

    // ─────────────────────────────────────────────────────────────
    [Header("Wave Intensity")]
    [Range(0f, 5f),
     Tooltip("Master amplitude multiplier. Also drives foam: 0 = calm/no foam, 3-5 = stormy white water")]
    public float waveIntensity = 1f;

    // ─────────────────────────────────────────────────────────────
    [Header("Foam / White Water")]
    [Range(0f, 1f),
     Tooltip("Normalised crest height above which foam appears. Lower = more foam coverage.")]
    public float foamThreshold = 0.55f;
    [Range(0.5f, 5f),
     Tooltip("Controls how sharply foam fades below the threshold edge.")]
    public float foamFalloff = 2f;

    // ─────────────────────────────────────────────────────────────
    [Header("Wave Layers")]
    public WaveLayer[] waveLayers = new WaveLayer[]
    {
        // Big rolling swell
        new WaveLayer { amplitude=0.70f, frequency=0.10f, speed=1.0f, steepness=0.55f, direction=  0f },
        // Cross-wind chop
        new WaveLayer { amplitude=0.40f, frequency=0.18f, speed=1.5f, steepness=0.45f, direction= 50f },
        // Mid ripple
        new WaveLayer { amplitude=0.22f, frequency=0.35f, speed=2.2f, steepness=0.30f, direction=-25f },
        // Short surface chop
        new WaveLayer { amplitude=0.10f, frequency=0.65f, speed=2.9f, steepness=0.20f, direction=110f },
    };

    // ─────────────────────────────────────────────────────────────
    private Mesh      _mesh;
    private Vector3[] _vertices;   // displaced positions (written every frame)
    private Vector2[] _restXZ;     // rest-space XZ, cached once, never modified
    private Color[]   _colors;     // vertex colour: R=foam
    private int       _vertCount;
    private float     _maxAmplitude;

    /// <summary>
    /// Network-synchronized time for wave computation.  All connected clients
    /// agree on this value (via NGO's ServerTime clock-sync), so waves are at
    /// the same phase everywhere.  Falls back to Time.time for offline / editor.
    /// </summary>
    private float SyncedTime
    {
        get
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
                return nm.ServerTime.TimeAsFloat;
            return Time.time;
        }
    }

    // ═════════════════════════════════════════════════════════════
    #region Unity Lifecycle

    private void Awake()
    {
        RefreshMaxAmplitude();
        BuildFlatMesh();

        if (shipTransform == null)
        {
            var shipGO = GameObject.Find("Ship");
            if (shipGO != null) shipTransform = shipGO.transform;
        }
    }

    private void OnValidate()
    {
        RefreshMaxAmplitude();
    }

    private void Update()
    {
        // Keep ocean grid centered on the ship so the mesh is always under the vessel
        if (shipTransform != null)
            transform.position = new Vector3(shipTransform.position.x, 0f, shipTransform.position.z);

        if (_mesh == null || _restXZ == null) return;

        float time   = SyncedTime;
        float maxAmp = _maxAmplitude * Mathf.Max(waveIntensity, 0.001f);

        for (int i = 0; i < _vertCount; i++)
        {
            float rx = _restXZ[i].x;
            float rz = _restXZ[i].y;

            // Accumulate Gerstner displacement
            float dX = 0f, dY = 0f, dZ = 0f;
            foreach (var w in waveLayers)
            {
                float rad  = w.direction * Mathf.Deg2Rad;
                float dirX = Mathf.Cos(rad);
                float dirZ = Mathf.Sin(rad);
                float amp  = w.amplitude * waveIntensity;
                float ph   = w.frequency * (dirX * rx + dirZ * rz) - w.speed * time;
                float sinP = Mathf.Sin(ph);
                float cosP = Mathf.Cos(ph);

                dX += w.steepness * amp * dirX * cosP;
                dZ += w.steepness * amp * dirZ * cosP;
                dY += amp * sinP;
            }

            _vertices[i] = new Vector3(rx + dX, dY, rz + dZ);

            // Foam: normalised height in [-1, 1] → [0, 1] above threshold
            float normHeight = dY / maxAmp;                                       // -1 … 1
            float foamRaw    = (normHeight - foamThreshold) / (1f - foamThreshold + 0.001f);
            float foam       = Mathf.Pow(Mathf.Clamp01(foamRaw), 1f / foamFalloff);
            foam            *= Mathf.Clamp01(waveIntensity * 0.55f);              // no foam when calm
            _colors[i]       = new Color(foam, 0f, 0f, 1f);
        }

        _mesh.SetVertices(_vertices);
        _mesh.SetColors(_colors);
        _mesh.RecalculateNormals();    // flat shading: per-face because mesh is non-indexed
        _mesh.RecalculateBounds();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════
    #region Mesh Generation

    private void RefreshMaxAmplitude()
    {
        _maxAmplitude = 0f;
        if (waveLayers == null) return;
        foreach (var w in waveLayers) _maxAmplitude += w.amplitude;
        _maxAmplitude = Mathf.Max(_maxAmplitude, 0.001f);
    }

    /// <summary>
    /// Non-indexed mesh: every quad → 2 triangles → 6 unique vertices.
    /// No shared vertices → RecalculateNormals gives each face one flat normal.
    /// </summary>
    private void BuildFlatMesh()
    {
        _vertCount = gridSize * gridSize * 6;
        _vertices  = new Vector3[_vertCount];
        _restXZ    = new Vector2[_vertCount];
        _colors    = new Color[_vertCount];
        var uvs    = new Vector2[_vertCount];
        var tris   = new int[_vertCount];

        float half = gridSize * tileSize * 0.5f;
        int   vi   = 0;

        for (int z = 0; z < gridSize; z++)
        for (int x = 0; x < gridSize; x++)
        {
            float x0 = x       * tileSize - half;
            float x1 = (x + 1) * tileSize - half;
            float z0 = z       * tileSize - half;
            float z1 = (z + 1) * tileSize - half;

            // Triangle A: BL, TL, TR
            SetVert(vi,   x0, z0, (float)x/gridSize,       (float)z/gridSize,       uvs); tris[vi]   = vi++;
            SetVert(vi,   x0, z1, (float)x/gridSize,       (float)(z+1)/gridSize,   uvs); tris[vi]   = vi++;
            SetVert(vi,   x1, z1, (float)(x+1)/gridSize,   (float)(z+1)/gridSize,   uvs); tris[vi]   = vi++;

            // Triangle B: BL, TR, BR
            SetVert(vi,   x0, z0, (float)x/gridSize,       (float)z/gridSize,       uvs); tris[vi]   = vi++;
            SetVert(vi,   x1, z1, (float)(x+1)/gridSize,   (float)(z+1)/gridSize,   uvs); tris[vi]   = vi++;
            SetVert(vi,   x1, z0, (float)(x+1)/gridSize,   (float)z/gridSize,       uvs); tris[vi]   = vi++;
        }

        _mesh = new Mesh { name = "OceanLowPoly", indexFormat = IndexFormat.UInt32 };
        _mesh.SetVertices(_vertices);
        _mesh.SetUVs(0, uvs);
        _mesh.SetTriangles(tris, 0);
        _mesh.SetColors(_colors);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        GetComponent<MeshFilter>().mesh = _mesh;
    }

    private void SetVert(int i, float x, float z, float u, float v, Vector2[] uvs)
    {
        _vertices[i] = new Vector3(x, 0f, z);
        _restXZ[i]   = new Vector2(x, z);
        uvs[i]       = new Vector2(u, v);
        _colors[i]   = Color.black;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════
    #region Gerstner Math

    /// <summary>
    /// Evaluates Gerstner wave height at local-space rest-position (rx, rz).
    /// Iterates to cancel the horizontal displacement error.
    /// </summary>
    private float GerstnerHeightAt(float rx, float rz, float time)
    {
        // Iteratively refine rest-pos to compensate Gerstner XZ shift
        const int iters = 5;
        for (int it = 0; it < iters; it++)
        {
            float cx = 0f, cz = 0f;
            foreach (var w in waveLayers)
            {
                float rad  = w.direction * Mathf.Deg2Rad;
                float dirX = Mathf.Cos(rad);
                float dirZ = Mathf.Sin(rad);
                float amp  = w.amplitude * waveIntensity;
                float cosP = Mathf.Cos(w.frequency * (dirX * rx + dirZ * rz) - w.speed * time);
                cx += w.steepness * amp * dirX * cosP;
                cz += w.steepness * amp * dirZ * cosP;
            }
            // Adjust rest pos (not the query pos) by subtracting displacement
            // We want: restPos + displacement = queryPos
            // So:       restPos = queryPos - displacement (at current restPos estimate)
            rx -= cx * 0.5f;   // half-step for stability
            rz -= cz * 0.5f;
        }

        float y = 0f;
        foreach (var w in waveLayers)
        {
            float rad  = w.direction * Mathf.Deg2Rad;
            float dirX = Mathf.Cos(rad);
            float dirZ = Mathf.Sin(rad);
            float amp  = w.amplitude * waveIntensity;
            y += amp * Mathf.Sin(w.frequency * (dirX * rx + dirZ * rz) - w.speed * time);
        }
        return y;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════
    #region Public API

    /// <summary>
    /// Returns the world-space Y height of the ocean surface at worldPosition.
    /// Call every frame from a ship buoyancy script.
    /// </summary>
    public float GetWaveHeight(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        float   y     = GerstnerHeightAt(local.x, local.z, SyncedTime);
        return transform.TransformPoint(new Vector3(local.x, y, local.z)).y;
    }

    /// <summary>
    /// Returns wave height AND approximate surface normal at worldPosition.
    /// Use the normal to tilt/roll the ship with the water surface.
    /// </summary>
    public (float height, Vector3 normal) GetWaveSurfaceData(Vector3 worldPosition)
    {
        const float d  = 0.5f;
        float       h0 = GetWaveHeight(worldPosition);
        float       hX = GetWaveHeight(worldPosition + new Vector3(d, 0f, 0f));
        float       hZ = GetWaveHeight(worldPosition + new Vector3(0f, 0f, d));

        Vector3 tX = new Vector3(d, hX - h0, 0f);
        Vector3 tZ = new Vector3(0f, hZ - h0, d);
        return (h0, Vector3.Cross(tZ, tX).normalized);
    }

    #endregion
}
