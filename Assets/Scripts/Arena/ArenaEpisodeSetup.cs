using UnityEngine;

/// <summary>
/// Static helper for episode setup: lesson resolution, drone placement, and obstacle spawning.
/// </summary>
public static class ArenaEpisodeSetup
{
    // ── Public entry point ────────────────────────────────────────────────

    /// <summary>
    /// Sets up one episode: resolves lesson, positions drone, and spawns obstacles.
    /// </summary>
    /// <param name="arenaId">Arena identifier for logs.</param>
    /// <param name="lessonIndex">Requested lesson index for this episode.</param>
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
        int lessonIndex,
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

        if (!TryResolveLesson(arenaId, curriculumPlan, lessonIndex,
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
    /// Resolves a lesson profile from <paramref name="plan"/>.
    /// Returns false when plan/profile is missing.
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
    /// Positions the drone in local space using the lesson spawn settings.
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
    /// Spawns obstacles when a generator exists and lesson allows obstacles.
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
