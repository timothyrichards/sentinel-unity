using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Editor tool to upload NavMesh grid data to SpacetimeDB server
/// This should be run once after exporting NavMesh data
/// </summary>
public class NavMeshUploader : EditorWindow
{
    [Header("Settings")]
    private string navMeshDataPath = "Assets/NavMeshData.json";
    private bool isUploading = false;
    private string statusMessage = "";
    private int uploadedCount = 0;
    private int totalPoints = 0;

    [MenuItem("Tools/NavMesh Uploader")]
    public static void ShowWindow()
    {
        GetWindow<NavMeshUploader>("NavMesh Uploader");
    }

    private void OnGUI()
    {
        GUILayout.Label("NavMesh Upload Settings", EditorStyles.boldLabel);

        navMeshDataPath = EditorGUILayout.TextField("NavMesh Data Path", navMeshDataPath);

        EditorGUILayout.Space();

        if (!File.Exists(navMeshDataPath))
        {
            EditorGUILayout.HelpBox($"NavMesh data file not found at {navMeshDataPath}", MessageType.Warning);
        }

        EditorGUILayout.Space();

        GUI.enabled = !isUploading && File.Exists(navMeshDataPath);
        if (GUILayout.Button(isUploading ? "Uploading..." : "Upload NavMesh Data"))
        {
            Upload();
        }
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(statusMessage, isUploading ? MessageType.Info : MessageType.None);
        }

        if (isUploading && totalPoints > 0)
        {
            EditorGUILayout.Space();
            float progress = (float)uploadedCount / totalPoints;
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, $"{uploadedCount} / {totalPoints}");
        }
    }

    private void Upload()
    {
        if (!SpacetimeManager.IsConnected())
        {
            EditorUtility.DisplayDialog("Error", "Not connected to SpacetimeDB. Please enter Play mode first.", "OK");
            return;
        }

        if (!File.Exists(navMeshDataPath))
        {
            EditorUtility.DisplayDialog("Error", $"NavMesh data file not found at {navMeshDataPath}", "OK");
            return;
        }

        isUploading = true;
        uploadedCount = 0;
        totalPoints = 0;
        statusMessage = "Reading NavMesh data...";

        try
        {
            string json = File.ReadAllText(navMeshDataPath);
            NavMeshData data = JsonUtility.FromJson<NavMeshData>(json);

            if (data == null || data.points == null || data.points.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", "Invalid or empty NavMesh data", "OK");
                isUploading = false;
                statusMessage = "Error: Invalid NavMesh data";
                return;
            }

            totalPoints = data.points.Length;

            // Upload config
            statusMessage = "Uploading NavMesh configuration...";
            SpacetimeManager.Conn.Reducers.NavmeshSetConfig(
                data.cellSize,
                data.zTolerance,
                data.boundsMinX,
                data.boundsMinZ
            );
            Debug.Log($"NavMesh config uploaded: cellSize={data.cellSize}, zTolerance={data.zTolerance}, boundsMin=({data.boundsMinX}, {data.boundsMinZ})");

            // Clear existing grid
            statusMessage = "Clearing existing NavMesh grid...";
            SpacetimeManager.Conn.Reducers.NavmeshClearGrid();
            Debug.Log("Existing NavMesh grid cleared");

            // Upload points
            statusMessage = $"Uploading {totalPoints} NavMesh points...";
            EditorUtility.DisplayProgressBar("NavMesh Uploader", statusMessage, 0f);

            for (int i = 0; i < data.points.Length; i++)
            {
                var point = data.points[i];

                SpacetimeManager.Conn.Reducers.NavmeshUploadPoint(
                    point.x,
                    point.y,
                    point.z,
                    point.gridX,
                    point.gridZ
                );
                uploadedCount++;

                // Update progress every 100 points
                if (uploadedCount % 100 == 0)
                {
                    float progress = (float)uploadedCount / totalPoints;
                    EditorUtility.DisplayProgressBar("NavMesh Uploader",
                        $"Uploading NavMesh points... {uploadedCount}/{totalPoints}", progress);
                    Debug.Log($"Progress: {uploadedCount}/{totalPoints} points uploaded");
                }
            }

            EditorUtility.ClearProgressBar();

            statusMessage = $"Upload complete! Successfully uploaded {uploadedCount}/{totalPoints} points";
            Debug.Log(statusMessage);

            // Get stats from server
            try
            {
                SpacetimeManager.Conn.Reducers.NavmeshGetStats();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to get NavMesh stats: {e.Message}");
            }

            EditorUtility.DisplayDialog("Success", statusMessage, "OK");
        }
        catch (System.Exception e)
        {
            statusMessage = $"Error: {e.Message}";
            Debug.LogError($"NavMesh upload failed: {e.Message}");
            EditorUtility.DisplayDialog("Error", $"NavMesh upload failed: {e.Message}", "OK");
            EditorUtility.ClearProgressBar();
        }
        finally
        {
            isUploading = false;
        }
    }

    private void OnInspectorUpdate()
    {
        // Repaint the window to update progress
        Repaint();
    }
}
