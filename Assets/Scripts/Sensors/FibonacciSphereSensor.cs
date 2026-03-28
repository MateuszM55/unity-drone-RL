using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// ISensor that casts rays in a uniform spherical pattern (Fibonacci Lattice)
/// using batched <see cref="RaycastCommand"/> for high-performance sensing.
/// Each ray produces: 1 normalized distance + one-hot encoding for detectable tags.
/// Observation size = RayCount * (1 + DetectableTagCount).
/// </summary>
public sealed class FibonacciSphereSensor : ISensor, IDisposable
{
    readonly string m_Name;
    readonly int m_RayCount;
    readonly float m_RayLength;
    readonly List<string> m_DetectableTags;
    readonly int m_FloatsPerRay;         // 1 (distance) + tag count
    readonly int m_ObservationSize;
    readonly LayerMask m_LayerMask;
    readonly ObservationSpec m_ObservationSpec;

    // Pre-computed local-space ray directions (unit vectors).
    readonly Vector3[] m_RayDirections;

    // Reusable managed array for observation floats (written each Update).
    readonly float[] m_Observations;

    Transform m_Transform;

    // Batched raycast native arrays — allocated once, reused every frame.
    NativeArray<RaycastCommand> m_RayCommands;
    NativeArray<RaycastHit> m_RayHits;

    // Deferred job handle — scheduled in Update, completed in Write.
    JobHandle m_PendingHandle;
    bool m_HasPendingJob;

    /// <summary>
    /// The pre-computed local-space ray directions (for Gizmo drawing).
    /// </summary>
    internal Vector3[] RayDirections => m_RayDirections;

    /// <summary>
    /// The most recent observation floats (for Gizmo drawing).
    /// </summary>
    internal float[] Observations => m_Observations;

    /// <summary>
    /// The transform used as the origin of rays.
    /// </summary>
    internal Transform SensorTransform
    {
        get => m_Transform;
        set => m_Transform = value;
    }

    internal float RayLength => m_RayLength;
    internal int FloatsPerRay => m_FloatsPerRay;

    public FibonacciSphereSensor(
        string name,
        int rayCount,
        float rayLength,
        List<string> detectableTags,
        LayerMask layerMask,
        Transform transform)
    {
        m_Name = name;
        m_RayCount = rayCount;
        m_RayLength = rayLength;
        m_DetectableTags = detectableTags ?? new List<string>();
        m_LayerMask = layerMask;
        m_Transform = transform;

        m_FloatsPerRay = 1 + m_DetectableTags.Count;   // distance + one-hot
        m_ObservationSize = m_RayCount * m_FloatsPerRay;
        m_ObservationSpec = ObservationSpec.Vector(m_ObservationSize);

        m_Observations = new float[m_ObservationSize];

        // --- Fibonacci Lattice ---
        m_RayDirections = GenerateFibonacciDirections(m_RayCount);

        // --- Native arrays for batched raycasts ---
        m_RayCommands = new NativeArray<RaycastCommand>(m_RayCount, Allocator.Persistent);
        m_RayHits = new NativeArray<RaycastHit>(m_RayCount, Allocator.Persistent);
    }

    // ──────────────────────────────────────────────
    //  Fibonacci Lattice — uniform sphere sampling
    // ──────────────────────────────────────────────

    static Vector3[] GenerateFibonacciDirections(int count)
    {
        var directions = new Vector3[count];
        float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;

        for (int i = 0; i < count; i++)
        {
            // Latitude: uniformly spaced along the vertical axis
            float theta = Mathf.Acos(1f - 2f * (i + 0.5f) / count);
            // Longitude: golden-angle increments
            float phi = 2f * Mathf.PI * i / goldenRatio;

            float sinTheta = Mathf.Sin(theta);
            directions[i] = new Vector3(
                sinTheta * Mathf.Cos(phi),
                Mathf.Cos(theta),
                sinTheta * Mathf.Sin(phi)
            ).normalized;
        }

        return directions;
    }

    // ──────────────────────────────────────────────
    //  ISensor
    // ──────────────────────────────────────────────

    /// <inheritdoc/>
    public ObservationSpec GetObservationSpec() => m_ObservationSpec;

    /// <inheritdoc/>
    public int Write(ObservationWriter writer)
    {
        // Complete the deferred raycast batch and process results
        if (m_HasPendingJob)
        {
            m_PendingHandle.Complete();
            m_HasPendingJob = false;
            ProcessHits();
        }

        for (int i = 0; i < m_ObservationSize; i++)
        {
            writer[i] = m_Observations[i];
        }
        return m_ObservationSize;
    }

    /// <inheritdoc/>
    public byte[] GetCompressedObservation() => null;

    /// <inheritdoc/>
    public CompressionSpec GetCompressionSpec() => CompressionSpec.Default();

    /// <inheritdoc/>
    public void Update()
    {
        if (m_Transform == null)
            return;

        ScheduleRays();
    }

    /// <inheritdoc/>
    public void Reset() { }

    /// <inheritdoc/>
    public string GetName() => m_Name;

    // ──────────────────────────────────────────────
    //  Batched Raycast (RaycastCommand + JobHandle)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Populates raycast commands and schedules the batch.
    /// The <see cref="JobHandle"/> is stored for deferred completion in <see cref="Write"/>.
    /// </summary>
    void ScheduleRays()
    {
        Vector3 origin = m_Transform.position;
        Quaternion rotation = m_Transform.rotation;

        // Populate commands
        for (int i = 0; i < m_RayCount; i++)
        {
            Vector3 worldDir = rotation * m_RayDirections[i];
            m_RayCommands[i] = new RaycastCommand(origin, worldDir, new QueryParameters(m_LayerMask), m_RayLength);
        }

        // Schedule the batch — do NOT complete here; let Write() do it
        m_PendingHandle = RaycastCommand.ScheduleBatch(m_RayCommands, m_RayHits, 32);
        m_HasPendingJob = true;
    }

    /// <summary>
    /// Reads the completed raycast results and fills <see cref="m_Observations"/>.
    /// </summary>
    void ProcessHits()
    {
        for (int i = 0; i < m_RayCount; i++)
        {
            int baseIdx = i * m_FloatsPerRay;
            var hit = m_RayHits[i];
            bool hasHit = hit.collider != null;

            // Normalized distance (1.0 = no hit / max range)
            m_Observations[baseIdx] = hasHit
                ? hit.distance / m_RayLength
                : 1f;

            // One-hot tag encoding
            for (int t = 0; t < m_DetectableTags.Count; t++)
            {
                m_Observations[baseIdx + 1 + t] = 0f;
            }

            if (hasHit)
            {
                for (int t = 0; t < m_DetectableTags.Count; t++)
                {
                    if (hit.collider.CompareTag(m_DetectableTags[t]))
                    {
                        m_Observations[baseIdx + 1 + t] = 1f;
                        break; // An object can match at most one tag
                    }
                }
            }
        }
    }

    // ──────────────────────────────────────────────
    //  IDisposable
    // ──────────────────────────────────────────────

    public void Dispose()
    {
        if (m_HasPendingJob)
        {
            m_PendingHandle.Complete();
            m_HasPendingJob = false;
        }
        if (m_RayCommands.IsCreated) m_RayCommands.Dispose();
        if (m_RayHits.IsCreated) m_RayHits.Dispose();
    }
}
