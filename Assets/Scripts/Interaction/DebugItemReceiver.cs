using UnityEngine;

/// <summary>
/// ==== TEAMMATE STUB — delete when the real inventory implements IItemReceiver ====
/// Stand-in for the inventory system (Chapters 3-5, teammate's side): accepts
/// every item and logs it, so harvest/build flows are testable today.
/// </summary>
public class DebugItemReceiver : MonoBehaviour, IItemReceiver
{
    public bool TryGiveItem(string itemId, int count)
    {
        Debug.Log($"[DebugItemReceiver] +{count} × '{itemId}'  " +
                  "(stub — gerçek envanter IItemReceiver'ı implement edince bu component silinecek)");
        return true;
    }
}
