using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared ScriptableObject containing the ordered list of curriculum lessons.
/// All arena instances reference this single asset, eliminating memory duplication
/// and enabling real-time updates across all training environments.
///
/// <b>Usage:</b>
/// 1. Create via Assets → Create → Drone → Curriculum Plan
/// 2. Add <see cref="LessonProfile"/> assets to the <see cref="lessons"/> list in order
/// 3. Assign to all <see cref="ArenaCurriculumManager"/> components (or their prefab)
///
/// <b>Benefits of Shared Data:</b>
/// - 100 arenas share one curriculum definition (no memory duplication)
/// - Changes in the Inspector propagate instantly to all arenas
/// - Easy A/B testing by swapping curriculum assets
/// </summary>
[CreateAssetMenu(fileName = "NewCurriculumPlan", menuName = "Drone/Curriculum Plan")]
public class CurriculumPlan : ScriptableObject
{
    [Header("Lessons")]
    [Tooltip("Ordered list of lesson profiles. The ML-Agents curriculum parameter 'lesson' selects which profile to use (0-indexed).")]
    [SerializeField] private List<LessonProfile> lessons = new List<LessonProfile>();

    /// <summary>Number of lessons in this curriculum.</summary>
    public int LessonCount => lessons.Count;

    /// <summary>
    /// Gets the lesson profile at the specified index.
    /// Returns null if index is out of range or the entry is null.
    /// </summary>
    /// <param name="index">Zero-based lesson index from ML-Agents curriculum.</param>
    public LessonProfile GetLesson(int index)
    {
        if (index < 0 || index >= lessons.Count)
            return null;
        return lessons[index];
    }

    /// <summary>
    /// Gets the lesson profile, clamping the index to valid range.
    /// Returns null only if the curriculum is empty.
    /// </summary>
    /// <param name="index">Lesson index (will be clamped to valid range).</param>
    /// <param name="clampedIndex">The actual index used after clamping.</param>
    public LessonProfile GetLessonClamped(int index, out int clampedIndex)
    {
        if (lessons.Count == 0)
        {
            clampedIndex = 0;
            return null;
        }

        clampedIndex = Mathf.Clamp(index, 0, lessons.Count - 1);
        return lessons[clampedIndex];
    }

    /// <summary>
    /// Returns <c>true</c> if every lesson entry is non-null; <c>false</c> otherwise.
    /// Pure predicate — no side effects, safe to call from any context.
    /// </summary>
    public bool IsValid()
    {
        for (int i = 0; i < lessons.Count; i++)
        {
            if (lessons[i] == null)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Logs a warning for every null lesson entry and returns <c>false</c> if any are found.
    /// Use this during initialisation where user-visible feedback is appropriate.
    /// For silent checks prefer <see cref="IsValid"/>.
    /// </summary>
    /// <returns>True if all lessons are valid; false if any are null.</returns>
    public bool ValidateAndWarn()
    {
        bool valid = true;
        for (int i = 0; i < lessons.Count; i++)
        {
            if (lessons[i] == null)
            {
                Debug.LogWarning($"[CurriculumPlan] Lesson at index {i} is null.", this);
                valid = false;
            }
        }
        return valid;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ValidateAndWarn();
    }
#endif
}
