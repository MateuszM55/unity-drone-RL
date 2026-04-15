using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// ISensor that casts sphere-rays in a 3D concentric cone pattern (1-4-8 layout)
/// aimed along the transform's local forward axis.
/// Uses batched <see cref="SpherecastCommand"/> for high-performance sensing.
///
/// Each ray produces: 1 linear inverse distance + one-hot encoding for detectable layers.
/// Observation size = RayCount * (1 + DetectableLayerCount).
///
/// Linear inverse distance: 1.0 = object touching the drone, 0.0 = path clear (max range).
/// </summary>
public sealed class FrontalConeSensor : ISensor, IDisposable
{
    readonly string m_Name;
    readonly int m_RayCount;
    readonly float m_RayLength;
    readonly float m_SphereRadius;
    readonly int[] m_DetectableLayers;
    readonly int m_FloatsPerRay;          // 1 (distance) + layer count
    readonly int m_ObservationSize;
    readonly LayerMask m_LayerMask;
    readonly ObservationSpec m_ObservationSpec;

    // Pre-computed local-space ray directions (unit vectors).
    readonly Vector3[] m_RayDirections;

    // Reusable managed array for observation floats.
    readonly float[] m_Observations;

    Transform m_Transform;

    // Batched spherecast native arrays — allocated once, reused every frame.
    NativeArray<SpherecastCommand> m_CastCommands;
    NativeArray<RaycastHit> m_CastHits;

    // Deferred job handle — scheduled in Update, completed in Write.
    JobHandle m_PendingHandle;
    bool m_HasPendingJob;

    /// <summary>Pre-computed local-space ray directions (for Gizmo drawing).</summary>
    internal Vector3[] RayDirections => m_RayDirections;

    /// <summary>Most recent observation floats (for Gizmo drawing).</summary>
    internal float[] Observations => m_Observations;

    /// <summary>The transform used as the origin of rays.</summary>
    internal Transform SensorTransform
    {
        get => m_Transform;
        set => m_Transform = value;
    }

    internal float RayLength => m_RayLength;
    internal float SphereRadius => m_SphereRadius;
    internal int FloatsPerRay => m_FloatsPerRay;

    // ──────────────────────────────────────────────
    //  Construction
    // ──────────────────────────────────────────────

    public FrontalConeSensor(
        string name,
        float coneHalfAngle,
        float rayLength,
        float sphereRadius,
        int[] detectableLayers,
        LayerMask layerMask,
        Transform transform)
    {
        m_Name = name;
        m_RayLength = rayLength;
        m_SphereRadius = sphereRadius;
        m_DetectableLayers = detectableLayers ?? Array.Empty<int>();
        m_LayerMask = layerMask;
        m_Transform = transform;

        // --- Generate concentric cone directions (1-4-8) ---
        m_RayDirections = GenerateConcentricConeDirections(coneHalfAngle);
        m_RayCount = m_RayDirections.Length; // always 13

        m_FloatsPerRay = 1 + m_DetectableLayers.Length;
        m_ObservationSize = m_RayCount * m_FloatsPerRay;
        m_ObservationSpec = ObservationSpec.Vector(m_ObservationSize);

        m_Observations = new float[m_ObservationSize];

        // NativeArrays are allocated lazily
        // to prevent persistent-allocator leaks when the Editor validation path calls
        // CreateSensors() without a matching Dispose().
    }

