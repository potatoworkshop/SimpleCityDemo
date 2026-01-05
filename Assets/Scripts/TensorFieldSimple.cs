using System.Collections.Generic;
using System.IO;
using UnityEngine;

public struct Tensor2D
{
    public readonly float A;
    public readonly float B;

    public Tensor2D(float a, float b)
    {
        A = a;
        B = b;
    }

    public static Tensor2D FromRTheta(float r, float thetaRad)
    {
        return new Tensor2D(r * Mathf.Cos(2f * thetaRad), r * Mathf.Sin(2f * thetaRad));
    }

    public static Tensor2D FromXY(Vector2 xy)
    {
        float xy2 = -2f * xy.x * xy.y;
        float diffSquares = xy.y * xy.y - xy.x * xy.x;
        return Normalize(new Tensor2D(diffSquares, xy2));
    }

    public static Tensor2D Normalize(Tensor2D tensor)
    {
        float l = Mathf.Sqrt(tensor.A * tensor.A + tensor.B * tensor.B);
        if (l <= Mathf.Epsilon)
        {
            return new Tensor2D(0f, 0f);
        }

        return new Tensor2D(tensor.A / l, tensor.B / l);
    }

    public static Tensor2D operator *(float scalar, Tensor2D tensor)
    {
        return new Tensor2D(scalar * tensor.A, scalar * tensor.B);
    }

    public static Tensor2D operator +(Tensor2D left, Tensor2D right)
    {
        return new Tensor2D(left.A + right.A, left.B + right.B);
    }

    public void EigenVectors(out Vector2 major, out Vector2 minor)
    {
        if (Mathf.Abs(B) < 0.0000001f)
        {
            if (Mathf.Abs(A) < 0.0000001f)
            {
                major = Vector2.zero;
                minor = Vector2.zero;
            }
            else
            {
                major = Vector2.right;
                minor = Vector2.up;
            }
        }
        else
        {
            float eval = Mathf.Sqrt(A * A + B * B);
            major = new Vector2(B, eval - A);
            minor = new Vector2(B, -eval - A);
        }
    }
}

public class TensorFieldSimple : MonoBehaviour
{
    [Header("Weighted 3")]
    public float weightA = 1f;
    public float weightB = 1f;
    public float weightC = 1f;
    public FieldConfig blendA = new FieldConfig();
    public FieldConfig blendB = new FieldConfig();
    public FieldConfig blendC = new FieldConfig();

    [System.Serializable]
    public class FieldConfig
    {
        public FieldType fieldType = FieldType.Constant;
        public float angleDegrees = 0f;
        public float strength = 1f;
        public Vector2 radialCenter = Vector2.zero;
        public float noiseScale = 0.05f;
        public float noiseAmplitude = 1f;
        public Vector2 noiseOffset = Vector2.zero;
        public float gradientStep = 0.5f;
    }

    public enum FieldType
    {
        Constant,
        Radial,
        HeightmapPerlin
    }

    [Header("Viz")]
    public bool drawTensorField = true;
    public Vector2 areaSize = new Vector2(60f, 60f);
    public int resolution = 12;
    public float lineLength = 4f;
    public bool drawMinor = true;
    public Color majorColor = new Color(0.2f, 0.8f, 1f, 1f);
    public Color minorColor = new Color(1f, 0.7f, 0.2f, 1f);

    [Header("Streamline")]
    public bool drawStreamline = true;
    public bool traceBothDirections = true;
    public bool useMajorStreamline = true;
    public bool useRk4 = true;
    public int streamlineCount = 1;
    public int maxStreamlines = 50;
    public bool spawnAlternateSeeds = true;
    public float seedSpacing = 10f;
    public bool includeStreamlineStart = true;
    public int streamlineSeed = 12345;
    public Vector2 streamlineStart = Vector2.zero;
    public float streamlineStep = 1f;
    public int streamlineSteps = 200;
    public Color streamlineColor = Color.white;

    [Header("Streamline Constraints")]
    public float minSegmentLength = 0.25f;
    public float mergeDistance = 1f;
    public float cosineSearchAngle = 0.95f;

