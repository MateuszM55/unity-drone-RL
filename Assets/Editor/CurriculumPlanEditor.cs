using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for <see cref="CurriculumPlan"/>.
///
/// Adds an "Export to StreamingAssets" button that serialises the curriculum —
/// including every lesson's field values — to
/// <c>StreamingAssets/{planName}.json</c>.  The built .exe reads that file at
/// runtime so you can open it in any text editor, change a lesson value, and
/// restart training — no rebuild required.
///
/// Workflow:
///   1. Tune the curriculum (and its <see cref="LessonProfile"/> assets) in the
///      Inspector as usual.
///   2. Press "Export to StreamingAssets" to write/overwrite the JSON.
///   3. Build (or use the existing build). At runtime the agent will load the JSON.
///   4. To change a lesson mid-training: open the JSON in Notepad, edit, save,
///      restart — no rebuild needed.
/// </summary>
[CustomEditor(typeof(CurriculumPlan))]
public class CurriculumPlanEditor : Editor
{
    // -----------------------------------------------------------------------
    // Serialisable mirror types
    // -----------------------------------------------------------------------

    /// <summary>Plain-data mirror of <see cref="LessonProfile"/> for JSON export.</summary>
    [System.Serializable]
    private class LessonData
    {
        public string lessonName;
        public float spawnHeight;
        public float spawnRadius;
        public float maxEpisodeDistance;
        public int maxObstacleCount;
        public float obstacleSpawnRadius;
        public float minObstacleSpawnRadius;
        public float hexSpacing;
        public float hexMinDistance;
        public float hexObstacleDensity;
    }

    /// <summary>Root wrapper written to the JSON file.</summary>
    [System.Serializable]
    private class CurriculumData
    {
        public string planName;
        public List<LessonData> lessons = new List<LessonData>();
    }

    // -----------------------------------------------------------------------
    // Inspector GUI
    // -----------------------------------------------------------------------

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("StreamingAssets JSON Bridge", EditorStyles.boldLabel);

        if (GUILayout.Button("Export to StreamingAssets"))
        {
            ExportToStreamingAssets((CurriculumPlan)target);
        }

        var plan = (CurriculumPlan)target;
        string expectedPath = Path.Combine(Application.streamingAssetsPath, plan.name + ".json");

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
                Debug.Log($"[CurriculumPlan] Deleted override: {expectedPath}");
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No StreamingAssets override found. The SO values will be used as-is.",
                MessageType.None);
        }
    }

    // -----------------------------------------------------------------------
    // Export logic
    // -----------------------------------------------------------------------

    private static void ExportToStreamingAssets(CurriculumPlan plan)
    {
        if (!plan.IsValid())
        {
            EditorUtility.DisplayDialog(
                "Export Failed",
                "The CurriculumPlan is not valid (empty or contains null lessons). " +
                "Fix the asset before exporting.",
                "OK");
            return;
        }

        var data = new CurriculumData { planName = plan.name };

        for (int i = 0; i < plan.LessonCount; i++)
        {
            LessonProfile lp = plan.GetLesson(i);
            data.lessons.Add(new LessonData
            {
                lessonName           = lp.name,
                spawnHeight          = lp.SpawnHeight,
                spawnRadius          = lp.SpawnRadius,
                maxEpisodeDistance   = lp.MaxEpisodeDistance,
                maxObstacleCount     = lp.MaxObstacleCount,
                obstacleSpawnRadius  = lp.ObstacleSpawnRadius,
                minObstacleSpawnRadius = lp.MinObstacleSpawnRadius,
                hexSpacing           = lp.HexSpacing,
                hexMinDistance       = lp.HexMinDistance,
                hexObstacleDensity   = lp.HexObstacleDensity,
            });
        }

        if (!Directory.Exists(Application.streamingAssetsPath))
            Directory.CreateDirectory(Application.streamingAssetsPath);

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        string path = Path.Combine(Application.streamingAssetsPath, plan.name + ".json");
        File.WriteAllText(path, json);
        AssetDatabase.Refresh();

        Debug.Log($"[CurriculumPlan] Exported '{plan.name}' ({plan.LessonCount} lessons) → {path}");
        EditorUtility.DisplayDialog(
            "Export Successful",
            $"Curriculum plan exported to:\n{path}\n\n" +
            $"{plan.LessonCount} lesson(s) embedded.\n\n" +
            "Edit individual lesson values in the JSON to override them at runtime without rebuilding.",
            "OK");
    }
}
