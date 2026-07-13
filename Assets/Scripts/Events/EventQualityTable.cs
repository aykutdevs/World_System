using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WILDCUT Chapters 9-10 — eventId → quality score mapping (design doc: the
/// "niteliği" of whatever the documentary camera captures). Data only: balancing
/// scores or adding new events is done in this asset, never in code.
/// Create via: Assets ▸ Create ▸ WILDCUT ▸ Events ▸ Event Quality Table.
/// </summary>
[CreateAssetMenu(menuName = "WILDCUT/Events/Event Quality Table", fileName = "EventQualityTable")]
public class EventQualityTable : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        [Tooltip("Must match the eventId string passed to WorldEventChannel.Raise().")]
        public string eventId;
        [Tooltip("Documentary value of this moment (higher = more spectacular footage).")]
        public int qualityScore;
    }

    public List<Entry> entries = new List<Entry>();

    /// <summary>Score for an event id; 0 when the id is not listed (a dull moment).</summary>
    public int GetScore(string eventId)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].eventId == eventId)
                return entries[i].qualityScore;
        return 0;
    }
}
