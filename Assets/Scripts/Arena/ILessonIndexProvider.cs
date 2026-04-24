using Unity.MLAgents;

/// <summary>
/// Strategy interface that resolves the current curriculum lesson index.
///
/// Implementations swap freely without touching <see cref="TrainingArena"/>:
/// <list type="bullet">
///   <item><see cref="AcademyLessonIndexProvider"/> — reads from the ML-Agents Academy (default during training).</item>
///   <item><see cref="ManualLessonIndexProvider"/> — returns a fixed value (Editor preview / unit tests).</item>
/// </list>
///
/// <b>Why a strategy instead of a bool flag?</b>
/// A bool + an int field conflates two concerns (how to get the value vs. what the value is)
/// and cannot be extended without modifying <see cref="TrainingArena"/>.
/// A strategy object is open for extension and trivially mockable.
/// </summary>
public interface ILessonIndexProvider
{
    /// <summary>Returns the lesson index to use for the current episode.</summary>
    int GetLessonIndex();
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Live implementation: reads the <c>lesson</c> parameter from the ML-Agents
/// <see cref="Academy"/> environment parameters.
/// Used during all normal training and inference runs.
/// </summary>
public sealed class AcademyLessonIndexProvider : ILessonIndexProvider
{
    /// <summary>
    /// Name of the ML-Agents curriculum parameter that carries the lesson index.
    /// Must match the key used in the trainer YAML (e.g. <c>lesson: 0</c>).
    /// </summary>
    public const string ParameterKey = "lesson";

    /// <inheritdoc/>
    public int GetLessonIndex()
        => (int)Academy.Instance.EnvironmentParameters.GetWithDefault(ParameterKey, 0f);
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Editor-preview / test implementation: always returns the index supplied at construction.
/// Assign to <see cref="TrainingArena"/> via <see cref="TrainingArena.SetLessonIndexProvider"/>
/// to preview a specific lesson without starting an ML-Agents training session.
/// </summary>
public sealed class ManualLessonIndexProvider : ILessonIndexProvider
{
    private readonly int _index;

    /// <param name="index">The fixed lesson index to return from every call.</param>
    public ManualLessonIndexProvider(int index) => _index = index;

    /// <inheritdoc/>
    public int GetLessonIndex() => _index;
}
