using Unity.MLAgents;

/// <summary>
/// Live implementation: reads the <c>lesson</c> parameter from the ML-Agents
/// <see cref="Academy"/> environment parameters.
/// Used during all normal training and inference runs.
/// </summary>
public sealed class AcademyLessonIndexProvider : ILessonIndexProvider
{
    /// <inheritdoc/>
    public int GetLessonIndex()
        => AcademyParameterReader.GetInt(AcademyParameterReader.LessonKey);
}
