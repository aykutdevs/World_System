using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WILDCUT Chapter 7 / Table 12 — scene-side implementation of the environmental
/// factor API. Resolves the active factor list from the MapGenerator's island
/// archetype (so every island family carries its own climate data) plus an
/// optional local list, and evaluates factors by world position. Altitude is
/// measured against the actual water surface (WaterPlane), so regenerated
/// islands stay consistent automatically.
/// The HEALTH system (teammate) is the consumer — see IEnvironmentalFactorProvider.
/// </summary>
public class WorldEnvironment : MonoBehaviour, IEnvironmentalFactorProvider
{
    [Header("References (auto-found if empty)")]
    public MapGenerator mapGenerator;

    [Tooltip("Extra factors active regardless of archetype (e.g. a global 'radiation' test).")]
    public List<EnvironmentalFactorDef> additionalFactors = new List<EnvironmentalFactorDef>();

    readonly HashSet<string> warnedIds = new HashSet<string>();

    float SeaLevelY => WaterPlane.Instance != null ? WaterPlane.Instance.SurfaceY : 0f;

    // ---- IEnvironmentalFactorProvider ----------------------------------- //

    public float GetFactor(string factorId, Vector3 worldPos)
    {
        EnvironmentalFactorDef def = FindDef(factorId);
        if (def == null)
        {
            if (warnedIds.Add(factorId))
                Debug.LogWarning($"[WorldEnvironment] Factor '{factorId}' is not defined for this " +
                                 "world (check the archetype's Environmental Factors list). Returning 0.");
            return 0f;
        }
        return def.Evaluate(worldPos.y - SeaLevelY);
    }

    public bool HasFactor(string factorId) => FindDef(factorId) != null;

    // ---- Factor resolution ----------------------------------------------- //

    EnvironmentalFactorDef FindDef(string factorId)
    {
        if (mapGenerator == null) mapGenerator = FindObjectOfType<MapGenerator>();

        EnvironmentalFactorDef[] archetypeFactors =
            (mapGenerator != null && mapGenerator.islandArchetype != null)
                ? mapGenerator.islandArchetype.environmentalFactors
                : null;

        if (archetypeFactors != null)
            foreach (EnvironmentalFactorDef d in archetypeFactors)
                if (d != null && d.factorId == factorId) return d;

        foreach (EnvironmentalFactorDef d in additionalFactors)
            if (d != null && d.factorId == factorId) return d;

        return null;
    }

    /// <summary>All factor defs active for the current world (used by the debug heatmap).</summary>
    public List<EnvironmentalFactorDef> GetActiveFactors()
    {
        var list = new List<EnvironmentalFactorDef>();
        if (mapGenerator == null) mapGenerator = FindObjectOfType<MapGenerator>();
        if (mapGenerator != null && mapGenerator.islandArchetype != null &&
            mapGenerator.islandArchetype.environmentalFactors != null)
            foreach (EnvironmentalFactorDef d in mapGenerator.islandArchetype.environmentalFactors)
                if (d != null) list.Add(d);
        foreach (EnvironmentalFactorDef d in additionalFactors)
            if (d != null) list.Add(d);
        return list;
    }

    // ---- Debug: shore vs peak sample ------------------------------------- //

    [ContextMenu("Log Factor Samples (shore vs peak)")]
    public void LogFactorSamples()
    {
        MapDisplay display = FindObjectOfType<MapDisplay>();
        if (display == null || display.meshFilter == null || display.meshFilter.sharedMesh == null)
        {
            Debug.LogWarning("[WorldEnvironment] No terrain mesh — generate the world first.");
            return;
        }

        // Highest terrain vertex = peak; a vertex just above sea level = shore.
        Transform tf    = display.meshFilter.transform;
        Vector3[] verts = display.meshFilter.sharedMesh.vertices;
        float sea       = SeaLevelY;

        Vector3 peak  = Vector3.negativeInfinity;
        Vector3 shore = Vector3.zero;
        float bestShoreDelta = float.MaxValue;

        foreach (Vector3 v in verts)
        {
            Vector3 w = tf.TransformPoint(v);
            if (w.y > peak.y) peak = w;
            float delta = w.y - (sea + 1f);
            if (delta >= 0f && delta < bestShoreDelta) { bestShoreDelta = delta; shore = w; }
        }

        List<EnvironmentalFactorDef> factors = GetActiveFactors();
        if (factors.Count == 0)
        {
            Debug.LogWarning("[WorldEnvironment] No factors defined — assign EnvironmentalFactorDef " +
                             "assets to the archetype (or Additional Factors).");
            return;
        }

        foreach (EnvironmentalFactorDef f in factors)
            Debug.Log($"[WorldEnvironment] '{f.factorId}':  shore(y={shore.y:F0}) = " +
                      $"{GetFactor(f.factorId, shore):F1}   |   peak(y={peak.y:F0}) = " +
                      $"{GetFactor(f.factorId, peak):F1}");
    }
}
