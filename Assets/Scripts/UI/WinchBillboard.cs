using UnityEngine;

/// <summary>
/// Displays rope length and winch state above the WinchStation.
/// Attach this component to the same GameObject as WinchStation.
/// </summary>
[RequireComponent(typeof(WinchStation))]
public class WinchBillboard : StationBillboardBase
{
    private WinchStation _winch;

    protected override void Awake()
    {
        _winch = GetComponent<WinchStation>();
        base.Awake();
    }

    protected override string GetDisplayText(PlayerController diver)
    {
        var cable = diver.GetComponent<DiveCableSystem>();
        float ropeLen = cable != null ? cable.CurrentCommsLength : 0f;

        string status;
        if (_winch.CurrentPullSpeed > 0.05f)       status = "Reeling In";
        else if (_winch.CurrentLowerSpeed > 0.05f)  status = "Paying Out";
        else                                         status = "Idle";

        float oxyPct = diver.OxygenCapacity > 0f
            ? diver.Oxygen / diver.OxygenCapacity * 100f
            : 0f;

        return $"Rope: {ropeLen:F1} m\n{status}\nO\u2082: {oxyPct:F0}%";
    }
}
