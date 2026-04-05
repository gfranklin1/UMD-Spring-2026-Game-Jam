using UnityEngine;

/// <summary>
/// Position-based Verlet rope simulation. No Unity physics engine involvement —
/// just arrays of Vector3 positions updated each frame.
/// Pin particle 0 to the station anchor and particle N-1 to the player attach point.
/// </summary>
public class VerletRope
{
    private readonly Vector3[] _positions;
    private readonly Vector3[] _previousPositions;
    private readonly int _particleCount;
    private readonly float _segmentLength;
    private readonly int _constraintIterations;

    private const float TeleportThreshold = 5f;

    public Vector3[] Positions => _positions;
    public int ParticleCount => _particleCount;

    public VerletRope(int particleCount, float ropeLength, int constraintIterations)
    {
        _particleCount = Mathf.Max(2, particleCount);
        _constraintIterations = Mathf.Max(1, constraintIterations);
        _segmentLength = ropeLength / (_particleCount - 1);

        _positions = new Vector3[_particleCount];
        _previousPositions = new Vector3[_particleCount];
    }

    /// <summary>
    /// Distribute particles evenly between start and end. No initial velocity.
    /// </summary>
    public void Initialize(Vector3 start, Vector3 end)
    {
        for (int i = 0; i < _particleCount; i++)
        {
            float t = i / (float)(_particleCount - 1);
            _positions[i] = Vector3.Lerp(start, end, t);
            _previousPositions[i] = _positions[i];
        }
    }

    /// <summary>
    /// Update pinned endpoint positions. If either endpoint teleports (>5 m jump),
    /// the entire rope is re-initialized to avoid velocity explosion.
    /// </summary>
    public void SetPinPositions(Vector3 start, Vector3 end)
    {
        bool teleported = Vector3.Distance(_positions[0], start) > TeleportThreshold
                       || Vector3.Distance(_positions[_particleCount - 1], end) > TeleportThreshold;

        if (teleported)
        {
            Initialize(start, end);
            return;
        }

        _positions[0] = start;
        _previousPositions[0] = start;
        _positions[_particleCount - 1] = end;
        _previousPositions[_particleCount - 1] = end;
    }

    /// <summary>
    /// Run one simulation step: Verlet integration then distance constraint solving.
    /// </summary>
    public void Simulate(float dt, RopeEnvironment env)
    {
        float dtSq = dt * dt;

        // --- Verlet integration (skip pinned endpoints 0 and N-1) ---
        for (int i = 1; i < _particleCount - 1; i++)
        {
            Vector3 current = _positions[i];
            Vector3 velocity = current - _previousPositions[i];

            bool inWater = current.y < env.WaterSurfaceY;
            float gravity = inWater ? env.WaterGravity : env.AirGravity;
            float drag = inWater ? env.WaterDrag : env.AirDrag;

            _previousPositions[i] = current;
            _positions[i] = current + velocity * drag + Vector3.up * (gravity * dtSq);
        }

        // --- Distance constraint solving ---
        for (int iter = 0; iter < _constraintIterations; iter++)
        {
            // Re-pin endpoints each iteration (they must not drift)
            _positions[0] = _previousPositions[0];
            _positions[_particleCount - 1] = _previousPositions[_particleCount - 1];

            for (int i = 0; i < _particleCount - 1; i++)
            {
                Vector3 delta = _positions[i + 1] - _positions[i];
                float distance = delta.magnitude;
                if (distance < 1e-6f) continue;

                float error = distance - _segmentLength;
                Vector3 correction = delta * (error / distance * 0.5f);

                bool pinA = (i == 0);
                bool pinB = (i + 1 == _particleCount - 1);

                if (pinA && pinB) continue; // both pinned, can't correct

                if (pinA)
                {
                    _positions[i + 1] -= correction * 2f;
                }
                else if (pinB)
                {
                    _positions[i] += correction * 2f;
                }
                else
                {
                    _positions[i] += correction;
                    _positions[i + 1] -= correction;
                }
            }
        }
    }

    /// <summary>
    /// Fill the positions array with a catenary-like curve (for non-owner visualization).
    /// </summary>
    public void GenerateCatenary(Vector3 start, Vector3 end, float sag)
    {
        for (int i = 0; i < _particleCount; i++)
        {
            float t = i / (float)(_particleCount - 1);
            Vector3 point = Vector3.Lerp(start, end, t);
            point.y -= Mathf.Sin(t * Mathf.PI) * sag;
            _positions[i] = point;
        }
        // Sync previous positions so switching to Verlet later doesn't explode
        System.Array.Copy(_positions, _previousPositions, _particleCount);
    }
}
