using System.Collections.Generic;
using UnityEngine;

public class RoadGridGenerator : MonoBehaviour
{
    [Header("Road")]
    public GameObject roadPrefab;
    public float roadWidth = 4f;
    public float roadThickness = 0.2f;
    public bool centerOnOrigin = true;

    [Header("Grid")]
    public int gridCellsX = 10;
    public int gridCellsZ = 10;
    public float gridSpacing = 30f;

    [HideInInspector]
    public List<GameObject> spawnedRoads = new List<GameObject>();

    [ContextMenu("Generate Roads")]
    public void Generate()
    {
        ClearChildren();
        spawnedRoads.Clear();

        if (roadPrefab == null)
        {
            Debug.LogError("Assign roadPrefab (e.g., a Cube or Plane). ");
            return;
        }

        int cellsX = Mathf.Max(1, gridCellsX);
        int cellsZ = Mathf.Max(1, gridCellsZ);
        float spacing = Mathf.Max(0.1f, gridSpacing);

        float width = cellsX * spacing;
        float depth = cellsZ * spacing;
        Vector3 center = centerOnOrigin ? Vector3.zero : transform.position;
        Vector3 origin = center - new Vector3(width * 0.5f, 0f, depth * 0.5f);

        // Horizontal lines (along X)
        for (int z = 0; z <= cellsZ; z++)
        {
            Vector3 start = origin + new Vector3(0f, 0f, z * spacing);
            Vector3 end = start + new Vector3(width, 0f, 0f);
            SpawnRoadSegment(start, end, $"Road_Row_{z}");
        }

        // Vertical lines (along Z)
        for (int x = 0; x <= cellsX; x++)
        {
            Vector3 start = origin + new Vector3(x * spacing, 0f, 0f);
            Vector3 end = start + new Vector3(0f, 0f, depth);
            SpawnRoadSegment(start, end, $"Road_Col_{x}");
        }
    }

    [ContextMenu("Clear Roads")]
    public void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
    }

    private void SpawnRoadSegment(Vector3 start, Vector3 end, string name)
    {
        float length = Vector3.Distance(start, end);
        if (length <= 0.001f)
        {
            return;
        }

        Vector3 mid = (start + end) * 0.5f;
        Quaternion rotation = Quaternion.LookRotation((end - start).normalized, Vector3.up);
        var road = Instantiate(roadPrefab, transform);
        road.name = name;
        road.transform.position = mid;
        road.transform.rotation = rotation;
        road.transform.localScale = new Vector3(roadWidth, roadThickness, length);
        spawnedRoads.Add(road);
    }
}
