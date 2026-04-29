using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Arena controller responsible for component discovery, curriculum resolution,
/// and per-episode setup.
/// </summary>
[DisallowMultipleComponent]
public class TrainingArena : MonoBehaviour, ITrainingArena
{
    // ========================================================================
    // SERIALIZED FIELDS
    // ========================================================================

    [Header("Arena Identification")]
    [Tooltip("Unique index assigned by ArenaManager at spawn time. -1 if not yet assigned.")]
    [SerializeField] private int arenaId = -1;

    [Header("Curriculum Plans")]
    [Tooltip("Curriculum plans for this arena. Active plan is selected by 'curriculum' (0-indexed).")]
    [SerializeField] private List<CurriculumPlan> curriculumPlans = new List<CurriculumPlan>();

    /// <summary>The curriculum plan currently active, resolved from the list via the 'curriculum' env param.</summary>
    private CurriculumPlan _activeCurriculumPlan;

    [Header("Local References (Auto-discovered)")]
    [Tooltip("The ML-Agent operating in this arena.")]
    [SerializeField] private Agent agent;

    [Tooltip("The target/landing pad for this arena.")]
    [SerializeField] private Transform target;

    [Tooltip("Obstacle generator for this arena (optional).")]
    [SerializeField] private HexSwissCheeseObstacleGenerator obstacleGenerator;

    [Header("Play-Mode Lesson Preview")]
    [Tooltip("Use a fixed lesson index in Play Mode instead of reading it from Academy parameters.")]
    [SerializeField] private bool useManualLessonPreview;

    [Tooltip("Lesson index used when Use Manual Lesson Preview is enabled.")]
    [SerializeField, Min(0)] private int manualLessonIndex;

    [Header("Spawn Orientation")]
    [Tooltip("If enabled, drone spawns with random yaw. Otherwise it faces the target.")]
    [SerializeField] private bool randomSpawnAngle = true;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private bool _isInitialised;

    // ========================================================================
    // ITrainingArena -- PUBLIC PROPERTIES
    // ========================================================================

    /// <inheritdoc/>
    public int ArenaId => arenaId;

    /// <inheritdoc/>
    public Agent Agent => agent;

    /// <inheritdoc/>
    public Transform Target => target;

    /// <inheritdoc/>
    public HexSwissCheeseObstacleGenerator ObstacleGenerator => obstacleGenerator;

    /// <inheritdoc/>
    public CurriculumPlan CurriculumPlan => _activeCurriculumPlan;

    /// <inheritdoc/>
    public int ActiveCurriculumIndex { get; private set; }

    /// <inheritdoc/>
    public int CurrentLessonIndex { get; private set; }

    // ========================================================================
    // ITrainingArena -- INITIALISATION
    // ========================================================================

    /// <inheritdoc/>
    public void Initialise(int id)
    {
        // Keep ID assignment idempotent in case initialization order varies.
        arenaId = id;
        Initialise();
    }

    /// <inheritdoc/>
    public void Initialise()
    {
        if (_isInitialised) return;
        _isInitialised = true;

        if (useManualLessonPreview)
            Debug.Log($"[TrainingArena {arenaId}] Manual lesson preview active — locked to lesson {manualLessonIndex}.", this);

        DiscoverComponents();

        ResolveActiveCurriculumPlan();

        if (_activeCurriculumPlan == null)
        {
            Debug.LogWarning(
                $"[TrainingArena {arenaId}] No active CurriculumPlan resolved. " +
                "Add CurriculumPlan assets to the curriculumPlans list in the Inspector.", this);
        }
        else
        {
            _activeCurriculumPlan.ValidateAndWarn();
        }

        obstacleGenerator?.Initialise(MaxObstacleCapacityAcrossLessons());
    }

    // ========================================================================
    // CURRICULUM RESOLUTION
    // ========================================================================

    /// <summary>
    /// Resolves the active curriculum plan from the 'curriculum' Academy parameter.
    /// </summary>
    private void ResolveActiveCurriculumPlan()
    {
        if (curriculumPlans == null || curriculumPlans.Count == 0)
            return;

        int idx = AcademyParameterReader.GetInt(AcademyParameterReader.CurriculumKey, 0);
        idx = Mathf.Clamp(idx, 0, curriculumPlans.Count - 1);
        ActiveCurriculumIndex = idx;
        _activeCurriculumPlan = curriculumPlans[idx];
    }

    /// <summary>Resolves lesson index for the next episode.</summary>
    private int ResolveLessonIndex()
    {
        if (useManualLessonPreview)
            return manualLessonIndex;

        return AcademyParameterReader.GetInt(AcademyParameterReader.LessonKey);
    }

    // ========================================================================
    // COMPONENT DISCOVERY
    // ========================================================================

