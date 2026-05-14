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
    [SerializeField] private string debugNormalizedDeltaDist;
    [SerializeField] private string debugTime;
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
    private float _totalNormalizedDeltaDistance;
    private float _totalTime;
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
        _totalNormalizedDeltaDistance += summary.NormalizedDeltaDistance;
        _totalTime                    += summary.Time;
        _totalRestlessness            += summary.Restlessness;
        _totalYawDeviation            += summary.YawDeviation;

        // --- Inspector debug (blank when zero) ---
        const string fmt = " 0.00000;-0.00000";
        debugNormalizedDeltaDist = summary.NormalizedDeltaDistance != 0f    ? summary.NormalizedDeltaDistance.ToString(fmt)    : "";
        debugTime                = summary.Time != 0f                       ? summary.Time.ToString(fmt)                       : "";
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
                case EpisodeOutcome.TargetReached:  successCount++;       break;
                case EpisodeOutcome.Crash:         crashCount++;         break;
                case EpisodeOutcome.ExcessiveTilt: excessiveTiltCount++; break;
                case EpisodeOutcome.BoundaryLeft:  boundaryLeftCount++;  break;
                case EpisodeOutcome.Timeout:               timeoutCount++;       break;
            }
        }

        float windowCount = _outcomeWindow.Count;

        // --- TensorBoard: episode reward totals ---
        var stats = Academy.Instance.StatsRecorder;
        if (_totalNormalizedDeltaDistance != 0f)  stats.Add("Rewards/NormalizedDeltaDistance", _totalNormalizedDeltaDistance);
        if (_totalTime != 0f)                     stats.Add("Rewards/Time",                    _totalTime);
        if (_totalRestlessness != 0f)             stats.Add("Rewards/Restlessness",           _totalRestlessness);
        if (_totalYawDeviation != 0f)             stats.Add("Rewards/YawDeviation",            _totalYawDeviation);

        // --- TensorBoard: rolling-window outcome percentages ---
        stats.Add("Outcomes/Success",        successCount       / windowCount);
        stats.Add("Outcomes/Crash",          crashCount         / windowCount);
        stats.Add("Outcomes/ExcessiveTilt",  excessiveTiltCount / windowCount);
        stats.Add("Outcomes/BoundaryLeft",   boundaryLeftCount  / windowCount);
        stats.Add("Outcomes/Timeout",        timeoutCount       / windowCount);

        // --- Reset reward accumulators (outcome counters persist per lesson) ---
        _totalNormalizedDeltaDistance = 0f;
        _totalTime                    = 0f;
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
