using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Abstract base class for drone ML-Agents. Provides shared physics setup,
/// observation collection, episode reset, and collision handling.
/// Aerodynamic drag is handled by the companion <see cref="DroneAerodynamics"/> component.
/// Subclasses implement <see cref="Agent.OnActionReceived"/> and
/// <see cref="Agent.Heuristic"/> for their specific control schemes.
///
/// OBSERVATION SPACE (16 floats — body-local frame where applicable):
///   - Local unit direction to target        (3)  direction only, always [-1,1]
///   - Squashed distance to target           (1)  tanh(d/10), always [0,1)
///   - Drone velocity (local)                (3)
///   - Drone angular velocity (local)        (3)
///   - Drone orientation (forward)           (3)  world-frame attitude
///   - Drone orientation (up)                (3)  world-frame attitude
///   (right = cross(forward, up) — omitted, linearly dependent)
///
/// The drone model is generated via <see cref="DroneGenerator"/>.
/// </summary>
public enum Lesson
{
    /// <summary>Drone spawns directly above the target — focus on hovering and landing.</summary>
    Landing = 0,
    /// <summary>Drone spawns at a random point on a circle around the target — focus on navigation.</summary>
    Navigation = 1,
    /// <summary>Drone spawns at a random point on a larger circle — longer-range navigation.</summary>
    FarNavigation = 2,
    /// <summary>Drone spawns far away with random obstacles placed between it and the target.</summary>
    Obstacles = 3
}

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DroneAerodynamics))]
public abstract class DroneMLAgentBase : Agent
{
    [Header("Physics Settings")]
    [SerializeField] protected float mass = 1f;

    [Header("Training")]
    [SerializeField] protected Transform target;
    [Tooltip("Max distance from target before episode ends (Landing & Navigation lessons).")]
    [SerializeField] protected float nearMaxEpisodeDistance = 10f;
    [Tooltip("Max distance from target before episode ends (FarNavigation lesson).")]
    [SerializeField] protected float farMaxEpisodeDistance = 20f;

    [Header("Touchdown")]
    [Tooltip("Seconds to wait after landing on the target before ending the episode.")]
    [SerializeField] protected float touchdownDelay = 1f;
    [Tooltip("Reference speed (m/s) at which the landing reward halves. Lower = stricter.")]
    [SerializeField] protected float maxSafeTouchdownSpeed = 2f;

    [Header("Spawn / Curriculum")]
    [Tooltip("Height above the target at which the drone spawns.")]
    [SerializeField] protected float spawnHeight = 3f;
    [Tooltip("Spawn distance for the Navigation lesson.")]
    [SerializeField] protected float navigationSpawnDistance = 5f;
    [Tooltip("Spawn distance for the FarNavigation lesson.")]
    [SerializeField] protected float farNavigationSpawnDistance = 15f;

    [Header("Obstacles (Lesson 3)")]
    [Tooltip("Prefab to spawn as an obstacle.")]
    [SerializeField] protected GameObject obstaclePrefab;
    [Tooltip("Maximum number of obstacles to place during the Obstacles lesson.")]
    [SerializeField] protected int obstacleCount = 5;
    [Tooltip("Obstacles are placed inside a ring around the target between min and max radius.")]
    [SerializeField] protected float obstacleSpawnRadius = 12f;
    [Tooltip("Minimum distance from the target — obstacles won't spawn closer than this.")]
    [SerializeField] protected float obstacleMinSpawnRadius = 3f;
    [Tooltip("Minimum distance between obstacles (Poisson Disk Sampling radius).")]
    [SerializeField] protected float obstacleMinSeparation = 3f;
    [Tooltip("Min / Max height range for obstacle placement.")]
    [SerializeField] protected float obstacleMinHeight = 0.5f;
    [SerializeField] protected float obstacleMaxHeight = 6f;

    [Header("Safety / Termination")]
    [Tooltip("Maximum tilt angle (degrees) from world up before the episode is terminated.")]
    [SerializeField] protected float maxTiltAngle = 60f;

