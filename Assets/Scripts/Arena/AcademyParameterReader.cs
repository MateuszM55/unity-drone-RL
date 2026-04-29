using Unity.MLAgents;

/// <summary>
/// Central place for ML-Agents environment parameter keys.
/// </summary>
public static class AcademyParameterReader
{
    // ── Parameter keys ────────────────────────────────────────────────────

    /// <summary>
    /// Selects the active lesson within the current curriculum (0-indexed).
    /// Read directly by <see cref="TrainingArena"/> during episode setup.
    /// </summary>
    public const string LessonKey = "lesson";

    /// <summary>Selects active reward profile (0-indexed).</summary>
    public const string RewardProfileKey = "reward_profile";

    /// <summary>Selects active curriculum plan (0-indexed).</summary>
    public const string CurriculumKey = "curriculum";

    /// <summary>Overrides arena count. Value 0 means "use Inspector value".</summary>
    public const string NumberOfArenasKey = "num_arenas";

    // ── Reader helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Reads an integer environment parameter from the ML-Agents Academy.
    /// Returns <paramref name="defaultValue"/> when the Academy is unavailable
    /// or the parameter has not been set.
    /// </summary>
    public static int GetInt(string key, int defaultValue = 256)
    {
        return (int)Academy.Instance.EnvironmentParameters.GetWithDefault(key, defaultValue);
    }
}
