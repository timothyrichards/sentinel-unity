using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Editor tool to sample the Unity NavMesh in a grid pattern and export the data
/// for server-side validation in SpacetimeDB
/// </summary>
public class NavMeshExporter : EditorWindow
{
    [Header("Sampling Settings")]
    [SerializeField] private float gridCellSize = 2f;
    [SerializeField] private float zTolerance = 2f;
    [SerializeField] private bool useAutoBounds = true;
    [SerializeField] private Vector3 boundsMin = new Vector3(-100, 0, -100);
    [SerializeField] private Vector3 boundsMax = new Vector3(100, 100, 100);

    private string outputPath = "Assets/NavMeshData.json";
    private int sampledPointsCount = 0;

    [MenuItem("Tools/NavMesh Exporter")]
    public static void ShowWindow()
    {
        GetWindow<NavMeshExporter>("NavMesh Exporter");
    }

    private void OnGUI()
    {
        GUILayout.Label("NavMesh Sampling Settings", EditorStyles.boldLabel);

        gridCellSize = EditorGUILayout.FloatField("Grid Cell Size", gridCellSize);
        zTolerance = EditorGUILayout.FloatField("Z Tolerance", zTolerance);

        EditorGUILayout.Space();
        GUILayout.Label("Bounds", EditorStyles.boldLabel);
        useAutoBounds = EditorGUILayout.Toggle("Auto-Detect Bounds", useAutoBounds);

        if (!useAutoBounds)
        {
            EditorGUILayout.HelpBox("Manual bounds mode - specify the area to sample.", MessageType.Info);
            boundsMin = EditorGUILayout.Vector3Field("Min", boundsMin);
            boundsMax = EditorGUILayout.Vector3Field("Max", boundsMax);
        }
        else
        {
            EditorGUILayout.HelpBox("Auto-detect will sample the entire NavMesh area.", MessageType.Info);
        }

        EditorGUILayout.Space();
        outputPath = EditorGUILayout.TextField("Output Path", outputPath);

        EditorGUILayout.Space();
        if (GUILayout.Button("Sample NavMesh and Export"))
        {
            SampleAndExport();
        }

        if (sampledPointsCount > 0)
        {
            EditorGUILayout.HelpBox($"Last export: {sampledPointsCount} walkable points sampled", MessageType.Info);
        }
    }

    private void SampleAndExport()
    {
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();

        if (triangulation.vertices.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "No NavMesh found in scene. Please bake a NavMesh first.", "OK");
            return;
        }

        // Calculate bounds from NavMesh if auto-detect is enabled
        Vector3 actualBoundsMin = boundsMin;
        Vector3 actualBoundsMax = boundsMax;

        if (useAutoBounds)
        {
            EditorUtility.DisplayProgressBar("NavMesh Exporter", "Calculating NavMesh bounds...", 0f);

            actualBoundsMin = triangulation.vertices[0];
            actualBoundsMax = triangulation.vertices[0];

            foreach (var vertex in triangulation.vertices)
            {
                actualBoundsMin = Vector3.Min(actualBoundsMin, vertex);
                actualBoundsMax = Vector3.Max(actualBoundsMax, vertex);
            }

            // Add a small margin
            actualBoundsMin -= Vector3.one * gridCellSize;
            actualBoundsMax += Vector3.one * gridCellSize;

            Debug.Log($"Auto-detected NavMesh bounds: Min {actualBoundsMin}, Max {actualBoundsMax}");
        }

        // Calculate grid size
        int gridCountX = Mathf.CeilToInt((actualBoundsMax.x - actualBoundsMin.x) / gridCellSize);
        int gridCountZ = Mathf.CeilToInt((actualBoundsMax.z - actualBoundsMin.z) / gridCellSize);
        int totalCells = gridCountX * gridCountZ;

