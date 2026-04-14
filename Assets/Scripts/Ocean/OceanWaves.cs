using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Low-poly ocean with LOD clipmap rings and GPU Gerstner waves.
///
/// Key features:
///   • Concentric LOD rings — high detail near the ship, coarser at distance.
///   • Non-indexed mesh with smooth per-vertex Gerstner normals.
///   • Compute-shader Gerstner displacement + normals (CPU fallback if unassigned).
///   • Vertex colour RED = foam; rises with waveIntensity.
///   • GetWaveHeight() / GetWaveSurfaceData() for ship buoyancy (CPU, unchanged).
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
    [Header("LOD Mesh")]
    [Tooltip("Grid cells per side for the center (highest detail) patch")]
    [SerializeField] private int centerResolution = 64;
    [Tooltip("World-unit size of each cell in the center patch")]
    [SerializeField] private float centerCellSize = 2f;
    [Tooltip("Number of LOD ring levels beyond the center patch (each doubles tile size)")]
    [SerializeField] private int ringCount = 4;

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
        new WaveLayer { amplitude=0.70f, frequency=0.10f, speed=1.0f, steepness=0.55f, direction=  0f },
        new WaveLayer { amplitude=0.40f, frequency=0.18f, speed=1.5f, steepness=0.45f, direction= 50f },
        new WaveLayer { amplitude=0.22f, frequency=0.35f, speed=2.2f, steepness=0.30f, direction=-25f },
        new WaveLayer { amplitude=0.10f, frequency=0.65f, speed=2.9f, steepness=0.20f, direction=110f },
    };

    // ─────────────────────────────────────────────────────────────
    [Header("Compute Shader")]
    [Tooltip("Assign OceanCompute.compute — falls back to CPU if null")]
    [SerializeField] private ComputeShader oceanCompute;

    // ═════════════════════════════════════════════════════════════
    #region GPU Structs

    [StructLayout(LayoutKind.Sequential)]
    private struct OceanVertex
    {
        public float px, py, pz;       // position
        public float nx, ny, nz;       // normal
        public float cr, cg, cb, ca;   // color (R = foam)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GPUWaveLayer
    {
        public float amplitude, frequency, speed, steepness, dirX, dirZ;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════
    #region Private State

    private Mesh      _mesh;
    private Vector2[] _restXZ;          // rest-space XZ for every vertex
    private int       _totalVerts;
    private int       _totalTris;
    private float     _maxAmplitude;
    private float     _maxExtent;       // half-extent of outermost ring

    // Compute path
    private GraphicsBuffer _vertexBuffer;
    private GraphicsBuffer _restXZBuffer;
    private GraphicsBuffer _waveLayerBuffer;
    private int            _kernelDisplace;
    private int            _kernelNormals;
    private bool           _useCompute;

    // CPU fallback
    private OceanVertex[] _cpuVerts;

    #endregion

    // ═════════════════════════════════════════════════════════════
    #region SyncedTime

    /// <summary>
    /// Network-synchronized time for wave computation.
    /// Falls back to Time.time for offline / editor.
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

    #endregion

    // ═════════════════════════════════════════════════════════════
    #region Unity Lifecycle

    private void Awake()
    {
        RefreshMaxAmplitude();
        BuildRestPositions();
        CreateMesh();

        _useCompute = oceanCompute != null && SystemInfo.supportsComputeShaders;
        if (_useCompute)
            InitCompute();
        else
            _cpuVerts = new OceanVertex[_totalVerts];

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
        if (shipTransform != null)
            transform.position = new Vector3(shipTransform.position.x, 0f, shipTransform.position.z);

        if (_mesh == null || _restXZ == null) return;

        if (_useCompute)
            DispatchCompute();
        else
            UpdateCPU();
    }

    private void OnDestroy()
    {
        _vertexBuffer?.Release();
        _restXZBuffer?.Release();
        _waveLayerBuffer?.Release();
        _vertexBuffer    = null;
        _restXZBuffer    = null;
        _waveLayerBuffer = null;
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
    /// Computes rest XZ positions for center patch + ring levels.
    /// Sets _restXZ, _totalVerts, _totalTris, _maxExtent.
    /// </summary>
    private void BuildRestPositions()
    {
        float centerHalf = centerResolution * centerCellSize * 0.5f;

        // Count total vertices
        _totalVerts = centerResolution * centerResolution * 6;
        float innerHalf = centerHalf;
        for (int ring = 0; ring < ringCount; ring++)
        {
            float ts = centerCellSize * Mathf.Pow(2f, ring + 1);
            float outerHalf = innerHalf * 2f;
            _totalVerts += CountRingVerts(innerHalf, outerHalf, ts);
            innerHalf = outerHalf;
        }
        _maxExtent = innerHalf;
        _totalTris = _totalVerts / 3;

        _restXZ = new Vector2[_totalVerts];
        int vi = 0;

        // LOD 0: center patch
        vi = AddRegionQuads(-centerHalf, centerHalf, -centerHalf, centerHalf, centerCellSize, vi);

        // LOD 1..N: concentric rings
        innerHalf = centerHalf;
        for (int ring = 0; ring < ringCount; ring++)
        {
            float ts = centerCellSize * Mathf.Pow(2f, ring + 1);
            float outerHalf = innerHalf * 2f;

            // Bottom strip: full width, below inner boundary
            vi = AddRegionQuads(-outerHalf, outerHalf, -outerHalf, -innerHalf, ts, vi);
            // Top strip: full width, above inner boundary
            vi = AddRegionQuads(-outerHalf, outerHalf,  innerHalf,  outerHalf, ts, vi);
            // Left strip: inner height only
            vi = AddRegionQuads(-outerHalf, -innerHalf, -innerHalf, innerHalf, ts, vi);
            // Right strip: inner height only
            vi = AddRegionQuads( innerHalf,  outerHalf, -innerHalf, innerHalf, ts, vi);

            innerHalf = outerHalf;
        }
    }

    private int CountRingVerts(float innerHalf, float outerHalf, float ts)
    {
        int topBottom = 2 * CountRegionVerts(-outerHalf, outerHalf, -outerHalf, -innerHalf, ts);
        int leftRight = 2 * CountRegionVerts(-outerHalf, -innerHalf, -innerHalf, innerHalf, ts);
        return topBottom + leftRight;
    }

    private int CountRegionVerts(float xMin, float xMax, float zMin, float zMax, float ts)
    {
        int cellsX = Mathf.RoundToInt((xMax - xMin) / ts);
        int cellsZ = Mathf.RoundToInt((zMax - zMin) / ts);
        return cellsX * cellsZ * 6;
    }

    /// <summary>
    /// Fills _restXZ for a rectangular grid region. Returns updated vertex index.
    /// </summary>
    private int AddRegionQuads(float xMin, float xMax, float zMin, float zMax, float ts, int vi)
    {
        int cellsX = Mathf.RoundToInt((xMax - xMin) / ts);
        int cellsZ = Mathf.RoundToInt((zMax - zMin) / ts);

        for (int z = 0; z < cellsZ; z++)
        for (int x = 0; x < cellsX; x++)
        {
            float x0 = xMin + x * ts;
            float x1 = x0 + ts;
            float z0 = zMin + z * ts;
            float z1 = z0 + ts;

            // Triangle A: BL, TL, TR
            _restXZ[vi++] = new Vector2(x0, z0);
            _restXZ[vi++] = new Vector2(x0, z1);
            _restXZ[vi++] = new Vector2(x1, z1);

            // Triangle B: BL, TR, BR
            _restXZ[vi++] = new Vector2(x0, z0);
            _restXZ[vi++] = new Vector2(x1, z1);
            _restXZ[vi++] = new Vector2(x1, z0);
        }
        return vi;
    }

    /// <summary>
    /// Creates the Mesh with a custom vertex layout (position + normal + color).
    /// Uses SetVertexBufferParams so the buffer can be bound to compute shaders.
    /// </summary>
    private void CreateMesh()
    {
        _mesh = new Mesh();
        _mesh.name = "OceanLOD";
        _mesh.indexFormat = IndexFormat.UInt32;
        _mesh.MarkDynamic();

        // Enable raw access so compute shader can write via RWByteAddressBuffer
        _mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;

        var layout = new[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color,    VertexAttributeFormat.Float32, 4),
        };
        _mesh.SetVertexBufferParams(_totalVerts, layout);

        // Upload initial vertex data (rest positions, normal up, black colour)
        var initVerts = new OceanVertex[_totalVerts];
        for (int i = 0; i < _totalVerts; i++)
        {
            initVerts[i].px = _restXZ[i].x;
            initVerts[i].pz = _restXZ[i].y;
            initVerts[i].ny = 1f;
            initVerts[i].ca = 1f;
        }
        _mesh.SetVertexBufferData(initVerts, 0, 0, _totalVerts);

        // Non-indexed: identity triangle list (tri[i] = i)
        _mesh.SetIndexBufferParams(_totalVerts, IndexFormat.UInt32);
        var indices = new int[_totalVerts];
        for (int i = 0; i < _totalVerts; i++) indices[i] = i;
        _mesh.SetIndexBufferData(indices, 0, 0, _totalVerts);

        _mesh.SetSubMesh(0, new SubMeshDescriptor(0, _totalVerts, MeshTopology.Triangles),
            MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

        // Conservative bounds — never auto-recalculated (compute writes are GPU-side)
        float maxH = _maxAmplitude * 5f + 10f;
        _mesh.bounds = new Bounds(Vector3.zero, new Vector3(_maxExtent * 2f, maxH, _maxExtent * 2f));

        GetComponent<MeshFilter>().mesh = _mesh;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════
    #region Compute Shader

    private void InitCompute()
    {
        _kernelDisplace = oceanCompute.FindKernel("DisplaceVertices");
        _kernelNormals  = oceanCompute.FindKernel("ComputeSmoothNormals");

        // Vertex buffer from the mesh — writable by compute
        _vertexBuffer = _mesh.GetVertexBuffer(0);

        // Rest positions (read-only on GPU)
        _restXZBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _totalVerts, sizeof(float) * 2);
        _restXZBuffer.SetData(_restXZ);

        // Wave layer buffer (updated each frame)
        int maxLayers = Mathf.Max(waveLayers.Length, 1);
        _waveLayerBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxLayers, sizeof(float) * 6);
    }

    private void DispatchCompute()
    {
        // Upload wave layer data (pre-compute direction vectors)
        var gpuLayers = new GPUWaveLayer[waveLayers.Length];
        for (int i = 0; i < waveLayers.Length; i++)
        {
            float rad = waveLayers[i].direction * Mathf.Deg2Rad;
            gpuLayers[i] = new GPUWaveLayer
            {
                amplitude = waveLayers[i].amplitude,
                frequency = waveLayers[i].frequency,
                speed     = waveLayers[i].speed,
                steepness = waveLayers[i].steepness,
                dirX      = Mathf.Cos(rad),
                dirZ      = Mathf.Sin(rad),
            };
        }
        // Resize buffer if wave layer count changed
        if (_waveLayerBuffer.count < waveLayers.Length)
        {
            _waveLayerBuffer.Release();
            _waveLayerBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, waveLayers.Length, sizeof(float) * 6);
        }
        _waveLayerBuffer.SetData(gpuLayers);

        // ── Kernel 1: Displace ──────────────────────────────────
        oceanCompute.SetBuffer(_kernelDisplace, "_VertexBuffer",  _vertexBuffer);
        oceanCompute.SetBuffer(_kernelDisplace, "_RestXZ",        _restXZBuffer);
        oceanCompute.SetBuffer(_kernelDisplace, "_WaveLayers",    _waveLayerBuffer);
        oceanCompute.SetInt("_VertexCount",     _totalVerts);
        oceanCompute.SetInt("_WaveLayerCount",  waveLayers.Length);
        oceanCompute.SetFloat("_SyncedTime",    SyncedTime);
        oceanCompute.SetFloat("_WaveIntensity", waveIntensity);
        oceanCompute.SetFloat("_FoamThreshold", foamThreshold);
        oceanCompute.SetFloat("_FoamFalloff",   foamFalloff);
        oceanCompute.SetFloat("_MaxAmplitude",  _maxAmplitude);

        int groupsVert = (_totalVerts + 255) / 256;
        oceanCompute.Dispatch(_kernelDisplace, groupsVert, 1, 1);

        // ── Kernel 2: Smooth normals ────────────────────────────
        oceanCompute.SetBuffer(_kernelNormals, "_VertexBuffer", _vertexBuffer);
        oceanCompute.SetBuffer(_kernelNormals, "_RestXZ", _restXZBuffer);
        oceanCompute.SetBuffer(_kernelNormals, "_WaveLayers", _waveLayerBuffer);
        oceanCompute.SetInt("_TriangleCount", _totalTris);
        oceanCompute.SetInt("_VertexCount", _totalVerts);
        oceanCompute.SetInt("_WaveLayerCount", waveLayers.Length);
        oceanCompute.SetFloat("_SyncedTime", SyncedTime);
        oceanCompute.SetFloat("_WaveIntensity", waveIntensity);

        int groupsNormal = (_totalVerts + 255) / 256;
        oceanCompute.Dispatch(_kernelNormals, groupsNormal, 1, 1);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════
    #region CPU Fallback

    private void UpdateCPU()
    {
        float time   = SyncedTime;
        float maxAmp = _maxAmplitude * Mathf.Max(waveIntensity, 0.001f);

        // Gerstner displacement
        for (int i = 0; i < _totalVerts; i++)
        {
            float rx = _restXZ[i].x;
            float rz = _restXZ[i].y;

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

            _cpuVerts[i].px = rx + dX;
            _cpuVerts[i].py = dY;
            _cpuVerts[i].pz = rz + dZ;

            float normHeight = dY / maxAmp;
            float foamRaw    = (normHeight - foamThreshold) / (1f - foamThreshold + 0.001f);
            float foam       = Mathf.Pow(Mathf.Clamp01(foamRaw), 1f / foamFalloff);
            foam            *= Mathf.Clamp01(waveIntensity * 0.55f);

            _cpuVerts[i].cr = foam;
            _cpuVerts[i].cg = 0f;
            _cpuVerts[i].cb = 0f;
            _cpuVerts[i].ca = 1f;
        }

        // Smooth Gerstner normals (per-vertex)
        for (int i = 0; i < _totalVerts; i++)
        {
            Vector3 normal;
            EvaluateGerstnerSurface(_restXZ[i].x, _restXZ[i].y, time, out float x, out float y, out float z, out normal);

            _cpuVerts[i].px = x;
            _cpuVerts[i].py = y;
            _cpuVerts[i].pz = z;
            _cpuVerts[i].nx = normal.x;
            _cpuVerts[i].ny = normal.y;
            _cpuVerts[i].nz = normal.z;
        }

        _mesh.SetVertexBufferData(_cpuVerts, 0, 0, _totalVerts,
            0, MeshUpdateFlags.DontRecalculateBounds);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════
    #region Gerstner Math

    /// <summary>
    /// Evaluates displaced Gerstner position and smooth surface normal at a rest-space sample.
    /// </summary>
    private void EvaluateGerstnerSurface(float rx, float rz, float time, out float px, out float py, out float pz, out Vector3 normal)
    {
        float dX = 0f, dY = 0f, dZ = 0f;
        Vector3 tangentX = new Vector3(1f, 0f, 0f);
        Vector3 tangentZ = new Vector3(0f, 0f, 1f);

        foreach (var w in waveLayers)
        {
            float rad  = w.direction * Mathf.Deg2Rad;
            float dirX = Mathf.Cos(rad);
            float dirZ = Mathf.Sin(rad);
            float amp  = w.amplitude * waveIntensity;
            float ph   = w.frequency * (dirX * rx + dirZ * rz) - w.speed * time;
            float sinP = Mathf.Sin(ph);
            float cosP = Mathf.Cos(ph);
            float qkA  = w.steepness * amp * w.frequency;
            float kA   = amp * w.frequency;

            dX += w.steepness * amp * dirX * cosP;
            dZ += w.steepness * amp * dirZ * cosP;
            dY += amp * sinP;

            tangentX.x -= qkA * dirX * dirX * sinP;
            tangentX.y += kA * dirX * cosP;
            tangentX.z -= qkA * dirX * dirZ * sinP;

            tangentZ.x -= qkA * dirX * dirZ * sinP;
            tangentZ.y += kA * dirZ * cosP;
            tangentZ.z -= qkA * dirZ * dirZ * sinP;
        }

        normal = Vector3.Cross(tangentZ, tangentX).normalized;
        px = rx + dX;
        py = dY;
        pz = rz + dZ;
    }

    /// <summary>
    /// Evaluates Gerstner wave height at local-space rest-position (rx, rz).
    /// Iterates to cancel the horizontal displacement error.
    /// </summary>
    private float GerstnerHeightAt(float rx, float rz, float time)
    {
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
            rx -= cx * 0.5f;
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
