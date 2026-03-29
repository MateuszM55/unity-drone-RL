/// <summary>Categorises the reason a training episode ended.</summary>
public enum EpisodeOutcome
{
    /// <summary>The drone reached the target successfully.</summary>
    Success_TargetReached,
    /// <summary>The drone collided with an obstacle or the environment boundary.</summary>
    Crash_Obstacle,
    /// <summary>The drone fell below the minimum allowed altitude.</summary>
    Crash_Ground,
    /// <summary>The drone exceeded the maximum safe tilt angle.</summary>
    Safety_ExcessiveTilt,
    /// <summary>The drone flew too far from the target.</summary>
    Safety_BoundaryLeft,
    /// <summary>The episode reached the maximum allowed steps.</summary>
    Timeout
}
