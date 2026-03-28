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
            "Otherwise the seed-point search will loop forever.");

        InitPool();
        InitPoissonGrid();
    }

    /// <summary>
    /// Generates up to <see cref="obstacleCount"/> obstacles around
    /// <paramref name="center"/> using Poisson Disk Sampling, then
    /// activates them from the pool.
    /// </summary>
    public void Generate(Vector3 center)
    {
        if (obstaclePrefab == null) return;

        RunPoissonDiskSampling();

        int count = Mathf.Min(pdsPoints.Count, obstacleCount);
        EnsurePoolCapacity(count);

        for (int i = 0; i < count; i++)
        {
            Vector2 pt = pdsPoints[i];

            // Convert from PDS local [0, areaSize] to world offset [-radius, +radius]
            Vector3 pos = center + new Vector3(
                pt.x - spawnRadius,
                Random.Range(minHeight, maxHeight),
                pt.y - spawnRadius);

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
        activeCount = count;
    }

    /// <summary>
    /// Generates obstacles using the given per-call parameters instead of
    /// the serialised defaults. The Poisson grid is rebuilt for the new
    /// dimensions and restored afterwards.
    /// </summary>
    public void Generate(Vector3 center, int overrideCount, float overrideRadius, float overrideMinSep)
    {
        int origCount = obstacleCount;
        float origRadius = spawnRadius;
        float origMinSep = minSeparation;

        obstacleCount = overrideCount;
        spawnRadius = overrideRadius;
        minSeparation = overrideMinSep;
        InitPoissonGrid();

        Generate(center);

        obstacleCount = origCount;
        spawnRadius = origRadius;
        minSeparation = origMinSep;
        InitPoissonGrid();
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
        pdsAreaSize = 2f * spawnRadius;
        pdsCellSize = minSeparation / 1.41421356f; // r / sqrt(2)
        pdsGridWidth = Mathf.CeilToInt(pdsAreaSize / pdsCellSize);
        pdsGridHeight = Mathf.CeilToInt(pdsAreaSize / pdsCellSize);
        pdsMinSepSqr = minSeparation * minSeparation;
        pdsRadiusSqr = spawnRadius * spawnRadius;
        pdsMinRadiusSqr = minSpawnRadius * minSpawnRadius;
        pdsGrid = new int[pdsGridWidth * pdsGridHeight];

        // Pre-size the lists to avoid runtime allocations
        pdsActive.Capacity = Mathf.Max(pdsActive.Capacity, obstacleCount * 2);
        pdsPoints.Capacity = Mathf.Max(pdsPoints.Capacity, obstacleCount * 2);
    }

    private void RunPoissonDiskSampling()
    {
        // Reset grid — fast integer fill, no allocations
        for (int i = 0; i < pdsGrid.Length; i++)
            pdsGrid[i] = -1;

        pdsActive.Clear();
        pdsPoints.Clear();

        // First seed: random point inside the circular spawn area
        // Bounded loop to avoid hanging when the ring is degenerate.
        Vector2 first = default;
        bool foundSeed = false;
        const int maxSeedAttempts = 1000;
        for (int attempt = 0; attempt < maxSeedAttempts; attempt++)
        {
            first = new Vector2(
                Random.Range(0f, pdsAreaSize),
                Random.Range(0f, pdsAreaSize));
            if (PdsInsideRing(first)) { foundSeed = true; break; }
        }
        if (!foundSeed)
        {
            Debug.LogWarning("[PoissonObstacleGenerator] Failed to find a valid seed point. " +
                "Check that minSpawnRadius < spawnRadius.");
            return;
        }

        PdsAddPoint(first);

        // Main loop — Bridson's algorithm
        while (pdsActive.Count > 0 && pdsPoints.Count < obstacleCount)
        {
            // Pick a random active point
            int activeIdx = Random.Range(0, pdsActive.Count);
            Vector2 origin = pdsActive[activeIdx];
            bool anyAccepted = false;

            for (int k = 0; k < PdsCandidateAttempts; k++)
            {
                // Random candidate in the annulus [r, 2r]
                float angle = Random.Range(0f, 2f * Mathf.PI);
                float dist = Random.Range(minSeparation, 2f * minSeparation);
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

                if (pdsPoints.Count >= obstacleCount)
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
