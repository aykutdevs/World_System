using UnityEngine;

/// <summary>
/// WILDCUT Chapter 7 — universal interaction contract.
/// ANY usable object (harvest node, door, chest, NPC, construction site...)
/// implements this; PlayerInteractor discovers it via raycast and needs no
/// knowledge of the concrete type. New interactable content = new component
/// implementing this interface, zero changes on the player side.
/// </summary>
public interface IInteractable
{
    /// <summary>Short prompt shown while aimed at (rendered as "[E] {prompt}").</summary>
    string GetPrompt();

    /// <summary>False = target exists but refuses interaction right now (prompt greys out).</summary>
    bool CanInteract(GameObject who);

    void Interact(GameObject who);
}
