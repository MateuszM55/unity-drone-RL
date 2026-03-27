using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Reads the ML-Agents curriculum, positions the drone at the correct
/// spawn point for the current <see cref="Lesson"/>, and manages
/// per-episode obstacle generation via <see cref="PoissonObstacleGenerator"/>.
///
/// Attach to the same GameObject as the drone agent.
/// Call <see cref="Initialise"/> once, then <see cref="SetupEpisode"/>
/// at the start of every episode.
/// </summary>
[RequireComponent(typeof(PoissonObstacleGenerator))]
public class DroneCurriculumManager : MonoBehaviour
{
    [Header("Training")]
    [SerializeField] private Transform target;
    [Tooltip("Max distance from target before episode ends (Landing & Navigation lessons).")]
    [SerializeField] private float nearMaxEpisodeDistance = 10f;
    [Tooltip("Max distance from target before episode ends (FarNavigation lesson).")]
    [SerializeField] private float farMaxEpisodeDistance = 20f;

    [Header("Spawn / Curriculum")]
    [Tooltip("Height above the target at which the drone spawns.")]
    [SerializeField] private float spawnHeight = 3f;
    [Tooltip("Spawn distance for the Navigation lesson.")]
    [SerializeField] private float navigationSpawnDistance = 5f;
    [Tooltip("Spawn distance for the FarNavigation lesson.")]
    [SerializeField] private float farNavigationSpawnDistance = 15f;

    private PoissonObstacleGenerator obstacleGenerator;

    /// <summary>The target transform assigned in the Inspector.</summary>
    public Transform Target => target;

    /// <summary>The lesson selected during the most recent <see cref="SetupEpisode"/> call.</summary>
    public Lesson CurrentLesson { get; private set; }

    /// <summary>
    /// Caches component references and pre-allocates the obstacle pool.
    /// Call once from the agent's <c>Initialize</c>.
    /// </summary>
    public void Initialise(Transform poolParent)
    {
        obstacleGenerator = GetComponent<PoissonObstacleGenerator>();
        obstacleGenerator.Initialise(poolParent);
    }

    /// <summary>
    /// Reads the current curriculum lesson, positions the drone, clears /
    /// spawns obstacles, and returns the max episode distance for this lesson.
    /// </summary>
    /// <param name="drone">The drone's transform to reposition.</param>
    /// <param name="startPosition">The drone's default local position (used as fallback).</param>
    /// <param name="startRotation">The drone's default local rotation.</param>
    /// <returns>Max allowed distance from target before the episode is terminated.</returns>
    public float SetupEpisode(Transform drone, Vector3 startPosition, Quaternion startRotation)
    {
        // Remove obstacles from the previous episode
        obstacleGenerator.Clear();

        // Read current lesson from curriculum
        CurrentLesson = (Lesson)(int)Academy.Instance.EnvironmentParameters
            .GetWithDefault("lesson", 0f);

        // Adjust max episode distance per lesson
        float maxEpisodeDistance = CurrentLesson == Lesson.FarNavigation || CurrentLesson == Lesson.Obstacles
            ? farMaxEpisodeDistance
            : nearMaxEpisodeDistance;

        Vector3 targetPos = target != null ? target.localPosition : startPosition;

        switch (CurrentLesson)
        {
            case Lesson.Landing:
                // Start directly above the target
                drone.localPosition = targetPos + Vector3.up * spawnHeight;
                break;

            case Lesson.Navigation:
            {
                // Start at a random point on a circle around the target
                float angle = Random.Range(0f, 2f * Mathf.PI);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * navigationSpawnDistance;
                drone.localPosition = targetPos + offset + Vector3.up * spawnHeight;
                break;
            }

            case Lesson.FarNavigation:
            {
                // Start at a random point on a larger circle
                float angle = Random.Range(0f, 2f * Mathf.PI);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * farNavigationSpawnDistance;
                drone.localPosition = targetPos + offset + Vector3.up * spawnHeight;
                break;
            }

            case Lesson.Obstacles:
            {
                // Start far away, same as FarNavigation
                float angle = Random.Range(0f, 2f * Mathf.PI);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * farNavigationSpawnDistance;
                drone.localPosition = targetPos + offset + Vector3.up * spawnHeight;

                // Spawn random obstacles inside the max-distance circle
                obstacleGenerator.Generate(targetPos, drone.parent);
                break;
            }

            default:
                drone.localPosition = startPosition;
                break;
        }

        drone.localRotation = startRotation;
        return maxEpisodeDistance;
    }
}
