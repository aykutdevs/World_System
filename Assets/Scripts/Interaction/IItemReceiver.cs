using UnityEngine;

/// <summary>
/// ================= TEAMMATE SEAM — INVENTORY (Chapters 3-5) =================
/// The world/map side only PRODUCES items (harvest drops, future loot). The
/// inventory system is the teammate's responsibility: they implement this
/// interface on any scene component and delete DebugItemReceiver — nothing on
/// the map side changes. Producers never call an inventory directly; they go
/// through ItemDelivery.TryDeliver below.
/// ============================================================================
/// </summary>
public interface IItemReceiver
{
    /// <returns>True if the items were accepted (false = e.g. inventory full;
    /// the producer may then drop them on the ground or keep the node alive).</returns>
    bool TryGiveItem(string itemId, int count);
}

/// <summary>
/// Locator so producers don't each search the scene: finds whatever component
/// implements IItemReceiver (the teammate's inventory once it exists, the
/// DebugItemReceiver stub until then) and caches it.
/// </summary>
public static class ItemDelivery
{
    static IItemReceiver cached;

    public static bool TryDeliver(string itemId, int count)
    {
        // MonoBehaviour receivers die on scene reload — re-search when stale.
        if (cached == null || (cached is MonoBehaviour mb && mb == null))
        {
            cached = null;
            foreach (MonoBehaviour b in Object.FindObjectsOfType<MonoBehaviour>())
                if (b is IItemReceiver r) { cached = r; break; }
        }

        if (cached == null)
        {
            Debug.LogWarning($"[ItemDelivery] No IItemReceiver in scene — {count} × '{itemId}' was lost. " +
                             "Add a DebugItemReceiver (or the real inventory) to any GameObject.");
            return false;
        }
        return cached.TryGiveItem(itemId, count);
    }
}
