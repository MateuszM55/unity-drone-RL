using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Receives a <see cref="RewardStepSummary"/> each step, accumulates episode
/// reward totals, tracks episode outcomes per curriculum lesson, and pushes
/// all data to TensorBoard via the ML-Agents <see cref="StatsRecorder"/>.
/// Exposes live values as Inspector-visible debug strings for tuning.
/// </summary>
[DisallowMultipleComponent]
public class DroneTelemetry : MonoBehaviour
{
    [Header("Debug - Live Rewards")]
    [SerializeField] private string debugDeltaDist;
    [SerializeField] private string debugNormalizedDeltaDist;
    [SerializeField] private string debugProximity;
    [SerializeField] private string debugEnergy;
    [SerializeField] private string debugSmoothness;
    [SerializeField] private string debugTilt;
    [SerializeField] private string debugAngularVelocity;
    [SerializeField] private string debugVelAlignment;
    [SerializeField] private string debugTime;
    [SerializeField] private string debugFastApproach;
    [SerializeField] private string debugRestlessness;
    [SerializeField] private string debugYawDeviation;
    [SerializeField] private string debugTotalStepReward;

    [Header("Debug - Episode Outcomes (rolling 100-episode window)")]
    [SerializeField] private string debugCurrentLesson;
    [SerializeField] private string debugWindowEpisodes;
    [SerializeField] private string debugSuccessRate;
    [SerializeField] private string debugCrashRate;
    [SerializeField] private string debugExcessiveTiltRate;
    [SerializeField] private string debugBoundaryLeftRate;
    [SerializeField] private string debugTimeoutRate;

    // --- Per-episode reward accumulators ---
    private float _totalDeltaDistance;
    private float _totalNormalizedDeltaDistance;
    private float _totalProximity;
    private float _totalEnergy;
    private float _totalSmoothness;
    private float _totalTilt;
    private float _totalAngularVelocity;
    private float _totalVelocityAlignment;
    private float _totalTime;
    private float _totalFastApproach;
    private float _totalRestlessness;
    private float _totalYawDeviation;

    // --- Rolling 100-episode outcome window ---
    private const int RollingWindowSize = 100;
    private readonly Queue<EpisodeOutcome> _outcomeWindow = new Queue<EpisodeOutcome>(RollingWindowSize);
    private int _currentLessonIndex;

    /// <summary>
    /// Guards against double-flush and enables MaxStep timeout detection.
    /// Starts <c>true</c> so the very first <see cref="OnNewEpisode"/> does
    /// not mistakenly flush a non-existent previous episode.
    /// </summary>
    private bool _flushed = true;

    /// <summary>
    /// Accumulates per-step reward components into episode totals and
    /// updates the Inspector debug strings.
    /// </summary>
    public void Record(RewardStepSummary summary)
    {
        // --- Accumulate episode totals ---
        _totalDeltaDistance           += summary.DeltaDistance;
        _totalNormalizedDeltaDistance += summary.NormalizedDeltaDistance;
        _totalProximity               += summary.Proximity;
        _totalEnergy                  += summary.Energy;
        _totalSmoothness              += summary.Smoothness;
        _totalTilt                    += summary.Tilt;
        _totalAngularVelocity         += summary.AngularVelocity;
        _totalVelocityAlignment       += summary.VelocityAlignment;
        _totalTime                    += summary.Time;
        _totalFastApproach            += summary.FastApproach;
        _totalRestlessness            += summary.Restlessness;
        _totalYawDeviation            += summary.YawDeviation;

        // --- Inspector debug (blank when zero) ---
        const string fmt = " 0.00000;-0.00000";
        debugDeltaDist           = summary.DeltaDistance != 0f              ? summary.DeltaDistance.ToString(fmt)              : "";
        debugNormalizedDeltaDist = summary.NormalizedDeltaDistance != 0f    ? summary.NormalizedDeltaDistance.ToString(fmt)    : "";
        debugProximity           = summary.Proximity != 0f                  ? summary.Proximity.ToString(fmt)                  : "";
        debugEnergy              = summary.Energy != 0f                     ? summary.Energy.ToString(fmt)                     : "";
        debugSmoothness          = summary.Smoothness != 0f                 ? summary.Smoothness.ToString(fmt)                 : "";
        debugTilt                = summary.Tilt != 0f                       ? summary.Tilt.ToString(fmt)                       : "";
        debugAngularVelocity     = summary.AngularVelocity != 0f            ? summary.AngularVelocity.ToString(fmt)            : "";
        debugVelAlignment        = summary.VelocityAlignment != 0f          ? summary.VelocityAlignment.ToString(fmt)          : "";
        debugTime                = summary.Time != 0f                       ? summary.Time.ToString(fmt)                       : "";
        debugFastApproach        = summary.FastApproach != 0f                  ? summary.FastApproach.ToString(fmt)                  : "";
        debugRestlessness        = summary.Restlessness != 0f   ? summary.Restlessness.ToString(fmt)   : "";
        debugYawDeviation        = summary.YawDeviation != 0f    ? summary.YawDeviation.ToString(fmt)    : "";
        debugTotalStepReward     = summary.Total.ToString(fmt);
    }

