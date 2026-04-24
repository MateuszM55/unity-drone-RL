using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns obstacles in a ring around a given centre point using the
/// Hexagonal Swiss Cheese algorithm on the XZ plane.
///
/// 1. Lay a staggered hexagonal grid of candidate points.
/// 2. Cookie-cut to a ring (inner / outer radius).
/// 3. Randomly cull points by a density percentage (Swiss cheese).
/// 4. Jitter survivors within a safe radius so the hex pattern disappears,
///    while mathematically guaranteeing no two obstacles violate the
///    minimum separation distance.
///
/// All internal buffers are pre-allocated — zero GC during generation.
///
/// Attach to the Obstacle Spawn Point GameObject inside the Arena Prefab.
/// Obstacles are always spawned around this GameObject's position.
/// Call <see cref="Initialise"/> once, then <see cref="Generate"/> / <see cref="Clear"/>
/// each episode.
///
/// <b>Note on Inspector fields:</b> During RL training, <see cref="TrainingArena"/> calls
/// the full-parameter <see cref="Generate(int,float,float,float,float,float,float,float)"/>
/// overload with per-lesson values from <see cref="LessonProfile"/>, bypassing the Inspector
/// defaults entirely. The Inspector fields are only used by the no-argument
/// <see cref="Generate()"/> overload, which is intended for manual in-Editor testing only.
/// </summary>
[DisallowMultipleComponent]
public class HexSwissCheeseObstacleGenerator : MonoBehaviour
{
    [Header("Obstacle Prefab & Counts")]
    [Tooltip("Prefab to spawn as an obstacle.")]
    [SerializeField] private GameObject obstaclePrefab;

    [Tooltip("Maximum number of obstacles to place per episode (Inspector default — used by no-arg Generate() only).")]
    [SerializeField] private int maxObstacleCount = 20;

    [Header("Spawn Ring (Inspector defaults — used by no-arg Generate() only)")]
    [Tooltip("Outer radius of the obstacle ring around the centre.")]
    [SerializeField] private float spawnRadius = 12f;

    [Tooltip("Inner radius — obstacles won't spawn closer than this to the centre.")]
    [SerializeField] private float minSpawnRadius = 5f;

    [Header("Hex Grid")]
    [Tooltip("Centre-to-centre distance between hex cells. Must be greater than Min Distance.")]
    [SerializeField] private float hexSpacing = 6f;

    [Tooltip("Absolute minimum distance between any two obstacles. Must be less than Hex Spacing.")]
    [SerializeField] private float minDistance = 4f;

    [Header("Swiss Cheese")]
    [Tooltip("Fraction of hex points to keep (0 = empty, 1 = all points).")]
    [SerializeField, Range(0f, 1f)] private float density = 0.35f;

    [Header("Height")]
    [Tooltip("Minimum height for obstacle placement.")]
    [SerializeField] private float minHeight = 3f;

    [Tooltip("Maximum height for obstacle placement.")]
    [SerializeField] private float maxHeight = 3f;

    [Header("Gizmos")]
    [Tooltip("Draw hex grid centre points in the Scene view.")]
    [SerializeField] private bool showGizmos = true;

    [Tooltip("Radius of each gizmo sphere drawn at a grid centre.")]
    [SerializeField] private float gizmoRadius = 0.15f;

