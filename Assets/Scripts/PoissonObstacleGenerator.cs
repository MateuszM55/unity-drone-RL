using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns obstacles in a ring around a given centre point using
/// Poisson Disk Sampling (Bridson's algorithm) on the XZ plane.
/// All internal buffers are pre-allocated — zero GC during generation.
///
/// Attach to the training-area parent (or the drone itself) and call
/// <see cref="Initialise"/> once, then <see cref="Generate"/> / <see cref="Clear"/>
/// each episode.
/// </summary>
[DisallowMultipleComponent]
public class PoissonObstacleGenerator : MonoBehaviour
{
    [Header("Obstacle Prefab & Counts")]
    [Tooltip("Prefab to spawn as an obstacle.")]
    [SerializeField] private GameObject obstaclePrefab;
    [Tooltip("Maximum number of obstacles to place per episode.")]
    [SerializeField] private int obstacleCount = 4;

    [Header("Spawn Ring")]
    [Tooltip("Obstacles are placed inside a ring around the target between min and max radius.")]
    [SerializeField] private float spawnRadius = 12f;
    [Tooltip("Minimum distance from the centre — obstacles won't spawn closer than this.")]
    [SerializeField] private float minSpawnRadius = 5f;

    [Header("Poisson Disk Sampling")]
    [Tooltip("Minimum distance between obstacles.")]
    [SerializeField] private float minSeparation = 8f;
    [Tooltip("Number of seed points scattered around the ring before growth starts. More seeds = better ring coverage when obstacleCount is low.")]
    [SerializeField] private int pdsSeedCount = 3;

    [Header("Height")]
    [Tooltip("Minimum height for obstacle placement.")]
    [SerializeField] private float minHeight = 3f;
    [Tooltip("Maximum height for obstacle placement.")]
    [SerializeField] private float maxHeight = 3f;

    [Header("Spawn Location")]
    [Tooltip("Parent transform under which pooled obstacles are instantiated. Defaults to this GameObject's transform.")]
    [SerializeField] private Transform spawnParent;

    // ── Object pool ──
    private readonly List<GameObject> pool = new List<GameObject>();
    private int activeCount;

    // ── Runtime Guard ──
    private bool _initialised;
    private float _lockedSpawnRadius;
    private float _lockedMinSeparation;
    private int _lockedPdsSeedCount;

    // ── Poisson Disk Sampling (zero-allocation) ──
    private const int PdsCandidateAttempts = 30;
    private float pdsAreaSize;
    private float pdsCellSize;
    private int pdsGridWidth;
    private int pdsGridHeight;
    private float pdsMinSepSqr;
    private float pdsRadiusSqr;
    private float pdsMinRadiusSqr;
    private int[] pdsGrid;
    private readonly List<Vector2> pdsActive = new List<Vector2>();
    private readonly List<Vector2> pdsPoints = new List<Vector2>();

    /// <summary>
    /// Pre-allocates the object pool and the Poisson grid.
    /// Call once during setup (e.g. from an Agent's <c>Initialize</c>).
    /// If <see cref="spawnParent"/> was not assigned in the Inspector,
    /// it defaults to this GameObject's transform.
    /// </summary>
    public void Initialise()
    {
        if (spawnParent == null)
            spawnParent = transform;

        Debug.Assert(minSpawnRadius < spawnRadius,
            $"[PoissonObstacleGenerator] minSpawnRadius ({minSpawnRadius}) must be < spawnRadius ({spawnRadius}). " +
            "The spawn ring would have zero area.");

        InitPool();
        InitPoissonGrid();

        _lockedSpawnRadius   = spawnRadius;
        _lockedMinSeparation = minSeparation;
        _lockedPdsSeedCount  = pdsSeedCount;
        _initialised         = true;
    }

    /// <summary>
    /// Generates up to <see cref="obstacleCount"/> obstacles around
    /// <paramref name="center"/> using Poisson Disk Sampling, then
    /// activates them from the pool.
    /// </summary>
    public void Generate(Vector3 center)
    {
        GenerateInternal(center, obstacleCount, spawnRadius, minSeparation);
    }

    /// <summary>
    /// Generates obstacles using the given per-call parameters instead of
    /// the serialised defaults.
    /// </summary>
    public void Generate(Vector3 center, int overrideCount, float overrideRadius, float overrideMinSep)
    {
        GenerateInternal(center, overrideCount, overrideRadius, overrideMinSep);
    }