    [Header("Seed Priority")]
    public Texture2D priorityMap;
    public float priorityMin = 0f;
    public float priorityMax = 1f;
    public float priorityDefault = 0.5f;
    public float priorityWeight = 1f;

    [Header("Seed Separation")]
    public Texture2D separationMap;
    public float separationMin = 2f;
    public float separationMax = 10f;
    public float separationDefault = 5f;
    public float separationWeight = 1f;

    [Header("Road Prefab")]
    public GameObject roadPrefab;
    public float roadWidth = 4f;
    public float roadThickness = 0.2f;
    public float roadYOffset = 0f;

    [HideInInspector]
    public List<GameObject> spawnedRoads = new List<GameObject>();

    [Header("Road Mask Bake")]
    public int bakeResolution = 1024;
    public float bakeRoadWidth = 4f;
    public string bakePath = "Assets/Generated/RoadMask.png";

    private List<Segment2D> cachedSegments = new List<Segment2D>();

    private Tensor2D SampleTensor(Vector2 position)
    {
        return SampleWeighted3(position);
    }

    private Tensor2D SampleWeighted3(Vector2 position)
    {
        float total = weightA + weightB + weightC;
        if (total <= Mathf.Epsilon)
        {
            return new Tensor2D(0f, 0f);
        }

        Tensor2D a = SampleTensorFromConfig(blendA, position);
        Tensor2D b = SampleTensorFromConfig(blendB, position);
        Tensor2D c = SampleTensorFromConfig(blendC, position);

        return (weightA / total) * a + (weightB / total) * b + (weightC / total) * c;
    }

    private Tensor2D SampleTensorFromConfig(FieldConfig config, Vector2 position)
    {
        switch (config.fieldType)
        {
            case FieldType.Radial:
                return config.strength * Tensor2D.FromXY(position - config.radialCenter);
            case FieldType.HeightmapPerlin:
                return SampleHeightmapTensor(
                    position,
                    config.noiseScale,
                    config.noiseAmplitude,
                    config.noiseOffset,
                    config.gradientStep
                );
            default:
                return Tensor2D.FromRTheta(config.strength, config.angleDegrees * Mathf.Deg2Rad);
        }
    }

    private Tensor2D SampleHeightmapTensor(Vector2 position, float scale, float amplitude, Vector2 offset, float step)
    {
        Vector2 grad = SampleHeightGradient(position, step, scale, amplitude, offset);
        if (grad.sqrMagnitude <= 0.000001f)
        {
            return new Tensor2D(0f, 0f);
        }

        float theta = Mathf.Atan2(grad.y, grad.x) + Mathf.PI * 0.5f;
        float r = grad.magnitude;
        return Tensor2D.Normalize(Tensor2D.FromRTheta(r, theta));
    }

    private Vector2 SampleHeightGradient(Vector2 position, float step, float scale, float amplitude, Vector2 offset)
    {
        float clampedStep = Mathf.Max(0.001f, step);
        float hL = SampleHeight(position + new Vector2(-clampedStep, 0f), scale, amplitude, offset);
        float hR = SampleHeight(position + new Vector2(clampedStep, 0f), scale, amplitude, offset);
        float hD = SampleHeight(position + new Vector2(0f, -clampedStep), scale, amplitude, offset);
        float hU = SampleHeight(position + new Vector2(0f, clampedStep), scale, amplitude, offset);

        return new Vector2((hR - hL) / (2f * clampedStep), (hU - hD) / (2f * clampedStep));
    }

    private float SampleHeight(Vector2 position, float scale, float amplitude, Vector2 offset)
    {
        float clampedScale = Mathf.Max(0.0001f, scale);
        float x = (position.x + offset.x) * clampedScale;
        float y = (position.y + offset.y) * clampedScale;
        return Mathf.PerlinNoise(x, y) * amplitude;
    }

    [ContextMenu("Rebuild Streamlines")]
    public void RebuildStreamlines()
    {
        Vector2 size = new Vector2(Mathf.Max(0.01f, areaSize.x), Mathf.Max(0.01f, areaSize.y));
        Vector2 min = -size * 0.5f;
        Vector2 max = size * 0.5f;

        cachedSegments = BuildStreamlines(min, max);
    }

