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
