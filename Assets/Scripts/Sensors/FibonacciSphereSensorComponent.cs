using System;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// A <see cref="SensorComponent"/> that creates a <see cref="FibonacciSphereSensor"/>.
/// Attach this to a drone (or any agent GameObject) to provide omnidirectional
/// ray-based observations distributed via a Fibonacci Lattice on a unit sphere.
/// </summary>
[AddComponentMenu("ML Agents/Fibonacci Sphere Sensor")]
public class FibonacciSphereSensorComponent : SensorComponent, IDisposable
{
    // ──────────────────────────────────────────────
    //  Serialized Fields
    // ──────────────────────────────────────────────

    [Header("Sensor Identity")]
    [SerializeField]
    string m_SensorName = "FibonacciSphereSensor";

    [Header("Ray Configuration")]
    [SerializeField, Range(1, 500)]
    [Tooltip("Total number of rays distributed over a unit sphere using the Fibonacci Lattice.")]
    int m_RayCount = 100;

    [SerializeField, Range(1f, 1000f)]
    [Tooltip("Maximum length of each ray.")]
    float m_RayLength = 20f;

    [SerializeField]
    [Tooltip("Physics layers the rays can hit.")]
    LayerMask m_RayLayerMask = -5; // Default physics layers

    [Header("Layer Detection")]
    [SerializeField]
    [Tooltip("Layer names to detect via one-hot encoding. Order matters (maps to observation indices).")]
    List<string> m_DetectableLayers = new List<string>();

    [Header("Debug Gizmos")]
    [SerializeField]
    Color m_RayHitColor = Color.red;

    [SerializeField]
    Color m_RayMissColor = Color.green;

    // ──────────────────────────────────────────────
    //  Public Properties
    // ──────────────────────────────────────────────

    /// <summary>Name of the sensor (must be unique per agent).</summary>
    public string SensorName
    {
        get => m_SensorName;
        set => m_SensorName = value;
    }

    /// <summary>Total ray count distributed via Fibonacci Lattice.</summary>
    public int RayCount
    {
        get => m_RayCount;
        set => m_RayCount = value;
    }

    /// <summary>Maximum ray length.</summary>
    public float RayLength
    {
        get => m_RayLength;
        set => m_RayLength = value;
    }

    /// <summary>Physics layers the rays can hit.</summary>
    public LayerMask RayLayerMask
    {
        get => m_RayLayerMask;
        set => m_RayLayerMask = value;
    }

    /// <summary>Layer names to detect (one-hot encoded).</summary>
    public List<string> DetectableLayers
    {
        get => m_DetectableLayers;
        set => m_DetectableLayers = value;
    }

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    FibonacciSphereSensor m_Sensor;

    // ──────────────────────────────────────────────
    //  SensorComponent
    // ──────────────────────────────────────────────

    /// <inheritdoc/>
    public override ISensor[] CreateSensors()
    {
        Dispose();

        // Convert layer names to indices once at creation time.
        var layerIndices = new int[m_DetectableLayers.Count];
        for (int i = 0; i < m_DetectableLayers.Count; i++)
        {
            layerIndices[i] = LayerMask.NameToLayer(m_DetectableLayers[i]);
        }

        m_Sensor = new FibonacciSphereSensor(
            m_SensorName,
            m_RayCount,
            m_RayLength,
            layerIndices,
            m_RayLayerMask,
            transform
        );

        return new ISensor[] { m_Sensor };
    }

    // ──────────────────────────────────────────────
    //  IDisposable
    // ──────────────────────────────────────────────

    public void Dispose()
    {
        if (!ReferenceEquals(m_Sensor, null))
        {
            m_Sensor.Dispose();
            m_Sensor = null;
        }
    }

    void OnDestroy()
    {
        Dispose();
    }

    // ──────────────────────────────────────────────
    //  Gizmo Visualization
    // ──────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (m_Sensor != null && m_Sensor.RayDirections != null)
        {
            DrawRuntimeGizmos();
        }
        else
        {
            DrawEditorPreviewGizmos();
        }
    }

    /// <summary>
    /// Draw Gizmos using the live sensor data (during Play mode).
    /// Colors rays based on hit/miss using the cached observations.
    /// </summary>
    void DrawRuntimeGizmos()
    {
        var dirs = m_Sensor.RayDirections;
        var obs = m_Sensor.Observations;
        int floatsPerRay = m_Sensor.FloatsPerRay;
        float rayLen = m_Sensor.RayLength;
        Vector3 origin = transform.position;
        Quaternion rotation = transform.rotation;

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 worldDir = rotation * dirs[i];
            float normalizedDist = obs[i * floatsPerRay]; // 0..1
            float dist = normalizedDist * rayLen;
            bool hit = normalizedDist < 1f;

            Gizmos.color = hit ? m_RayHitColor : m_RayMissColor;
            Gizmos.DrawRay(origin, worldDir * dist);

            if (hit)
            {
                Gizmos.DrawWireSphere(origin + worldDir * dist, 0.05f);
            }
        }
    }

    /// <summary>
    /// Draw Gizmos in Edit mode (no sensor exists yet).
    /// Shows the ray directions at full length so the designer can verify coverage.
    /// </summary>
    void DrawEditorPreviewGizmos()
    {
        float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;
        Vector3 origin = transform.position;
        Quaternion rotation = transform.rotation;

        Gizmos.color = m_RayMissColor;
        for (int i = 0; i < m_RayCount; i++)
        {
            float theta = Mathf.Acos(1f - 2f * (i + 0.5f) / m_RayCount);
            float phi = 2f * Mathf.PI * i / goldenRatio;
            float sinTheta = Mathf.Sin(theta);

            Vector3 localDir = new Vector3(
                sinTheta * Mathf.Cos(phi),
                Mathf.Cos(theta),
                sinTheta * Mathf.Sin(phi)
            ).normalized;

            Vector3 worldDir = rotation * localDir;
            Gizmos.DrawRay(origin, worldDir * m_RayLength);
        }
    }
}
