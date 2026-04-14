using UnityEngine;

/// <summary>
/// Scriptable-object "data card" for a single curriculum lesson.
///
/// Create profiles via the Unity menu: Assets → Create → Drone → Lesson Profile.
/// Add them in order to the <c>curriculumPlan</c> list on <see cref="DroneCurriculumManager"/>.
/// </summary>
[CreateAssetMenu(fileName = "NewLessonProfile", menuName = "Drone/Lesson Profile")]
public class LessonProfile : ScriptableObject
{
    [Header("Spawn")]
    [Tooltip("Height above the target at which the drone spawns.")]
    public float spawnHeight = 3f;
    [Tooltip("Horizontal distance from the target. 0 = spawn directly above.")]
    public float spawnRadius = 0f;

    [Header("Episode")]
    [Tooltip("Max distance from the target before the episode is terminated.")]
    public float maxEpisodeDistance = 10f;

    [Header("Obstacles")]
    [Tooltip("Number of obstacles to spawn each episode. 0 = no obstacles.")]
    public int maxObstacleCount = 0;
    [Tooltip("Outer radius of the obstacle ring around the target.")]
    public float obstacleSpawnRadius = 12f;
    [Tooltip("Inner radius — obstacles won't spawn closer than this to the target.")]
    public float minObstacleSpawnRadius = 5f;

    [Header("Hex Grid")]
    [Tooltip("Centre-to-centre distance between hex cells. Must be greater than hexMinDistance.")]
    public float hexSpacing = 6f;
    [Tooltip("Absolute minimum distance between any two obstacles. Must be less than hexSpacing.")]
    public float hexMinDistance = 4f;
    [Tooltip("Fraction of hex grid points to keep (0 = empty, 1 = all points).")]
    [Range(0f, 1f)]
    public float hexObstacleDensity = 0.35f;

    [Header("Obstacle Height")]
    [Tooltip("Minimum height for obstacle placement.")]
    public float obstacleMinHeight = 3f;
    [Tooltip("Maximum height for obstacle placement.")]
    public float obstacleMaxHeight = 3f;
}
