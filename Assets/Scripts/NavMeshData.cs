using UnityEngine;

/// <summary>
/// Data structure for exported NavMesh grid
/// Shared between NavMeshExporter, NavMeshUploader, and NavMeshVisualizer
/// </summary>
[System.Serializable]
public class NavMeshData
{
    public float cellSize;
    public float zTolerance;
    public float boundsMinX;
    public float boundsMinZ;
    public NavMeshGridPoint[] points;
}

[System.Serializable]
public class NavMeshGridPoint
{
    public float x;
    public float y;
    public float z;
    public int gridX;
    public int gridZ;
}
