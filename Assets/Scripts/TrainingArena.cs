using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Unified arena controller that handles both arena identity and curriculum management.
/// Attach to the root GameObject of your Arena Prefab.
///
/// <b>Hierarchy Example:</b>
/// <code>
/// Arena_000 (TrainingArena)
///   ├── Drone (DroneMLAgentBase, Rigidbody, etc.)
///   ├── Target (Landing Pad)
///   ├── Floor
///   ├── Obstacle Spawn Point (HexSwissCheeseObstacleGenerator)
/// </code>
///
/// <b>Key Responsibilities:</b>
/// - Arena identification (assigned by <see cref="ArenaManager"/>)
/// - Shared curriculum data via <see cref="CurriculumPlan"/> ScriptableObject
/// - Episode setup: positioning drone, spawning obstacles
/// - Local component references (single source of truth)
///
/// <b>Discovery Pattern:</b>
/// The drone finds this controller via <c>GetComponentInParent&lt;TrainingArena&gt;()</c>,
/// enabling multiple arena instances with isolated management.
///
/// <b>Shared Data Pattern:</b>
/// All arena instances reference a single <see cref="CurriculumPlan"/> asset.
/// With 100 arenas, there's still only one copy of the lesson data in memory.
/// </summary>
[DisallowMultipleComponent]
public class TrainingArena : MonoBehaviour
{
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
    [Tooltip("When enabled, ignores ML-Agents Academy and uses Manual Lesson Index. For in-Editor testing only.")]
    [SerializeField] private bool useManualLessonPreview;

    [Tooltip("Lesson to preview when Use Manual Lesson Preview is checked.")]
    [SerializeField, Min(0)] private int manualLessonIndex;

    private bool isInitialised;

    // ═══════════════════════════════════════════════════════════════════════
    // PUBLIC PROPERTIES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Unique arena index (0-based). -1 if not yet assigned.</summary>
    public int ArenaId => arenaId;

    /// <summary>The ML-Agent operating in this arena.</summary>
    public Agent Agent => agent;

    /// <summary>The target/landing pad for this arena.</summary>
    public Transform Target => target;

    /// <summary>The obstacle generator for this arena (may be null).</summary>
    public HexSwissCheeseObstacleGenerator ObstacleGenerator => obstacleGenerator;

    /// <summary>The shared curriculum plan asset.</summary>
    public CurriculumPlan CurriculumPlan => curriculumPlan;

    /// <summary>The lesson index selected during the most recent <see cref="SetupEpisode"/> call.</summary>
    public int CurrentLessonIndex { get; private set; }

    // ═══════════════════════════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initialises the arena with its unique ID and discovers local components.
    /// Called by <see cref="ArenaManager"/> after instantiation.
    /// </summary>
    /// <param name="id">Unique arena index assigned by the manager.</param>
    public void Initialise(int id)
    {
        arenaId = id;
        Initialise();
    }

    /// <summary>
    /// Initialises the arena and its components.
    /// Called by the drone agent's <c>Initialize</c> or by <see cref="ArenaManager"/>.
    /// Safe to call multiple times (idempotent).
    /// </summary>
    public void Initialise()
    {
        if (isInitialised) return;
        isInitialised = true;

        // Auto-discover components if not explicitly assigned
        DiscoverComponents();

        // Validate curriculum
        if (curriculumPlan == null)
        {
            Debug.LogWarning($"[TrainingArena {arenaId}] CurriculumPlan is not assigned. " +
                "Create one via Assets → Create → Drone → Curriculum Plan.", this);
        }
        else
        {
            curriculumPlan.ValidateAndWarn();
        }

        // Initialize obstacle generator if present
        if (obstacleGenerator != null)
        {
            obstacleGenerator.Initialise();
        }
    }