    [Tooltip("Colour used for the grid-centre spheres.")]
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 0.5f, 0.8f);

    // ── Object pool ───────────────────────────────────────────────────────
    private readonly List<GameObject> pool = new List<GameObject>();
    private int activeCount;

    // ── Pre-allocated candidate buffer ────────────────────────────────────
    private readonly List<Vector2> candidates = new List<Vector2>();

    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-allocates the object pool.
    /// Call once during setup (e.g. from an Agent's <c>Initialize</c>).
    /// </summary>
    /// <param name="capacityOverride">
    /// Optional pool capacity. When provided (e.g. the maximum <c>MaxObstacleCount</c>
    /// across all curriculum lessons) it supersedes the Inspector <see cref="maxObstacleCount"/>
    /// field, ensuring the pool is large enough for every lesson without runtime allocations.
    /// Pass 0 (default) to use the Inspector value.
    /// </param>
    public void Initialise(int capacityOverride = 0)
    {
        int capacity = capacityOverride > 0 ? capacityOverride : maxObstacleCount;
        if (!ValidateSpacingConstraint(minSpawnRadius, spawnRadius, minDistance, hexSpacing))
        {
            Debug.LogWarning("[HexSwissCheese] Initialise aborted due to invalid spacing constraints.", this);
            return;
        }
        InitPool(capacity);
    }

    /// <summary>
    /// Generates obstacles using the Inspector default fields.
    /// <para><b>For manual in-Editor testing only.</b> During RL training, <see cref="TrainingArena"/>
    /// calls the full-parameter overload with per-lesson values from <see cref="LessonProfile"/>
    /// — the Inspector fields are not used.</para>
    /// </summary>
    public void Generate()
    {
        GenerateInternal(maxObstacleCount, spawnRadius, minSpawnRadius,
                         hexSpacing, minDistance, density, minHeight, maxHeight);
    }

    /// <summary>
    /// Generates obstacles using fully overridden parameters.
    /// Called by <see cref="TrainingArena"/> each episode to apply per-lesson curriculum settings.
    /// </summary>
    public void Generate(int count, float outerR, float innerR,
                         float spacing, float minDist, float fillDensity,
                         float minH, float maxH)
    {
        GenerateInternal(count, outerR, innerR, spacing, minDist, fillDensity, minH, maxH);
    }

    /// <summary>Deactivates all obstacles spawned during the current episode.</summary>
    public void Clear()
    {
        for (int i = 0; i < activeCount; i++)
        {
            if (pool[i] != null)
                pool[i].SetActive(false);
        }
        activeCount = 0;
    }

    // ── Core algorithm ────────────────────────────────────────────────────

    private void GenerateInternal(int count, float outerR, float innerR,
                                  float spacing, float minDist, float fillDensity,
                                  float minH, float maxH)
    {
        if (obstaclePrefab == null) return;

        // Hard guard: minDist must be strictly less than spacing or the jitter buffer
        // goes zero/negative, which breaks the minimum-separation guarantee.
        if (minDist >= spacing)
        {
            Debug.LogError(
                $"[HexSwissCheese] minDist ({minDist}) >= spacing ({spacing}). " +
                "Generation aborted — fix the LessonProfile or Inspector values.", this);
            return;
        }

        Clear();

        float buffer = spacing - minDist;
        float maxJitter = buffer * 0.5f;   // always > 0 after the guard above
        float outerRSqr = outerR * outerR;
        float innerRSqr = innerR * innerR;

        // ── Random rotation for dynamic lanes ────────────────────────────
        float randomAngle = Random.Range(0f, Mathf.PI * 2f);
        float cosA = Mathf.Cos(randomAngle);
        float sinA = Mathf.Sin(randomAngle);

        // ── Phase 1: Build the hexagonal grid ────────────────────────────
        // Row height for a hex grid = spacing * sqrt(3) / 2
        float rowHeight = spacing * 0.8660254f;
        float halfSpacing = spacing * 0.5f;

        candidates.Clear();

        // Grid covers [-outerR, +outerR] in both X and Z
        int cols = Mathf.CeilToInt(2f * outerR / spacing) + 1;
        int rows = Mathf.CeilToInt(2f * outerR / rowHeight) + 1;

        for (int row = 0; row < rows; row++)
        {
            float z = -outerR + row * rowHeight;
            bool oddRow = (row & 1) == 1;

            for (int col = 0; col < cols; col++)
            {
                float x = -outerR + col * spacing;
                if (oddRow) x += halfSpacing;

                // ── Phase 2: Cookie-cut to ring ───────────────────────────
                // Distance check (rotation doesn't change distance)
                float distSqr = x * x + z * z;
                if (distSqr < innerRSqr || distSqr > outerRSqr)
                    continue;

                // Apply the rotation matrix
                float rotatedX = x * cosA - z * sinA;
                float rotatedZ = x * sinA + z * cosA;

                candidates.Add(new Vector2(rotatedX, rotatedZ));
            }
        }

        // ── Phase 3: Exact-count shuffle ──────────────────────────────────
        // Shuffle all candidates, then take exactly the target count instead of
        // coin-flipping each point (which causes binomial variance and empty episodes).
        ShuffleInPlace(candidates);
        int placed = Mathf.Min(Mathf.RoundToInt(candidates.Count * fillDensity), count);

        EnsurePoolCapacity(placed);

        // ── Phase 4: Jitter & spawn ───────────────────────────────────────
        for (int i = 0; i < placed; i++)
        {
            Vector2 pt = candidates[i];

            // Random direction + distance within safe jitter radius
            float angle = Random.Range(0f, 2f * Mathf.PI);
            float jitterDist = Random.Range(0f, maxJitter);
            float jx = Mathf.Cos(angle) * jitterDist;
            float jz = Mathf.Sin(angle) * jitterDist;

            Vector3 pos = transform.position + new Vector3(
                pt.x + jx,
                Random.Range(minH, maxH),
                pt.y + jz);

            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject obstacle = pool[i];
            obstacle.transform.SetPositionAndRotation(pos, rot);
            obstacle.SetActive(true);

            if (obstacle.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        activeCount = placed;
    }

    // ── Pool management ───────────────────────────────────────────────────

    private void InitPool(int capacity)
    {
        if (obstaclePrefab == null) return;

        for (int i = 0; i < capacity; i++)
        {
            GameObject obj = Instantiate(obstaclePrefab, Vector3.zero, Quaternion.identity, transform);
            obj.SetActive(false);
            pool.Add(obj);
        }
    }

    /// <summary>Grows the pool if it has fewer than <paramref name="required"/> entries.</summary>
    private void EnsurePoolCapacity(int required)
    {
        if (obstaclePrefab == null) return;

        while (pool.Count < required)
        {
            GameObject obj = Instantiate(obstaclePrefab, Vector3.zero, Quaternion.identity, transform);
            obj.SetActive(false);
            pool.Add(obj);
        }
    }

    // ── Validation ────────────────────────────────────────────────────────

    /// <summary>
    /// Validates that spawn radii and spacing constraints are physically viable.
    /// Returns <c>false</c> if the configuration is invalid (caller should abort generation).
    /// </summary>
    private bool ValidateSpacingConstraint(float minRadius, float maxRadius, float minDist, float spacing)
    {
        bool valid = true;

        if (minRadius >= maxRadius)
        {
            Debug.LogError(
                $"[HexSwissCheese] minSpawnRadius ({minRadius}) >= spawnRadius ({maxRadius}). " +
                "No candidates will be generated.", this);
            valid = false;
        }

        if (minDist >= spacing)
        {
            Debug.LogError(
                $"[HexSwissCheese] minDistance ({minDist}) >= hexSpacing ({spacing}). " +
                "Jitter would be zero or negative — fix Inspector or LessonProfile values.", this);
            valid = false;
        }

        return valid;
    }

    // ── Utility ───────────────────────────────────────────────────────────

    /// <summary>Fisher-Yates in-place shuffle — zero allocations.</summary>
    private static void ShuffleInPlace(List<Vector2> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Vector2 tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    // ── Editor visualisation ──────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        float rowHeight = hexSpacing * 0.8660254f;
        float halfSpacing = hexSpacing * 0.5f;
        float outerRSqr = spawnRadius * spawnRadius;
        float innerRSqr = minSpawnRadius * minSpawnRadius;

        // Note: Gizmos show non-rotated grid since rotation is randomised per episode
        int cols = Mathf.CeilToInt(2f * spawnRadius / hexSpacing) + 1;
        int rows = Mathf.CeilToInt(2f * spawnRadius / rowHeight) + 1;

        Vector3 center = transform.position;
        float midHeight = (minHeight + maxHeight) * 0.5f;
        Gizmos.color = gizmoColor;

        for (int row = 0; row < rows; row++)
        {
            float z = -spawnRadius + row * rowHeight;
            bool oddRow = (row & 1) == 1;

            for (int col = 0; col < cols; col++)
            {
                float x = -spawnRadius + col * hexSpacing;
                if (oddRow) x += halfSpacing;

                float distSqr = x * x + z * z;
                if (distSqr < innerRSqr || distSqr > outerRSqr)
                    continue;

                // Use transform.position.y + midHeight for consistency with actual obstacle spawning
                Gizmos.DrawSphere(center + new Vector3(x, midHeight, z), gizmoRadius);
            }
        }
    }
}
