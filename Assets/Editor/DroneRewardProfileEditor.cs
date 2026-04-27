using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for <see cref="DroneRewardProfile"/>.
///
/// Adds an "Export to StreamingAssets" button that serialises the SO's current
/// values to <c>StreamingAssets/{profileName}.json</c>.  The built .exe reads
/// that file at runtime, so you can open it in any text editor, change a value,
/// and restart training — no rebuild required.
///
/// Workflow:
///   1. Tune the profile in the Inspector as usual.
///   2. Press "Export to StreamingAssets" to write/overwrite the JSON.
///   3. Build (or use the existing build). At runtime the agent will load the JSON.
///   4. To change a value mid-training: open the JSON in Notepad, edit, save, restart.
/// </summary>
[CustomEditor(typeof(DroneRewardProfile))]
public class DroneRewardProfileEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("StreamingAssets JSON Bridge", EditorStyles.boldLabel);

        if (GUILayout.Button("Export to StreamingAssets"))
        {
            ExportToStreamingAssets((DroneRewardProfile)target);
        }

        var profile = (DroneRewardProfile)target;
        string expectedPath = Path.Combine(Application.streamingAssetsPath, profile.name + ".json");
        if (File.Exists(expectedPath))
        {
            EditorGUILayout.HelpBox(
                $"JSON override active:\n{expectedPath}",
                MessageType.Info);

            if (GUILayout.Button("Delete StreamingAssets Override"))
            {
                File.Delete(expectedPath);
                string meta = expectedPath + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
                AssetDatabase.Refresh();
                Debug.Log($"[DroneRewardProfile] Deleted override: {expectedPath}");
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No StreamingAssets override found. The SO values will be used as-is.",
                MessageType.None);
        }
    }

    private static void ExportToStreamingAssets(DroneRewardProfile profile)
    {
        if (!Directory.Exists(Application.streamingAssetsPath))
            Directory.CreateDirectory(Application.streamingAssetsPath);

        string json = JsonUtility.ToJson(profile, prettyPrint: true);
        string path = Path.Combine(Application.streamingAssetsPath, profile.name + ".json");
        File.WriteAllText(path, json);
        AssetDatabase.Refresh();

        Debug.Log($"[DroneRewardProfile] Exported '{profile.name}' → {path}");
        EditorUtility.DisplayDialog(
            "Export Successful",
            $"Reward profile exported to:\n{path}\n\nEdit this file in any text editor to override values at runtime without rebuilding.",
            "OK");
    }
}
