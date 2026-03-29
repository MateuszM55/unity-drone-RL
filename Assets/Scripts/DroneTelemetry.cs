using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Receives a <see cref="RewardStepSummary"/> each step and pushes values
/// to TensorBoard via the ML-Agents <see cref="StatsRecorder"/>, and
/// exposes them as Inspector-visible debug strings for live tuning.
/// </summary>
[DisallowMultipleComponent]
public class DroneTelemetry : MonoBehaviour
{
    [Header("Debug - Live Rewards")]
    public string debugDeltaDist;
    public string debugProximity;
    public string debugEnergy;
    public string debugSmoothness;
    public string debugTilt;
    public string debugAngularVelocity;
    public string debugVelAlignment;
    public string debugTime;
    public string debugFastApproach;
    public string debugTotalStepReward;

    /// <summary>
    /// Logs the per-step reward components to TensorBoard (skipping zero-value
    /// entries) and updates the Inspector debug strings.
    /// </summary>
    public void Record(RewardStepSummary summary)
    {
        // --- TensorBoard stats (skip zero-value rewards) ---
        var stats = Academy.Instance.StatsRecorder;
        if (summary.DeltaDistance != 0f)     stats.Add("Rewards/DeltaDistance",      summary.DeltaDistance);
        if (summary.Proximity != 0f)         stats.Add("Rewards/Proximity",          summary.Proximity);
        if (summary.Energy != 0f)            stats.Add("Rewards/Energy",             summary.Energy);
        if (summary.Smoothness != 0f)        stats.Add("Rewards/Smoothness",         summary.Smoothness);
        if (summary.Tilt != 0f)              stats.Add("Rewards/Tilt",               summary.Tilt);
        if (summary.AngularVelocity != 0f)   stats.Add("Rewards/AngularVelocity",    summary.AngularVelocity);
        if (summary.VelocityAlignment != 0f) stats.Add("Rewards/VelocityAlignment",  summary.VelocityAlignment);
        if (summary.Time != 0f)              stats.Add("Rewards/Time",               summary.Time);
        if (summary.FastApproach != 0f)      stats.Add("Rewards/FastApproach",       summary.FastApproach);

        // --- Inspector debug (blank when zero) ---
        const string fmt = " 0.00000;-0.00000";
        debugDeltaDist       = summary.DeltaDistance != 0f     ? summary.DeltaDistance.ToString(fmt)     : "";
        debugProximity       = summary.Proximity != 0f         ? summary.Proximity.ToString(fmt)         : "";
        debugEnergy          = summary.Energy != 0f            ? summary.Energy.ToString(fmt)            : "";
        debugSmoothness      = summary.Smoothness != 0f        ? summary.Smoothness.ToString(fmt)        : "";
        debugTilt            = summary.Tilt != 0f              ? summary.Tilt.ToString(fmt)              : "";
        debugAngularVelocity = summary.AngularVelocity != 0f   ? summary.AngularVelocity.ToString(fmt)   : "";
        debugVelAlignment    = summary.VelocityAlignment != 0f ? summary.VelocityAlignment.ToString(fmt) : "";
        debugTime            = summary.Time != 0f              ? summary.Time.ToString(fmt)              : "";
        debugFastApproach    = summary.FastApproach != 0f      ? summary.FastApproach.ToString(fmt)      : "";
        debugTotalStepReward = summary.Total.ToString(fmt);
    }
}
