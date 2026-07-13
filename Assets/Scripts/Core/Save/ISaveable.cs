// ---- Core.Save — JSON save/load service (reusable across projects) ----
// Depends only on UnityEngine (JsonUtility + persistentDataPath), never on
// game code. Games implement ISaveable binders and hand them to SaveService.

namespace Core.Save
{
    /// <summary>
    /// One save-participating object. SaveId must be unique and stable across
    /// sessions (it is the lookup key inside the save file). CaptureState
    /// returns a JSON string (e.g. JsonUtility.ToJson of a [Serializable]
    /// data struct); RestoreState receives that same string back on load.
    /// </summary>
    public interface ISaveable
    {
        string SaveId { get; }
        string CaptureState();
        void RestoreState(string json);
    }
}
