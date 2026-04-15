using System;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// A <see cref="SensorComponent"/> that creates a <see cref="SixAxisSensor"/>.
/// Provides 5 orthogonal proximity rays (Up, Down, Left, Right, Back) using
/// volumetric SphereCasts and tanh-normalised inverse distances.
///
/// Mimics ultrasonic / infrared proximity sensors on commercial drones.
/// Attach to the same GameObject as the drone agent.
/// </summary>
[AddComponentMenu("ML Agents/Six Axis Sensor")]
public class SixAxisSensorComponent : SensorComponent, IDisposable
{
    // ──────────────────────────────────────────────
    //  Serialized Fields
    // ──────────────────────────────────────────────

    [Header("Sensor Identity")]
    [SerializeField]
    string m_SensorName = "SixAxisSensor";

    [Header("Ray Configuration")]
    [SerializeField, Range(1f, 1000f)]
    [Tooltip("Maximum length of each sphere-cast ray.")]
    float m_RayLength = 20f;

    [SerializeField, Range(0.01f, 2f)]
    [Tooltip("Radius of the SphereCast (≈ drone half-width). Prevents thin objects slipping between rays.")]
    float m_SphereRadius = 0.2f;

    [SerializeField, Range(0.5f, 10f)]
    [Tooltip("Scale factor for tanh normalisation. Higher = sharper danger signal near the drone.")]
    float m_TanhScale = 3f;

    [SerializeField]
    [Tooltip("Physics layers the rays can hit.")]
    LayerMask m_RayLayerMask = -5;

    [Header("Layer Detection")]
    [SerializeField]
    [Tooltip("Layer names to detect via one-hot encoding. Order maps to observation indices.")]
    List<string> m_DetectableLayers = new List<string>();

    [Header("Debug Gizmos")]
    [SerializeField]
    Color m_RayHitColor = Color.red;

    [SerializeField]
    Color m_RayMissColor = Color.yellow;

    // ──────────────────────────────────────────────
    //  Public Properties
    // ──────────────────────────────────────────────

    public string SensorName { get => m_SensorName; set => m_SensorName = value; }
    public float RayLength { get => m_RayLength; set => m_RayLength = value; }
    public float SphereRadius { get => m_SphereRadius; set => m_SphereRadius = value; }
    public float TanhScale { get => m_TanhScale; set => m_TanhScale = value; }
    public LayerMask RayLayerMask { get => m_RayLayerMask; set => m_RayLayerMask = value; }
    public List<string> DetectableLayers { get => m_DetectableLayers; set => m_DetectableLayers = value; }

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    SixAxisSensor m_Sensor;

    // ──────────────────────────────────────────────
    //  SensorComponent
    // ──────────────────────────────────────────────

    /// <inheritdoc/>
    public override ISensor[] CreateSensors()
    {
        Dispose();

        var layerIndices = new int[m_DetectableLayers.Count];
        for (int i = 0; i < m_DetectableLayers.Count; i++)
            layerIndices[i] = LayerMask.NameToLayer(m_DetectableLayers[i]);

        m_Sensor = new SixAxisSensor(
            m_SensorName,
            m_RayLength,
            m_SphereRadius,
            m_TanhScale,
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

    static readonly Vector3[] s_PreviewDirections =
    {
        Vector3.up,
        Vector3.down,
        Vector3.left,
        Vector3.right,
        Vector3.back
    };

    static readonly string[] s_DirectionLabels =
    {
        "Up", "Down", "Left", "Right", "Back"
    };

    void OnDrawGizmosSelected()
    {
        if (m_Sensor != null && m_Sensor.Observations != null)
            DrawRuntimeGizmos();
        else
            DrawEditorPreviewGizmos();
    }

    void DrawRuntimeGizmos()
    {
        var dirs = m_Sensor.RayDirections;
        var obs = m_Sensor.Observations;
        int floatsPerRay = m_Sensor.FloatsPerRay;
        float rayLen = m_Sensor.RayLength;
        float radius = m_Sensor.SphereRadius;
        Vector3 origin = transform.position;
        Quaternion rotation = transform.rotation;

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 worldDir = rotation * dirs[i];
            float tanhVal = obs[i * floatsPerRay];
            bool hit = tanhVal > 0.001f;

            Gizmos.color = hit ? m_RayHitColor : m_RayMissColor;

            if (hit)
            {
                float dist = rayLen * (1f - tanhVal);
                Gizmos.DrawRay(origin, worldDir * dist);
                Gizmos.DrawWireSphere(origin + worldDir * dist, radius);
            }
            else
            {
                Gizmos.DrawRay(origin, worldDir * rayLen);
            }
        }
    }

    void DrawEditorPreviewGizmos()
    {
        Vector3 origin = transform.position;
        Quaternion rotation = transform.rotation;

        Gizmos.color = m_RayMissColor;
        for (int i = 0; i < s_PreviewDirections.Length; i++)
        {
            Vector3 worldDir = rotation * s_PreviewDirections[i];
            Gizmos.DrawRay(origin, worldDir * m_RayLength);
            Gizmos.DrawWireSphere(origin + worldDir * m_RayLength, m_SphereRadius);
        }
    }
}
