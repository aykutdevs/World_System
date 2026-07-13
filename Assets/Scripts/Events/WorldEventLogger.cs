using UnityEngine;

/// <summary>
/// WILDCUT Chapters 9-10 — minimal reference listener for the world event channel.
/// Logs every raised event to the console so the pipeline can be verified before
/// the real consumers (documentary camera, score system) exist. Those will
/// subscribe to the same channel exactly like this component does.
/// </summary>
public class WorldEventLogger : MonoBehaviour
{
    [Tooltip("The channel to listen on (same asset the raisers reference).")]
    public WorldEventChannel channel;

    void OnEnable()
    {
        if (channel != null) channel.OnRaised += Log;
    }

    void OnDisable()
    {
        if (channel != null) channel.OnRaised -= Log;
    }

    void Log(WorldEventPayload p)
    {
        Debug.Log($"[WorldEvent] '{p.eventId}'  quality={p.qualityScore}  " +
                  $"pos={p.worldPos}  t={p.time:F1}s");
    }
}
