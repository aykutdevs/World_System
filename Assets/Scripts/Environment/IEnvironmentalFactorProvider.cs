using UnityEngine;

/// <summary>
/// ============ TEAMMATE SEAM — HEALTH SYSTEM (Table 12, Chapters 1-5) ============
/// The map side PROVIDES environmental factors; the health system CONSUMES them.
/// The teammate's health bars should query this interface (WorldEnvironment in
/// the scene implements it) and never touch MapGenerator/terrain internals:
///
///     var env = FindObjectOfType&lt;WorldEnvironment&gt;();          // or cache it
///     float t = env.GetFactor("temperature", player.position);  // °C at player
///
/// Which factors exist — and their values — is data on the IslandArchetype asset.
/// ================================================================================
/// </summary>
public interface IEnvironmentalFactorProvider
{
    /// <summary>Factor value at a world position. Unknown factorId returns 0
    /// (and logs a warning once).</summary>
    float GetFactor(string factorId, Vector3 worldPos);

    /// <summary>True if the current world exposes this factor.</summary>
    bool HasFactor(string factorId);
}
