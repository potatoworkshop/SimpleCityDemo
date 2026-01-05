using System.Collections.Generic;
using UnityEngine;

public class CityGenerator : MonoBehaviour
{
    [Header("City Layout")]
    public int gridSizeX = 200;
    public int gridSizeZ = 200;
    public float blockSpacing = 2.5f;

    [Header("Building")]
    public GameObject buildingPrefab;
    public Vector2 heightRange = new Vector2(2f, 60f);
    public Vector2 footprintRange = new Vector2(0.8f, 2.2f);
    public bool useHeightMap = false;
    public Texture2D heightMap;
    public Vector2 heightMapWorldSize = new Vector2(200f, 200f);
    public Vector2 heightMapWorldCenter = Vector2.zero;
    public int heightSampleCount = 5;
    public float heightSampleSpan = 5f;
    public Vector2 heightSampleDirection = Vector2.right;
    public float heightMapDefault = 0.5f;
    public bool heightMapOutsideIsDefault = true;

    [Header("Density Map")]
    public bool useDensityMap = false;
    public Texture2D densityMap;
    public Vector2 densityMapWorldSize = new Vector2(200f, 200f);
    public Vector2 densityMapWorldCenter = Vector2.zero;
    public float densityMapDefault = 0.5f;
    public bool densityMapOutsideIsDefault = true;
    public bool densityAffectsSpawn = false;
    public float densitySpawnMin = 0f;
    public float densitySpawnMax = 1f;

    [Header("Color Map")]
    public bool useColorMap = false;
    public Texture2D colorMap;
    public Vector2 colorMapWorldSize = new Vector2(200f, 200f);
    public Vector2 colorMapWorldCenter = Vector2.zero;
    public Color colorMapDefault = Color.white;
    public bool colorMapOutsideIsDefault = true;
    public bool lerpWithTintColor = false;
    public Color tintColor = Color.white;
    public float tintLerp = 0.5f;

    [Header("Road Avoidance")]
    public bool avoidRoads = true;
    public float roadSpacing = 20f;
    public float roadWidth = 4f;
    public bool roadsCenteredOnOrigin = true;

    [Header("Road Mask")]
    public bool useRoadMask = false;
    public Texture2D roadMask;
    public Vector2 maskWorldSize = new Vector2(200f, 200f);
    public Vector2 maskWorldCenter = Vector2.zero;
    public float maskThreshold = 0.5f;
    public bool invertMask = false;
    public bool maskOutsideIsBlocked = true;
    public bool alignToRoadMask = false;
    public float maskDirectionSampleOffset = 2f;
    public float maskDirectionMinMagnitude = 0.001f;
    public bool invertMaskDirection = false;

    [Header("Exclusion Mask")]
    public bool useExclusionMask = false;
    public Texture2D exclusionMask;
    public Vector2 exclusionWorldSize = new Vector2(200f, 200f);
    public Vector2 exclusionWorldCenter = Vector2.zero;
    public bool exclusionOutsideIsBlocked = false;

    [Header("Instancing")]
    public bool useInstancing = true;
    public Material instancedMaterialOverride;

    private const int MaxInstancesPerBatch = 1023;
    private Mesh instanceMesh;
    private Material instanceMaterial;
    private readonly List<Matrix4x4[]> instanceBatches = new List<Matrix4x4[]>();
    private readonly List<MaterialPropertyBlock> instancePropertyBlocks = new List<MaterialPropertyBlock>();
    private int instanceCount;

    [ContextMenu("Generate City")]
    public void Generate()
    {
        if (useInstancing)
        {
            GenerateInstanced();
            return;
        }

        GenerateGameObjects();
    }

