using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Feature #6 — Autonomous Procedural Prop Spawner.
/// Generates trees and rocks entirely from Unity primitives — no prefabs required.
/// Placement uses actual mesh vertex positions + surface normals for slope filtering
/// and Perlin-noise cluster modulation for natural-looking biome distribution.
/// </summary>
[ExecuteInEditMode]
public class PropSpawner : MonoBehaviour
{
    [Header("References")]
    public MapGenerator mapGenerator;
    [Tooltip("Same MeshFilter assigned on GrassSpawner — the terrain mesh object.")]
    public MeshFilter   terrainMeshFilter;

    [Header("Tree Settings")]
    [Range(0f, 1f)]
    [Tooltip("Base probability that any single grass vertex spawns a tree. " +
             "Actual chance is multiplied by cluster noise, so results are patchy.")]
    public float treeBaseChance      = 0.08f;
    public float treeTrunkHeightMin  = 1.5f;
    public float treeTrunkHeightMax  = 3.5f;
    [Tooltip("Minimum world-unit gap between tree bases. Prevents mesh overlap.")]
    public float treeMinSpacing      = 2.5f;

    [Header("Rock Settings")]
    [Range(0f, 1f)]
    public float rockBaseChance = 0.04f;
    public float rockSizeMin    = 0.30f;
    public float rockSizeMax    = 1.20f;

    [Header("Slope Filter  (mesh surface normals)")]
    [Tooltip("Minimum Y component of the world-space surface normal.\n" +
             "1.0 = perfectly flat  |  0.85 ≈ 32°  |  0.70 ≈ 45°.\n" +
             "Trees need flatter ground than rocks.")]
    [Range(0.5f, 1f)]
    public float minNormalY_Trees = 0.85f;
    [Range(0.3f, 1f)]
    public float minNormalY_Rocks = 0.65f;

    [Header("Cluster Noise")]
    [Tooltip("Frequency of the Perlin layer that controls forest-patch density. " +
             "Lower = larger patches.")]
    [Range(0.01f, 0.5f)]
    public float clusterFrequency = 0.12f;
    [Range(0f, 1f)]
    [Tooltip("Perlin values below this produce sparse / no tree coverage.")]
    public float clusterThreshold  = 0.35f;

    const string ROOT_NAME = "Procedural_Props";

    // ------------------------------------------------------------------ //
    //  Entry point
    // ------------------------------------------------------------------ //

