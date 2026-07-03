using UnityEngine;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { NoiseMap, ColourMap, Mesh }
    public DrawMode drawMode;

    public int mapWidth;
    public int mapHeight;
    public float noiseScale;

    public int octaves;
    [Range(0, 1)]
    public float persistance;
    public float lacunarity;

    public int seed;
    public Vector2 offset;

    public float meshHeightMultiplier;
    public AnimationCurve meshHeightCurve;

    [Range(0, 1)]
    public float waterLevel = 0.4f;

    public bool autoUpdate;

    public TerrainType[] regions;

    // ---- Feature #6: Procedural World System — Modular Biome Props ----
    [Header("Feature #6 — Biome Props (modular, reusable per biome)")]
    public BiomeProps[] biomeProps;

    [HideInInspector]
    public float[,] latestNoiseMap;

    // ------------------------------------------------------------------ //

    public void GenerateMap()
    {
        float[,] noiseMap = Noise.GenerateNoiseMap(
            mapWidth, mapHeight, seed, noiseScale,
            octaves, persistance, lacunarity, offset);
        latestNoiseMap = noiseMap;

        Color[] colourMap = BuildColourMap(noiseMap);

        MapDisplay display = FindObjectOfType<MapDisplay>();
        if (display == null) { Debug.LogError("MapGenerator: No MapDisplay found in scene."); return; }

        if (drawMode == DrawMode.NoiseMap)
            display.DrawTexture(TextureGenerator.TextureFromHeightMap(noiseMap));
        else if (drawMode == DrawMode.ColourMap)
            display.DrawTexture(TextureGenerator.TextureFromColourMap(colourMap, mapWidth, mapHeight));
        else if (drawMode == DrawMode.Mesh)
            display.DrawMesh(
                MeshGenerator.GenerateTerrainMesh(noiseMap, meshHeightMultiplier, meshHeightCurve, waterLevel),
                TextureGenerator.TextureFromColourMap(colourMap, mapWidth, mapHeight));

        // Grass, Props, and AI nodes are disabled — NavMesh focus only.
        // FindObjectOfType<GrassSpawner>()?.SpawnGrass();
        // FindObjectOfType<PropSpawner>()?.SpawnProps();
        // FindObjectOfType<AISpawnManager>()?.PlaceSpawnPoints();

        display.BakeNavMesh();
    }

    // ------------------------------------------------------------------ //

    public Color[] BuildColourMap(float[,] noiseMap)
    {
        Color[] colourMap = new Color[mapWidth * mapHeight];
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                float currentHeight = noiseMap[x, y];
                for (int i = 0; i < regions.Length; i++)
                {
                    if (currentHeight <= regions[i].height)
                    {
                        Color baseColor = regions[i].colour;

                        if (i + 1 < regions.Length)
                        {
                            float lowerBound = (i > 0) ? regions[i - 1].height : 0f;
                            float upperBound = regions[i].height;
                            float blendZone  = (upperBound - lowerBound) * 0.40f;

                            if (blendZone > 0f && currentHeight > upperBound - blendZone)
                            {
                                float t = Mathf.SmoothStep(0f, 1f,
                                    Mathf.InverseLerp(upperBound - blendZone, upperBound, currentHeight));
                                baseColor = Color.Lerp(baseColor, regions[i + 1].colour, t);
                            }
                        }

                        colourMap[y * mapWidth + x] = baseColor;
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
