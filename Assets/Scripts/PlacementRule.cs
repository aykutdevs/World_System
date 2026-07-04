using UnityEngine;

// ---- Feature #6 follow-up: Rule-based asset placement ----
// One asset (or family of variants) = one PlacementRule asset.
// Adding a new prop to the world requires NO code:
//   Assets ▸ Create ▸ WILDCUT ▸ Placement Rule → drag prefab(s) → set region/slope/density.
// RuleBasedSpawner reads every rule in its list and scatters accordingly.

[CreateAssetMenu(menuName = "WILDCUT/Placement Rule", fileName = "NewPlacementRule")]
public class PlacementRule : ScriptableObject
{
    [Header("What to place")]
    [Tooltip("One is picked at random per spawn. A rule with NO prefabs assigned is skipped " +
             "entirely — nothing spawns until you drag a prefab here.")]
    public GameObject[] prefabs;

    [Header("Where it may appear")]
    [Tooltip("Case-insensitive keywords matched against TerrainType region names " +
             "(e.g. 'grass' matches 'grass', 'Grassland'; 'rock' matches 'Rock', 'Rock 2'). " +
             "Empty list = any region.")]
    public string[] allowedRegionKeywords = { "grass" };

    [Tooltip("Extra clamp on normalized map height (0..1). Raise the minimum to keep an asset " +
             "away from the beach even when its region band touches the sand.")]
    [Range(0f, 1f)] public float minHeight = 0f;
    [Range(0f, 1f)] public float maxHeight = 1f;

    [Tooltip("Allowed surface slope in degrees (0 = flat).")]
    [Range(0f, 90f)] public float minSlopeAngle = 0f;
    [Range(0f, 90f)] public float maxSlopeAngle = 30f;

    [Header("How densely")]
    [Tooltip("Chance per terrain vertex cell (0..1) before cluster/zone modifiers.")]
    [Range(0f, 1f)] public float density = 0.05f;

    [Tooltip("Modulate density with a Perlin layer so placement forms natural patches " +
             "(forests) instead of uniform sprinkling.")]
    public bool useClusterNoise = false;
    [Range(0.01f, 0.5f)] public float clusterFrequency = 0.12f;
    [Range(0f, 1f)]      public float clusterThreshold = 0.35f;

    [Tooltip("Minimum world-space distance to other instances of THIS rule (simple overlap guard).")]
    public float minSpacing = 0f;

    [Header("Look")]
    public bool randomYRotation = true;
    [Tooltip("Random uniform scale range applied to each instance.")]
    public Vector2 scaleRange = new Vector2(0.8f, 1.3f);

    [Header("Settlement zones")]
    [Tooltip("Density multiplier inside reserved settlement (village) zones. " +
             "0.15 = zones stay mostly clear; 1 = ignore zones.")]
    [Range(0f, 1f)] public float settlementDensityMultiplier = 0.15f;

    /// <summary>Convert slope-angle window to surface-normal Y window (cheap per-vertex test).</summary>
    public float MaxNormalY => Mathf.Cos(minSlopeAngle * Mathf.Deg2Rad);
    public float MinNormalY => Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad);

    void OnValidate()
    {
        if (maxHeight < minHeight) maxHeight = minHeight;
        if (maxSlopeAngle < minSlopeAngle) maxSlopeAngle = minSlopeAngle;
        if (scaleRange.x <= 0f) scaleRange.x = 0.01f;
        if (scaleRange.y < scaleRange.x) scaleRange.y = scaleRange.x;
    }
}
