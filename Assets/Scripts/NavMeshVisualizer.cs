using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;

/// <summary>
/// Visualizes exported NavMesh grid points in the Scene view
/// Add this component to any GameObject to see the sampled points
/// </summary>
[ExecuteInEditMode]
public class NavMeshVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    public string navMeshDataPath = "Assets/NavMeshData.json";
    public bool showPoints = true;
    public bool showGrid = true;
    public Color pointColor = new Color(0, 1, 0, 0.5f);
    public Color gridColor = new Color(0, 1, 1, 0.3f);
    public float pointSize = 0.5f;

    [Header("Info")]
    [SerializeField] private int totalPoints = 0;
    [SerializeField] private Vector3 boundsMin;
    [SerializeField] private Vector3 boundsMax;
    [SerializeField] private float cellSize;

    private NavMeshData navMeshData;
    private bool dataLoaded = false;

    [ContextMenu("Load NavMesh Data")]
    public void LoadData()
    {
        if (!File.Exists(navMeshDataPath))
        {
            Debug.LogError($"NavMesh data file not found at {navMeshDataPath}");
            return;
        }

        string json = File.ReadAllText(navMeshDataPath);
        navMeshData = JsonUtility.FromJson<NavMeshData>(json);

        if (navMeshData != null && navMeshData.points != null)
        {
            totalPoints = navMeshData.points.Length;
            cellSize = navMeshData.cellSize;

            // Calculate bounds
            if (totalPoints > 0)
            {
                boundsMin = new Vector3(navMeshData.points[0].x, navMeshData.points[0].y, navMeshData.points[0].z);
                boundsMax = boundsMin;

                foreach (var point in navMeshData.points)
                {
                    Vector3 pos = new Vector3(point.x, point.y, point.z);
                    boundsMin = Vector3.Min(boundsMin, pos);
                    boundsMax = Vector3.Max(boundsMax, pos);
                }
            }

            dataLoaded = true;
            Debug.Log($"Loaded {totalPoints} NavMesh points. Bounds: {boundsMin} to {boundsMax}");
        }
    }

    [ContextMenu("Clear Data")]
    public void ClearData()
    {
        navMeshData = null;
        dataLoaded = false;
        totalPoints = 0;
        Debug.Log("NavMesh visualization data cleared");
    }

    private void OnDrawGizmos()
    {
        if (!dataLoaded || navMeshData == null || navMeshData.points == null)
            return;

        // Draw grid lines
        if (showGrid)
        {
            Gizmos.color = gridColor;

            // Draw bounding box
            DrawBoundingBox(boundsMin, boundsMax);
        }

        // Draw sample points
        if (showPoints)
        {
            Gizmos.color = pointColor;

            foreach (var point in navMeshData.points)
            {
                Vector3 pos = new Vector3(point.x, point.y, point.z);
                Gizmos.DrawSphere(pos, pointSize);
            }
        }
    }

    private void DrawBoundingBox(Vector3 min, Vector3 max)
    {
        // Bottom face
        Gizmos.DrawLine(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z));
        Gizmos.DrawLine(new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z));
        Gizmos.DrawLine(new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z));
        Gizmos.DrawLine(new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z));

        // Top face
        Gizmos.DrawLine(new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z));
        Gizmos.DrawLine(new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z));
        Gizmos.DrawLine(new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z));
        Gizmos.DrawLine(new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z));

        // Vertical edges
        Gizmos.DrawLine(new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z));
        Gizmos.DrawLine(new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z));
        Gizmos.DrawLine(new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z));
        Gizmos.DrawLine(new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z));
    }
}