    /// <summary>
    /// Core generation logic. Rebuilds the Poisson grid for the given
    /// dimensions, runs sampling, and activates obstacles from the pool.
    /// No serialised fields are mutated.
    /// </summary>
    private void GenerateInternal(Vector3 center, int count, float radius, float sep)
    {
        if (obstaclePrefab == null) return;

        // Deactivate any previously active obstacles
        Clear();

        InitPoissonGrid(count, radius, sep);
        RunPoissonDiskSampling(count, sep);

        int placed = Mathf.Min(pdsPoints.Count, count);
        EnsurePoolCapacity(placed);

        for (int i = 0; i < placed; i++)
        {
            Vector2 pt = pdsPoints[i];

            // Convert from PDS local [0, areaSize] to world offset [-radius, +radius]
            Vector3 pos = center + new Vector3(
                pt.x - radius,
                Random.Range(minHeight, maxHeight),
                pt.y - radius);

            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject obstacle = pool[i];
            obstacle.transform.SetPositionAndRotation(pos, rot);
            obstacle.SetActive(true);

            if (obstacle.TryGetComponent<Rigidbody>(out var obstacleRb))
            {
                obstacleRb.linearVelocity = Vector3.zero;
                obstacleRb.angularVelocity = Vector3.zero;
            }
        }
        activeCount = placed;
    }

