using UnityEngine;

/// <summary>
/// Scriptable-object "data card" for a single curriculum lesson.
///
/// Create profiles via the Unity menu: Assets → Create → Drone → Lesson Profile.
/// Add them in order to the <c>lessons</c> list on <see cref="CurriculumPlan"/>.
///
/// <b>All fields are serialized as private and exposed via read-only properties.</b>
/// Mutating lesson data at runtime would corrupt every arena simultaneously, because
/// all arenas reference this single shared asset instance.
/// </summary>
[CreateAssetMenu(fileName = "NewLessonProfile", menuName = "Drone/Lesson Profile")]
public class LessonProfile : ScriptableObject
{
    [Header("Spawn")]
    [Tooltip("Height above the target at which the drone spawns.")]
    [SerializeField, Min(0f)] private float spawnHeight = 3f;

    [Tooltip("Horizontal distance from the target. 0 = spawn directly above.")]
    [SerializeField, Min(0f)] private float spawnRadius = 0f;

    [Header("Episode")]
    [Tooltip("Max distance from the target before the episode is terminated.")]
    [SerializeField, Min(1f)] private float maxEpisodeDistance = 10f;

    [Header("Obstacles")]
    [Tooltip("Number of obstacles to spawn each episode. 0 = no obstacles.")]
    [SerializeField, Min(0)] private int maxObstacleCount = 0;

    [Tooltip("Outer radius of the obstacle ring around the target.")]
    [SerializeField, Min(0f)] private float obstacleSpawnRadius = 12f;

    [Tooltip("Inner radius — obstacles won't spawn closer than this to the target. Must be less than Obstacle Spawn Radius.")]
    [SerializeField, Min(0f)] private float minObstacleSpawnRadius = 5f;

    [Header("Hex Grid")]
    [Tooltip("Centre-to-centre distance between hex cells. Must be greater than Hex Min Distance.")]
    [SerializeField, Min(0.01f)] private float hexSpacing = 6f;

    [Tooltip("Absolute minimum distance between any two obstacles. Must be less than Hex Spacing.")]
    [SerializeField, Min(0f)] private float hexMinDistance = 4f;

    [Tooltip("Fraction of hex grid points to keep (0 = empty, 1 = all points).")]
    [SerializeField, Range(0f, 1f)] private float hexObstacleDensity = 0.35f;

    [Header("Obstacle Height")]
    [Tooltip("Minimum height for obstacle placement.")]
    [SerializeField, Min(0f)] private float obstacleMinHeight = 3f;

    [Tooltip("Maximum height for obstacle placement. Must be >= Obstacle Min Height.")]
    [SerializeField, Min(0f)] private float obstacleMaxHeight = 3f;

    // ── Read-only properties ──────────────────────────────────────────────

    /// <summary>Height above the target at which the drone spawns.</summary>
    public float SpawnHeight => spawnHeight;

    /// <summary>Horizontal distance from the target. 0 = spawn directly above.</summary>
    public float SpawnRadius => spawnRadius;

    /// <summary>Max distance from the target before the episode is terminated.</summary>
    public float MaxEpisodeDistance => maxEpisodeDistance;

    /// <summary>Number of obstacles to spawn each episode. 0 = no obstacles.</summary>
    public int MaxObstacleCount => maxObstacleCount;

    /// <summary>Outer radius of the obstacle ring around the target.</summary>
    public float ObstacleSpawnRadius => obstacleSpawnRadius;

    /// <summary>Inner radius — obstacles won't spawn closer than this to the target.</summary>
    public float MinObstacleSpawnRadius => minObstacleSpawnRadius;

    /// <summary>Centre-to-centre distance between hex cells.</summary>
    public float HexSpacing => hexSpacing;

    /// <summary>Absolute minimum distance between any two obstacles.</summary>
    public float HexMinDistance => hexMinDistance;

    /// <summary>Fraction of hex grid points to keep (0–1).</summary>
    public float HexObstacleDensity => hexObstacleDensity;

    /// <summary>Minimum height for obstacle placement.</summary>
    public float ObstacleMinHeight => obstacleMinHeight;

    /// <summary>Maximum height for obstacle placement.</summary>
    public float ObstacleMaxHeight => obstacleMaxHeight;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (minObstacleSpawnRadius >= obstacleSpawnRadius)
        {
            Debug.LogWarning(
                $"[LessonProfile '{name}'] minObstacleSpawnRadius ({minObstacleSpawnRadius}) " +
                $"must be less than obstacleSpawnRadius ({obstacleSpawnRadius}).", this);
        }

        if (hexMinDistance >= hexSpacing)
        {
            Debug.LogWarning(
                $"[LessonProfile '{name}'] hexMinDistance ({hexMinDistance}) " +
                $"must be less than hexSpacing ({hexSpacing}). " +
                "This would produce zero or negative jitter in the obstacle generator.", this);
        }

        if (obstacleMaxHeight < obstacleMinHeight)
        {
            Debug.LogWarning(
                $"[LessonProfile '{name}'] obstacleMaxHeight ({obstacleMaxHeight}) " +
                $"must be >= obstacleMinHeight ({obstacleMinHeight}).", this);
        }
    }
#endif
}