    // ──────────────────────────────────────────────
    //  Concentric Cone — 1 center + 4 inner + 8 outer
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates 13 ray directions in a concentric cone pattern around local +Z (forward).
    /// Ring 0: 1 ray dead-center (forward).
    /// Ring 1: 4 rays at 1/3 of <paramref name="halfAngle"/>.
    /// Ring 2: 8 rays at the full <paramref name="halfAngle"/>.
    /// </summary>
    static Vector3[] GenerateConcentricConeDirections(float halfAngle)
    {
        var dirs = new Vector3[13];

        // Ring 0 — center
        dirs[0] = Vector3.forward;

        // Ring 1 — 4 rays, 1/3 half-angle, 90° apart
        float innerAngle = halfAngle / 3f;
        float innerRad = innerAngle * Mathf.Deg2Rad;
        float sinInner = Mathf.Sin(innerRad);
        float cosInner = Mathf.Cos(innerRad);
        for (int i = 0; i < 4; i++)
        {
            float azimuth = i * Mathf.PI * 0.5f; // 0, 90, 180, 270 degrees
            dirs[1 + i] = new Vector3(
                sinInner * Mathf.Cos(azimuth),
                sinInner * Mathf.Sin(azimuth),
                cosInner
            ).normalized;
        }

        // Ring 2 — 8 rays, full half-angle, 45° apart
        float outerRad = halfAngle * Mathf.Deg2Rad;
        float sinOuter = Mathf.Sin(outerRad);
        float cosOuter = Mathf.Cos(outerRad);
        for (int i = 0; i < 8; i++)
        {
            float azimuth = i * Mathf.PI * 0.25f; // 0, 45, 90, ... 315 degrees
            dirs[5 + i] = new Vector3(
                sinOuter * Mathf.Cos(azimuth),
                sinOuter * Mathf.Sin(azimuth),
                cosOuter
            ).normalized;
        }

        return dirs;
    }

    // ──────────────────────────────────────────────
    //  ISensor
    // ──────────────────────────────────────────────

    /// <inheritdoc/>
    public ObservationSpec GetObservationSpec() => m_ObservationSpec;

    /// <inheritdoc/>
    public int Write(ObservationWriter writer)
    {
        if (m_HasPendingJob)
        {
            m_PendingHandle.Complete();
            m_HasPendingJob = false;
            ProcessHits();
        }

        for (int i = 0; i < m_ObservationSize; i++)
            writer[i] = m_Observations[i];

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

        ScheduleCasts();
    }

    /// <inheritdoc/>
    public void Reset() { }

    /// <inheritdoc/>
    public string GetName() => m_Name;

    // ──────────────────────────────────────────────
    //  Batched SphereCast
    // ──────────────────────────────────────────────

    void ScheduleCasts()
    {
        // Lazy allocation: only reaches here during Play mode, never during editor validation.
        if (!m_CastCommands.IsCreated)
        {
            m_CastCommands = new NativeArray<SpherecastCommand>(m_RayCount, Allocator.Persistent);
            m_CastHits = new NativeArray<RaycastHit>(m_RayCount, Allocator.Persistent);
        }

        Vector3 origin = m_Transform.position;
        Quaternion rotation = m_Transform.rotation;
        PhysicsScene physicsScene = m_Transform.gameObject.scene.GetPhysicsScene();
        var queryParams = new QueryParameters(m_LayerMask);

        for (int i = 0; i < m_RayCount; i++)
        {
            Vector3 worldDir = rotation * m_RayDirections[i];
            m_CastCommands[i] = new SpherecastCommand(
                physicsScene, origin, m_SphereRadius, worldDir, queryParams, m_RayLength);
        }

        m_PendingHandle = SpherecastCommand.ScheduleBatch(m_CastCommands, m_CastHits, 16);
        m_HasPendingJob = true;
    }

    void ProcessHits()
    {
        for (int i = 0; i < m_RayCount; i++)
        {
            int baseIdx = i * m_FloatsPerRay;
            var hit = m_CastHits[i];
            bool hasHit = hit.collider != null;

            // Linear inverse distance: 1.0 = touching, 0.0 = clear (max range)
            m_Observations[baseIdx] = hasHit ? 1f - (hit.distance / m_RayLength) : 0f;

            // One-hot layer encoding
            for (int t = 0; t < m_DetectableLayers.Length; t++)
                m_Observations[baseIdx + 1 + t] = 0f;

            if (hasHit)
            {
                int hitLayer = hit.collider.gameObject.layer;
                for (int t = 0; t < m_DetectableLayers.Length; t++)
                {
                    if (hitLayer == m_DetectableLayers[t])
                    {
                        m_Observations[baseIdx + 1 + t] = 1f;
                        break;
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
        if (m_CastCommands.IsCreated) m_CastCommands.Dispose();
        if (m_CastHits.IsCreated) m_CastHits.Dispose();
    }
}