    [ContextMenu("Generate City (GameObjects)")]
    public void GenerateGameObjects()
    {
        ClearChildren();

        if (buildingPrefab == null)
        {
            Debug.LogError("Assign buildingPrefab (e.g., a Cube prefab).");
            return;
        }

        var rand = new System.Random(12345);

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int z = 0; z < gridSizeZ; z++)
            {
                Vector2 world = GridToWorldXZ(x, z, roadsCenteredOnOrigin);
                if (avoidRoads && IsBlocked(world))
                {
                    continue;
                }
                if (!ShouldPlaceBuilding(world, rand))
                {
                    continue;
                }

                var go = Instantiate(buildingPrefab, transform);
                go.name = $"B_{x}_{z}";
                go.transform.position = new Vector3(world.x, 0f, world.y);
                go.transform.rotation = SampleBuildingRotation(world);
                if (useColorMap)
                {
                    ApplyColorToRenderers(go, SampleBuildingColor(world));
                }

                float h = Mathf.Lerp(heightRange.x, heightRange.y, SampleHeightFactor(world, rand));
                float s = SampleFootprint(world, rand);

                go.transform.localScale = new Vector3(s, h, s);
                go.transform.position += new Vector3(0f, h * 0.5f, 0f);
            }
        }

        Debug.Log($"Generated: {gridSizeX * gridSizeZ} buildings");
    }

    [ContextMenu("Generate City (Instanced)")]
    public void GenerateInstanced()
    {
        ClearChildren();

        if (!TryGetPrefabMeshAndMaterial(out Mesh mesh, out Material material))
        {
            return;
        }

        instanceMesh = mesh;
        SetupInstanceMaterial(material);

        instanceBatches.Clear();
        instancePropertyBlocks.Clear();
        instanceCount = 0;

        var rand = new System.Random(12345);
        int total = gridSizeX * gridSizeZ;
        var matrices = new List<Matrix4x4>(total);
        List<Vector4> colors = useColorMap ? new List<Vector4>(total) : null;

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int z = 0; z < gridSizeZ; z++)
            {
                Vector2 world = GridToWorldXZ(x, z, roadsCenteredOnOrigin);
                if (avoidRoads && IsBlocked(world))
                {
                    continue;
                }
                if (!ShouldPlaceBuilding(world, rand))
                {
                    continue;
                }

                float h = Mathf.Lerp(heightRange.x, heightRange.y, SampleHeightFactor(world, rand));
                float s = SampleFootprint(world, rand);

                var position = new Vector3(world.x, h * 0.5f, world.y);
                var scale = new Vector3(s, h, s);
                matrices.Add(Matrix4x4.TRS(position, SampleBuildingRotation(world), scale));
                if (colors != null)
                {
                    colors.Add(SampleBuildingColor(world));
                }
            }
        }

        for (int i = 0; i < matrices.Count; i += MaxInstancesPerBatch)
        {
            int count = Mathf.Min(MaxInstancesPerBatch, matrices.Count - i);
            var batch = new Matrix4x4[count];
            matrices.CopyTo(i, batch, 0, count);
            instanceBatches.Add(batch);

            if (colors != null)
            {
                var colorBatch = new Vector4[count];
                colors.CopyTo(i, colorBatch, 0, count);
                var block = new MaterialPropertyBlock();
                block.SetVectorArray("_BaseColor", colorBatch);
                block.SetVectorArray("_Color", colorBatch);
                instancePropertyBlocks.Add(block);
            }
            else
            {
                instancePropertyBlocks.Add(null);
            }
        }

        instanceCount = matrices.Count;
        Debug.Log($"Generated instanced: {instanceCount} buildings");
    }

    private void LateUpdate()
    {
        if (!useInstancing || instanceMesh == null || instanceMaterial == null || instanceBatches.Count == 0)
        {
            return;
        }

        for (int i = 0; i < instanceBatches.Count; i++)
        {
            var block = instancePropertyBlocks.Count == instanceBatches.Count ? instancePropertyBlocks[i] : null;
            if (block != null)
            {
                Graphics.DrawMeshInstanced(instanceMesh, 0, instanceMaterial, instanceBatches[i], instanceBatches[i].Length, block);
            }
            else
            {
                Graphics.DrawMeshInstanced(instanceMesh, 0, instanceMaterial, instanceBatches[i]);
            }
        }
    }

    [ContextMenu("Clear City")]
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

        ClearInstancedData();
    }

    private void ClearInstancedData()
    {
        instanceBatches.Clear();
        instancePropertyBlocks.Clear();
        instanceCount = 0;
        instanceMesh = null;
    }

    private bool TryGetPrefabMeshAndMaterial(out Mesh mesh, out Material material)
    {
        mesh = null;
        material = null;

        if (buildingPrefab == null)
        {
            Debug.LogError("Assign buildingPrefab (e.g., a Cube prefab).");
            return false;
        }

        var meshFilter = buildingPrefab.GetComponentInChildren<MeshFilter>();
        var meshRenderer = buildingPrefab.GetComponentInChildren<MeshRenderer>();

        if (meshFilter == null || meshRenderer == null)
        {
            Debug.LogError("buildingPrefab needs a MeshFilter and MeshRenderer for instancing.");
            return false;
        }

        mesh = meshFilter.sharedMesh;
        material = meshRenderer.sharedMaterial;

        if (mesh == null || material == null)
        {
            Debug.LogError("buildingPrefab mesh or material is missing.");
            return false;
        }

        return true;
    }

    private void SetupInstanceMaterial(Material sourceMaterial)
    {
        CleanupInstanceMaterial();

        Material source = instancedMaterialOverride != null ? instancedMaterialOverride : sourceMaterial;
        instanceMaterial = new Material(source);
        instanceMaterial.enableInstancing = true;
    }

    private void OnDisable()
    {
        CleanupInstanceMaterial();
    }

    private void CleanupInstanceMaterial()
    {
        if (instanceMaterial == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(instanceMaterial);
        }
        else
        {
            DestroyImmediate(instanceMaterial);
        }

        instanceMaterial = null;
    }

    private bool IsBlocked(Vector2 world)
    {
        if (useExclusionMask && IsOnExclusionMask(world))
        {
            return true;
        }

        if (useRoadMask)
        {
            return IsOnRoadMask(world);
        }

        return IsOnRoad(world);
    }

    private Vector2 GridToWorldXZ(int gridX, int gridZ, bool centeredOnOrigin)
    {
        float worldX = gridX * blockSpacing;
        float worldZ = gridZ * blockSpacing;

        if (centeredOnOrigin)
        {
            worldX -= (gridSizeX - 1) * blockSpacing * 0.5f;
            worldZ -= (gridSizeZ - 1) * blockSpacing * 0.5f;
        }

        return new Vector2(worldX, worldZ);
    }

    private bool IsOnRoad(Vector2 world)
    {
        if (roadSpacing <= 0f || roadWidth <= 0f)
        {
            return false;
        }

        float halfWidth = roadWidth * 0.5f;
        float xMod = Mathf.Abs(Mathf.Repeat(world.x + halfWidth, roadSpacing) - halfWidth);
        float zMod = Mathf.Abs(Mathf.Repeat(world.y + halfWidth, roadSpacing) - halfWidth);

        return xMod < halfWidth || zMod < halfWidth;
    }

    private bool IsOnRoadMask(Vector2 world)
    {
        if (!TrySampleRoadMask(world, out float value))
        {
            return maskOutsideIsBlocked;
        }

        bool isRoad = value < maskThreshold;
        if (invertMask)
        {
            isRoad = !isRoad;
        }

        return isRoad;
    }

    private bool IsOnExclusionMask(Vector2 world)
    {
        if (exclusionMask == null)
        {
            return exclusionOutsideIsBlocked;
        }

        if (!TrySampleMask(exclusionMask, exclusionWorldSize, exclusionWorldCenter, world, out float value))
        {
            return exclusionOutsideIsBlocked;
        }

        return value > 0f;
    }

    private Quaternion SampleBuildingRotation(Vector2 world)
    {
        if (!alignToRoadMask)
        {
            return Quaternion.identity;
        }

        if (!TryGetRoadMaskDirection(world, out Vector2 dir))
        {
            return Quaternion.identity;
        }

        return Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up);
    }

    private bool TryGetRoadMaskDirection(Vector2 world, out Vector2 direction)
    {
        direction = Vector2.right;

        float offset = Mathf.Max(0.01f, maskDirectionSampleOffset);
        if (!TrySampleRoadMask(world + new Vector2(offset, 0f), out float r) ||
            !TrySampleRoadMask(world + new Vector2(-offset, 0f), out float l) ||
            !TrySampleRoadMask(world + new Vector2(0f, offset), out float u) ||
            !TrySampleRoadMask(world + new Vector2(0f, -offset), out float d))
        {
            return false;
        }

        Vector2 grad = new Vector2(r - l, u - d);
        float minMag = Mathf.Max(0.000001f, maskDirectionMinMagnitude);
        if (grad.sqrMagnitude < minMag * minMag)
        {
            return false;
        }

        Vector2 dir = -grad.normalized;
        if (invertMaskDirection)
        {
            dir = -dir;
        }

        direction = dir;
        return true;
    }

    private bool TrySampleRoadMask(Vector2 world, out float value)
    {
        return TrySampleMask(roadMask, maskWorldSize, maskWorldCenter, world, out value);
    }

    private bool TrySampleMask(Texture2D mask, Vector2 worldSize, Vector2 worldCenter, Vector2 world, out float value)
    {
        value = 0f;
        if (mask == null)
        {
            return false;
        }

        Vector2 size = worldSize;
        if (size.x <= 0f || size.y <= 0f)
        {
            size = new Vector2(gridSizeX * blockSpacing, gridSizeZ * blockSpacing);
        }

        float u = Mathf.InverseLerp(worldCenter.x - size.x * 0.5f, worldCenter.x + size.x * 0.5f, world.x);
        float v = Mathf.InverseLerp(worldCenter.y - size.y * 0.5f, worldCenter.y + size.y * 0.5f, world.y);

        if (u < 0f || u > 1f || v < 0f || v > 1f)
        {
            return false;
        }

        try
        {
            value = mask.GetPixelBilinear(u, v).grayscale;
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }

    private float SampleHeightFactor(Vector2 world, System.Random rand)
    {
        if (!useHeightMap)
        {
            return (float)rand.NextDouble();
        }

        return SampleHeightAlongLine(world);
    }

    private float SampleFootprint(Vector2 world, System.Random rand)
    {
        if (!useDensityMap)
        {
            return Mathf.Lerp(footprintRange.x, footprintRange.y, (float)rand.NextDouble());
        }

        float density = SampleDensityMap(world);
        float t = Mathf.Clamp01(density);
        return Mathf.Lerp(footprintRange.y, footprintRange.x, t);
    }

    private float SampleHeightAlongLine(Vector2 center)
    {
        int count = Mathf.Max(1, heightSampleCount);
        float span = Mathf.Max(0f, heightSampleSpan);
        Vector2 dir = heightSampleDirection.sqrMagnitude > 0.000001f
            ? heightSampleDirection.normalized
            : Vector2.right;

        if (count == 1 || span <= 0.0001f)
        {
            return SampleHeightMap(center);
        }

        float start = -span * 0.5f;
        float step = span / (count - 1);
        float sum = 0f;

        for (int i = 0; i < count; i++)
        {
            float offset = start + step * i;
            Vector2 p = center + dir * offset;
            sum += SampleHeightMap(p);
        }

        return Mathf.Clamp01(sum / count);
    }

    private bool ShouldPlaceBuilding(Vector2 world, System.Random rand)
    {
        if (!densityAffectsSpawn)
        {
            return true;
        }

        float density = SampleDensityMap(world);
        float t = Mathf.Clamp01(density);
        float chance = Mathf.Lerp(densitySpawnMin, densitySpawnMax, t);
        chance = Mathf.Clamp01(chance);
        return (float)rand.NextDouble() <= chance;
    }

    private Color SampleBuildingColor(Vector2 world)
    {
        if (!useColorMap)
        {
            return ApplyTint(Color.white);
        }

        if (!TrySampleColorMap(world, out Color color))
        {
            return ApplyTint(colorMapOutsideIsDefault ? colorMapDefault : Color.white);
        }

        return ApplyTint(color);
    }

    private float SampleHeightMap(Vector2 world)
    {
        if (heightMap == null)
        {
            return Mathf.Clamp01(heightMapDefault);
        }

        Vector2 size = heightMapWorldSize;
        if (size.x <= 0f || size.y <= 0f)
        {
            size = new Vector2(gridSizeX * blockSpacing, gridSizeZ * blockSpacing);
        }

        float u = Mathf.InverseLerp(heightMapWorldCenter.x - size.x * 0.5f, heightMapWorldCenter.x + size.x * 0.5f, world.x);
        float v = Mathf.InverseLerp(heightMapWorldCenter.y - size.y * 0.5f, heightMapWorldCenter.y + size.y * 0.5f, world.y);

        if (u < 0f || u > 1f || v < 0f || v > 1f)
        {
            return heightMapOutsideIsDefault ? Mathf.Clamp01(heightMapDefault) : 0f;
        }

        try
        {
            return heightMap.GetPixelBilinear(u, v).grayscale;
        }
        catch (UnityException)
        {
            return Mathf.Clamp01(heightMapDefault);
        }
    }

    private float SampleDensityMap(Vector2 world)
    {
        if (densityMap == null)
        {
            return Mathf.Clamp01(densityMapDefault);
        }

        Vector2 size = densityMapWorldSize;
        if (size.x <= 0f || size.y <= 0f)
        {
            size = new Vector2(gridSizeX * blockSpacing, gridSizeZ * blockSpacing);
        }

        float u = Mathf.InverseLerp(densityMapWorldCenter.x - size.x * 0.5f, densityMapWorldCenter.x + size.x * 0.5f, world.x);
        float v = Mathf.InverseLerp(densityMapWorldCenter.y - size.y * 0.5f, densityMapWorldCenter.y + size.y * 0.5f, world.y);

        if (u < 0f || u > 1f || v < 0f || v > 1f)
        {
            return densityMapOutsideIsDefault ? Mathf.Clamp01(densityMapDefault) : 0f;
        }

        try
        {
            return densityMap.GetPixelBilinear(u, v).grayscale;
        }
        catch (UnityException)
        {
            return Mathf.Clamp01(densityMapDefault);
        }
    }

    private bool TrySampleColorMap(Vector2 world, out Color color)
    {
        color = Color.white;
        if (colorMap == null)
        {
            return false;
        }

        Vector2 size = colorMapWorldSize;
        if (size.x <= 0f || size.y <= 0f)
        {
            size = new Vector2(gridSizeX * blockSpacing, gridSizeZ * blockSpacing);
        }

        float u = Mathf.InverseLerp(colorMapWorldCenter.x - size.x * 0.5f, colorMapWorldCenter.x + size.x * 0.5f, world.x);
        float v = Mathf.InverseLerp(colorMapWorldCenter.y - size.y * 0.5f, colorMapWorldCenter.y + size.y * 0.5f, world.y);

        if (u < 0f || u > 1f || v < 0f || v > 1f)
        {
            return false;
        }

        try
        {
            color = colorMap.GetPixelBilinear(u, v);
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }

    private static void ApplyColorToRenderers(GameObject target, Color color)
    {
        var renderers = target.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            var block = new MaterialPropertyBlock();
            renderers[i].GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderers[i].SetPropertyBlock(block);
        }
    }

    private Color ApplyTint(Color baseColor)
    {
        if (!lerpWithTintColor)
        {
            return baseColor;
        }

        float t = Mathf.Clamp01(tintLerp);
        return Color.Lerp(baseColor, tintColor, t);
    }

}