    /// <summary>Auto-discovers missing local references in children.</summary>
    [ContextMenu("Discover Components")]
    public void DiscoverComponents()
    {
        if (agent == null)
        {
            agent = GetComponentInChildren<Agent>(true);
            if (agent == null)
                Debug.LogWarning($"[TrainingArena {arenaId}] No Agent found in children.", this);
        }

        if (target == null)
        {
            target = FindTarget();
            if (target == null)
                Debug.LogWarning(
                    $"[TrainingArena {arenaId}] No Target found. " +
                    "Tag a child GameObject with the \"Target\" tag.", this);
        }

        if (obstacleGenerator == null)
            obstacleGenerator = GetComponentInChildren<HexSwissCheeseObstacleGenerator>(true);
    }

    /// <summary>
    /// Returns the highest <see cref="LessonProfile.MaxObstacleCount"/> across all lessons
    /// in the curriculum, so the obstacle pool is pre-sized to handle any lesson without
    /// runtime allocations. Returns 0 when no curriculum is assigned.
    /// </summary>
    private int MaxObstacleCapacityAcrossLessons()
    {
        int max = 0;
        if (curriculumPlans == null) return 0;

        foreach (var plan in curriculumPlans)
        {
            if (plan == null) continue;
            for (int i = 0; i < plan.LessonCount; i++)
            {
                LessonProfile profile = plan.GetLesson(i);
                if (profile != null && profile.MaxObstacleCount > max)
                    max = profile.MaxObstacleCount;
            }
        }
        return max;
    }

    /// <summary>
    /// Finds the first child tagged "Target".
    /// Falls back to a name-contains search only when no tagged object exists,
    /// supporting legacy prefabs that have not been retagged yet.
    /// </summary>
    private Transform FindTarget()
    {
        // Primary pass: tag-based (avoids string allocations per child)
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child != transform && child.CompareTag("Target"))
                return child;
        }

        // Fallback pass: name-based (legacy prefab support — tag objects with "Target" to remove this scan)
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform) continue;
            string n = child.name;
            if (n.Contains("Target") || n.Contains("LandingPad") || n.Contains("Landing Pad"))
            {
                Debug.LogWarning(
                    $"[TrainingArena {arenaId}] Found target by name (\"{child.name}\") instead of tag. " +
                    "Tag the GameObject with \"Target\" to silence this warning.", this);
                return child;
            }
        }

        return null;
    }

    // ========================================================================
    // ITrainingArena -- EPISODE MANAGEMENT
    // ========================================================================

    /// <inheritdoc/>
    public float SetupEpisode(Transform drone, Vector3 defaultPosition, Quaternion defaultRotation)
    {
        ResolveActiveCurriculumPlan();

        float result = ArenaEpisodeSetup.Execute(
            arenaId,
            ResolveLessonIndex(),
            _activeCurriculumPlan,
            drone,
            target,
            obstacleGenerator,
            defaultPosition,
            defaultRotation,
            randomSpawnAngle,
            out int lessonIndex);

        CurrentLessonIndex = lessonIndex;
        return result;
    }

    /// <summary>
    /// Resets this arena's agent. Useful for manual testing or custom reset logic.
    /// During normal ML-Agents training, agents call EndEpisode() themselves.
    /// </summary>
    [ContextMenu("Reset Arena")]
    public void ResetArena()
    {
        agent?.EndEpisode();
    }

    // ========================================================================
    // EDITOR
    // ========================================================================

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Only run discovery in Edit mode. During Play mode the initialisation pipeline
        // owns component references; running DiscoverComponents() here would fight
        // serialized assignments and cause flicker or null-ref errors.
        if (Application.isPlaying) return;

        if (agent == null || target == null || obstacleGenerator == null)
            DiscoverComponents();
    }

    private void OnDrawGizmosSelected()
    {
        // Target marker
        if (target != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(target.position, 1f);
        }

        // Agent marker
        if (agent != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(agent.transform.position, 0.5f);
        }

        // Spawn-radius ring: manual index when preview is on,
        // current lesson during Play, lesson 0 in Edit mode.
        CurriculumPlan gizmoPlan = _activeCurriculumPlan
            ?? (curriculumPlans != null && curriculumPlans.Count > 0 ? curriculumPlans[0] : null);

        if (target != null && gizmoPlan != null && gizmoPlan.LessonCount > 0)
        {
            int drawIndex = useManualLessonPreview
                ? manualLessonIndex
                : (Application.isPlaying ? CurrentLessonIndex : 0);
            LessonProfile profile = gizmoPlan.GetLessonClamped(drawIndex, out _);

            if (profile != null && profile.SpawnRadius > 0f)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
                DrawWireCircle(
                    target.position + Vector3.up * profile.SpawnHeight,
                    profile.SpawnRadius);
            }
        }
    }

    private static void DrawWireCircle(Vector3 center, float radius, int segments = 32)
    {
        float step = 2f * Mathf.PI / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float a = i * step;
            Vector3 pt = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, pt);
            prev = pt;
        }
    }
#endif
}