    public void SpawnProps()
    {
        GameObject old = GameObject.Find(ROOT_NAME);
        if (old != null) SafeDestroy(old);

        if (!Validate()) return;

        float[,]      noiseMap  = mapGenerator.latestNoiseMap;
        TerrainType[] regions   = mapGenerator.regions;
        int           mapW      = noiseMap.GetLength(0);
        int           mapH      = noiseMap.GetLength(1);

        Mesh mesh = terrainMeshFilter.sharedMesh;
        if (mesh.normals == null || mesh.normals.Length != mapW * mapH)
        {
            // Force recalculate if normals are missing
            mesh.RecalculateNormals();
        }

        Vector3[] verts     = mesh.vertices;
        Vector3[] normals   = mesh.normals;
        Transform meshTF    = terrainMeshFilter.transform;
        // Terrain world scale — multiply all prop sizes so they match the visible terrain.
        // With Mesh.localScale = (10,10,10): a 1.5-unit trunk becomes 15 world units, etc.
        float     propScale = meshTF.lossyScale.y;

        if (verts.Length != mapW * mapH)
        {
            Debug.LogError("[PropSpawner] Vertex count mismatch — regenerate the map first.");
            return;
        }

        // ---- Locate grass height band ----
        float grassMinH = 0f, grassMaxH = 1f;
        bool  found     = false;
        for (int i = 0; i < regions.Length; i++)
        {
            string n = regions[i].name.ToLower();
            if (n.Contains("grass") || n.Contains("çimen") || n.Contains("cimen") ||
                n.Contains("yeşil") || n.Contains("yesil"))
            {
                grassMinH = (i > 0) ? regions[i - 1].height : 0f;
                grassMaxH = regions[i].height;
                found     = true;
                break;
            }
        }
        if (!found && regions.Length >= 3)
        {
            int mid   = regions.Length / 2;
            grassMinH = (mid > 0) ? regions[mid - 1].height : 0f;
            grassMaxH = regions[mid].height;
            Debug.LogWarning("[PropSpawner] No 'grass' region found — using middle region as fallback.");
        }

        // ---- Build scene hierarchy ----
        GameObject root      = new GameObject(ROOT_NAME);
        GameObject treeRoot  = new GameObject("Trees");
        treeRoot.transform.parent  = root.transform;
        GameObject rockRoot  = new GameObject("Rocks");
        rockRoot.transform.parent  = root.transform;

        int seed          = mapGenerator.seed;
        int treeCount     = 0;
        int rockCount     = 0;
        var treePositions = new List<Vector3>(256); // spacing lookup

        for (int y = 1; y < mapH - 1; y++)
        {
            for (int x = 1; x < mapW - 1; x++)
            {
                float h = noiseMap[x, y];
                if (h < grassMinH || h > grassMaxH) continue;

                int     vi         = y * mapW + x;
                Vector3 worldPos   = meshTF.TransformPoint(verts[vi]);
                // World-space surface normal — Y component encodes surface flatness
                Vector3 worldNorm  = meshTF.TransformDirection(normals[vi]).normalized;

                // ---- Trees ----
                if (worldNorm.y >= minNormalY_Trees)
                {
                    float cluster = Mathf.PerlinNoise(
                        (x + seed * 7.31f) * clusterFrequency,
                        (y + seed * 3.17f) * clusterFrequency);

                    if (cluster >= clusterThreshold &&
                        Random.value < treeBaseChance * cluster)
                    {
                        if (!TooCloseToExisting(worldPos, treePositions, treeMinSpacing * propScale))
                        {
                            CreateProceduralTree(worldPos, treeRoot.transform, propScale);
                            treePositions.Add(worldPos);
                            treeCount++;
                        }
                    }
                }

                // ---- Rocks ----
                if (worldNorm.y >= minNormalY_Rocks &&
                    Random.value < rockBaseChance)
                {
                    CreateProceduralRock(worldPos, rockRoot.transform, propScale);
                    rockCount++;
                }
            }
        }

        Debug.Log($"[PropSpawner] Spawned {treeCount} trees + {rockCount} rocks " +
                  $"under '{ROOT_NAME}'.");
    }

    // ------------------------------------------------------------------ //
    //  Procedural Tree
    //  Structure: root → Cylinder (trunk) + N × Sphere (foliage layers)
    //  Collider:  single BoxCollider on root
    // ------------------------------------------------------------------ //

