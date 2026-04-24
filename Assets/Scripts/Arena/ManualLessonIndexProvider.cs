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