    [ContextMenu("Build Roads")]
    public void BuildRoads()
    {
        ClearRoads();

        if (roadPrefab == null)
        {
            Debug.LogError("Assign roadPrefab to build roads.");
            return;
        }

        if (cachedSegments == null || cachedSegments.Count == 0)
        {
            Debug.LogWarning("No cached streamlines. Run Rebuild Streamlines first.");
            return;
        }

        Vector3 center = transform.position;
        for (int i = 0; i < cachedSegments.Count; i++)
        {
            SpawnRoadSegment(center, cachedSegments[i].A, cachedSegments[i].B, $"Road_{i:0000}");
        }
    }

    [ContextMenu("Clear Roads")]
    public void ClearRoads()
    {
        for (int i = spawnedRoads.Count - 1; i >= 0; i--)
        {
            var go = spawnedRoads[i];
            if (go == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        spawnedRoads.Clear();
    }

    [ContextMenu("Rebuild Streamlines And Roads")]
    public void RebuildStreamlinesAndRoads()
    {
        RebuildStreamlines();
        BuildRoads();
    }

    [ContextMenu("Bake Road Mask")]
    public void BakeRoadMask()
    {
        if (cachedSegments == null || cachedSegments.Count == 0)
        {
            Debug.LogWarning("No cached streamlines. Run Rebuild Streamlines first.");
            return;
        }

        int res = Mathf.Clamp(bakeResolution, 16, 8192);
        float halfWidth = Mathf.Max(0.001f, bakeRoadWidth * 0.5f);

        Vector2 size = new Vector2(Mathf.Max(0.01f, areaSize.x), Mathf.Max(0.01f, areaSize.y));
        Vector2 min = -size * 0.5f;
        Vector2 max = size * 0.5f;
        float worldPerPixelX = size.x / (res - 1);
        float worldPerPixelY = size.y / (res - 1);

        Color32 white = new Color32(255, 255, 255, 255);
        Color32 black = new Color32(0, 0, 0, 255);
        Color32[] pixels = new Color32[res * res];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = white;
        }

        float halfWidthSqr = halfWidth * halfWidth;
        for (int i = 0; i < cachedSegments.Count; i++)
        {
            Vector2 a = cachedSegments[i].A;
            Vector2 b = cachedSegments[i].B;
            if ((a - b).sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            Vector2 segMin = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y)) - new Vector2(halfWidth, halfWidth);
            Vector2 segMax = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)) + new Vector2(halfWidth, halfWidth);

            int x0 = Mathf.Clamp(Mathf.FloorToInt((segMin.x - min.x) / worldPerPixelX), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((segMax.x - min.x) / worldPerPixelX), 0, res - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((segMin.y - min.y) / worldPerPixelY), 0, res - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((segMax.y - min.y) / worldPerPixelY), 0, res - 1);

