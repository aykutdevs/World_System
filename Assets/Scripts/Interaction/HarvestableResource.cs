using System.Collections;
using UnityEngine;

/// <summary>
/// WILDCUT Chapter 7 — runtime side of a harvestable node. Reads everything
/// from a HarvestableResourceDef asset. Each Interact() is one hit; at zero
/// the drops go to the IItemReceiver (inventory seam), an optional world event
/// is raised, and the node either respawns after a delay or is destroyed.
/// RuleBasedSpawner attaches this automatically when a PlacementRule has a
/// harvestableDef assigned — no code needed to make a prop harvestable.
/// </summary>
public class HarvestableResource : MonoBehaviour, IInteractable
{
    public HarvestableResourceDef def;

    public int  HitsRemaining { get; private set; }
    public bool Depleted      { get; private set; }

    bool initialized;

    // Lazy init instead of Awake: when RuleBasedSpawner does AddComponent, Awake
    // fires BEFORE def is assigned — durability must be read on first use.
    void EnsureInit()
    {
        if (initialized || def == null) return;
        HitsRemaining = def.hitsToHarvest;
        initialized   = true;
    }

    // ---- IInteractable -------------------------------------------------- //

    public string GetPrompt()
    {
        EnsureInit();
        if (def == null || Depleted) return null;
        string tool = string.IsNullOrEmpty(def.requiredToolTag) ? "" : $"  ({def.requiredToolTag} gerekir)";
        return $"{def.displayName} — {def.promptVerb}  [{HitsRemaining} vuruş]{tool}";
    }

    public bool CanInteract(GameObject who)
    {
        EnsureInit();
        return def != null && !Depleted && HasRequiredTool(who);
    }

    public void Interact(GameObject who)
    {
        if (!CanInteract(who)) return;

        HitsRemaining--;
        if (HitsRemaining > 0) return;

        Deplete();
    }

    // ================= TEAMMATE HOOK — TOOL CHECK (Chapters 3-5) =================
    // Always true FOR NOW. When the inventory/equipment system exists, this must
    // ask it whether 'who' holds a tool matching def.requiredToolTag, e.g.:
    //     return string.IsNullOrEmpty(def.requiredToolTag)
    //         || inventory.HasToolWithTag(def.requiredToolTag);
    // Empty requiredToolTag always means "harvestable by hand".
    // =============================================================================
    bool HasRequiredTool(GameObject who)
    {
        return true;
    }

    // ---- Depletion / respawn -------------------------------------------- //

    void Deplete()
    {
        Depleted = true;

        foreach (ItemDrop drop in def.drops)
            ItemDelivery.TryDeliver(drop.itemId, drop.count);

        // Core.Pooling proof point: pooled pickup marker where the node dropped
        // its items (null-safe — no PickupPool in the scene = no visual).
        PickupPool.TrySpawn(transform.position + Vector3.up * 0.4f);

        if (def.eventChannel != null && !string.IsNullOrEmpty(def.depletedEventId))
            def.eventChannel.Raise(def.depletedEventId, transform.position);

        if (def.respawnSeconds > 0f && Application.isPlaying)
        {
            SetVisible(false);
            StartCoroutine(RespawnAfterDelay());
        }
        else
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }

    IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(def.respawnSeconds);
        HitsRemaining = def.hitsToHarvest;
        Depleted      = false;
        SetVisible(true);
    }

    // Hide renderers/colliders instead of deactivating the GameObject — a
    // deactivated object cannot run its own respawn coroutine.
    void SetVisible(bool visible)
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = visible;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = visible;
    }
}