    void CreateProceduralTree(Vector3 pos, Transform parent, float ts)
    {
        GameObject root = new GameObject("Tree");
        root.transform.parent   = parent;
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        float scale  = Random.Range(0.75f, 1.40f);
        float trunkH = Random.Range(treeTrunkHeightMin, treeTrunkHeightMax) * scale * ts;
        float trunkR = Random.Range(0.10f, 0.20f) * scale * ts;

        // Trunk
        Material trunkMat = MakeMaterial(new Color(
            Random.Range(0.27f, 0.42f),
            Random.Range(0.14f, 0.23f),
            Random.Range(0.05f, 0.11f)));

        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Trunk";
        trunk.transform.parent        = root.transform;
        trunk.transform.localPosition = new Vector3(0f, trunkH * 0.5f, 0f);
        trunk.transform.localScale    = new Vector3(trunkR * 2f, trunkH * 0.5f, trunkR * 2f);
        trunk.GetComponent<Renderer>().sharedMaterial = trunkMat;
        SafeDestroyComp(trunk.GetComponent<Collider>());

        // Foliage — 2-3 spheres, stacked, tapering upward
        int   layers    = Random.Range(2, 4);
        float foliageSz = Random.Range(0.90f, 1.50f) * scale * ts;
        float baseY     = trunkH * 0.75f;

        for (int i = 0; i < layers; i++)
        {
            float t    = layers > 1 ? (float)i / (layers - 1) : 0f;
            float size = foliageSz * Mathf.Lerp(1.0f, 0.48f, t);
            float yOff = baseY + i * foliageSz * 0.52f;

            Material leafMat = MakeMaterial(new Color(
                Random.Range(0.04f, 0.17f),
                Random.Range(0.36f, 0.60f),
                Random.Range(0.04f, 0.13f)));

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"Foliage_{i}";
            sphere.transform.parent        = root.transform;
            sphere.transform.localPosition = new Vector3(
                Random.Range(-0.08f, 0.08f) * scale * ts,
                yOff,
                Random.Range(-0.08f, 0.08f) * scale * ts);
            sphere.transform.localScale = new Vector3(
                size * Random.Range(0.88f, 1.14f),
                size * Random.Range(0.80f, 1.04f),
                size * Random.Range(0.88f, 1.14f));
            sphere.GetComponent<Renderer>().sharedMaterial = leafMat;
            SafeDestroyComp(sphere.GetComponent<Collider>());
        }

        // Collider on root (approx. bounding shape for NavMesh obstacles)
        BoxCollider col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, trunkH * 0.55f, 0f);
        col.size   = new Vector3(foliageSz * 0.85f, trunkH, foliageSz * 0.85f);
    }

    // ------------------------------------------------------------------ //
    //  Procedural Rock
    //  Structure: root → 2-3 non-uniform Cubes/Spheres, stacked jaggedly
    //  Collider:  single BoxCollider on root
    // ------------------------------------------------------------------ //

    void CreateProceduralRock(Vector3 pos, Transform parent, float ts)
    {
        GameObject root = new GameObject("Rock");
        root.transform.parent   = parent;
        root.transform.position = new Vector3(
            pos.x + Random.Range(-0.25f, 0.25f) * ts,
            pos.y,
            pos.z + Random.Range(-0.25f, 0.25f) * ts);
        root.transform.rotation = Quaternion.Euler(
            Random.Range(-15f, 15f),
            Random.Range(0f, 360f),
            Random.Range(-15f, 15f));

        int   pieces  = Random.Range(2, 4);
        float maxSize = 0f;

        for (int i = 0; i < pieces; i++)
        {
            bool useCube = Random.value > 0.35f;
            GameObject piece = GameObject.CreatePrimitive(
                useCube ? PrimitiveType.Cube : PrimitiveType.Sphere);
            piece.name = $"Piece_{i}";
            piece.transform.parent = root.transform;

            float bs = Random.Range(rockSizeMin, rockSizeMax) * ts;
            if (bs > maxSize) maxSize = bs;

            piece.transform.localScale = new Vector3(
                bs * Random.Range(0.50f, 1.60f),
                bs * Random.Range(0.38f, 0.90f),
                bs * Random.Range(0.55f, 1.50f));
            piece.transform.localPosition = new Vector3(
                Random.Range(-0.22f, 0.22f) * bs,
                i * bs * 0.20f,
                Random.Range(-0.22f, 0.22f) * bs);
            piece.transform.localRotation = Quaternion.Euler(
                Random.Range(0f, 30f),
                Random.Range(0f, 360f),
                Random.Range(0f, 30f));

            Color grey = new Color(
                Random.Range(0.36f, 0.57f),
                Random.Range(0.34f, 0.52f),
                Random.Range(0.30f, 0.47f));
            piece.GetComponent<Renderer>().sharedMaterial = MakeMaterial(grey);
            SafeDestroyComp(piece.GetComponent<Collider>());
        }

        BoxCollider col = root.AddComponent<BoxCollider>();
        col.size   = Vector3.one * maxSize * 1.15f;
        col.center = new Vector3(0f, col.size.y * 0.40f, 0f);
    }

    // ------------------------------------------------------------------ //
    //  Helpers
    // ------------------------------------------------------------------ //

    static bool TooCloseToExisting(Vector3 pos, List<Vector3> placed, float minDist)
    {
        float minSq = minDist * minDist;
        foreach (Vector3 p in placed)
            if ((pos - p).sqrMagnitude < minSq) return true;
        return false;
    }

    static Material MakeMaterial(Color color)
    {
        Shader sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Diffuse");
        var mat = new Material(sh);
        mat.color = color;
        return mat;
    }

    static void SafeDestroy(GameObject go)
    {
        if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
    }

    static void SafeDestroyComp(Component c)
    {
        if (c == null) return;
        if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
    }

    bool Validate()
    {
        if (mapGenerator == null || mapGenerator.latestNoiseMap == null)
        { Debug.LogWarning("[PropSpawner] Run MapGenerator.GenerateMap() first."); return false; }

        if (terrainMeshFilter == null || terrainMeshFilter.sharedMesh == null)
        { Debug.LogWarning("[PropSpawner] Assign the terrain MeshFilter reference."); return false; }

        return true;
    }
}
