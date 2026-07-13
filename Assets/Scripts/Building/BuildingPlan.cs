using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WILDCUT Chapter 8 — data definition of one buildable structure.
/// A new building = a new asset of this type added to BuildPlacer's Plans list.
/// No code. Required materials are DATA ONLY — checking/consuming them is the
/// teammate's inventory side; today the construction site counts debug deliveries.
/// Create via: Assets ▸ Create ▸ WILDCUT ▸ Building Plan.
/// </summary>
[CreateAssetMenu(menuName = "WILDCUT/Building Plan", fileName = "NewBuildingPlan")]
public class BuildingPlan : ScriptableObject
{
    [Header("Presentation")]
    public string displayName = "Yapı";

    [Header("Prefabs")]
    [Tooltip("Finished building look. REQUIRED.")]
    public GameObject finalPrefab;
    [Tooltip("Optional preview shape for placement. Empty = final prefab is used " +
             "(rendered semi-transparent by BuildPlacer either way).")]
    public GameObject ghostPrefab;
    [Tooltip("Optional 'under construction' look. Empty = final prefab tinted orange " +
             "until all materials are delivered.")]
    public GameObject constructionPrefab;

    [Header("Footprint & ground")]
    [Tooltip("Ground area the building occupies, world units (X × Z).")]
    public Vector2 footprintSize = new Vector2(4f, 4f);
    [Tooltip("Max terrain slope (degrees) the footprint may sit on.")]
    [Range(0f, 45f)] public float maxGroundSlopeAngle = 18f;
    [Tooltip("Flatten the terrain under the footprint to the building base on placement.")]
    public bool flattenGround = true;

    [Header("Required materials (DATA ONLY — envanter kontrolü ekip arkadaşının tarafı)")]
    public List<ItemRequirement> requiredItems = new List<ItemRequirement>
    {
        new ItemRequirement { itemId = "wood", count = 4 }
    };

    [Header("World events (optional, Chapters 9-10)")]
    public WorldEventChannel eventChannel;
    [Tooltip("Raised when construction finishes. Empty = no event.")]
    public string completedEventId = "building_completed";

    /// <summary>Total material units — the debug construction flow counts against this.</summary>
    public int TotalRequiredCount
    {
        get
        {
            int total = 0;
            foreach (ItemRequirement r in requiredItems) total += r.count;
            return total;
        }
    }

    void OnValidate()
    {
        if (footprintSize.x < 0.5f) footprintSize.x = 0.5f;
        if (footprintSize.y < 0.5f) footprintSize.y = 0.5f;
    }
}

/// <summary>Item id + amount needed. Mirror of ItemDrop on the consuming side.</summary>
[System.Serializable]
public struct ItemRequirement
{
    public string itemId;
    [Min(1)] public int count;
}
