using UnityEngine;

[RequireComponent(typeof(DronePDController))]
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

        // Link to Controller
        var controller = GetComponent<DronePDController>();
        if (controller != null)
        {
#if UNITY_EDITOR
            UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(controller);
            UnityEditor.SerializedProperty rotorsProp = so.FindProperty("rotorTransforms");

            if (rotorsProp != null && rotorsProp.isArray)
            {
                rotorsProp.arraySize = 4;
                for (int i = 0; i < 4; i++)
                {
                    rotorsProp.GetArrayElementAtIndex(i).objectReferenceValue = newRotors[i];
                }
                so.ApplyModifiedProperties();
                Debug.Log("Drone generated and linked.");
            }
#endif
        }
    }

    private Transform CreateRotor(string name, Vector3 localPos, Color color)
    {
        GameObject rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rotor.name = name;
        rotor.transform.parent = transform;
        rotor.transform.localPosition = localPos;
        rotor.transform.localScale = new Vector3(rotorRadius * 2, rotorHeight, rotorRadius * 2);

        DestroyImmediate(rotor.GetComponent<Collider>());

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