    /// <summary>
    /// Writes accumulated episode reward totals and rolling-window outcome
    /// percentages to TensorBoard, then resets the reward accumulators.
    /// Outcome rates are computed over the most recent 100 episodes (or fewer
    /// if the window has not yet filled). The window is cleared when the
    /// curriculum lesson changes.
    /// Call exactly once per episode, immediately before <c>EndEpisode</c>.
    /// </summary>
    public void FlushEpisode(EpisodeOutcome reason)
    {
        if (_flushed) return;
        _flushed = true;

        // --- Record outcome in rolling window ---
        if (_outcomeWindow.Count >= RollingWindowSize)
            _outcomeWindow.Dequeue();
        _outcomeWindow.Enqueue(reason);

        // --- Compute outcome rates from the queue (single source of truth) ---
        int successCount = 0, crashCount = 0, excessiveTiltCount = 0, boundaryLeftCount = 0, timeoutCount = 0;
        foreach (EpisodeOutcome o in _outcomeWindow)
        {
            switch (o)
            {
                case EpisodeOutcome.Success_TargetReached: successCount++;       break;
                case EpisodeOutcome.Crash:                 crashCount++;         break;
                case EpisodeOutcome.Safety_ExcessiveTilt:  excessiveTiltCount++; break;
                case EpisodeOutcome.Safety_BoundaryLeft:   boundaryLeftCount++;  break;
                case EpisodeOutcome.Timeout:               timeoutCount++;       break;
            }
        }

        float windowCount = _outcomeWindow.Count;

        // --- TensorBoard: episode reward totals ---
        var stats = Academy.Instance.StatsRecorder;
        if (_totalDeltaDistance != 0f)            stats.Add("Rewards/DeltaDistance",           _totalDeltaDistance);
        if (_totalNormalizedDeltaDistance != 0f)  stats.Add("Rewards/NormalizedDeltaDistance", _totalNormalizedDeltaDistance);
        if (_totalProximity != 0f)                stats.Add("Rewards/Proximity",               _totalProximity);
        if (_totalEnergy != 0f)                   stats.Add("Rewards/Energy",                  _totalEnergy);
        if (_totalSmoothness != 0f)               stats.Add("Rewards/Smoothness",              _totalSmoothness);
        if (_totalTilt != 0f)                     stats.Add("Rewards/Tilt",                    _totalTilt);
        if (_totalAngularVelocity != 0f)          stats.Add("Rewards/AngularVelocity",         _totalAngularVelocity);
        if (_totalVelocityAlignment != 0f)        stats.Add("Rewards/VelocityAlignment",       _totalVelocityAlignment);
        if (_totalTime != 0f)                     stats.Add("Rewards/Time",                    _totalTime);
        if (_totalFastApproach != 0f)             stats.Add("Rewards/FastApproach",            _totalFastApproach);
        if (_totalRestlessness != 0f)             stats.Add("Rewards/Restlessness",           _totalRestlessness);
        if (_totalYawDeviation != 0f)             stats.Add("Rewards/YawDeviation",            _totalYawDeviation);

        // --- TensorBoard: rolling-window outcome percentages ---
        stats.Add("Outcomes/Success",        successCount       / windowCount);
        stats.Add("Outcomes/Crash",          crashCount         / windowCount);
        stats.Add("Outcomes/ExcessiveTilt",  excessiveTiltCount / windowCount);
        stats.Add("Outcomes/BoundaryLeft",   boundaryLeftCount  / windowCount);
        stats.Add("Outcomes/Timeout",        timeoutCount       / windowCount);

        // --- Reset reward accumulators (outcome counters persist per lesson) ---
        _totalDeltaDistance           = 0f;
        _totalNormalizedDeltaDistance = 0f;
        _totalProximity               = 0f;
        _totalEnergy                  = 0f;
        _totalSmoothness              = 0f;
        _totalTilt                    = 0f;
        _totalAngularVelocity         = 0f;
        _totalVelocityAlignment       = 0f;
        _totalTime                    = 0f;
        _totalFastApproach            = 0f;
        _totalRestlessness            = 0f;
        _totalYawDeviation            = 0f;

        // --- Inspector: rolling-window outcome rates ---
        const string pct = "P1";
        debugCurrentLesson     = _currentLessonIndex.ToString();
        debugWindowEpisodes    = _outcomeWindow.Count + " / " + RollingWindowSize;
        debugSuccessRate       = (successCount       / windowCount).ToString(pct);
        debugCrashRate         = (crashCount         / windowCount).ToString(pct);
        debugExcessiveTiltRate = (excessiveTiltCount / windowCount).ToString(pct);
        debugBoundaryLeftRate  = (boundaryLeftCount  / windowCount).ToString(pct);
        debugTimeoutRate       = (timeoutCount       / windowCount).ToString(pct);
    }

    /// <summary>
    /// Called at the start of every episode. Detects MaxStep timeouts
    /// (episodes that ended without an explicit <see cref="FlushEpisode"/>
    /// call) and performs a hard reset of outcome counters when the
    /// curriculum lesson changes.
    /// </summary>
    public void OnNewEpisode(int lessonIndex)
    {
        // Detect MaxStep timeout — previous episode was never explicitly flushed
        if (!_flushed)
            FlushEpisode(EpisodeOutcome.Timeout);

        // Hard reset queue on lesson change
        if (lessonIndex != _currentLessonIndex)
        {
            _outcomeWindow.Clear();
            _currentLessonIndex = lessonIndex;
            debugCurrentLesson = _currentLessonIndex.ToString();
        }

        _flushed = false;
    }

    }
