using UnityEngine;

/// <summary>
/// WILDCUT Chapter 7 — generic "look at → prompt → press E" interactor.
/// Raycasts from the FPS camera; whatever IInteractable it hits gets a prompt
/// on screen and receives Interact() on the interact key. Fully agnostic:
/// doors, chests, NPCs, harvest nodes — anything with an IInteractable works.
/// UI is a deliberately plain OnGUI label for now (design doc: no fancy UI yet).
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Header("Reach")]
    [Tooltip("Max interaction distance from the camera (world units).")]
    public float interactRange = 3f;

    [Header("Input")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("Ray origin; empty = main camera (found automatically at runtime).")]
    public Transform rayOrigin;

    /// <summary>What the player is currently aiming at (null = nothing usable). Read by tests.</summary>
    public IInteractable CurrentTarget { get; private set; }
    /// <summary>Whether CurrentTarget accepts interaction right now.</summary>
    public bool TargetUsable { get; private set; }

    string currentPrompt;
    Camera cachedCamera;

    void Update()
    {
        UpdateTarget();

        if (CurrentTarget != null && TargetUsable && Input.GetKeyDown(interactKey))
            CurrentTarget.Interact(gameObject);
    }

    /// <summary>Programmatic "press E" — used by MCP play-mode tests.</summary>
    public bool TryInteract()
    {
        UpdateTarget();
        if (CurrentTarget == null || !TargetUsable) return false;
        CurrentTarget.Interact(gameObject);
        return true;
    }

    void UpdateTarget()
    {
        CurrentTarget = null;
        TargetUsable  = false;
        currentPrompt = null;

        Transform origin = ResolveOrigin();
        if (origin == null) return;

        if (!Physics.Raycast(origin.position, origin.forward, out RaycastHit hit, interactRange))
            return;

        // Interactable may live on the collider itself or a parent (e.g. a prop
        // whose collider sits on a child mesh).
        IInteractable target = hit.collider.GetComponentInParent<IInteractable>();
        if (target == null) return;

        CurrentTarget = target;
        TargetUsable  = target.CanInteract(gameObject);
        currentPrompt = target.GetPrompt();
    }

    Transform ResolveOrigin()
    {
        if (rayOrigin != null) return rayOrigin;
        if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
            cachedCamera = Camera.main;
        return cachedCamera != null ? cachedCamera.transform : null;
    }

    // ---- Minimal HUD: crosshair dot + centered prompt ------------------- //

    void OnGUI()
    {
        // Crosshair dot so the player can tell what the ray points at.
        var dotStyle = new GUIStyle(GUI.skin.label)
        { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
        GUI.Label(new Rect(Screen.width / 2f - 10, Screen.height / 2f - 10, 20, 20), "•", dotStyle);

        if (string.IsNullOrEmpty(currentPrompt)) return;

        var style = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize  = 16,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = TargetUsable ? Color.white : new Color(1f, 1f, 1f, 0.45f);

        string text = $"[{interactKey}]  {currentPrompt}";
        float  w    = style.CalcSize(new GUIContent(text)).x + 24f;
        GUI.Box(new Rect((Screen.width - w) / 2f, Screen.height * 0.62f, w, 34f), text, style);
    }
}