    /// <summary>
    /// Deactivates all obstacles spawned during the current episode.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < activeCount; i++)
        {
            if (pool[i] != null)
                pool[i].SetActive(false);
        }
        activeCount = 0;
    }

    // ── Runtime Guard ────────────────────────────────────────────────────

    private void OnValidate()
    {
        if (!Application.isPlaying || !_initialised)
            return;

        bool reverted = false;

        if (spawnRadius != _lockedSpawnRadius)
        {
            spawnRadius = _lockedSpawnRadius;
            reverted    = true;
        }
        if (minSeparation != _lockedMinSeparation)
        {
            minSeparation = _lockedMinSeparation;
            reverted      = true;
        }
        if (pdsSeedCount != _lockedPdsSeedCount)
        {
            pdsSeedCount = _lockedPdsSeedCount;
            reverted     = true;
        }

        if (reverted)
            Debug.LogWarning("[PoissonObstacleGenerator] spawnRadius, minSeparation and pdsSeedCount are locked during Play mode — they define the pre-allocated grid size. Stop Play before changing them.", this);
    }

    // ── Pool management ──────────────────────────────────────────────────

    private void InitPool()
    {
        if (obstaclePrefab == null) return;

        for (int i = 0; i < obstacleCount; i++)
        {
            GameObject obj = Instantiate(obstaclePrefab, Vector3.zero, Quaternion.identity, spawnParent);
            obj.SetActive(false);
            pool.Add(obj);
        }
    }

    private void EnsurePoolCapacity(int required)
    {
        if (obstaclePrefab == null) return;

        while (pool.Count < required)
        {
            GameObject obj = Instantiate(obstaclePrefab, Vector3.zero, Quaternion.identity, spawnParent);
            obj.SetActive(false);
            pool.Add(obj);
        }
    }

    // ── Poisson Disk Sampling ────────────────────────────────────────────

    private void InitPoissonGrid()
    {
        InitPoissonGrid(obstacleCount, spawnRadius, minSeparation);
    }

    private void InitPoissonGrid(int count, float radius, float sep)
    {
        pdsAreaSize = 2f * radius;
        pdsCellSize = sep / 1.41421356f; // r / sqrt(2)
        pdsGridWidth = Mathf.CeilToInt(pdsAreaSize / pdsCellSize);
        pdsGridHeight = Mathf.CeilToInt(pdsAreaSize / pdsCellSize);
        pdsMinSepSqr = sep * sep;
        pdsRadiusSqr = radius * radius;
        pdsMinRadiusSqr = minSpawnRadius * minSpawnRadius;
        pdsGrid = new int[pdsGridWidth * pdsGridHeight];

        // Pre-size the lists to avoid runtime allocations
        pdsActive.Capacity = Mathf.Max(pdsActive.Capacity, count * 2);
        pdsPoints.Capacity = Mathf.Max(pdsPoints.Capacity, count * 2);
    }

    private void RunPoissonDiskSampling(int count, float sep)
    {
        // Reset grid — fast fill, no allocations
        System.Array.Fill(pdsGrid, -1);

        pdsActive.Clear();
        pdsPoints.Clear();

        // Multiple seeds — one per angular sector of the ring.
        // Dividing the circle into pdsSeedCount equal slices and picking a random
        // point inside each slice ensures growth fronts start simultaneously all
        // the way around, preventing the crystal-growth clumping problem.
        // PdsIsValid guards each candidate so minimum-separation is never violated.
        float angleStep = 2f * Mathf.PI / pdsSeedCount;
        float halfStep  = angleStep * 0.5f;
        for (int s = 0; s < pdsSeedCount; s++)
        {
            if (pdsPoints.Count >= count) break;
            float angle  = s * angleStep + Random.Range(-halfStep, halfStep);
            float radius = Random.Range(minSpawnRadius, spawnRadius);
            Vector2 seed = new Vector2(
                pdsAreaSize * 0.5f + Mathf.Cos(angle) * radius,
                pdsAreaSize * 0.5f + Mathf.Sin(angle) * radius);
            if (pdsPoints.Count == 0 || PdsIsValid(seed))
                PdsAddPoint(seed);
        }

        // Main loop — Bridson's algorithm
        while (pdsActive.Count > 0 && pdsPoints.Count < count)
        {
            // Pick a random active point
            int activeIdx = Random.Range(0, pdsActive.Count);
            Vector2 origin = pdsActive[activeIdx];
            bool anyAccepted = false;

            for (int k = 0; k < PdsCandidateAttempts; k++)
            {
                // Random candidate in the annulus [r, 2r]
                float angle = Random.Range(0f, 2f * Mathf.PI);
                float dist = Random.Range(sep, 2f * sep);
                Vector2 candidate = new Vector2(
                    origin.x + Mathf.Cos(angle) * dist,
                    origin.y + Mathf.Sin(angle) * dist);

                // Bounds check
                if (candidate.x < 0f || candidate.x >= pdsAreaSize ||
                    candidate.y < 0f || candidate.y >= pdsAreaSize)
                    continue;

                // Circular area check (squared — no sqrt)
                if (!PdsInsideRing(candidate))
                    continue;

                // Neighbor proximity check (squared — no sqrt)
                if (!PdsIsValid(candidate))
                    continue;

                PdsAddPoint(candidate);
                anyAccepted = true;

                // Break after accepting one candidate so every active seed
                // grows at the same rate — prevents quadrant clumping.
                break;
            }

            // Dead end — remove from active list (O(1) swap-and-pop)
            if (!anyAccepted)
            {
                int lastIdx = pdsActive.Count - 1;
                pdsActive[activeIdx] = pdsActive[lastIdx];
                pdsActive.RemoveAt(lastIdx);
            }
        }
    }

    private bool PdsInsideRing(Vector2 point)
    {
        float dx = point.x - spawnRadius;
        float dz = point.y - spawnRadius;
        float distSqr = dx * dx + dz * dz;
        return distSqr <= pdsRadiusSqr && distSqr >= pdsMinRadiusSqr;
    }

    private void PdsAddPoint(Vector2 point)
    {
        int idx = pdsPoints.Count;
        pdsPoints.Add(point);
        pdsActive.Add(point);

        int gx = (int)(point.x / pdsCellSize);
        int gy = (int)(point.y / pdsCellSize);
        pdsGrid[gy * pdsGridWidth + gx] = idx;
    }

    private bool PdsIsValid(Vector2 candidate)
    {
        int gx = (int)(candidate.x / pdsCellSize);
        int gy = (int)(candidate.y / pdsCellSize);

        // Check 5×5 neighbourhood (guaranteed to cover r with cellSize = r/√2)
        int minX = Mathf.Max(0, gx - 2);
        int maxX = Mathf.Min(pdsGridWidth - 1, gx + 2);
        int minY = Mathf.Max(0, gy - 2);
        int maxY = Mathf.Min(pdsGridHeight - 1, gy + 2);

        for (int y = minY; y <= maxY; y++)
        {
            int row = y * pdsGridWidth;
            for (int x = minX; x <= maxX; x++)
            {
                int pointIdx = pdsGrid[row + x];
                if (pointIdx < 0) continue;

                Vector2 existing = pdsPoints[pointIdx];
                float ddx = candidate.x - existing.x;
                float ddy = candidate.y - existing.y;

                // Squared distance comparison — avoids sqrt
                if (ddx * ddx + ddy * ddy < pdsMinSepSqr)
                    return false;
            }
        }
        return true;
    }
}
