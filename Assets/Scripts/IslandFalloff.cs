using UnityEngine;

// ---- Feature #6: Procedural World System — Map Concepts ----
// A map concept is a post-process applied on top of the raw Noise heightmap.
// Island is the first concept; future concepts (Continent, Archipelago, Desert…)
// plug into MapGenerator.ApplyMapConcept without touching Noise.cs.

public enum MapConcept
{
    Island,     // edges forced below water level via falloff mask
    Continent   // raw noise, no falloff (plain Sebastian-Lague-style terrain)
}

public enum FalloffShape
{
    Circular,   // round island (distance = sqrt(nx² + ny²))
    Square      // squarish island (distance = max(|nx|, |ny|))
}

[System.Serializable]
public class IslandFalloffSettings
{
    [Tooltip("Circular gives a round island, Square fills more of the map.")]
    public FalloffShape shape = FalloffShape.Circular;

    [Tooltip("Radius (0..1, normalized) around the center where the falloff has no effect. " +
             "Bigger value = bigger guaranteed island core.")]
    [Range(0f, 0.9f)]
    public float islandSizePercent = 0.45f;

    [Tooltip("Input: normalized distance from island core (0) to map edge (1). " +
             "Output: height multiplier (1 = keep noise, 0 = force to sea floor).")]
    public AnimationCurve falloffCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Coastline Roughness")]
    [Tooltip("Perturbs the coastline with extra noise so it isn't a perfect circle/square. " +
             "0 = smooth geometric coast.")]
    [Range(0f, 0.5f)]
    public float coastlineNoiseStrength = 0.15f;

    [Tooltip("Frequency of the coastline noise. Higher = more, smaller bays and capes.")]
    public float coastlineNoiseScale = 4f;

    [Header("Edge Water Guarantee")]
    [Tooltip("Outermost band of the map (normalized width) that is forced to water " +
             "no matter what the curve or coastline noise does.")]
    [Range(0.01f, 0.3f)]
    public float edgeWaterMargin = 0.08f;
}

public static class FalloffGenerator
{
    /// <summary>
    /// Builds a [width, height] multiplier map in 0..1:
    /// 1 inside the island core, easing to 0 at the map edges.
    /// Multiply the normalized noise map by this to get an island.
    /// The outermost edgeWaterMargin band is hard-clamped to 0 so the
    /// four map borders are below water level for every seed.
    /// </summary>
    public static float[,] GenerateFalloffMap(int width, int height, IslandFalloffSettings settings, int seed)
    {
        float[,] map = new float[width, height];

        AnimationCurve curve = (settings.falloffCurve != null && settings.falloffCurve.length > 0)
            ? new AnimationCurve(settings.falloffCurve.keys)
            : AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        // Seed-driven offsets so the coastline shape changes with every island.
        System.Random prng = new System.Random(seed);
        float noiseOffsetX = (float)(prng.NextDouble() * 1000.0);
        float noiseOffsetY = (float)(prng.NextDouble() * 1000.0);

        float invW = 1f / Mathf.Max(1, width - 1);
        float invH = 1f / Mathf.Max(1, height - 1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = x * invW * 2f - 1f;   // -1..1
                float ny = y * invH * 2f - 1f;

                float dist = settings.shape == FalloffShape.Square
                    ? Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny))
                    : Mathf.Sqrt(nx * nx + ny * ny);   // can exceed 1 in corners
                dist = Mathf.Clamp01(dist);

                // Remap so the island core (dist < islandSizePercent) stays untouched,
                // then 0..1 from core edge to map edge.
                float t = Mathf.InverseLerp(settings.islandSizePercent, 1f, dist);

                // Coastline roughness: perturb t with seed-offset Perlin noise.
                // Weight 4t(1-t) peaks mid-coast and vanishes at the core and the
                // map edge, so neither the plateau nor the water guarantee is affected.
                if (settings.coastlineNoiseStrength > 0f)
                {
                    float n = Mathf.PerlinNoise(
                        (nx + 1f) * 0.5f * settings.coastlineNoiseScale + noiseOffsetX,
                        (ny + 1f) * 0.5f * settings.coastlineNoiseScale + noiseOffsetY);
                    float weight = 4f * t * (1f - t);
                    t = Mathf.Clamp01(t + (n - 0.5f) * 2f * settings.coastlineNoiseStrength * weight);
                }

                float multiplier = Mathf.Clamp01(curve.Evaluate(t));

                // Hard guarantee: force the outermost border band to 0 using the raw
                // per-axis distance (all four borders), independent of curve/noise.
                float border = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
                float edgeT  = Mathf.InverseLerp(1f - settings.edgeWaterMargin, 1f, border);
                multiplier  *= 1f - edgeT;

                map[x, y] = multiplier;
            }
        }

        return map;
    }
}
