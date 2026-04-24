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
    readonly float m_RayLength;
    readonly float m_SphereRadius;
    readonly int[] m_DetectableLayers;
    readonly int m_FloatsPerRay;          // 1 (distance) + layer count
    readonly int m_ObservationSize;
    readonly LayerMask m_LayerMask;
    readonly float m_TiltAngle;
    readonly float m_ConeHalfAngle;
    readonly ObservationSpec m_ObservationSpec;

    // True once ProcessHits has run at least once this sensor lifetime.
    // Prevents Write() returning a stale all-zero buffer on the very first
    // call before Update() has had a chance to schedule a cast.
    bool m_Initialized;

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
    internal float TiltAngle => m_TiltAngle;

    /// <summary>
    /// The half-angle the cone was built with, in degrees.
    /// Exposed so <see cref="FrontalConeSensorComponent"/> Gizmos always reflect
    /// the value the sensor was actually constructed with.
    /// </summary>
    internal float ConeHalfAngle => m_ConeHalfAngle;

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
        Transform transform,
        float tiltAngle = 0f)
    {
        m_Name = name;
        m_ConeHalfAngle = coneHalfAngle;
        m_RayLength = rayLength;
        m_SphereRadius = sphereRadius;
        m_DetectableLayers = detectableLayers ?? Array.Empty<int>();
        m_LayerMask = layerMask;
        m_Transform = transform;
        m_TiltAngle = tiltAngle;

        // --- Generate concentric cone directions (1-4-8) ---
        m_RayDirections = GenerateConcentricConeDirections(coneHalfAngle);

        m_FloatsPerRay = 1 + m_DetectableLayers.Length;
        m_ObservationSize = m_RayDirections.Length * m_FloatsPerRay;
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
    ///
    /// Made <c>internal static</c> so <see cref="FrontalConeSensorComponent"/>
    /// can call it from <c>DrawEditorPreviewGizmos</c> instead of duplicating the logic.
    /// </summary>
    internal static Vector3[] GenerateConcentricConeDirections(float halfAngle)
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
        else if (!m_Initialized && m_Transform != null)
        {
            // ML-Agents may call Write() before the first Update() (e.g. at episode start).
            // Schedule and immediately complete a synchronous cast so the buffer is not
            // emitted as all-zeros, which would look identical to a fully clear scene.
            ScheduleCasts();
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
            m_CastCommands = new NativeArray<SpherecastCommand>(m_RayDirections.Length, Allocator.Persistent);
            m_CastHits = new NativeArray<RaycastHit>(m_RayDirections.Length, Allocator.Persistent);
        }

        Vector3 origin = m_Transform.position;
        // Apply tilt: positive angle pitches the cone upward (rotates around local right axis).
        Quaternion rotation = m_Transform.rotation * Quaternion.AngleAxis(-m_TiltAngle, Vector3.right);
        PhysicsScene physicsScene = m_Transform.gameObject.scene.GetPhysicsScene();
        var queryParams = new QueryParameters(m_LayerMask);

        for (int i = 0; i < m_RayDirections.Length; i++)
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
        SensorProcessing.ProcessHits(
            m_CastHits,
            m_RayDirections.Length,
            m_FloatsPerRay,
            m_DetectableLayers,
            m_RayLength,
            m_Observations);

        m_Initialized = true;
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
