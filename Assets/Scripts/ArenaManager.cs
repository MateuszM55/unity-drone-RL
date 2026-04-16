using UnityEngine;

/// <summary>
/// Factory component that spawns multiple training arenas at runtime.
/// Attach to an empty "Environment Manager" GameObject in the scene.
///
/// <b>Setup Instructions:</b>
/// 1. Create an Arena Prefab containing:
///    - Arena root with <see cref="TrainingArena"/> component
///    - Drone agent (child)
///    - Target/Landing pad (child)
///    - Ground/Floor (child)
///    - Obstacle Spawn Point with <see cref="HexSwissCheeseObstacleGenerator"/> (child)
///    - Any walls or boundaries (children)
/// 2. Ensure all positioning in agent scripts uses local coordinates.
/// 3. Assign the prefab to <see cref="arenaPrefab"/>.
/// 4. Set <see cref="numberOfArenas"/> to the desired count.
/// 5. Arenas are spawned in a grid pattern on Awake().
///
/// All agents in spawned arenas share the same Behavior Name (set in the prefab),
/// enabling ML-Agents to collect experiences from all instances simultaneously.
/// </summary>
[DisallowMultipleComponent]
public class ArenaManager : MonoBehaviour
{
    [Header("Arena Template")]
    [Tooltip("Prefab containing the complete training arena (Drone, Target, Floor, Obstacles).")]
    [SerializeField] private GameObject arenaPrefab;

    [Header("Spawning Configuration")]
    [Tooltip("Number of arena instances to spawn.")]
    [SerializeField, Min(1)] private int numberOfArenas = 1;

    [Tooltip("Distance between arena centers. Should be large enough to prevent inter-arena interference.")]
    [SerializeField, Min(10f)] private float arenaSpacing = 50f;

    [Tooltip("Maximum arenas per row before starting a new row.")]
    [SerializeField, Min(1)] private int arenasPerRow = 5;

    [Header("Runtime Spawned & Editor Placeholder Arenas")]
    [Tooltip("Tracks spawned instances at runtime. In the Editor, you can assign a manually placed 'Template' arena here to have it automatically cleaned up and replaced when Play is pressed.")]
    [SerializeField] private TrainingArena[] spawnedArenas;

    /// <summary>Number of active arena instances.</summary>
    public int ArenaCount => spawnedArenas?.Length ?? 0;

    /// <summary>Access to spawned arena instances for external systems.</summary>
    public TrainingArena[] SpawnedArenas => spawnedArenas;

    private void Awake()
    {
        SpawnArenas();
    }

    /// <summary>
    /// Spawns all arena instances in a 2D grid pattern on the XZ plane.
    /// Called automatically on Awake, but can be called manually for testing.
    /// </summary>
    [ContextMenu("Spawn Arenas")]
    public void SpawnArenas()
    {
        if (arenaPrefab == null)
        {
            Debug.LogError("[ArenaManager] Arena Prefab is not assigned!", this);
            return;
        }

        // Clean up any existing arenas
        ClearArenas();

        spawnedArenas = new TrainingArena[numberOfArenas];

        for (int i = 0; i < numberOfArenas; i++)
        {
            Vector3 position = CalculateGridPosition(i);
            GameObject arenaGO = Instantiate(arenaPrefab, position, Quaternion.identity, transform);
            arenaGO.name = $"Arena_{i:D3}";

            // Get or add TrainingArena component
            TrainingArena arena = arenaGO.GetComponent<TrainingArena>();
            if (arena == null)
            {
                arena = arenaGO.AddComponent<TrainingArena>();
            }

            arena.Initialise(i);
            spawnedArenas[i] = arena;
        }

        Debug.Log($"[ArenaManager] Spawned {numberOfArenas} arena(s) with {arenaSpacing}m spacing.");
    }

    /// <summary>
    /// Destroys all spawned arena instances.
    /// </summary>
    [ContextMenu("Clear Arenas")]
    public void ClearArenas()
    {
        if (spawnedArenas == null) return;

        foreach (var arena in spawnedArenas)
        {
            if (arena != null && arena.gameObject != null)
            {
                if (Application.isPlaying)
                    Destroy(arena.gameObject);
                else
                    DestroyImmediate(arena.gameObject);
            }
        }

        spawnedArenas = null;
    }

    /// <summary>
    /// Calculates the world position for an arena at the given index.
    /// Arenas are arranged in a grid on the XZ plane.
    /// </summary>
    /// <param name="index">Zero-based arena index.</param>
    /// <returns>World position for the arena's root transform.</returns>
    private Vector3 CalculateGridPosition(int index)
    {
        int row = index / arenasPerRow;
        int col = index % arenasPerRow;

        // Center the grid around the manager's position
        float totalWidth = (Mathf.Min(numberOfArenas, arenasPerRow) - 1) * arenaSpacing;
        float totalDepth = ((numberOfArenas - 1) / arenasPerRow) * arenaSpacing;

        float x = col * arenaSpacing - totalWidth * 0.5f;
        float z = row * arenaSpacing - totalDepth * 0.5f;

        return transform.position + new Vector3(x, 0f, z);
    }

    /// <summary>
    /// Gets an arena instance by index.
    /// </summary>
    public TrainingArena GetArena(int index)
    {
        if (spawnedArenas == null || index < 0 || index >= spawnedArenas.Length)
            return null;
        return spawnedArenas[index];
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Clamp values to sensible ranges
        numberOfArenas = Mathf.Max(1, numberOfArenas);
        arenaSpacing = Mathf.Max(10f, arenaSpacing);
        arenasPerRow = Mathf.Max(1, arenasPerRow);
    }

    private void OnDrawGizmosSelected()
    {
        if (arenaPrefab == null) return;

        Gizmos.color = new Color(0f, 1f, 0.5f, 0.3f);

        for (int i = 0; i < numberOfArenas; i++)
        {
            Vector3 pos = CalculateGridPosition(i);
            Gizmos.DrawWireCube(pos, new Vector3(arenaSpacing * 0.8f, 5f, arenaSpacing * 0.8f));

            // Draw arena index label
#if UNITY_EDITOR
            UnityEditor.Handles.Label(pos + Vector3.up * 3f, $"Arena {i}");
#endif
        }
    }
#endif
}