            for (int y = y0; y <= y1; y++)
            {
                float wy = min.y + (y + 0.5f) * worldPerPixelY;
                int row = y * res;
                for (int x = x0; x <= x1; x++)
                {
                    float wx = min.x + (x + 0.5f) * worldPerPixelX;
                    Vector2 p = new Vector2(wx, wy);

                    float dSqr = DistancePointToSegmentSqr(p, a, b);
                    if (dSqr <= halfWidthSqr)
                    {
                        pixels[row + x] = black;
                    }
                }
            }
        }

        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply();

        string fullPath = ResolveBakePath(bakePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? string.Empty);
        File.WriteAllBytes(fullPath, tex.EncodeToPNG());

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    private void OnDrawGizmos()
    {
        Vector2 size = new Vector2(Mathf.Max(0.01f, areaSize.x), Mathf.Max(0.01f, areaSize.y));
        Vector2 min = -size * 0.5f;
        Vector2 max = size * 0.5f;
        Vector3 center = transform.position;

        if (drawTensorField)
        {
            int steps = Mathf.Max(1, resolution);
            float halfLen = lineLength * 0.5f;
            for (int z = 0; z <= steps; z++)
            {
                float v = z / (float)steps;
                for (int x = 0; x <= steps; x++)
                {
                    float u = x / (float)steps;
                    Vector2 p2 = min + new Vector2(u * size.x, v * size.y);

                    Tensor2D tensor = SampleTensor(p2);
                    tensor.EigenVectors(out Vector2 major, out Vector2 minor);

                    if (major.sqrMagnitude > 0.000001f)
                    {
                        major.Normalize();
                        Vector3 p3 = center + new Vector3(p2.x, 0f, p2.y);
                        Vector3 d = new Vector3(major.x, 0f, major.y) * halfLen;
                        Gizmos.color = majorColor;
                        Gizmos.DrawLine(p3 - d, p3 + d);
                    }

                    if (drawMinor && minor.sqrMagnitude > 0.000001f)
                    {
                        minor.Normalize();
                        Vector3 p3 = center + new Vector3(p2.x, 0f, p2.y);
                        Vector3 d = new Vector3(minor.x, 0f, minor.y) * halfLen;
                        Gizmos.color = minorColor;
                        Gizmos.DrawLine(p3 - d, p3 + d);
                    }
                }
            }
        }

        if (drawStreamline)
        {
            DrawCachedStreamlines(center);
        }
    }

    private void DrawCachedStreamlines(Vector3 center)
    {
        if (cachedSegments == null || cachedSegments.Count == 0)
        {
            return;
        }

        Gizmos.color = streamlineColor;
        for (int i = 0; i < cachedSegments.Count; i++)
        {
            DrawSegment(center, cachedSegments[i].A, cachedSegments[i].B);
        }
    }

    private List<Segment2D> BuildStreamlines(Vector2 min, Vector2 max)
    {
        float step = Mathf.Max(0.001f, streamlineStep);
        int maxSteps = Mathf.Max(1, streamlineSteps);
        float minSegment = Mathf.Max(0.0001f, minSegmentLength);
        float mergeDist = Mathf.Max(0.0001f, mergeDistance);
        int maxTraces = Mathf.Max(1, maxStreamlines);
        float spacing = Mathf.Max(0.001f, seedSpacing);

        List<Vector2> vertices = new List<Vector2>();
        List<Segment2D> segments = new List<Segment2D>();
        List<SeedInfo> seeds = BuildStreamlineSeeds(min, max, useMajorStreamline);
        if (seeds.Count == 0)
        {
            return segments;
        }

        int traced = 0;
        while (seeds.Count > 0 && traced < maxTraces)
        {
            if (!TryDequeueSeed(seeds, min, max, segments, out SeedInfo seed))
            {
                continue;
            }

            Vector2 start = seed.Position;
            if (TryFindNearbyVertex(start, vertices, mergeDist, false, out Vector2 snapStart))
            {
                start = snapStart;
            }

            vertices.Add(start);

            TraceStreamlineDirection(
                min,
                max,
                vertices,
                segments,
                seeds,
                step,
                maxSteps,
                minSegment,
                mergeDist,
                spacing,
                seed.UseMajor,
                start,
                false
            );
            if (traceBothDirections)
            {
                TraceStreamlineDirection(
                    min,
                    max,
                    vertices,
                    segments,
                    seeds,
                    step,
                    maxSteps,
                    minSegment,
                    mergeDist,
                    spacing,
                    seed.UseMajor,
                    start,
                    true
                );
            }

            traced++;
        }

        return segments;
    }

    private List<SeedInfo> BuildStreamlineSeeds(Vector2 min, Vector2 max, bool useMajor)
    {
        List<SeedInfo> seeds = new List<SeedInfo>();
        int count = Mathf.Max(1, streamlineCount);

        if (includeStreamlineStart && IsInsideArea(streamlineStart, min, max))
        {
            seeds.Add(new SeedInfo(streamlineStart, useMajor, SampleSeedPriority(streamlineStart, min, max)));
        }

        int remaining = count - seeds.Count;
        if (remaining <= 0)
        {
            seeds.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return seeds;
        }

        System.Random rng = new System.Random(streamlineSeed);
        for (int i = 0; i < remaining; i++)
        {
            float u = (float)rng.NextDouble();
            float v = (float)rng.NextDouble();
            Vector2 p = new Vector2(
                Mathf.Lerp(min.x, max.x, u),
                Mathf.Lerp(min.y, max.y, v)
            );
            seeds.Add(new SeedInfo(p, useMajor, SampleSeedPriority(p, min, max)));
        }

        seeds.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        return seeds;
    }

    private void TraceStreamlineDirection(
        Vector2 min,
        Vector2 max,
        List<Vector2> vertices,
        List<Segment2D> segments,
        List<SeedInfo> seedQueue,
        float step,
        int maxSteps,
        float minSegment,
        float mergeDist,
        float spacing,
        bool useMajor,
        Vector2 start,
        bool reverse
    )
    {
        Vector2 p = start;
        Vector2 prevDir = Vector2.zero;
        float seedDistance = 0f;

        for (int i = 0; i < maxSteps; i++)
        {
            Vector2 dir = SampleStreamDirection(p, prevDir, useMajor);
            if (i == 0 && reverse)
            {
                dir = -dir;
            }

            if (dir.sqrMagnitude <= 0.000001f)
            {
                break;
            }

            Vector2 next = p + dir * step;
            bool outOfBounds = !IsInsideArea(next, min, max);
            if (outOfBounds)
            {
                if (!TryClipToBounds(p, next, min, max, out next))
                {
                    break;
                }
            }

            float segLen = Vector2.Distance(p, next);
            if (segLen < minSegment)
            {
                break;
            }

            if (!outOfBounds && TryFindNearbyVertex(next, vertices, mergeDist, true, out Vector2 snapVertex))
            {
                next = snapVertex;
                RecordSegment(segments, vertices, p, next, false);
                break;
            }

            if (!outOfBounds && TryFindSegmentIntersection(p, next, segments, mergeDist, out Vector2 hitPoint))
            {
                next = hitPoint;
                RecordSegment(segments, vertices, p, next, true);
                break;
            }

            RecordSegment(segments, vertices, p, next, true);

            if (spawnAlternateSeeds && !outOfBounds)
            {
                seedDistance += segLen;
                if (seedDistance >= spacing)
                {
                    seedDistance = 0f;
                    EnqueueSeed(seedQueue, new SeedInfo(next, !useMajor, SampleSeedPriority(next, min, max)));
                }
            }

            prevDir = dir;
            p = next;

            if (outOfBounds)
            {
                break;
            }
        }
    }

    private Vector2 SampleStreamDirection(Vector2 position, Vector2 previousDirection)
    {
        if (useRk4)
        {
            return SampleStreamDirectionRk4(position, previousDirection, useMajorStreamline);
        }

        return SampleStreamDirectionBase(position, previousDirection, useMajorStreamline);
    }

    private Vector2 SampleStreamDirection(Vector2 position, Vector2 previousDirection, bool useMajor)
    {
        if (useRk4)
        {
            return SampleStreamDirectionRk4(position, previousDirection, useMajor);
        }

        return SampleStreamDirectionBase(position, previousDirection, useMajor);
    }

    private Vector2 SampleStreamDirectionRk4(Vector2 position, Vector2 previousDirection, bool useMajor)
    {
        float step = Mathf.Max(0.001f, streamlineStep);

        Vector2 k1 = SampleStreamDirectionBase(position, previousDirection, useMajor);
        if (k1.sqrMagnitude <= 0.000001f)
        {
            return Vector2.zero;
        }

        Vector2 k2 = SampleStreamDirectionBase(position + k1 * (step * 0.5f), k1, useMajor);
        Vector2 k3 = SampleStreamDirectionBase(position + k2 * (step * 0.5f), k2, useMajor);
        Vector2 k4 = SampleStreamDirectionBase(position + k3 * step, k3, useMajor);

        Vector2 dir = (k1 + 2f * k2 + 2f * k3 + k4) / 6f;
        if (dir.sqrMagnitude <= 0.000001f)
        {
            return Vector2.zero;
        }

        dir.Normalize();
        if (previousDirection.sqrMagnitude > 0.000001f && Vector2.Dot(previousDirection, dir) < 0f)
        {
            dir = -dir;
        }

        return dir;
    }

    private Vector2 SampleStreamDirectionBase(Vector2 position, Vector2 previousDirection, bool useMajor)
    {
        Tensor2D tensor = SampleTensor(position);
        tensor.EigenVectors(out Vector2 major, out Vector2 minor);

        Vector2 dir = useMajor ? major : minor;
        if (dir.sqrMagnitude <= 0.000001f)
        {
            return Vector2.zero;
        }

        dir.Normalize();
        if (previousDirection.sqrMagnitude > 0.000001f && Vector2.Dot(previousDirection, dir) < 0f)
        {
            dir = -dir;
        }

        return dir;
    }

    private static bool IsInsideArea(Vector2 position, Vector2 min, Vector2 max)
    {
        return position.x >= min.x && position.x <= max.x && position.y >= min.y && position.y <= max.y;
    }

    private static Vector3 ToWorld(Vector2 position, Vector3 center)
    {
        return center + new Vector3(position.x, 0f, position.y);
    }

    private static void DrawSegment(Vector3 center, Vector2 a, Vector2 b)
    {
        Gizmos.DrawLine(ToWorld(a, center), ToWorld(b, center));
    }

    private static void RecordSegment(List<Segment2D> segments, List<Vector2> vertices, Vector2 a, Vector2 b, bool addEndVertex)
    {
        segments.Add(new Segment2D(a, b));
        if (addEndVertex)
        {
            vertices.Add(b);
        }
    }

    private void SpawnRoadSegment(Vector3 center, Vector2 a, Vector2 b, string name)
    {
        Vector3 start = ToWorld(a, center);
        Vector3 end = ToWorld(b, center);
        start.y += roadYOffset;
        end.y += roadYOffset;

        float length = Vector3.Distance(start, end);
        if (length <= 0.001f)
        {
            return;
        }

        Vector3 mid = (start + end) * 0.5f;
        Quaternion rotation = Quaternion.LookRotation((end - start).normalized, Vector3.up);
        GameObject road = Instantiate(roadPrefab, transform);
        road.name = name;
        road.transform.position = mid;
        road.transform.rotation = rotation;
        road.transform.localScale = new Vector3(roadWidth, roadThickness, length);
        spawnedRoads.Add(road);
    }

    private static string ResolveBakePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Path.Combine(Application.dataPath, "Generated/RoadMask.png");
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        if (path.StartsWith("Assets/"))
        {
            return Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
        }

        return Path.Combine(Application.dataPath, path);
    }

    private static bool TryFindNearbyVertex(Vector2 point, List<Vector2> vertices, float distance, out Vector2 nearest)
    {
        float bestSqr = distance * distance;
        int lastIndex = vertices.Count;
        nearest = Vector2.zero;
        bool found = false;

        for (int i = 0; i < lastIndex; i++)
        {
            float d = (vertices[i] - point).sqrMagnitude;
            if (d <= bestSqr)
            {
                bestSqr = d;
                nearest = vertices[i];
                found = true;
            }
        }

        return found;
    }

    private static bool TryFindNearbyVertex(Vector2 point, List<Vector2> vertices, float distance, bool ignoreLast, out Vector2 nearest)
    {
        if (!ignoreLast)
        {
            return TryFindNearbyVertex(point, vertices, distance, out nearest);
        }

        float bestSqr = distance * distance;
        int lastIndex = vertices.Count - 1;
        nearest = Vector2.zero;
        bool found = false;

        for (int i = 0; i < lastIndex; i++)
        {
            float d = (vertices[i] - point).sqrMagnitude;
            if (d <= bestSqr)
            {
                bestSqr = d;
                nearest = vertices[i];
                found = true;
            }
        }

        return found;
    }

    private static bool TryFindSegmentIntersection(Vector2 a, Vector2 b, List<Segment2D> segments, float snapDistance, out Vector2 hitPoint)
    {
        hitPoint = Vector2.zero;
        int lastIndex = segments.Count - 1;
        if (lastIndex <= 0)
        {
            return false;
        }

        for (int i = 0; i < lastIndex; i++)
        {
            if (TryGetSegmentIntersection(a, b, segments[i].A, segments[i].B, out Vector2 intersection))
            {
                if ((intersection - a).sqrMagnitude <= snapDistance * snapDistance)
                {
                    continue;
                }

                Vector2 snapped = SnapIntersectionToEndpoint(intersection, segments[i], snapDistance);
                hitPoint = snapped;
                return true;
            }
        }

        return false;
    }

    private bool TryDequeueSeed(List<SeedInfo> seeds, Vector2 min, Vector2 max, List<Segment2D> segments, out SeedInfo seed)
    {
        while (seeds.Count > 0)
        {
            seed = seeds[0];
            seeds.RemoveAt(0);

            if (IsSeedValid(seed.Position, seed.UseMajor, min, max, segments))
            {
                return true;
            }
        }

        seed = default;
        return false;
    }

    private bool IsSeedValid(Vector2 position, bool useMajor, Vector2 min, Vector2 max, List<Segment2D> segments)
    {
        if (!IsInsideArea(position, min, max))
        {
            return false;
        }

        Vector2 dir = SampleStreamDirectionBase(position, Vector2.zero, useMajor);
        if (dir.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        float separation = SampleSeedSeparation(position, min, max);
        if (separation <= 0.0001f)
        {
            return false;
        }

        float cosThreshold = Mathf.Clamp(cosineSearchAngle, -1f, 1f);
        if (HasParallelEdgeNearby(position, dir, segments, separation, cosThreshold))
        {
            return false;
        }

        return true;
    }

    private static bool HasParallelEdgeNearby(Vector2 point, Vector2 direction, List<Segment2D> segments, float distance, float cosThreshold)
    {
        if (segments.Count == 0)
        {
            return false;
        }

        float distSqr = distance * distance;
        Vector2 dir = direction.normalized;

        for (int i = 0; i < segments.Count; i++)
        {
            Segment2D s = segments[i];
            float segLenSqr = (s.B - s.A).sqrMagnitude;
            if (segLenSqr <= 0.000001f)
            {
                continue;
            }

            float dSqr = DistancePointToSegmentSqr(point, s.A, s.B);
            if (dSqr > distSqr)
            {
                continue;
            }

            Vector2 segDir = (s.B - s.A).normalized;
            float dot = Mathf.Abs(Vector2.Dot(segDir, dir));
            if (dot > cosThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnqueueSeed(List<SeedInfo> seeds, SeedInfo seed)
    {
        int index = seeds.FindIndex(s => seed.Priority > s.Priority);
        if (index < 0)
        {
            seeds.Add(seed);
        }
        else
        {
            seeds.Insert(index, seed);
        }
    }

    private static float DistancePointToSegmentSqr(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abLenSqr = ab.sqrMagnitude;
        if (abLenSqr <= 0.000001f)
        {
            return (p - a).sqrMagnitude;
        }

        float t = Vector2.Dot(p - a, ab) / abLenSqr;
        t = Mathf.Clamp01(t);
        Vector2 closest = a + t * ab;
        return (p - closest).sqrMagnitude;
    }

    private float SampleSeedPriority(Vector2 position, Vector2 min, Vector2 max)
    {
        float priority = SamplePriority(position, min, max);
        float separation = SampleSeedSeparation(position, min, max);
        float invSep = 1f / Mathf.Max(0.0001f, separation);

        return priorityWeight * priority + separationWeight * invSep;
    }

    private float SamplePriority(Vector2 position, Vector2 min, Vector2 max)
    {
        if (priorityMap == null)
        {
            return priorityDefault;
        }

        float fallback = Mathf.InverseLerp(priorityMin, priorityMax, priorityDefault);
        float t = SampleMapValue(priorityMap, position, min, max, fallback);
        return Mathf.Lerp(priorityMin, priorityMax, t);
    }

    private float SampleSeedSeparation(Vector2 position, Vector2 min, Vector2 max)
    {
        if (separationMap == null)
        {
            return separationDefault;
        }

        float fallback = Mathf.InverseLerp(separationMin, separationMax, separationDefault);
        float t = SampleMapValue(separationMap, position, min, max, fallback);
        return Mathf.Lerp(separationMin, separationMax, t);
    }

    private static float SampleMapValue(Texture2D map, Vector2 position, Vector2 min, Vector2 max, float fallbackNormalized)
    {
        if (map == null)
        {
            return Mathf.Clamp01(fallbackNormalized);
        }

        Vector2 size = max - min;
        if (Mathf.Abs(size.x) < Mathf.Epsilon || Mathf.Abs(size.y) < Mathf.Epsilon)
        {
            return Mathf.Clamp01(fallbackNormalized);
        }

        float u = Mathf.InverseLerp(min.x, max.x, position.x);
        float v = Mathf.InverseLerp(min.y, max.y, position.y);

        try
        {
            Color c = map.GetPixelBilinear(u, v);
            return c.grayscale;
        }
        catch (UnityException)
        {
            return Mathf.Clamp01(fallbackNormalized);
        }
    }

    private static Vector2 SnapIntersectionToEndpoint(Vector2 point, Segment2D segment, float snapDistance)
    {
        float snapSqr = snapDistance * snapDistance;
        float dA = (segment.A - point).sqrMagnitude;
        float dB = (segment.B - point).sqrMagnitude;

        if (dA <= snapSqr && dA <= dB)
        {
            return segment.A;
        }

        if (dB <= snapSqr)
        {
            return segment.B;
        }

        return point;
    }

    private static bool TryGetSegmentIntersection(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2, out Vector2 intersection)
    {
        Vector2 r = p2 - p1;
        Vector2 s = q2 - q1;
        float rxs = Cross(r, s);
        float qpxr = Cross(q1 - p1, r);

        if (Mathf.Abs(rxs) < 0.000001f)
        {
            intersection = Vector2.zero;
            return false;
        }

        float t = Cross(q1 - p1, s) / rxs;
        float u = qpxr / rxs;

        if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
        {
            intersection = p1 + t * r;
            return true;
        }

        intersection = Vector2.zero;
        return false;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static bool TryClipToBounds(Vector2 start, Vector2 end, Vector2 min, Vector2 max, out Vector2 clipped)
    {
        if (!IsInsideArea(start, min, max))
        {
            clipped = start;
            return false;
        }

        Vector2 d = end - start;
        float t0 = 0f;
        float t1 = 1f;

        if (!ClipTest(-d.x, start.x - min.x, ref t0, ref t1) ||
            !ClipTest(d.x, max.x - start.x, ref t0, ref t1) ||
            !ClipTest(-d.y, start.y - min.y, ref t0, ref t1) ||
            !ClipTest(d.y, max.y - start.y, ref t0, ref t1))
        {
            clipped = start;
            return false;
        }

        clipped = start + d * t1;
        return true;
    }

    private static bool ClipTest(float p, float q, ref float t0, ref float t1)
    {
        if (Mathf.Abs(p) < 0.000001f)
        {
            return q >= 0f;
        }

        float r = q / p;
        if (p < 0f)
        {
            if (r > t1)
            {
                return false;
            }

            if (r > t0)
            {
                t0 = r;
            }
        }
        else
        {
            if (r < t0)
            {
                return false;
            }

            if (r < t1)
            {
                t1 = r;
            }
        }

        return true;
    }

    private readonly struct SeedInfo
    {
        public readonly Vector2 Position;
        public readonly bool UseMajor;
        public readonly float Priority;

        public SeedInfo(Vector2 position, bool useMajor, float priority)
        {
            Position = position;
            UseMajor = useMajor;
            Priority = priority;
        }
    }

    private struct Segment2D
    {
        public Vector2 A;
        public Vector2 B;

        public Segment2D(Vector2 a, Vector2 b)
        {
            A = a;
            B = b;
        }
    }
}