        // Estimate occupied cells based on NavMesh vertices
        HashSet<Vector2Int> estimatedOccupiedCells = new HashSet<Vector2Int>();
        for (int i = 0; i < triangulation.vertices.Length; i++)
        {
            Vector3 vertex = triangulation.vertices[i];
            int gridX = Mathf.FloorToInt((vertex.x - actualBoundsMin.x) / gridCellSize);
            int gridZ = Mathf.FloorToInt((vertex.z - actualBoundsMin.z) / gridCellSize);
            estimatedOccupiedCells.Add(new Vector2Int(gridX, gridZ));
        }
        int occupiedCellCount = estimatedOccupiedCells.Count;

        // Show confirmation dialog
        // More realistic estimate: ~600-1000 cells per second
        float estimatedSeconds = occupiedCellCount / 600f;
        string timeEstimate = estimatedSeconds < 60
            ? $"{estimatedSeconds:F0} seconds"
            : $"{(estimatedSeconds / 60f):F1} minutes";

        string boundsInfo = $"Bounds: {actualBoundsMin} to {actualBoundsMax}\n" +
                           $"Size: {actualBoundsMax - actualBoundsMin}\n" +
                           $"Grid: {gridCountX} x {gridCountZ} = {totalCells:N0} total cells\n" +
                           $"Occupied cells: {occupiedCellCount:N0} ({(occupiedCellCount * 100f / totalCells):F1}%)\n" +
                           $"Cell Size: {gridCellSize}\n\n" +
                           $"Estimated time: {timeEstimate}\n\n" +
                           $"Continue?";

        if (!EditorUtility.DisplayDialog("NavMesh Export Confirmation", boundsInfo, "Export", "Cancel"))
        {
            return;
        }

        EditorUtility.DisplayProgressBar("NavMesh Exporter",
            $"Sampling {estimatedOccupiedCells.Count} occupied grid cells (skipping {totalCells - estimatedOccupiedCells.Count} empty cells)...", 0f);

        List<NavMeshGridPoint> walkablePoints = new List<NavMeshGridPoint>();
        int processedCells = 0;
        int sampledCells = estimatedOccupiedCells.Count;

        // Only sample grid cells that contain NavMesh vertices
        foreach (var cell in estimatedOccupiedCells)
        {
            processedCells++;
            if (processedCells % 100 == 0)
            {
                float progress = (float)processedCells / sampledCells;
                EditorUtility.DisplayProgressBar("NavMesh Exporter",
                    $"Sampling NavMesh grid... {processedCells}/{sampledCells}", progress);
            }

            // Calculate world position for this grid cell
            float x = actualBoundsMin.x + cell.x * gridCellSize;
            float z = actualBoundsMin.z + cell.y * gridCellSize;

            // Sample point at the center of the grid cell
            Vector3 samplePoint = new Vector3(x + gridCellSize * 0.5f, actualBoundsMax.y, z + gridCellSize * 0.5f);

            // Try to project point onto NavMesh
            if (NavMesh.SamplePosition(samplePoint, out NavMeshHit hit, actualBoundsMax.y - actualBoundsMin.y, NavMesh.AllAreas))
            {
                walkablePoints.Add(new NavMeshGridPoint
                {
                    x = hit.position.x,
                    y = hit.position.y,
                    z = hit.position.z,
                    gridX = cell.x,
                    gridZ = cell.y
                });
            }
        }

        EditorUtility.DisplayProgressBar("NavMesh Exporter", "Writing to file...", 0.95f);

        // Export to JSON
        NavMeshData data = new NavMeshData
        {
            cellSize = gridCellSize,
            zTolerance = zTolerance,
            boundsMinX = actualBoundsMin.x,
            boundsMinZ = actualBoundsMin.z,
            points = walkablePoints.ToArray()
        };

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(outputPath, json);

        EditorUtility.ClearProgressBar();

        sampledPointsCount = walkablePoints.Count;
        Debug.Log($"NavMesh export complete! Sampled {walkablePoints.Count} walkable points to {outputPath}");
        EditorUtility.DisplayDialog("Success",
            $"NavMesh exported successfully!\n\nWalkable points: {walkablePoints.Count}\nFile: {outputPath}", "OK");
    }
}
