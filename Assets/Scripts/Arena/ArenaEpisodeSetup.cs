using UnityEngine;

/// <summary>
/// Pure static helper that contains all episode-setup math for a training arena.
///
/// <b>Responsibilities:</b>
/// <list type="bullet">
///   <item>Resolving the lesson profile from a <see cref="CurriculumPlan"/>.</item>
///   <item>Positioning and orienting the drone relative to the target.</item>
///   <item>Delegating obstacle generation to <see cref="HexSwissCheeseObstacleGenerator"/>.</item>
/// </list>
///
/// Keeping this logic here instead of inside <see cref="TrainingArena"/> means:
/// <list type="bullet">
///   <item>The arena MonoBehaviour remains a thin orchestrator.</item>
///   <item>This math is testable without a scene or MonoBehaviour.</item>
///   <item>Alternative arena types can reuse the same logic.</item>
/// </list>
/// </summary>
public static class ArenaEpisodeSetup
{
    // ── Public entry point ────────────────────────────────────────────────

    /// <summary>
    /// Fully sets up one episode: resolves the lesson, positions the drone, spawns obstacles.
    /// </summary>
    /// <param name="arenaId">Arena identifier — used only for log messages.</param>
    /// <param name="lessonIndexProvider">Strategy that returns the current lesson index.</param>
    /// <param name="curriculumPlan">Shared curriculum asset. May be null.</param>
    /// <param name="drone">Drone transform to reposition (local-space coordinates).</param>
    /// <param name="target">Target/landing-pad transform (local-space coordinates). May be null.</param>
    /// <param name="obstacleGenerator">Obstacle generator component. May be null.</param>
    /// <param name="defaultPosition">Fallback drone local position when curriculum is unavailable.</param>
    /// <param name="defaultRotation">Fallback drone local rotation when curriculum is unavailable.</param>
    /// <param name="randomSpawnAngle">When <c>true</c> the drone spawns with a random yaw; when <c>false</c> it faces the target.</param>
    /// <param name="currentLessonIndex">Out: the clamped lesson index that was actually used.</param>
    /// <returns>Max allowed distance from target before the episode should be terminated.</returns>
    public static float Execute(
        int arenaId,
        ILessonIndexProvider lessonIndexProvider,
        CurriculumPlan curriculumPlan,
        Transform drone,
        Transform target,
        HexSwissCheeseObstacleGenerator obstacleGenerator,
        Vector3 defaultPosition,
        Quaternion defaultRotation,
        bool randomSpawnAngle,
        out int currentLessonIndex)
    {
        ClearObstacles(obstacleGenerator);

        int rawIndex = lessonIndexProvider.GetLessonIndex();

        if (!TryResolveLesson(arenaId, curriculumPlan, rawIndex,
                              out LessonProfile profile, out int clampedIndex))
        {
            currentLessonIndex = 0;
                ApplyDefaultPose(drone, defaultPosition, defaultRotation);
                return 10f;
            }

            currentLessonIndex = clampedIndex;

            Vector3 targetLocalPos = target != null ? target.localPosition : defaultPosition;

            PositionDrone(drone, targetLocalPos, profile, defaultRotation, randomSpawnAngle);
        SpawnObstacles(obstacleGenerator, profile);

        return profile.MaxEpisodeDistance;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static void ClearObstacles(HexSwissCheeseObstacleGenerator generator)
    {
        generator?.Clear();
    }

    /// <summary>
    /// Resolves the <see cref="LessonProfile"/> from <paramref name="plan"/>.
    /// Returns <c>false</c> (and logs a warning) when the plan is null, empty, or yields a null profile.
    /// </summary>
    private static bool TryResolveLesson(
        int arenaId,
        CurriculumPlan plan,
        int rawIndex,
        out LessonProfile profile,
        out int clampedIndex)
    {
        profile = null;
        clampedIndex = 0;

        if (plan == null || plan.LessonCount == 0)
        {
            Debug.LogWarning(
                $"[TrainingArena {arenaId}] No curriculum plan or empty lessons " +
                "— using default drone position.");
            return false;
        }

        profile = plan.GetLessonClamped(rawIndex, out clampedIndex);

        if (profile == null)
        {
            Debug.LogWarning(
                $"[TrainingArena {arenaId}] Lesson {clampedIndex} is null " +
                "— using default drone position.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Positions the drone in local space relative to <paramref name="targetLocalPos"/>
    /// according to the spawn parameters in <paramref name="profile"/>.
    /// When <paramref name="randomSpawnAngle"/> is <c>true</c> a random yaw (0–360°) is applied;
    /// otherwise the drone is rotated to face the target.
    /// </summary>
    private static void PositionDrone(
        Transform drone,
        Vector3 targetLocalPos,
        LessonProfile profile,
        Quaternion defaultRotation,
        bool randomSpawnAngle)
    {
        Vector3 spawnPos;

        if (profile.SpawnRadius > 0f)
        {
            float angle = Random.Range(0f, 2f * Mathf.PI);
            Vector3 radialOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * profile.SpawnRadius;
            spawnPos = targetLocalPos + radialOffset + Vector3.up * profile.SpawnHeight;
        }
        else
        {
            spawnPos = targetLocalPos + Vector3.up * profile.SpawnHeight;
        }

        drone.localPosition = spawnPos;

        if (randomSpawnAngle)
        {
            // Randomise yaw (rotation around Y axis) across full 360 degrees.
            float randomYaw = Random.Range(0f, 360f);
            drone.localRotation = Quaternion.Euler(0f, randomYaw, 0f);
        }
        else
        {
            // Face the target (horizontal plane only).
            Vector3 toTarget = targetLocalPos - spawnPos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
                drone.localRotation = Quaternion.LookRotation(toTarget, Vector3.up);
            else
                drone.localRotation = defaultRotation;
        }
    }

    /// <summary>
    /// Spawns obstacles via <paramref name="generator"/> when the profile requires them.
    /// Does nothing if the generator is null or the profile requests zero obstacles.
    /// <para>
    /// <see cref="LessonProfile.MaxObstacleCount"/> is passed directly to
    /// <see cref="HexSwissCheeseObstacleGenerator.Generate"/> as the upper-bound count.
    /// The generator is responsible for the actual placement; it may produce fewer
    /// obstacles if the available area cannot accommodate the full count.
    /// Height is determined by the Inspector fields on the generator.
    /// </para>
    /// </summary>
    private static void SpawnObstacles(
        HexSwissCheeseObstacleGenerator generator,
        LessonProfile profile)
    {
        if (generator == null || profile.MaxObstacleCount <= 0)
            return;

        generator.Generate(
            profile.MaxObstacleCount,
            profile.ObstacleSpawnRadius,
            profile.MinObstacleSpawnRadius,
            profile.HexSpacing,
            profile.HexMinDistance,
            profile.HexObstacleDensity);
    }

    /// <summary>Resets the drone to the supplied default local pose.</summary>
    private static void ApplyDefaultPose(Transform drone, Vector3 position, Quaternion rotation)
    {
        drone.localPosition = position;
        drone.localRotation = rotation;
    }
}
