using UnityEngine;

/// <summary>
/// WILDCUT Chapter 8, flow steps 4-5 (skeleton) — a freshly placed structure in
/// its "under construction" state. IInteractable: each E press delivers one
/// material unit (DEBUG counting for now); when plan.TotalRequiredCount is
/// reached the site switches to the final look and optionally raises a world
/// event. Created and initialised by BuildPlacer — not added by hand.
/// </summary>
public class ConstructionSite : MonoBehaviour, IInteractable
{
    public BuildingPlan plan;

    [Tooltip("Set by BuildPlacer: true = this object is a dedicated construction " +
             "prefab that gets REPLACED by finalPrefab on completion; false = this " +
             "IS the final prefab, tinted until complete.")]
    public bool swapToFinalOnComplete;

    public int  Delivered { get; private set; }
    public bool Completed { get; private set; }

    static readonly Color ConstructionTint = new Color(1f, 0.72f, 0.35f);

    MaterialPropertyBlock mpb;

    void Start()
    {
        if (!swapToFinalOnComplete && !Completed)
            ApplyTint(true);
    }

    // ---- IInteractable -------------------------------------------------- //

    public string GetPrompt()
    {
        if (plan == null || Completed) return null;
        return $"{plan.displayName} — Malzeme ver  ({Delivered}/{plan.TotalRequiredCount})";
    }

    public bool CanInteract(GameObject who) => plan != null && !Completed;

    public void Interact(GameObject who)
    {
        if (!CanInteract(who)) return;

        // ============ TEAMMATE HOOK — MATERIAL DELIVERY (Chapters 3-5) ============
        // DEBUG behaviour: every E press counts as 1 delivered material unit.
        // When the inventory exists, this must instead take real items from it
        // against plan.requiredItems (itemId + count), e.g.:
        //     if (inventory.TryTake(nextNeeded.itemId, 1)) Delivered++;
        // ==========================================================================
        Delivered++;
        Debug.Log($"[ConstructionSite] '{plan.displayName}' malzeme {Delivered}/{plan.TotalRequiredCount} (debug sayaç).");

        if (Delivered >= plan.TotalRequiredCount)
            Complete();
    }

    /// <summary>Save/load support: re-applies a delivered-materials count
    /// without firing events (the site stays under construction).</summary>
    public void RestoreProgress(int delivered)
    {
        int max = plan != null ? plan.TotalRequiredCount : delivered;
        Delivered = Mathf.Clamp(delivered, 0, max);
    }

    // ---- Completion ------------------------------------------------------ //

    void Complete()
    {
        Completed = true;

        if (plan.eventChannel != null && !string.IsNullOrEmpty(plan.completedEventId))
            plan.eventChannel.Raise(plan.completedEventId, transform.position);

        if (swapToFinalOnComplete && plan.finalPrefab != null)
        {
            GameObject final = Instantiate(plan.finalPrefab, transform.position,
                                           transform.rotation, transform.parent);
            final.name = plan.displayName;
            final.AddComponent<PlacedBuilding>().plan = plan;
            PlacedBuilding.EnsureCollider(final, plan.footprintSize);
            Destroy(gameObject);
        }
        else
        {
            ApplyTint(false);   // final prefab was already in place — just untint
        }

        Debug.Log($"[ConstructionSite] '{plan.displayName}' tamamlandı!");
    }

    // Tint via MaterialPropertyBlock — no material instances are created, so the
    // shared materials of the prefab stay untouched.
    void ApplyTint(bool on)
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (on)
            {
                mpb.Clear();
                mpb.SetColor("_Color", ConstructionTint);
                r.SetPropertyBlock(mpb);
            }
            else
            {
                r.SetPropertyBlock(null);
            }
        }
    }
}
