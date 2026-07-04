using UnityEngine;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { NoiseMap, ColourMap, Mesh }
    public DrawMode drawMode;

    public int mapWidth;
    public int mapHeight;

    [Tooltip("Supersamples the heightmap: extra vertices per map cell. World size stays " +
             "identical; 2 = 4× vertex density for smoother terrain. Lower to 1 if performance drops.")]
    [Range(1, 4)]
    public int meshResolutionMultiplier = 2;

    public float noiseScale;

    public int octaves;
    [Range(0, 1)]
    public float persistance;
    public float lacunarity;

    [Header("Seed")]
    [Tooltip("When on, every GenerateMap() picks a fresh random seed (new island each time). " +
             "Turn off to reproduce a specific island via the Seed field below.")]
    public bool useRandomSeed = true;
    [Tooltip("Fixed seed — only used when Use Random Seed is off.")]
    public int seed;
    [Tooltip("Seed actually used by the last generation (copy this to reproduce the island).")]
    public int lastGeneratedSeed;
    public Vector2 offset;

    public float meshHeightMultiplier;
    public AnimationCurve meshHeightCurve;

    [Range(0, 1)]
    public float waterLevel = 0.4f;

    public bool autoUpdate;

    [Tooltip("Generate a fresh map automatically when entering Play mode.")]
    public bool generateOnPlay = true;

    // ---- Feature #6: Map Concept (modular — future concepts plug in here) ----
    [Header("Map Concept")]
    public MapConcept mapConcept = MapConcept.Island;
    public IslandFalloffSettings islandFalloff = new IslandFalloffSettings();

    [Tooltip("Optional. When assigned, noise/height/shape parameters are picked from this " +
             "archetype's ranges per seed (the fields below then show the ACTIVE VARIANT " +
             "and are overwritten on every generation). Leave empty to use fixed values.")]
    public IslandArchetype islandArchetype;
    [Tooltip("Parameter combination used by the last generation (read-only info).")]
    public string lastVariantInfo = "";

    // ---- Mountain rounding: altitude-weighted low-pass filter ----
    // The higher a cell sits, the harder it is blurred toward its neighbours, so
    // peaks erode into rounded hilltops while lowlands keep their fine detail.
    // This is what turns "jagged glacier ridge" into "weathered rolling hills".
    [Header("Mountain Rounding (rolling hills, not spikes)")]
    public bool roundMountains = true;
    [Tooltip("Normalized height where rounding starts ramping in (full power near 1.0).")]
    [Range(0.3f, 0.9f)] public float roundingStartHeight = 0.55f;
    [Range(0f, 1f)] public float mountainRoundingStrength = 0.85f;
    [Range(1, 6)] public int mountainRoundingIterations = 3;

    // ---- Single landmass guarantee ----
    [Header("Single Landmass")]
    [Tooltip("After generation, flood-fill the land cells: the biggest piece is the island, " +
             "larger fragments get a sandbar bridge to it, tiny islets are sunk. " +
             "Guarantees ONE connected island for every seed/variant.")]
    public bool ensureSingleLandmass = true;
    [Tooltip("Result of the last landmass check (read-only info).")]
    public string lastLandmassReport = "";

    // ---- Feature #6 follow-up: walkability (run/walk gameplay needs gentle ground) ----
    [Header("Walkability — Smooth Steep Slopes")]
    [Tooltip("Post-process pass that relaxes overly steep cells toward their neighbours' " +
             "average. Keeps mountains but widens walkable ground.")]
    public bool smoothSteepSlopes = true;
    [Tooltip("0 = no effect, 1 = steep cells snap fully to neighbour average.")]
    [Range(0f, 1f)] public float smoothStrength = 0.6f;
    [Range(1, 6)]   public int   smoothIterations = 3;
    [Tooltip("Height difference (normalized units) to the neighbour average above which " +
             "a cell counts as too steep and gets smoothed.")]
    [Range(0.005f, 0.1f)] public float smoothSlopeThreshold = 0.025f;

    public TerrainType[] regions;

    // ---- Feature #6: Procedural World System — Modular Biome Props ----
    [Header("Feature #6 — Biome Props (modular, reusable per biome)")]
    public BiomeProps[] biomeProps;

    [HideInInspector]
    public float[,] latestNoiseMap;

    bool hasGeneratedOnce;

    // ------------------------------------------------------------------ //

    void Start()
    {
        if (generateOnPlay)
            GenerateMap();
    }

    /// <summary>
    /// Shared heightmap pipeline: resolve seed → raw noise → map-concept post-process.
    /// Both GenerateMap() and the World Automation panel go through here.
    /// Pass reuseLastSeed=true to re-render the same island (used by autoUpdate
    /// so tweaking Inspector values doesn't reshuffle the whole map).
    /// </summary>
    public float[,] GenerateHeightMap(bool reuseLastSeed = false)
    {
        int activeSeed;
        if (reuseLastSeed && hasGeneratedOnce)
            activeSeed = lastGeneratedSeed;
        else if (useRandomSeed)
            activeSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        else
            activeSeed = seed;

        lastGeneratedSeed = activeSeed;
        hasGeneratedOnce  = true;

        // Archetype: pick this seed's parameter combination from the ranges.
        // Deterministic — regenerating with the same seed rebuilds the same variant.
        if (islandArchetype != null && mapConcept == MapConcept.Island)
            lastVariantInfo = islandArchetype.ApplyVariant(this, activeSeed);

        // Supersampled generation: more samples per map cell, same world footprint.
        // noiseScale is multiplied so world-space feature size is resolution-independent.
        int mult = Mathf.Max(1, meshResolutionMultiplier);
        int genW = (mapWidth  - 1) * mult + 1;
        int genH = (mapHeight - 1) * mult + 1;

        float[,] noiseMap = Noise.GenerateNoiseMap(
            genW, genH, activeSeed, noiseScale * mult,
            octaves, persistance, lacunarity, offset);

        ApplyMapConcept(noiseMap, activeSeed);

        if (roundMountains)
            RoundMountains(noiseMap);

        if (smoothSteepSlopes)
            SmoothSteepSlopes(noiseMap);

        // Last terrain step (before zones/spawns downstream): never ship an archipelago.
        if (ensureSingleLandmass && mapConcept == MapConcept.Island)
        {
            lastLandmassReport = LandmassUtility.EnsureSingleLandmass(
                noiseMap, waterLevel, 2 * mult);
            Debug.Log($"[MapGenerator] {lastLandmassReport}");
        }

        latestNoiseMap = noiseMap;

        Debug.Log($"[MapGenerator] Generated {mapConcept} with seed: {activeSeed}");
        return noiseMap;
    }

    // Concept post-process on the normalized noise map. New concepts = new case.
    void ApplyMapConcept(float[,] noiseMap, int activeSeed)
    {
        int w = noiseMap.GetLength(0);
        int h = noiseMap.GetLength(1);
        switch (mapConcept)
        {
            case MapConcept.Island:
                float[,] falloff = FalloffGenerator.GenerateFalloffMap(
                    w, h, islandFalloff, activeSeed);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        noiseMap[x, y] *= falloff[x, y];
                break;

            case MapConcept.Continent:
                break; // raw noise, no mask
        }
    }

    // Altitude-weighted blur: weight ramps from 0 at roundingStartHeight to full near
    // the peaks, so summits become domes while plains keep their detail. Uses the
    // 8-neighbour average as the low-pass target.
    void RoundMountains(float[,] map)
    {
        int w = map.GetLength(0);
        int h = map.GetLength(1);
        int mult  = Mathf.Max(1, meshResolutionMultiplier);
        int iters = mountainRoundingIterations * mult;
        float[,] src = map;

        for (int it = 0; it < iters; it++)
        {
            float[,] dst = (float[,])src.Clone();
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float v = src[x, y];
                    float t = Mathf.InverseLerp(roundingStartHeight, 0.95f, v);
                    if (t <= 0f) continue;

                    float avg = (src[x - 1, y] + src[x + 1, y] + src[x, y - 1] + src[x, y + 1] +
                                 src[x - 1, y - 1] + src[x + 1, y - 1] +
                                 src[x - 1, y + 1] + src[x + 1, y + 1]) * 0.125f;
                    dst[x, y] = Mathf.Lerp(v, avg, t * mountainRoundingStrength);
                }
            }
            src = dst;
        }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                map[x, y] = src[x, y];
    }

    // Selective relaxation: only cells whose height differs from their 4-neighbour
    // average by more than the threshold are pulled toward it, so plains widen and
    // jagged spikes soften while broad mountain forms survive. Water cells are
    // skipped so the island falloff / edge-water guarantee is never raised.
    void SmoothSteepSlopes(float[,] map)
    {
        int w = map.GetLength(0);
        int h = map.GetLength(1);
        float[,] src = map;

        // Cell spacing shrinks with the resolution multiplier, so the per-cell gradient
        // threshold shrinks and iterations grow to cover the same world-space radius.
        int   mult      = Mathf.Max(1, meshResolutionMultiplier);
        float threshold = smoothSlopeThreshold / mult;
        int   iters     = smoothIterations * mult;

        for (int it = 0; it < iters; it++)
        {
            float[,] dst = (float[,])src.Clone();
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float v = src[x, y];
                    if (v < waterLevel) continue;   // never touch water / falloff band

                    float avg = (src[x - 1, y] + src[x + 1, y] +
                                 src[x, y - 1] + src[x, y + 1]) * 0.25f;
                    if (Mathf.Abs(v - avg) > threshold)
                        dst[x, y] = Mathf.Lerp(v, avg, smoothStrength);
                }
            }
            src = dst;
        }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                map[x, y] = src[x, y];
    }

    /// <param name="previewOnly">True = terrain mesh + colours only (used by Auto Update
    /// slider previews). Zones, props, NavMesh and character placement are skipped —
    /// run the full generate (button/panel) to refresh those.</param>
    public void GenerateMap(bool reuseLastSeed = false, bool previewOnly = false)
    {
        float[,] noiseMap = GenerateHeightMap(reuseLastSeed);

        Color[] colourMap = BuildColourMap(noiseMap);
        int texW = noiseMap.GetLength(0);
        int texH = noiseMap.GetLength(1);
        float vertexSpacing = 1f / Mathf.Max(1, meshResolutionMultiplier);

        MapDisplay display = FindObjectOfType<MapDisplay>();
        if (display == null) { Debug.LogError("MapGenerator: No MapDisplay found in scene."); return; }

        if (drawMode == DrawMode.NoiseMap)
            display.DrawTexture(TextureGenerator.TextureFromHeightMap(noiseMap));
        else if (drawMode == DrawMode.ColourMap)
            display.DrawTexture(TextureGenerator.TextureFromColourMap(colourMap, texW, texH));
        else if (drawMode == DrawMode.Mesh)
            display.DrawMesh(
                MeshGenerator.GenerateTerrainMesh(noiseMap, meshHeightMultiplier, meshHeightCurve, waterLevel, vertexSpacing),
                TextureGenerator.TextureFromColourMap(colourMap, texW, texH));

        if (previewOnly) return;   // Auto Update slider preview: terrain only

        // ---- Feature #6 follow-up pipeline ----
        // Zones first (spawners thin their density inside them), then a clean
        // re-scatter of props for the new island. Grass is now rule-based only
        // (no procedural placeholder blades). AI nodes stay disabled.
        if (drawMode == DrawMode.Mesh)
        {
            FindObjectOfType<SettlementZoneFinder>()?.FindZones();
            FindObjectOfType<RuleBasedSpawner>()?.SpawnAll();   // clears old props itself
        }
        // FindObjectOfType<AISpawnManager>()?.PlaceSpawnPoints();

        display.BakeNavMesh();

        // Characters last: the enemy needs the freshly baked NavMesh to snap onto.
        if (drawMode == DrawMode.Mesh)
            FindObjectOfType<WorldCharacterSpawner>()?.PlaceCharacters();
    }

    // ------------------------------------------------------------------ //

    public Color[] BuildColourMap(float[,] noiseMap)
    {
        int w = noiseMap.GetLength(0);
        int h = noiseMap.GetLength(1);
        Color[] colourMap = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float currentHeight = noiseMap[x, y];
                for (int i = 0; i < regions.Length; i++)
                {
                    if (currentHeight <= regions[i].height)
                    {
                        Color baseColor = regions[i].colour;

                        if (i + 1 < regions.Length)
                        {
                            // Narrow SmoothStep band right at the boundary: regions stay
                            // clearly separated, only the pixel staircase is softened.
                            float lowerBound = (i > 0) ? regions[i - 1].height : 0f;
                            float upperBound = regions[i].height;
                            float blendZone  = Mathf.Min(0.025f, (upperBound - lowerBound) * 0.5f);

                            if (blendZone > 0f && currentHeight > upperBound - blendZone)
                            {
                                float t = Mathf.SmoothStep(0f, 1f,
                                    Mathf.InverseLerp(upperBound - blendZone, upperBound, currentHeight));
                                baseColor = Color.Lerp(baseColor, regions[i + 1].colour, t * 0.5f);
                            }
                        }

                        colourMap[y * w + x] = baseColor;
                        break;
                    }
                }
            }
        }
        return colourMap;
    }

    private void OnValidate()
    {
        if (mapWidth  < 1) mapWidth  = 1;
        if (mapHeight < 1) mapHeight = 1;
        if (lacunarity < 1) lacunarity = 1;
        if (octaves < 0) octaves = 0;
    }
}

// ---- Terrain region (colour band) ----
[System.Serializable]
public struct TerrainType
{
    public string name;
    public float  height;
    public Color  colour;
}

// ---- Feature #6: per-biome prop configuration ----
[System.Serializable]
public struct BiomeProps
{
    [Tooltip("Display name for this biome slot (e.g. 'Grassland', 'Rocky Highland').")]
    public string biomeName;

    [Tooltip("Case-insensitive keyword matched against TerrainType region names " +
             "(e.g. 'grass' matches any region named 'Grass', 'Grassland', 'çimen', etc.).")]
    public string regionNameKeyword;

    [Header("Trees")]
    public GameObject[] treePrefabs;
    [Range(0f, 1f)] public float treeSpawnChance;
    public float treeScaleMin;
    public float treeScaleMax;

    [Header("Rocks")]
    public GameObject[] rockPrefabs;
    [Range(0f, 1f)] public float rockSpawnChance;
    public float rockScaleMin;
    public float rockScaleMax;
}