    protected Rigidbody rb;
    protected Vector3 startPosition;
    protected Quaternion startRotation;
    protected Keyboard keyboard;
    protected float maxTiltDot;
    protected float maxEpisodeDistance;
    protected bool hasLanded;
    protected float touchdownTimer;
    protected readonly System.Collections.Generic.List<GameObject> obstaclePool
        = new System.Collections.Generic.List<GameObject>();
    protected int activeObstacleCount;

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
    private readonly System.Collections.Generic.List<Vector2> pdsActive
        = new System.Collections.Generic.List<Vector2>();
    private readonly System.Collections.Generic.List<Vector2> pdsPoints
        = new System.Collections.Generic.List<Vector2>();

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.useGravity = true;
        rb.centerOfMass = Vector3.zero;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Let DroneAerodynamics be the sole source of damping
        GetComponent<DroneAerodynamics>().InitialiseDamping();

        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        keyboard = Keyboard.current;
        maxTiltDot = Mathf.Cos(maxTiltAngle * Mathf.Deg2Rad);

        InitObstaclePool();
        InitPoissonGrid();
    }

    public override void OnEpisodeBegin()
    {
        // Reset drone physics
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Reset touchdown state
        hasLanded = false;
        touchdownTimer = 0f;

        // Remove obstacles from the previous episode
        ClearObstacles();

        // Read current lesson from curriculum
        Lesson lesson = (Lesson)(int)Academy.Instance.EnvironmentParameters
            .GetWithDefault("lesson", 0f);

        // Adjust max episode distance per lesson
        maxEpisodeDistance = lesson == Lesson.FarNavigation || lesson == Lesson.Obstacles
            ? farMaxEpisodeDistance
            : nearMaxEpisodeDistance;

        Vector3 targetPos = target != null ? target.localPosition : startPosition;

        switch (lesson)
        {
            case Lesson.Landing:
                // Start directly above the target
                transform.localPosition = targetPos + Vector3.up * spawnHeight;
                break;

            case Lesson.Navigation:
            {
                // Start at a random point on a circle around the target
                float angle = Random.Range(0f, 2f * Mathf.PI);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * navigationSpawnDistance;
                transform.localPosition = targetPos + offset + Vector3.up * spawnHeight;
                break;
            }

            case Lesson.FarNavigation:
            {
                // Start at a random point on a larger circle
                float angle = Random.Range(0f, 2f * Mathf.PI);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * farNavigationSpawnDistance;
                transform.localPosition = targetPos + offset + Vector3.up * spawnHeight;
                break;
            }

            case Lesson.Obstacles:
            {
                // Start far away, same as FarNavigation
                float angle = Random.Range(0f, 2f * Mathf.PI);
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * farNavigationSpawnDistance;
                transform.localPosition = targetPos + offset + Vector3.up * spawnHeight;

                // Spawn random obstacles inside the max-distance circle
                SpawnObstacles(targetPos);
                break;
            }

            default:
                transform.localPosition = startPosition;
                break;
        }

        transform.localRotation = startRotation;
    }

    protected void InitObstaclePool()
    {
        if (obstaclePrefab == null) return;

        for (int i = 0; i < obstacleCount; i++)
        {
            GameObject obj = Instantiate(obstaclePrefab, Vector3.zero, Quaternion.identity, transform.parent);
            obj.SetActive(false);
            obstaclePool.Add(obj);
        }
    }

    protected void EnsurePoolCapacity(int required)
    {
        if (obstaclePrefab == null) return;

        while (obstaclePool.Count < required)
        {
            GameObject obj = Instantiate(obstaclePrefab, Vector3.zero, Quaternion.identity, transform.parent);
            obj.SetActive(false);
            obstaclePool.Add(obj);
        }
    }

    protected void ClearObstacles()
    {
        for (int i = 0; i < activeObstacleCount; i++)
        {
            if (obstaclePool[i] != null)
                obstaclePool[i].SetActive(false);
        }
        activeObstacleCount = 0;
    }

    protected void SpawnObstacles(Vector3 center)
    {
        if (obstaclePrefab == null) return;

        // --- Run Poisson Disk Sampling on the XZ plane ---
        RunPoissonDiskSampling();

        int count = Mathf.Min(pdsPoints.Count, obstacleCount);
        EnsurePoolCapacity(count);

        for (int i = 0; i < count; i++)
        {
            Vector2 pt = pdsPoints[i];

            // Convert from PDS local [0, areaSize] to world offset [-radius, +radius]
            Vector3 pos = center + new Vector3(
                pt.x - obstacleSpawnRadius,
                Random.Range(obstacleMinHeight, obstacleMaxHeight),
                pt.y - obstacleSpawnRadius);

            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject obstacle = obstaclePool[i];
            obstacle.transform.SetPositionAndRotation(pos, rot);
            obstacle.SetActive(true);

            if (obstacle.TryGetComponent<Rigidbody>(out var obstacleRb))
            {
                obstacleRb.linearVelocity = Vector3.zero;
                obstacleRb.angularVelocity = Vector3.zero;
            }
        }
        activeObstacleCount = count;
    }

    // ── Poisson Disk Sampling ────────────────────────────────────────────

    private void InitPoissonGrid()
    {
        pdsAreaSize = 2f * obstacleSpawnRadius;
        pdsCellSize = obstacleMinSeparation / 1.41421356f; // r / sqrt(2)
        pdsGridWidth = Mathf.CeilToInt(pdsAreaSize / pdsCellSize);
        pdsGridHeight = Mathf.CeilToInt(pdsAreaSize / pdsCellSize);
        pdsMinSepSqr = obstacleMinSeparation * obstacleMinSeparation;
        pdsRadiusSqr = obstacleSpawnRadius * obstacleSpawnRadius;
        pdsMinRadiusSqr = obstacleMinSpawnRadius * obstacleMinSpawnRadius;
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
        Vector2 first;
        do
        {
            first = new Vector2(
                Random.Range(0f, pdsAreaSize),
                Random.Range(0f, pdsAreaSize));
        }
        while (!PdsInsideRing(first));

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
                float dist = Random.Range(obstacleMinSeparation, 2f * obstacleMinSeparation);
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
        float dx = point.x - obstacleSpawnRadius;
        float dz = point.y - obstacleSpawnRadius;
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
                float dx = candidate.x - existing.x;
                float dy = candidate.y - existing.y;

                // Squared distance comparison — avoids sqrt
                if (dx * dx + dy * dy < pdsMinSepSqr)
                    return false;
            }
        }
        return true;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 targetPos = target != null ? target.localPosition : startPosition + Vector3.up * 3f;

        // Decompose target vector → local unit direction (3) + squashed distance (1)
        DroneRewardHelper.DecomposeTargetVector(
            transform, targetPos - transform.localPosition,
            out Vector3 localDir, out float squashedDist);
        sensor.AddObservation(localDir);
        sensor.AddObservation(squashedDist);

        // Velocity in body frame (3) — matches real IMU output
        sensor.AddObservation(transform.InverseTransformDirection(rb.linearVelocity));

        // Angular velocity in body frame (3) — matches real gyroscope output
        sensor.AddObservation(transform.InverseTransformDirection(rb.angularVelocity));

        // Orientation axes (6) — world-frame attitude for gravity awareness
        // (right omitted: right = cross(forward, up), linearly dependent)
        sensor.AddObservation(transform.forward);
        sensor.AddObservation(transform.up);
    }

    protected virtual void FixedUpdate()
    {
        // --- Touchdown countdown ---
        if (hasLanded)
        {
            touchdownTimer -= Time.fixedDeltaTime;
            if (touchdownTimer <= 0f)
            {
                EndEpisode();
                return;
            }
        }

        // Aerodynamic drag is now handled by DroneAerodynamics (FixedUpdate)
    }

    protected virtual void OnCollisionEnter(Collision collision)
    {
        // Landing on the target: reward inversely proportional to touchdown speed
        if (target != null && collision.transform == target)
        {
            if (!hasLanded)
            {
                hasLanded = true;
                touchdownTimer = touchdownDelay;
                float speed = rb.linearVelocity.magnitude;
                AddReward(DroneRewardHelper.TouchdownReward(speed, maxSafeTouchdownSpeed));
            }
            return;
        }

        // Collision with obstacle or ground: -1.0 penalty
        SetReward(-1.0f);
        EndEpisode();
    }
}
