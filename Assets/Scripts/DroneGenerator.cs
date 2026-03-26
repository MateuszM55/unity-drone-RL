using UnityEngine;

public class DroneGenerator : MonoBehaviour
{
    [Header("Dimensions")]
    [SerializeField] private Vector3 bodySize = new Vector3(1f, 0.2f, 0.5f);
    [SerializeField] private float armLength = 0.6f;
    [SerializeField] private float rotorRadius = 0.3f;
    [SerializeField] private float rotorHeight = 0.05f;

    [Header("Visuals")]
    [SerializeField] private Color bodyColor = Color.gray;
    [SerializeField] private Color frontRotorColor = Color.red;
    [SerializeField] private Color rearRotorColor = Color.blue;

    [ContextMenu("Generate Drone Model")]
    public void GenerateDrone()
    {
        // Cleanup existing children
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        // Create Body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.parent = transform;
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = bodySize;

        // Apply body color safely
        if (body.TryGetComponent<Renderer>(out var bodyRenderer))
        {
            Material bodyMat = new Material(bodyRenderer.sharedMaterial);
            bodyMat.color = bodyColor;
            bodyRenderer.sharedMaterial = bodyMat;
        }

        // Create Rotors
        Transform[] newRotors = new Transform[4];
        float xDist = armLength * Mathf.Sin(45 * Mathf.Deg2Rad);
        float zDist = armLength * Mathf.Cos(45 * Mathf.Deg2Rad);
        float yPos = bodySize.y / 2;

        newRotors[0] = CreateRotor("Rotor_FL", new Vector3(-xDist, yPos, zDist), frontRotorColor);
        newRotors[1] = CreateRotor("Rotor_FR", new Vector3(xDist, yPos, zDist), frontRotorColor);
        newRotors[2] = CreateRotor("Rotor_RL", new Vector3(-xDist, yPos, -zDist), rearRotorColor);
        newRotors[3] = CreateRotor("Rotor_RR", new Vector3(xDist, yPos, -zDist), rearRotorColor);

        // Link rotor transforms to controllers via serialized properties
#if UNITY_EDITOR
        LinkRotors<DronePDController>(newRotors, "rotorTransforms");
        LinkRotors<DroneSimpleML_Agent>(newRotors, "rotorTransforms");
#endif
    }

#if UNITY_EDITOR
    private void LinkRotors<T>(Transform[] rotors, string propertyName) where T : Component
    {
        var component = GetComponent<T>();
        if (component == null) return;

        var so = new UnityEditor.SerializedObject(component);
        var prop = so.FindProperty(propertyName);

        if (prop != null && prop.isArray)
        {
            prop.arraySize = rotors.Length;
            for (int i = 0; i < rotors.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = rotors[i];
            }
            so.ApplyModifiedProperties();
            Debug.Log($"Drone rotors linked to {typeof(T).Name}.");
        }
    }
#endif

    private Transform CreateRotor(string name, Vector3 localPos, Color color)
    {
        GameObject rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rotor.name = name;
        rotor.transform.parent = transform;
        rotor.transform.localPosition = localPos;
        rotor.transform.localScale = new Vector3(rotorRadius * 2, rotorHeight, rotorRadius * 2);

        // Replace the default CapsuleCollider with a BoxCollider that matches the rotor disc
        DestroyImmediate(rotor.GetComponent<Collider>());
        rotor.AddComponent<BoxCollider>();

        // Apply rotor color safely
        if (rotor.TryGetComponent<Renderer>(out var r))
        {
            Material newMat = new Material(r.sharedMaterial);
            newMat.color = color;
            r.sharedMaterial = newMat;
        }

        return rotor.transform;
    }
}