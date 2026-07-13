using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WILDCUT Chapter 7 — data definition of a harvestable resource type.
/// One asset per resource kind (tree, rock, bush...). Adding a new resource to
/// the game requires NO code: create this asset, fill the fields, then either
/// assign it to a PlacementRule (auto-attached to every spawned prop) or drop
/// a HarvestableResource component on any object and point it here.
/// Create via: Assets ▸ Create ▸ WILDCUT ▸ Harvestable Resource.
/// </summary>
[CreateAssetMenu(menuName = "WILDCUT/Harvestable Resource", fileName = "NewHarvestable")]
public class HarvestableResourceDef : ScriptableObject
{
    [Header("Presentation")]
    public string displayName = "Kaynak";
    [Tooltip("Verb used in the interaction prompt: 'Kes', 'Topla', 'Kaz'...")]
    public string promptVerb = "Topla";

    [Header("Harvesting")]
    [Tooltip("How many interactions (hits) deplete the node.")]
    [Min(1)] public int hitsToHarvest = 3;
    [Tooltip("Tool tag required to harvest (e.g. 'axe', 'pickaxe'). " +
             "EMPTY = can be harvested by hand.")]
    public string requiredToolTag = "";

    [Header("Drops (delivered through IItemReceiver — inventory is the teammate's side)")]
    public List<ItemDrop> drops = new List<ItemDrop> { new ItemDrop { itemId = "wood", count = 3 } };

    [Header("Respawn")]
    [Tooltip("Seconds until the node regrows after depletion. 0 = never respawns " +
             "(the object is destroyed).")]
    [Min(0f)] public float respawnSeconds = 0f;

    [Header("World events (optional, Chapters 9-10)")]
    [Tooltip("Channel to broadcast on when the node is depleted. Empty = no event.")]
    public WorldEventChannel eventChannel;
    [Tooltip("Event id raised on depletion, e.g. 'tree_chopped'.")]
    public string depletedEventId = "";

    void OnValidate()
    {
        for (int i = 0; i < drops.Count; i++)
            if (drops[i].count < 1)
            {
                ItemDrop d = drops[i];
                d.count = 1;
                drops[i] = d;
            }
    }
}

/// <summary>Item id + amount. Pure data — no inventory logic on the map side.</summary>
[System.Serializable]
public struct ItemDrop
{
    public string itemId;
    [Min(1)] public int count;
}
