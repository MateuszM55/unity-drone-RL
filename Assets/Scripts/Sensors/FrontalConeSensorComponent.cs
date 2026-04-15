using System;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// A <see cref="SensorComponent"/> that creates a <see cref="FrontalConeSensor"/>.
/// Provides a 13-ray concentric cone (1-4-8 pattern) aimed along local forward,
/// using volumetric SphereCasts and linear inverse-distance observations.
///
/// Attach to the same GameObject as the drone agent.
/// </summary>
[AddComponentMenu("ML Agents/Frontal Cone Sensor")]
public class FrontalConeSensorComponent : SensorComponent, IDisposable
{
    // ──────────────────────────────────────────────
    //  Serialized Fields
    // ──────────────────────────────────────────────

    [Header("Sensor Identity")]
    [SerializeField]
    string m_SensorName = "FrontalConeSensor";

    [Header("Cone Configuration")]
    [SerializeField, Range(10f, 90f)]
    [Tooltip("Half-angle of the cone in degrees (total FOV = 2× this value). 45° gives a 90° FOV.")]
    float m_ConeHalfAngle = 45f;

    [SerializeField, Range(1f, 1000f)]
    [Tooltip("Maximum length of each sphere-cast ray.")]
    float m_RayLength = 20f;

    [SerializeField, Range(0.01f, 2f)]
    [Tooltip("Radius of the SphereCast (≈ drone half-width). Prevents thin objects slipping between rays.")]
    float m_SphereRadius = 0.2f;

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
    Color m_RayMissColor = Color.cyan;

    // ──────────────────────────────────────────────
    //  Public Properties
    // ──────────────────────────────────────────────

    public string SensorName { get => m_SensorName; set => m_SensorName = value; }
    public float ConeHalfAngle { get => m_ConeHalfAngle; set => m_ConeHalfAngle = value; }
    public float RayLength { get => m_RayLength; set => m_RayLength = value; }
    public float SphereRadius { get => m_SphereRadius; set => m_SphereRadius = value; }
    public LayerMask RayLayerMask { get => m_RayLayerMask; set => m_RayLayerMask = value; }
    public List<string> DetectableLayers { get => m_DetectableLayers; set => m_DetectableLayers = value; }

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    FrontalConeSensor m_Sensor;

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

        m_Sensor = new FrontalConeSensor(
            m_SensorName,
            m_ConeHalfAngle,
            m_RayLength,
            m_SphereRadius,
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
            float val = obs[i * floatsPerRay];
            bool hit = val > 0.001f;
            // Recover physical distance from the linear observation: dist = (1 - val) * rayLen
            float dist = rayLen * (1f - val);

            Gizmos.color = hit ? m_RayHitColor : m_RayMissColor;
            Gizmos.DrawRay(origin, worldDir * dist);
            Gizmos.DrawWireSphere(origin + worldDir * dist, radius);
        }
    }

    void DrawEditorPreviewGizmos()
    {
        Vector3 origin = transform.position;
        Quaternion rotation = transform.rotation;

        // Reconstruct the 13 directions for preview
        float halfAngle = m_ConeHalfAngle;

        // Center
        Vector3[] dirs = new Vector3[13];
        dirs[0] = Vector3.forward;

        float innerRad = (halfAngle / 3f) * Mathf.Deg2Rad;
        float sinI = Mathf.Sin(innerRad);
        float cosI = Mathf.Cos(innerRad);
        for (int i = 0; i < 4; i++)
        {
            float az = i * Mathf.PI * 0.5f;
            dirs[1 + i] = new Vector3(sinI * Mathf.Cos(az), sinI * Mathf.Sin(az), cosI).normalized;
        }

        float outerRad = halfAngle * Mathf.Deg2Rad;
        float sinO = Mathf.Sin(outerRad);
        float cosO = Mathf.Cos(outerRad);
        for (int i = 0; i < 8; i++)
        {
            float az = i * Mathf.PI * 0.25f;
            dirs[5 + i] = new Vector3(sinO * Mathf.Cos(az), sinO * Mathf.Sin(az), cosO).normalized;
        }

        Gizmos.color = m_RayMissColor;
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 worldDir = rotation * dirs[i];
            Gizmos.DrawRay(origin, worldDir * m_RayLength);
            Gizmos.DrawWireSphere(origin + worldDir * m_RayLength, m_SphereRadius);
        }
    }
}
