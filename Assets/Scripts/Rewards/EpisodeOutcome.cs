/// <summary>Categorises the reason a training episode ended.</summary>
public enum EpisodeOutcome
{
    /// <summary>The drone reached the target successfully.</summary>
    TargetReached,
    /// <summary>The drone collided with an obstacle or the environment boundary.</summary>
    Crash,
    /// <summary>The drone exceeded the maximum safe tilt angle.</summary>
    ExcessiveTilt,
    /// <summary>The drone flew too far from the target.</summary>
    BoundaryLeft,
    /// <summary>The episode reached the maximum allowed steps.</summary>
    Timeout
}