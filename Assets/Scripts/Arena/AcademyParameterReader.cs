using Unity.MLAgents;

/// <summary>
/// Central registry of ML-Agents environment parameter keys and helpers for reading them.
///
/// All parameter keys used in the YAML trainer config (under <c>environment_parameters</c>)
/// must be listed here so there is a single source of truth.
///
/// <b>YAML usage example:</b>
/// <code>
/// environment_parameters:
///   lesson:          { curriculum: ... }
///   reward_profile:  { sampler_type: constant, value: 0 }
///   curriculum:      { sampler_type: constant, value: 0 }
/// </code>
/// </summary>
public static class AcademyParameterReader
{
    // ── Parameter keys ────────────────────────────────────────────────────

    /// <summary>
    /// Selects the active lesson within the current curriculum (0-indexed).
    /// Used by <see cref="AcademyLessonIndexProvider"/>.
    /// </summary>
    public const string LessonKey = "lesson";

    /// <summary>
    /// Selects which <see cref="DroneRewardProfile"/> from the drone's
    /// <c>rewardProfiles</c> list to use for the current training run (0-indexed).
    /// Defaults to 0 (first profile) when not present in the YAML.
    /// </summary>
    public const string RewardProfileKey = "reward_profile";

    /// <summary>
    /// Selects which <see cref="CurriculumPlan"/> from the arena's
    /// <c>curriculumPlans</c> list to use for the current training run (0-indexed).
    /// Defaults to 0 (first plan) when not present in the YAML.
    /// </summary>
    public const string CurriculumKey = "curriculum";

    /// <summary>
    /// Overrides the number of arena instances spawned by <see cref="ArenaManager"/>.
    /// When set, takes precedence over the Inspector value.
    /// A value of 0 (or absent) means "use the Inspector value".
    /// </summary>
    public const string NumberOfArenasKey = "num_arenas";

    // ── Reader helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Reads an integer environment parameter from the ML-Agents Academy.
    /// Returns <paramref name="defaultValue"/> when the Academy is unavailable
    /// or the parameter has not been set.
    /// </summary>
    public static int GetInt(string key, int defaultValue = 0)
    {
        return (int)Academy.Instance.EnvironmentParameters.GetWithDefault(key, defaultValue);
    }
}
