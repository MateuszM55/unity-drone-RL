using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// Factory component that spawns multiple training arenas at runtime and
/// arranges them in a 2-D grid on the XZ plane.
///
/// Attach to an empty "Environment Manager" GameObject in the scene.
///
/// <b>Setup Instructions:</b>
/// <list type="number">
///   <item>Create an Arena Prefab containing:
///     <list type="bullet">
///       <item>Arena root with TrainingArena component.</item>
///       <item>Drone agent (child).</item>
///       <item>Target/Landing pad (child) -- tagged "Target".</item>
///       <item>Ground/Floor (child).</item>
///       <item>Obstacle Spawn Point with HexSwissCheeseObstacleGenerator (child, optional).</item>
///       <item>Any walls or boundaries (children).</item>
///     </list>
///   </item>
///   <item>Ensure all positioning in agent scripts uses local coordinates.</item>
///   <item>Assign the prefab to arenaPrefab.</item>
///   <item>Set numberOfArenas to the desired count.</item>
///   <item>Arenas are spawned in a grid pattern on Awake.</item>
/// </list>
///
/// All agents in spawned arenas share the same Behavior Name (set in the prefab),
/// enabling ML-Agents to collect experiences from all instances simultaneously.
/// </summary>
[DisallowMultipleComponent]
public class ArenaManager : MonoBehaviour
{
    // ========================================================================
    // SERIALIZED FIELDS
    // ========================================================================

    [Header("Arena Template")]
    [Tooltip("Prefab containing the complete training arena (Drone, Target, Floor, Obstacles).")]
    [SerializeField] private GameObject arenaPrefab;

    [Header("Spawning Configuration")]
    [Tooltip("Number of arena instances to spawn. Can be overridden at runtime via the 'num_arenas' " +
             "environment parameter in the YAML trainer config (0 or absent = use this Inspector value).")]
    [SerializeField, Min(1)] private int numberOfArenas = 1;

    [Tooltip("Distance between arena centres. Should be large enough to prevent inter-arena interference.")]
    [SerializeField, Min(10f)] private float arenaSpacing = 50f;

    [Tooltip("Maximum number of arenas per row before starting a new row.")]
    [SerializeField, Min(1)] private int arenasPerRow = 5;

    [Header("Runtime Spawned & Editor Placeholder Arenas")]
    [Tooltip("Tracks spawned instances at runtime. In the Editor you can assign a manually-placed " +
             "template arena here so it is automatically cleaned up and replaced when Play is pressed.")]
    [SerializeField] private TrainingArena[] spawnedArenas;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    /// <summary>
    /// Prevents ClearArenas from being invoked re-entrantly (e.g. if SpawnArenas is
    /// called a second time before the first batch of Destroy calls has flushed).
    /// </summary>
    private bool _isSpawning;

    /// <summary>
    /// The resolved arena count used for the most recent spawn (Inspector value or env-param override).
    /// Stored so <see cref="CalculateGridPosition"/> and Gizmos stay consistent during a spawn pass.
    /// </summary>
    private int _effectiveArenaCount;

    // ========================================================================
    // PUBLIC API
    // ========================================================================

    /// <summary>Number of currently active arena instances.</summary>
    public int ArenaCount => spawnedArenas?.Length ?? 0;

    /// <summary>Read-only access to the spawned arena instances for external systems.</summary>
    public TrainingArena[] SpawnedArenas => spawnedArenas;

    /// <summary>Gets the arena at <paramref name="index"/>, or null when out of range.</summary>
    public TrainingArena GetArena(int index)
    {
        if (spawnedArenas == null || index < 0 || index >= spawnedArenas.Length)
            return null;
        return spawnedArenas[index];
    }

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    private void Awake()
    {
        SpawnArenas();
    }

    // ========================================================================
    // ARENA SPAWNING
    // ========================================================================

