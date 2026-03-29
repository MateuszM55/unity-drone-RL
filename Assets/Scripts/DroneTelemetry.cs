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

    // --- Per-episode accumulators ---
    private float _totalDeltaDistance;
    private float _totalProximity;
    private float _totalEnergy;
    private float _totalSmoothness;
    private float _totalTilt;
    private float _totalAngularVelocity;
    private float _totalVelocityAlignment;
    private float _totalTime;
    private float _totalFastApproach;

    /// <summary>
    /// Accumulates per-step reward components into episode totals and
    /// updates the Inspector debug strings.
    /// </summary>
    public void Record(RewardStepSummary summary)
    {
        // --- Accumulate episode totals ---
        _totalDeltaDistance     += summary.DeltaDistance;
        _totalProximity         += summary.Proximity;
        _totalEnergy            += summary.Energy;
        _totalSmoothness        += summary.Smoothness;
        _totalTilt              += summary.Tilt;
        _totalAngularVelocity   += summary.AngularVelocity;
        _totalVelocityAlignment += summary.VelocityAlignment;
        _totalTime              += summary.Time;
        _totalFastApproach      += summary.FastApproach;

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

    /// <summary>
    /// Writes the accumulated episode totals to TensorBoard (skipping zero-value
    /// entries), then resets all accumulators for the next episode.
    /// Call this exactly once, immediately before <see cref="Agent.EndEpisode"/>.
    /// </summary>
    public void FlushEpisode()
    {
        var stats = Academy.Instance.StatsRecorder;
        if (_totalDeltaDistance != 0f)     stats.Add("Rewards/DeltaDistance",     _totalDeltaDistance);
        if (_totalProximity != 0f)         stats.Add("Rewards/Proximity",         _totalProximity);
        if (_totalEnergy != 0f)            stats.Add("Rewards/Energy",            _totalEnergy);
        if (_totalSmoothness != 0f)        stats.Add("Rewards/Smoothness",        _totalSmoothness);
        if (_totalTilt != 0f)              stats.Add("Rewards/Tilt",              _totalTilt);
        if (_totalAngularVelocity != 0f)   stats.Add("Rewards/AngularVelocity",   _totalAngularVelocity);
        if (_totalVelocityAlignment != 0f) stats.Add("Rewards/VelocityAlignment", _totalVelocityAlignment);
        if (_totalTime != 0f)              stats.Add("Rewards/Time",              _totalTime);
        if (_totalFastApproach != 0f)      stats.Add("Rewards/FastApproach",      _totalFastApproach);

        _totalDeltaDistance     = 0f;
        _totalProximity         = 0f;
        _totalEnergy            = 0f;
        _totalSmoothness        = 0f;
        _totalTilt              = 0f;
        _totalAngularVelocity   = 0f;
        _totalVelocityAlignment = 0f;
        _totalTime              = 0f;
        _totalFastApproach      = 0f;
    }
}
