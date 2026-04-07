using UnityEngine;

/// <summary>
/// Displays pump momentum, flow rate, and diver oxygen above the AirPumpStation.
/// Attach this component to the same GameObject as AirPumpStation.
/// </summary>
[RequireComponent(typeof(AirPumpStation))]
public class AirPumpBillboard : StationBillboardBase
{
    private AirPumpStation _pump;

    protected override void Awake()
    {
        _pump = GetComponent<AirPumpStation>();
        base.Awake();
    }

    protected override string GetDisplayText(PlayerController diver)
    {
        float flow   = diver.PumpFlowRate;
        float oxyPct = diver.OxygenCapacity > 0f
            ? diver.Oxygen / diver.OxygenCapacity * 100f
            : 0f;

        return $"Flow: {flow:F1} O\u2082/s\nDiver O\u2082: {oxyPct:F0}%";
    }
}
