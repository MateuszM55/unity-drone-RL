using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Reads the ML-Agents curriculum lesson index, selects the matching
/// <see cref="LessonProfile"/> from <see cref="curriculumPlan"/>, positions
/// the drone, and manages per-episode obstacle generation via either
/// <see cref="PoissonObstacleGenerator"/> or <see cref="HexSwissCheeseObstacleGenerator"/>.
///
/// Attach to the same GameObject as the drone agent.
/// Call <see cref="Initialise"/> once, then <see cref="SetupEpisode"/>
/// at the start of every episode.
/// </summary>
[DisallowMultipleComponent]
public class DroneCurriculumManager : MonoBehaviour
{
    [Header("Training")]
    [SerializeField] private Transform target;

    [Header("Curriculum")]
    [Tooltip("Ordered list of lesson profiles. The curriculum parameter 'lesson_index' selects which profile to use.")]
    [SerializeField] private List<LessonProfile> curriculumPlan = new List<LessonProfile>();

    [Header("Editor Preview")]
    [Tooltip("When enabled, ignores the ML-Agents Academy and uses Manual Lesson Index instead. For in-Editor testing only.")]
    [SerializeField] private bool useManualLessonPreview;
    [Tooltip("Lesson to preview when Use Manual Lesson Preview is checked.")]
    [SerializeField, Min(0)] private int manualLessonIndex;

    [Header("Obstacle Generator")]
    [Tooltip("When true, uses the Hex Swiss Cheese generator instead of Poisson Disk Sampling.")]
    [SerializeField] private bool useHexSwissCheese;

    private PoissonObstacleGenerator poissonGenerator;
    private HexSwissCheeseObstacleGenerator hexGenerator;

    /// <summary>The target transform assigned in the Inspector.</summary>
    public Transform Target => target;

    /// <summary>The lesson index selected during the most recent <see cref="SetupEpisode"/> call.</summary>
    public int CurrentLessonIndex { get; private set; }

    /// <summary>
    /// Caches component references and pre-allocates the obstacle pool.
    /// Call once from the agent's <c>Initialize</c>.
    /// </summary>
    public void Initialise()
    {
        if (useHexSwissCheese)
        {
            hexGenerator = GetComponent<HexSwissCheeseObstacleGenerator>();
            Debug.Assert(hexGenerator != null,
                "[DroneCurriculumManager] useHexSwissCheese is enabled but no HexSwissCheeseObstacleGenerator component was found.");
            hexGenerator.Initialise();
        }
        else
        {
            poissonGenerator = GetComponent<PoissonObstacleGenerator>();
            Debug.Assert(poissonGenerator != null,
                "[DroneCurriculumManager] No PoissonObstacleGenerator component was found.");
            poissonGenerator.Initialise();
        }
    }

    /// <summary>
    /// Reads the current curriculum lesson index, selects the matching
    /// <see cref="LessonProfile"/>, positions the drone, clears / spawns
    /// obstacles, and returns the max episode distance for this lesson.
    /// </summary>
    /// <param name="drone">The drone's transform to reposition.</param>
    /// <param name="startPosition">The drone's default local position (used as fallback).</param>
    /// <param name="startRotation">The drone's default local rotation.</param>
    /// <returns>Max allowed distance from target before the episode is terminated.</returns>
    public float SetupEpisode(Transform drone, Vector3 startPosition, Quaternion startRotation)
    {
        if (useHexSwissCheese)
            hexGenerator.Clear();
        else
            poissonGenerator.Clear();

        int lessonIndex = useManualLessonPreview
            ? manualLessonIndex
            : (int)Academy.Instance.EnvironmentParameters.GetWithDefault("lesson", 0f);
        CurrentLessonIndex = Mathf.Clamp(lessonIndex, 0, Mathf.Max(0, curriculumPlan.Count - 1));

        if (curriculumPlan.Count == 0 || curriculumPlan[CurrentLessonIndex] == null)
        {
            Debug.LogWarning("[DroneCurriculumManager] curriculumPlan is empty or has a null entry — using drone start position.", this);
            drone.localPosition = startPosition;
            drone.localRotation = startRotation;
            return 10f;
        }

        LessonProfile profile = curriculumPlan[CurrentLessonIndex];
        Vector3 targetPos = target != null ? target.localPosition : startPosition;

        // Position the drone
        if (profile.spawnRadius > 0f)
        {
            float angle = Random.Range(0f, 2f * Mathf.PI);
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * profile.spawnRadius;
            drone.localPosition = targetPos + offset + Vector3.up * profile.spawnHeight;
        }
        else
        {
            drone.localPosition = targetPos + Vector3.up * profile.spawnHeight;
        }

        // Spawn obstacles
        if (profile.maxObstacleCount > 0)
        {
            if (useHexSwissCheese)
                hexGenerator.Generate(targetPos, profile.maxObstacleCount,
                    profile.obstacleSpawnRadius, profile.obstacleMinSeparation, profile.hexObstacleDensity);
            else
                poissonGenerator.Generate(targetPos, profile.maxObstacleCount,
                    profile.obstacleSpawnRadius, profile.obstacleMinSeparation);
        }

        drone.localRotation = startRotation;
        return profile.maxEpisodeDistance;
    }
}
