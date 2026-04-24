using UnityEngine;

/// <summary>
/// Editor tool that procedurally builds the drone visual mesh and physics colliders,
/// then wires the generated rotor transforms back into every sibling
/// DroneMLAgentBase subcomponent on the same GameObject.
///
/// Usage: Right-click this component in the Inspector and choose
/// Generate Drone Model. Existing child GameObjects are destroyed and rebuilt.
///
/// Rotor order: FL(0), FR(1), RL(2), RR(3) -- matches the action-space convention
/// used by all DroneMLAgentBase subclasses.
///
/// All generation and rotor-linking logic is Editor-only; the component has no
/// runtime behaviour. Calling GenerateDrone in a player build logs a warning and
/// returns immediately without modifying the scene.
/// </summary>
public class DroneGenerator : MonoBehaviour
{
    [Header("Dimensions")]
    [SerializeField] private Vector3 bodySize    = new Vector3(1f, 0.2f, 0.5f);
    [SerializeField] private float   armLength   = 0.6f;
    [SerializeField] private float   rotorRadius = 0.3f;
    [SerializeField] private float   rotorHeight = 0.05f;

    [Header("Visuals")]
    [SerializeField] private Color bodyColor       = Color.gray;
    [SerializeField] private Color frontRotorColor = Color.red;
    [SerializeField] private Color rearRotorColor  = Color.blue;

    [ContextMenu("Generate Drone Model")]
    public void GenerateDrone()
    {
#if UNITY_EDITOR
        // Destroy existing children so the mesh is rebuilt from scratch.
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        // Body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name             = "Body";
        body.layer            = LayerMask.NameToLayer("Ignore Raycast");
        body.transform.parent = transform;
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale    = bodySize;

        if (body.TryGetComponent<Renderer>(out var bodyRenderer))
        {
            Material bodyMat = new Material(bodyRenderer.sharedMaterial);
            bodyMat.color = bodyColor;
            bodyRenderer.sharedMaterial = bodyMat;
        }

        // Rotors
        float xDist = armLength * Mathf.Sin(45f * Mathf.Deg2Rad);
        float zDist = armLength * Mathf.Cos(45f * Mathf.Deg2Rad);
        float yPos  = bodySize.y / 2f;

        Transform[] newRotors = new Transform[4];
        newRotors[0] = CreateRotor("Rotor_FL", new Vector3(-xDist, yPos,  zDist), frontRotorColor);
        newRotors[1] = CreateRotor("Rotor_FR", new Vector3( xDist, yPos,  zDist), frontRotorColor);
        newRotors[2] = CreateRotor("Rotor_RL", new Vector3(-xDist, yPos, -zDist), rearRotorColor);
        newRotors[3] = CreateRotor("Rotor_RR", new Vector3( xDist, yPos, -zDist), rearRotorColor);

        // Link rotor transforms into every DroneMLAgentBase subclass on this GameObject.
        // Using GetComponents<DroneMLAgentBase> means new subclasses are picked up automatically
        // without modifying this tool.
        foreach (DroneMLAgentBase agent in GetComponents<DroneMLAgentBase>())
        {
            var so   = new UnityEditor.SerializedObject(agent);
            var prop = so.FindProperty("rotorTransforms");
            if (prop == null || !prop.isArray) continue;

            prop.arraySize = newRotors.Length;
            for (int i = 0; i < newRotors.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = newRotors[i];

            so.ApplyModifiedProperties();
            Debug.Log($"[DroneGenerator] Rotor transforms linked to {agent.GetType().Name}.", agent);
        }
#else
        Debug.LogWarning("[DroneGenerator] GenerateDrone is an Editor-only tool and has no effect in a player build.", this);
#endif
    }

#if UNITY_EDITOR
    private Transform CreateRotor(string rotorName, Vector3 localPos, Color color)
    {
        GameObject rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rotor.name             = rotorName;
        rotor.layer            = LayerMask.NameToLayer("Ignore Raycast");
        rotor.transform.parent = transform;
        rotor.transform.localPosition = localPos;
        rotor.transform.localScale    = new Vector3(rotorRadius * 2f, rotorHeight, rotorRadius * 2f);

        // Replace the default CapsuleCollider with a flat BoxCollider matching the rotor disc.
        DestroyImmediate(rotor.GetComponent<Collider>());
        rotor.AddComponent<BoxCollider>();

        if (rotor.TryGetComponent<Renderer>(out var r))
        {
            Material mat = new Material(r.sharedMaterial);
            mat.color = color;
            r.sharedMaterial = mat;
        }

        return rotor.transform;
    }
#endif
}