using System;
using UnityEngine;

/// <summary>
/// WILDCUT Chapters 9-10 — world event infrastructure (documentary / score base).
/// A ScriptableObject event channel: any system Raises named events, any listener
/// Subscribes — neither side references the other, only this shared asset.
/// The documentary camera (Chapter 9) will subscribe here later; today the only
/// listener is WorldEventLogger. Adding a new event type requires NO code:
/// pick an eventId string and (optionally) add its quality score to the
/// EventQualityTable asset assigned below.
/// Create via: Assets ▸ Create ▸ WILDCUT ▸ Events ▸ World Event Channel.
/// </summary>
[CreateAssetMenu(menuName = "WILDCUT/Events/World Event Channel", fileName = "Channel_WorldEvents")]
public class WorldEventChannel : ScriptableObject
{
    [Tooltip("Optional — every raised payload is stamped with the event's quality score " +
             "from this table (the 'niteliği' of what a documentary shot would capture). " +
             "Unlisted events get score 0.")]
    public EventQualityTable qualityTable;

    /// <summary>Subscribe in OnEnable, unsubscribe in OnDisable (see WorldEventLogger).</summary>
    public event Action<WorldEventPayload> OnRaised;

    public void Raise(string eventId, Vector3 worldPos)
    {
        var payload = new WorldEventPayload
        {
            eventId      = eventId,
            worldPos     = worldPos,
            time         = Application.isPlaying ? Time.time : 0f,
            qualityScore = qualityTable != null ? qualityTable.GetScore(eventId) : 0
        };
        OnRaised?.Invoke(payload);
    }
}

/// <summary>What happened, where, when, and how noteworthy it is (0-100).</summary>
[Serializable]
public struct WorldEventPayload
{
    public string  eventId;
    public Vector3 worldPos;
    public float   time;           // Time.time at the moment of Raise
    public int     qualityScore;   // from EventQualityTable; 0 if unlisted
}
