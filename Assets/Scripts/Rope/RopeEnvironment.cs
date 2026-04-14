using UnityEngine;

/// <summary>
/// Per-frame environment parameters passed into the Verlet rope simulation.
/// </summary>
public struct RopeEnvironment
{
    public float AirGravity;
    public float WaterGravity;
    public float AirDrag;
    public float WaterDrag;
    public float WaterSurfaceY;
}