    /// <summary>
    /// Automatically discovers agent, target, and obstacle generator
    /// within this arena's hierarchy if not already assigned.
    /// </summary>
    [ContextMenu("Discover Components")]
    public void DiscoverComponents()
    {
        // Find agent
        if (agent == null)
        {
            agent = GetComponentInChildren<Agent>(true);
            if (agent == null)
            {
                Debug.LogWarning($"[TrainingArena {arenaId}] No Agent found in children.", this);
            }
        }

        // Find target (look for objects tagged "Target" or named "Target/LandingPad/Pad")
        if (target == null)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child == transform) continue;
                if (child.CompareTag("Target") ||
                    child.name.Contains("Target") ||
                    child.name.Contains("LandingPad") ||
                    child.name.Contains("Pad"))
                {
                    target = child;
                    break;
                }
            }

            if (target == null)
            {
                Debug.LogWarning($"[TrainingArena {arenaId}] No Target found. Tag or name a child 'Target'.", this);
            }
        }

        // Find obstacle generator
        if (obstacleGenerator == null)
        {
            obstacleGenerator = GetComponentInChildren<HexSwissCheeseObstacleGenerator>(true);
            // Note: obstacleGenerator can be null if this arena doesn't use obstacles
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EPISODE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets up the arena for a new episode: reads the curriculum lesson,
    /// positions the drone, and spawns obstacles.
    /// </summary>
    /// <param name="drone">The drone's transform to reposition.</param>
    /// <param name="defaultPosition">Fallback local position if curriculum is invalid.</param>
    /// <param name="defaultRotation">The drone's default local rotation.</param>
    /// <returns>Max allowed distance from target before the episode terminates.</returns>
    public float SetupEpisode(Transform drone, Vector3 defaultPosition, Quaternion defaultRotation)
    {
        // Clear obstacles from previous episode
        if (obstacleGenerator != null)
        {
            obstacleGenerator.Clear();
        }

        // Read lesson index from ML-Agents Academy (or manual override)
        int lessonIndex = useManualLessonPreview
            ? manualLessonIndex
            : (int)Academy.Instance.EnvironmentParameters.GetWithDefault("lesson", 0f);

        // Get lesson profile from shared curriculum
        if (curriculumPlan == null || curriculumPlan.LessonCount == 0)
        {
            Debug.LogWarning($"[TrainingArena {arenaId}] No curriculum plan or empty lessons — using default position.", this);
            CurrentLessonIndex = 0;
            drone.localPosition = defaultPosition;
            drone.localRotation = defaultRotation;
            return 10f;
        }

        LessonProfile profile = curriculumPlan.GetLessonClamped(lessonIndex, out int clampedIndex);
        CurrentLessonIndex = clampedIndex;

        if (profile == null)
        {
            Debug.LogWarning($"[TrainingArena {arenaId}] Lesson {clampedIndex} is null — using default position.", this);
            drone.localPosition = defaultPosition;
            drone.localRotation = defaultRotation;
            return 10f;
        }

        // Calculate target position (local to arena)
        Vector3 targetPos = target != null ? target.localPosition : defaultPosition;

        // Position the drone relative to target
        if (profile.SpawnRadius > 0f)
        {
            float angle = Random.Range(0f, 2f * Mathf.PI);
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * profile.SpawnRadius;
            drone.localPosition = targetPos + offset + Vector3.up * profile.SpawnHeight;
        }
        else
        {
            drone.localPosition = targetPos + Vector3.up * profile.SpawnHeight;
        }

        // Rotate drone to face the target (yaw only, keep level)
        Vector3 toTarget = targetPos - drone.localPosition;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.001f)
        {
            drone.localRotation = Quaternion.LookRotation(toTarget, Vector3.up);
        }
        else
        {
            drone.localRotation = defaultRotation;
        }

        // Spawn obstacles if configured
        if (obstacleGenerator != null && profile.MaxObstacleCount > 0)
        {
            obstacleGenerator.Generate(
                profile.MaxObstacleCount,
                profile.ObstacleSpawnRadius,
                profile.MinObstacleSpawnRadius,
                profile.HexSpacing,
                profile.HexMinDistance,
                profile.HexObstacleDensity,
                profile.ObstacleMinHeight,
                profile.ObstacleMaxHeight);
        }

        return profile.MaxEpisodeDistance;
    }

    /// <summary>
    /// Resets only this arena's agent. Use for manual testing or custom reset logic.
    /// In normal ML-Agents training, agents call EndEpisode() themselves.
    /// </summary>
    [ContextMenu("Reset Arena")]
    public void ResetArena()
    {
        if (agent != null)
        {
            agent.EndEpisode();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EDITOR
    // ═══════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Auto-discover in editor when references are cleared
        if (agent == null || target == null || obstacleGenerator == null)
        {
            DiscoverComponents();
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Draw arena bounds
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.3f);
        Gizmos.DrawWireCube(transform.position, new Vector3(40f, 10f, 40f));

        // Draw target location
        if (target != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(target.position, 1f);
        }

        // Draw agent location
        if (agent != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(agent.transform.position, 0.5f);
        }

        // Draw spawn area if curriculum is assigned
        if (target != null && curriculumPlan != null && curriculumPlan.LessonCount > 0)
        {
            int previewIndex = useManualLessonPreview ? manualLessonIndex : 0;
            var profile = curriculumPlan.GetLessonClamped(previewIndex, out _);
            if (profile != null && profile.spawnRadius > 0f)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
                DrawWireCircle(target.position + Vector3.up * profile.spawnHeight, profile.spawnRadius);
            }
        }
    }

    private void DrawWireCircle(Vector3 center, float radius, int segments = 32)
    {
        float angleStep = 2f * Mathf.PI / segments;
        Vector3 prevPoint = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep;
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }
    }
#endif
}
