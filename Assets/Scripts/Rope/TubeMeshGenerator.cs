using UnityEngine;

/// <summary>
/// Generates a procedural tube mesh along a set of positions (rope particles).
/// Creates a child GameObject with MeshFilter + MeshRenderer.
/// Pre-allocates all buffers to avoid GC each frame.
/// </summary>
public class TubeMeshGenerator
{
    private readonly int _sides;
    private readonly int _particleCount;
    private readonly float _radius;

    private readonly Mesh _mesh;
    private readonly Vector3[] _vertices;
    private readonly Vector3[] _normals;
    private readonly Vector2[] _uvs;
    private readonly int[] _triangles;

    private readonly GameObject _meshGO;
    private readonly MeshRenderer _renderer;
    private readonly MaterialPropertyBlock _mpb;

    private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");

    public TubeMeshGenerator(int particleCount, int sides, float radius, Material material, string name)
    {
        _particleCount = particleCount;
        _sides = Mathf.Max(3, sides);
        _radius = radius;

        int vertCount = _particleCount * _sides;
        int triCount = (_particleCount - 1) * _sides * 6;

        _vertices = new Vector3[vertCount];
        _normals = new Vector3[vertCount];
        _uvs = new Vector2[vertCount];
        _triangles = new int[triCount];

        // Build UVs (static)
        for (int i = 0; i < _particleCount; i++)
        {
            float v = i / (float)(_particleCount - 1);
            for (int j = 0; j < _sides; j++)
            {
                float u = j / (float)_sides;
                _uvs[i * _sides + j] = new Vector2(u, v);
            }
        }

        // Build triangle indices (static — never changes)
        int tri = 0;
        for (int i = 0; i < _particleCount - 1; i++)
        {
            for (int j = 0; j < _sides; j++)
            {
                int current = i * _sides + j;
                int next = i * _sides + (j + 1) % _sides;
                int currentNext = (i + 1) * _sides + j;
                int nextNext = (i + 1) * _sides + (j + 1) % _sides;

                _triangles[tri++] = current;
                _triangles[tri++] = currentNext;
                _triangles[tri++] = next;

                _triangles[tri++] = next;
                _triangles[tri++] = currentNext;
                _triangles[tri++] = nextNext;
            }
        }

        // Create mesh
        _mesh = new Mesh { name = name + "_TubeMesh" };
        _mesh.vertices = _vertices;
        _mesh.normals = _normals;
        _mesh.uv = _uvs;
        _mesh.triangles = _triangles;

        // Create GameObject
        _meshGO = new GameObject(name);
        var filter = _meshGO.AddComponent<MeshFilter>();
        filter.sharedMesh = _mesh;

        _renderer = _meshGO.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = material != null
            ? material
            : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;

        _mpb = new MaterialPropertyBlock();
    }

    /// <summary>
    /// Rebuild tube vertices/normals from the given positions array.
    /// </summary>
    public void UpdateMesh(Vector3[] positions, int count)
    {
        if (positions == null || count < 2) return;

        Vector3 prevForward = Vector3.forward;

        for (int i = 0; i < count && i < _particleCount; i++)
        {
            // Compute tangent (forward direction)
            Vector3 forward;
            if (i == 0)
                forward = positions[1] - positions[0];
            else if (i == count - 1)
                forward = positions[count - 1] - positions[count - 2];
            else
                forward = positions[i + 1] - positions[i - 1];

            if (forward.sqrMagnitude < 1e-8f)
                forward = prevForward;
            else
                forward.Normalize();

            prevForward = forward;

            // Build local frame via cross products
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-6f)
                right = Vector3.Cross(Vector3.forward, forward);
            right.Normalize();

            Vector3 up = Vector3.Cross(forward, right).normalized;

            // Generate ring vertices
            for (int j = 0; j < _sides; j++)
            {
                float angle = j * (2f * Mathf.PI / _sides);
                Vector3 offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * _radius;
                int idx = i * _sides + j;
                _vertices[idx] = positions[i] + offset;
                _normals[idx] = offset.normalized;
            }
        }

        _mesh.vertices = _vertices;
        _mesh.normals = _normals;
        _mesh.RecalculateBounds();
    }

    /// <summary>
    /// Tint the mesh color via MaterialPropertyBlock (no material instancing).
    /// </summary>
    public void SetColor(Color color)
    {
        if (_renderer == null) return;
        _mpb.SetColor(s_BaseColor, color);
        _renderer.SetPropertyBlock(_mpb);
    }

    public void SetActive(bool active)
    {
        if (_meshGO != null) _meshGO.SetActive(active);
    }

    public void Dispose()
    {
        if (_meshGO != null) Object.Destroy(_meshGO);
        if (_mesh != null) Object.Destroy(_mesh);
    }
}
