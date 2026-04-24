using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Unified arena controller that handles arena identity, curriculum management,
/// and per-episode setup.
///
/// Attach to the root GameObject of your Arena Prefab.
///
/// <b>Hierarchy Example:</b>
/// <code>
/// Arena_000 (TrainingArena)
///   * Drone (DroneMLAgentBase, Rigidbody, ...)
///   * Target (Landing Pad)  -- tag or name must contain "Target"
///   * Floor
///   * Obstacle Spawn Point (HexSwissCheeseObstacleGenerator)
/// </code>
///
/// <b>Key Responsibilities:</b>
/// <list type="bullet">
///   <item>Arena identification (assigned by ArenaManager).</item>
///   <item>Shared curriculum data via CurriculumPlan ScriptableObject.</item>
///   <item>Episode setup: positions the drone and spawns obstacles (delegated to ArenaEpisodeSetup).</item>
///   <item>Local component references -- single source of truth.</item>
/// </list>
///
/// <b>Discovery Pattern:</b>
/// The drone finds this controller via GetComponentInParent, enabling multiple arena instances
/// with isolated management.
///
/// <b>Shared Data Pattern:</b>
/// All arena instances reference a single CurriculumPlan asset.
/// With 100 arenas there is still only one copy of the lesson data in memory.
///
/// <b>Lesson-Index Strategy:</b>
/// By default the arena reads the lesson parameter from the ML-Agents Academy (AcademyLessonIndexProvider).
/// Call SetLessonIndexProvider to swap in a ManualLessonIndexProvider for Editor preview or unit tests,
/// replacing the old boolean useManualLessonPreview pattern.
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

    [Header("Shared Curriculum")]
    [Tooltip("ScriptableObject containing the ordered list of lessons. Shared across all arena instances.")]
    [SerializeField] private CurriculumPlan curriculumPlan;

    [Header("Local References (Auto-discovered)")]
    [Tooltip("The ML-Agent operating in this arena.")]
    [SerializeField] private Agent agent;

    [Tooltip("The target/landing pad for this arena.")]
    [SerializeField] private Transform target;

    [Tooltip("Obstacle generator for this arena (optional).")]
    [SerializeField] private HexSwissCheeseObstacleGenerator obstacleGenerator;

    [Header("Editor Preview")]
    [Tooltip("When enabled, overrides the lesson index from the ML-Agents Academy with the value below. For in-Editor testing only -- has no effect during training.")]
    [SerializeField] private bool useManualLessonPreview;

    [Tooltip("Lesson index to use when Use Manual Lesson Preview is enabled.")]
    [SerializeField, Min(0)] private int manualLessonIndex;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private bool _isInitialised;

    /// <summary>
    /// Determines how the current lesson index is resolved at the start of each episode.
    /// Defaults to AcademyLessonIndexProvider (or ManualLessonIndexProvider when
    /// useManualLessonPreview is set). Override via SetLessonIndexProvider for preview or unit tests.
    /// </summary>
    private ILessonIndexProvider _lessonIndexProvider;

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
    public CurriculumPlan CurriculumPlan => curriculumPlan;

    /// <inheritdoc/>
    public int CurrentLessonIndex { get; private set; }

    // ========================================================================
    // CONFIGURATION
    // ========================================================================

    /// <summary>
    /// Replaces the lesson-index resolution strategy.
    /// Must be called before the first SetupEpisode to take effect.
    /// Example: arena.SetLessonIndexProvider(new ManualLessonIndexProvider(2));
    /// </summary>
    /// <param name="provider">Non-null strategy instance.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when provider is null.</exception>
    public void SetLessonIndexProvider(ILessonIndexProvider provider)
    {
        _lessonIndexProvider = provider ?? throw new System.ArgumentNullException(nameof(provider));
    }

    // ========================================================================
    // ITrainingArena -- INITIALISATION
    // ========================================================================

    /// <inheritdoc/>
    public void Initialise(int id)
    {
        arenaId = id;
        Initialise();
    }

    /// <inheritdoc/>
    public void Initialise()
    {
        if (_isInitialised) return;
        _isInitialised = true;

        // Honour any externally-supplied provider first; then respect the inspector toggle;
        // finally fall back to the live Academy provider.
        if (_lessonIndexProvider == null)
        {
            _lessonIndexProvider = useManualLessonPreview
                ? (ILessonIndexProvider)new ManualLessonIndexProvider(manualLessonIndex)
                : new AcademyLessonIndexProvider();
        }

        DiscoverComponents();

        if (curriculumPlan == null)
        {
            Debug.LogWarning(
                $"[TrainingArena {arenaId}] CurriculumPlan is not assigned. " +
                "Create one via Assets > Create > Drone > Curriculum Plan.", this);
        }
        else
        {
            curriculumPlan.ValidateAndWarn();
        }

        obstacleGenerator?.Initialise();
    }

    // ========================================================================
    // COMPONENT DISCOVERY
    // ========================================================================

    /// <summary>
    /// Automatically discovers the agent, target, and obstacle generator
    /// within this arena's child hierarchy when they are not already assigned.
    /// </summary>
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

        // Fallback pass: name-based (legacy prefab support)
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform) continue;
            string n = child.name;
            if (n.Contains("Target") || n.Contains("LandingPad") || n.Contains("Pad"))
                return child;
        }

        return null;
    }

    // ========================================================================
    // ITrainingArena -- EPISODE MANAGEMENT
    // ========================================================================

    /// <inheritdoc/>
    public float SetupEpisode(Transform drone, Vector3 defaultPosition, Quaternion defaultRotation)
    {
        float result = ArenaEpisodeSetup.Execute(
            arenaId,
            _lessonIndexProvider,
            curriculumPlan,
            drone,
            target,
            obstacleGenerator,
            defaultPosition,
            defaultRotation,
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
        // Arena bounds
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.3f);
        Gizmos.DrawWireCube(transform.position, new Vector3(40f, 10f, 40f));

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

        // Spawn-radius ring for the previewed lesson
        if (target != null && curriculumPlan != null && curriculumPlan.LessonCount > 0)
        {
            int previewIndex = useManualLessonPreview ? manualLessonIndex : 0;
            LessonProfile profile = curriculumPlan.GetLessonClamped(previewIndex, out _);

            // Use public properties (SpawnRadius, SpawnHeight) -- the private backing
            // fields are inaccessible from outside LessonProfile.
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