    /// <summary>
    /// Destroys all existing arena instances then spawns a fresh grid.
    /// Safe to call from the Inspector context menu or from other scripts.
    /// </summary>
    [ContextMenu("Spawn Arenas")]
    public void SpawnArenas()
    {
        if (arenaPrefab == null)
        {
            Debug.LogError("[ArenaManager] Arena Prefab is not assigned!", this);
            return;
        }

        if (_isSpawning)
        {
            Debug.LogWarning("[ArenaManager] SpawnArenas called while already spawning — ignored.", this);
            return;
        }

        _isSpawning = true;

        try
        {
            ClearArenas();

            int effectiveCount = ResolveArenaCount();
            _effectiveArenaCount = effectiveCount;
            var validArenas = new System.Collections.Generic.List<TrainingArena>(effectiveCount);

            for (int i = 0; i < effectiveCount; i++)
            {
                Vector3 position = CalculateGridPosition(i);
                GameObject arenaGO = Instantiate(arenaPrefab, position, Quaternion.identity, transform);
                arenaGO.name = $"Arena_{i:D3}";

                TrainingArena arena = arenaGO.GetComponent<TrainingArena>();
                if (arena == null)
                {
                    Debug.LogError(
                        $"[ArenaManager] Arena prefab is missing TrainingArena component! " +
                        $"Arena_{i:D3} will not be spawned. Fix the prefab and call SpawnArenas again.", this);
                    Destroy(arenaGO);
                    continue;
                }

                arena.Initialise(i);
                validArenas.Add(arena);
            }

            spawnedArenas = validArenas.ToArray();
            Academy.Instance.StatsRecorder.Add("Config/NumArenas", spawnedArenas.Length);
            Debug.Log($"[ArenaManager] Spawned {spawnedArenas.Length} arena(s) with {arenaSpacing}m spacing.", this);
        }
        finally
        {
            _isSpawning = false;
        }
    }

    /// <summary>
    /// Destroys all spawned arena instances and clears the tracking array.
    /// Safe to call when there are no arenas (no-op).
    /// </summary>
    [ContextMenu("Clear Arenas")]
    public void ClearArenas()
    {
        if (spawnedArenas == null) return;

        foreach (TrainingArena arena in spawnedArenas)
        {
            if (arena == null || arena.gameObject == null) continue;

            if (Application.isPlaying)
                Destroy(arena.gameObject);
            else
                DestroyImmediate(arena.gameObject);
        }

        spawnedArenas = null;
    }

    // ========================================================================
    // GRID LAYOUT
    // ========================================================================

    /// <summary>
    /// Returns the number of arenas to spawn, preferring the <c>num_arenas</c>
    /// ML-Agents environment parameter when it is set to a positive value,
    /// otherwise falling back to the Inspector <see cref="numberOfArenas"/> field.
    /// </summary>
    private int ResolveArenaCount()
    {
        int envParam = AcademyParameterReader.GetInt(AcademyParameterReader.NumberOfArenasKey, 0);
        if (envParam > 0)
        {
            Debug.Log($"[ArenaManager] num_arenas env param = {envParam} (overrides Inspector value {numberOfArenas}).", this);
            return envParam;
        }
        return numberOfArenas;
    }

    /// <summary>
    /// Calculates the world position for the arena at <paramref name="index"/>.
    /// Arenas are arranged in a grid on the XZ plane, centred around this
    /// manager's transform position.
    /// </summary>
    /// <param name="index">Zero-based arena index.</param>
    /// <returns>World position for the arena's root transform.</returns>
    private Vector3 CalculateGridPosition(int index)
    {
        int row = index / arenasPerRow;
        int col = index % arenasPerRow;

        int count = _effectiveArenaCount > 0 ? _effectiveArenaCount : numberOfArenas;

        // Number of columns in the last (possibly partial) row determines total width.
        int columnsInGrid = Mathf.Min(count, arenasPerRow);

        // Prevent integer division truncation: cast numerator to float before dividing.
        int totalRows = Mathf.CeilToInt((float)count / arenasPerRow);

        float totalWidth  = (columnsInGrid - 1) * arenaSpacing;
        float totalDepth  = (totalRows - 1) * arenaSpacing;

        float x = col * arenaSpacing - totalWidth  * 0.5f;
        float z = row * arenaSpacing - totalDepth  * 0.5f;

        return transform.position + new Vector3(x, 0f, z);
    }

    // ========================================================================
    // EDITOR
    // ========================================================================

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (arenaPrefab == null) return;

        Gizmos.color = new Color(0f, 1f, 0.5f, 0.8f);

        int count = _effectiveArenaCount > 0 ? _effectiveArenaCount : numberOfArenas;
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = CalculateGridPosition(i);
            Gizmos.DrawSphere(pos, 1f);
            UnityEditor.Handles.Label(pos + Vector3.up * 2f, $"Arena {i}");
        }
    }
#endif
}
