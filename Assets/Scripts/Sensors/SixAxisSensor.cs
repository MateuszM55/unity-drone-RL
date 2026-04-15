using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// ISensor that casts 5 sphere-rays in an orthogonal cross pattern
/// (Up, Down, Left, Right, Back) for proximity safety around the drone.
/// Forward is intentionally omitted — the <see cref="FrontalConeSensor"/> covers that axis.
///
/// Uses batched <see cref="SpherecastCommand"/> for high-performance sensing.
/// Each ray produces: 1 tanh-normalised inverse distance + one-hot encoding for detectable layers.
/// Observation size = 5 * (1 + DetectableLayerCount).
///
/// Tanh inverse distance: 1.0 = object touching the drone, 0.0 = path clear.
/// Mimics the ultrasonic / infrared proximity sensors on commercial drones.
/// </summary>
public sealed class SixAxisSensor : ISensor, IDisposable
{
    const int RayCount = 5;

    readonly string m_Name;
    readonly float m_RayLength;
    readonly float m_SphereRadius;
    readonly float m_TanhScale;
    readonly int[] m_DetectableLayers;
    readonly int m_FloatsPerRay;
    readonly int m_ObservationSize;
    readonly LayerMask m_LayerMask;
    readonly ObservationSpec m_ObservationSpec;

    // Fixed orthogonal directions (local space, no forward).
    static readonly Vector3[] s_Directions =
    {
        Vector3.up,       // 0
        Vector3.down,     // 1
        Vector3.left,     // 2
        Vector3.right,    // 3
        Vector3.back      // 4
    };

    readonly float[] m_Observations;

    // Raw physical hit distances for Gizmo drawing (not sent to the network).
    // Stores hit.distance on a hit, or m_RayLength on a miss.
    readonly float[] m_GizmoDistances;

    Transform m_Transform;

    NativeArray<SpherecastCommand> m_CastCommands;
    NativeArray<RaycastHit> m_CastHits;

    JobHandle m_PendingHandle;
    bool m_HasPendingJob;

    /// <summary>Local-space ray directions (for Gizmo drawing).</summary>
    internal Vector3[] RayDirections => s_Directions;

    /// <summary>Most recent observation floats (for Gizmo drawing).</summary>
    internal float[] Observations => m_Observations;

    internal Transform SensorTransform
    {
        get => m_Transform;
        set => m_Transform = value;
    }

    internal float RayLength => m_RayLength;
    internal float SphereRadius => m_SphereRadius;
    internal int FloatsPerRay => m_FloatsPerRay;

    /// <summary>Raw physical hit distances per ray (for accurate Gizmo drawing).</summary>
    internal float[] GizmoDistances => m_GizmoDistances;

    // ──────────────────────────────────────────────
    //  Construction
    // ──────────────────────────────────────────────

    public SixAxisSensor(
        string name,
        float rayLength,
        float sphereRadius,
        float tanhScale,
        int[] detectableLayers,
        LayerMask layerMask,
        Transform transform)
    {
        m_Name = name;
        m_RayLength = rayLength;
        m_SphereRadius = sphereRadius;
        m_TanhScale = tanhScale;
        m_DetectableLayers = detectableLayers ?? Array.Empty<int>();
        m_LayerMask = layerMask;
        m_Transform = transform;

        m_FloatsPerRay = 1 + m_DetectableLayers.Length;
        m_ObservationSize = RayCount * m_FloatsPerRay;
        m_ObservationSpec = ObservationSpec.Vector(m_ObservationSize);

        m_Observations = new float[m_ObservationSize];

        m_GizmoDistances = new float[RayCount];
        for (int i = 0; i < RayCount; i++)
            m_GizmoDistances[i] = m_RayLength;

        // NativeArrays are allocated lazily on the first ScheduleCasts() call (Play mode only)
        // to prevent persistent-allocator leaks when the Editor validation path calls
        // CreateSensors() without a matching Dispose().
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
            m_CastCommands = new NativeArray<SpherecastCommand>(RayCount, Allocator.Persistent);
            m_CastHits = new NativeArray<RaycastHit>(RayCount, Allocator.Persistent);
        }

        Vector3 origin = m_Transform.position;
        Quaternion rotation = m_Transform.rotation;
        PhysicsScene physicsScene = m_Transform.gameObject.scene.GetPhysicsScene();
        var queryParams = new QueryParameters(m_LayerMask);

        for (int i = 0; i < RayCount; i++)
        {
            Vector3 worldDir = rotation * s_Directions[i];
            m_CastCommands[i] = new SpherecastCommand(
                physicsScene, origin, m_SphereRadius, worldDir, queryParams, m_RayLength);
        }

        m_PendingHandle = SpherecastCommand.ScheduleBatch(m_CastCommands, m_CastHits, 8);
        m_HasPendingJob = true;
    }

    void ProcessHits()
    {
        for (int i = 0; i < RayCount; i++)
        {
            int baseIdx = i * m_FloatsPerRay;
            var hit = m_CastHits[i];
            bool hasHit = hit.collider != null;

            // Store raw distance for Gizmo drawing (not used by the network)
            m_GizmoDistances[i] = hasHit ? hit.distance : m_RayLength;

            // Tanh inverse distance: 1.0 = touching, 0.0 = clear
            if (hasHit)
            {
                float normalised = hit.distance / m_RayLength;
                float proximity = 1f - normalised;
                m_Observations[baseIdx] = Tanh(proximity * m_TanhScale);
            }
            else
            {
                m_Observations[baseIdx] = 0f;
            }

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

    static float Tanh(float x)
    {
        float e2x = Mathf.Exp(2f * x);
        return (e2x - 1f) / (e2x + 1f);
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
