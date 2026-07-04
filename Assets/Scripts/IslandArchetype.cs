using UnityEngine;

/// <summary>
/// WILDCUT — Island archetype: a FAMILY of islands, not a single shape.
/// Every generation parameter is a min-max range instead of one fixed value;
/// each regenerate picks a combination from these ranges, deterministically
/// derived from the island seed (same seed → same variant). The result keeps
/// the family's character (ridged island with channels/bays) while varying
/// peak count, layout and coastline every time.
/// Create via: Assets ▸ Create ▸ WILDCUT ▸ Island Archetype.
/// </summary>
[CreateAssetMenu(menuName = "WILDCUT/Island Archetype", fileName = "NewIslandArchetype")]
public class IslandArchetype : ScriptableObject
{
    [Header("Noise character")]
    public Vector2 noiseScaleRange = new Vector2(30f, 42f);
    [Tooltip("Whole numbers picked inclusively. Kept low — high octave counts put " +
             "jagged high-frequency detail on the peaks.")]
    public Vector2Int octavesRange = new Vector2Int(4, 5);
    public Vector2 persistanceRange = new Vector2(0.38f, 0.46f);
    public Vector2 lacunarityRange = new Vector2(1.9f, 2.2f);

    [Header("Mountains (kept low & rounded per design feedback)")]
    public Vector2 heightMultiplierRange = new Vector2(25f, 32f);

    [Header("Island shape")]
    [Tooltip("Flat core radius of the falloff — bigger = more guaranteed walkable interior.")]
    public Vector2 islandSizeRange = new Vector2(0.55f, 0.70f);
    public Vector2 coastlineNoiseRange = new Vector2(0.10f, 0.22f);

    /// <summary>
    /// Writes a seed-deterministic parameter combination into the generator's
    /// fields (so the Inspector always shows the active variant's values).
    /// Returns a short description for the automation panel.
    /// </summary>
    public string ApplyVariant(MapGenerator gen, int seed)
    {
        var rng = new System.Random(seed);
        float Next(Vector2 range) => Mathf.Lerp(range.x, range.y, (float)rng.NextDouble());

        gen.noiseScale           = Next(noiseScaleRange);
        gen.octaves              = rng.Next(octavesRange.x, octavesRange.y + 1);
        gen.persistance          = Next(persistanceRange);
        gen.lacunarity           = Next(lacunarityRange);
        gen.meshHeightMultiplier = Next(heightMultiplierRange);
        gen.islandFalloff.islandSizePercent      = Next(islandSizeRange);
        gen.islandFalloff.coastlineNoiseStrength = Next(coastlineNoiseRange);

        return $"scale {gen.noiseScale:F0} | oct {gen.octaves} | pers {gen.persistance:F2} | " +
               $"lac {gen.lacunarity:F2} | height {gen.meshHeightMultiplier:F0} | " +
               $"core {gen.islandFalloff.islandSizePercent:F2} | coast {gen.islandFalloff.coastlineNoiseStrength:F2}";
    }

    void OnValidate()
    {
        if (octavesRange.x < 1) octavesRange.x = 1;
        if (octavesRange.y < octavesRange.x) octavesRange.y = octavesRange.x;
    }
}
