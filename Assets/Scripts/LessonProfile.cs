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
    public int obstacleCount = 0;
    [Tooltip("Radius within which obstacles are placed around the target.")]
    public float obstacleSpawnRadius = 12f;
    [Tooltip("Minimum separation between obstacles (Poisson disk sampling).")]
    public float obstacleMinSeparation = 8f;
}
