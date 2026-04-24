using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.MLAgents.Sensors;
using UnityEngine;


/// <summary>
/// ISensor that casts 6 sphere-rays in a full orthogonal pattern
/// (Up, Down, Left, Right, Back, Forward) for proximity safety around the drone.
///
/// Uses batched <see cref="SpherecastCommand"/> for high-performance sensing.
/// Each ray produces: 1 linear inverse distance + one-hot encoding for detectable layers.
/// Observation size = ActiveRayCount * (1 + DetectableLayerCount).
/// Individual rays can be disabled via the <see cref="SixAxisSensorComponent"/> toggle array.
///
/// Linear inverse distance: 1.0 = object touching the drone, 0.0 = path clear (max range).
/// Mimics the ultrasonic / infrared proximity sensors on commercial drones.
/// </summary>
public sealed class SixAxisSensor : ISensor, IDisposable
{
    const int RayCount = 6;

    readonly string m_Name;
    readonly float m_RayLength;
    readonly float m_SphereRadius;
    readonly int[] m_DetectableLayers;
    readonly int m_FloatsPerRay;
    readonly int m_ObservationSize;
    readonly LayerMask m_LayerMask;
    readonly ObservationSpec m_ObservationSpec;

    // Fixed orthogonal directions (local space, no forward).
    static readonly Vector3[] s_AllDirections =
    {
        Vector3.up,       // 0
        Vector3.down,     // 1
        Vector3.left,     // 2
        Vector3.right,    // 3
        Vector3.back,     // 4
        Vector3.forward   // 5
    };

    readonly Vector3[] m_ActiveDirections;
    readonly int m_ActiveRayCount;

    readonly float[] m_Observations;

    Transform m_Transform;

    NativeArray<SpherecastCommand> m_CastCommands;
    NativeArray<RaycastHit> m_CastHits;

    JobHandle m_PendingHandle;
    bool m_HasPendingJob;

    // True once ProcessHits has run at least once this sensor lifetime.
    // Prevents Write() returning a stale all-zero buffer on the very first
    // call before Update() has had a chance to schedule a cast.
    bool m_Initialized;

    /// <summary>Local-space ray directions (for Gizmo drawing).</summary>
    internal Vector3[] RayDirections => m_ActiveDirections;

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

    // ──────────────────────────────────────────────
    //  Construction
    // ──────────────────────────────────────────────

    public SixAxisSensor(
        string name,
        float rayLength,
        float sphereRadius,
        int[] detectableLayers,
        LayerMask layerMask,
        Transform transform,
        bool[] rayEnabled = null)
    {
        m_Name = name;
        m_RayLength = rayLength;
        m_SphereRadius = sphereRadius;
        m_DetectableLayers = detectableLayers ?? Array.Empty<int>();
        m_LayerMask = layerMask;
        m_Transform = transform;

        // Build active directions from the enabled mask using a fixed array + counter
        // to avoid the List<T> + ToArray() double allocation.
        var activeDirs = new Vector3[s_AllDirections.Length];
        int activeCount = 0;
        for (int i = 0; i < s_AllDirections.Length; i++)
        {
            bool enabled = rayEnabled == null || i >= rayEnabled.Length || rayEnabled[i];
            if (enabled)
                activeDirs[activeCount++] = s_AllDirections[i];
        }
        // Trim to the exact count actually used.
        m_ActiveDirections = new Vector3[activeCount];
        Array.Copy(activeDirs, m_ActiveDirections, activeCount);
        m_ActiveRayCount = activeCount;

        m_FloatsPerRay = 1 + m_DetectableLayers.Length;
        m_ObservationSize = m_ActiveRayCount * m_FloatsPerRay;
        m_ObservationSpec = ObservationSpec.Vector(m_ObservationSize);

        m_Observations = new float[m_ObservationSize];

        // NativeArrays are allocated lazily
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
            m_CastCommands = new NativeArray<SpherecastCommand>(m_ActiveRayCount, Allocator.Persistent);
            m_CastHits = new NativeArray<RaycastHit>(m_ActiveRayCount, Allocator.Persistent);
        }

        Vector3 origin = m_Transform.position;
        Quaternion rotation = m_Transform.rotation;
        PhysicsScene physicsScene = m_Transform.gameObject.scene.GetPhysicsScene();
        var queryParams = new QueryParameters(m_LayerMask);

        for (int i = 0; i < m_ActiveRayCount; i++)
        {
            Vector3 worldDir = rotation * m_ActiveDirections[i];
            m_CastCommands[i] = new SpherecastCommand(
                physicsScene, origin, m_SphereRadius, worldDir, queryParams, m_RayLength);
        }

        m_PendingHandle = SpherecastCommand.ScheduleBatch(m_CastCommands, m_CastHits, 8);
        m_HasPendingJob = true;
    }

    void ProcessHits()
    {
        SensorProcessing.ProcessHits(
            m_CastHits,
            m_ActiveRayCount,
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
