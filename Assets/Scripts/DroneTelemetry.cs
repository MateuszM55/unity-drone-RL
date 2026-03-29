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

    [Header("Debug - Episode Outcomes (per lesson)")]
    public string debugCurrentLesson;
    public string debugTotalEpisodes;
    public string debugSuccessRate;
    public string debugCrashObstacleRate;
    public string debugExcessiveTiltRate;
    public string debugBoundaryLeftRate;
    public string debugTimeoutRate;

    // --- Per-episode reward accumulators ---
    private float _totalDeltaDistance;
    private float _totalProximity;
    private float _totalEnergy;
    private float _totalSmoothness;
    private float _totalTilt;
    private float _totalAngularVelocity;
    private float _totalVelocityAlignment;
    private float _totalTime;
    private float _totalFastApproach;

    // --- Per-lesson outcome counters ---
    private Lesson _currentLesson;
    private int _totalEpisodes;
    private int _successCount;
    private int _crashObstacleCount;
    private int _excessiveTiltCount;
    private int _boundaryLeftCount;
    private int _timeoutCount;

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
    /// Writes accumulated episode reward totals and outcome percentages to
    /// TensorBoard, then resets the reward accumulators. Outcome counters
    /// persist until a lesson change triggers a hard reset.
    /// Call exactly once per episode, immediately before <c>EndEpisode</c>.
    /// </summary>
    public void FlushEpisode(EpisodeOutcome reason)
    {
        if (_flushed) return;
        _flushed = true;

        // --- Record outcome ---
        _totalEpisodes++;
        switch (reason)
        {
            case EpisodeOutcome.Success_TargetReached: _successCount++;       break;
            case EpisodeOutcome.Crash_Obstacle:        _crashObstacleCount++; break;
            case EpisodeOutcome.Safety_ExcessiveTilt:   _excessiveTiltCount++; break;
            case EpisodeOutcome.Safety_BoundaryLeft:    _boundaryLeftCount++;  break;
            case EpisodeOutcome.Timeout:               _timeoutCount++;       break;
        }

        // --- TensorBoard: episode reward totals ---
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

        // --- TensorBoard: outcome percentages & lesson index ---
        float total = _totalEpisodes;
        stats.Add("Outcomes/Success",        _successCount       / total);
        stats.Add("Outcomes/Crash_Obstacle", _crashObstacleCount / total);
        stats.Add("Outcomes/ExcessiveTilt",  _excessiveTiltCount / total);
        stats.Add("Outcomes/BoundaryLeft",   _boundaryLeftCount  / total);
        stats.Add("Outcomes/Timeout",        _timeoutCount       / total);
        stats.Add("Environment/LessonIndex", (int)_currentLesson);

        // --- Reset reward accumulators (outcome counters persist per lesson) ---
        _totalDeltaDistance     = 0f;
        _totalProximity         = 0f;
        _totalEnergy            = 0f;
        _totalSmoothness        = 0f;
        _totalTilt              = 0f;
        _totalAngularVelocity   = 0f;
        _totalVelocityAlignment = 0f;
        _totalTime              = 0f;
        _totalFastApproach      = 0f;

        // --- Inspector: outcome rates ---
        const string pct = "P1";
        debugCurrentLesson     = _currentLesson.ToString();
        debugTotalEpisodes     = _totalEpisodes.ToString();
        debugSuccessRate       = (_successCount       / total).ToString(pct);
        debugCrashObstacleRate = (_crashObstacleCount / total).ToString(pct);
        debugExcessiveTiltRate = (_excessiveTiltCount / total).ToString(pct);
        debugBoundaryLeftRate  = (_boundaryLeftCount  / total).ToString(pct);
        debugTimeoutRate       = (_timeoutCount       / total).ToString(pct);
    }

    /// <summary>
    /// Called at the start of every episode. Detects MaxStep timeouts
    /// (episodes that ended without an explicit <see cref="FlushEpisode"/>
    /// call) and performs a hard reset of outcome counters when the
    /// curriculum lesson changes.
    /// </summary>
    public void OnNewEpisode(Lesson currentLesson)
    {
        // Detect MaxStep timeout — previous episode was never explicitly flushed
        if (!_flushed)
            FlushEpisode(EpisodeOutcome.Timeout);

        // Hard reset counters on lesson change
        if (currentLesson != _currentLesson)
        {
            ResetOutcomeCounters();
            _currentLesson = currentLesson;
            debugCurrentLesson = _currentLesson.ToString();
        }

        _flushed = false;
    }

    private void ResetOutcomeCounters()
    {
        _totalEpisodes      = 0;
        _successCount       = 0;
        _crashObstacleCount = 0;
        _excessiveTiltCount = 0;
        _boundaryLeftCount  = 0;
        _timeoutCount       = 0;
    }
}
