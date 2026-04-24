using Unity.MLAgents;

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
