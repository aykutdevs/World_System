using UnityEngine;

// ---- WILDCUT — First-person player controller ----
// State machine per the design doc: Grounded / Airborne / Climbing are separate
// states with their own update methods, so future states (swim, crouch, vault)
// slot in without turning Update() into a monolith.
//
// Movement uses Unity's CharacterController (not Rigidbody): on procedural mesh
// colliders with steep slopes it needs no friction/mass tuning, never gets pushed
// through by physics, and slopeLimit/stepOffset map directly onto the terrain's
// walkability thresholds. Trade-off: no physics momentum — acceptable for now.

public enum PlayerState { Grounded, Airborne, Climbing, Swimming }

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 6f;
    [Tooltip("Run = walk speed × this while Shift is held.")]
    public float runMultiplier = 1.8f;
    public float jumpHeight = 1.3f;
    public float gravity = 25f;
    [Range(0f, 1f)] public float airControl = 0.6f;

    [Header("Climbing")]
    [Tooltip("Surface angle (degrees) above which pushing forward starts climbing. " +
             "Kept consistent with CharacterController.slopeLimit (45°) and the " +
             "walkability heatmap's red band.")]
    public float climbMinAngle = 45f;
    public float climbSpeed = 2.5f;
    public float climbCheckDistance = 1.3f;

    [Header("Swimming")]
    public float swimSpeed = 4.5f;
    [Tooltip("Vertical swim speed: Space rises toward the surface, Shift dives.")]
    public float swimVerticalSpeed = 3f;
    [Tooltip("How deep the body must be under the water surface before swimming starts " +
             "(chest height — lets the player wade in the shallows on foot).")]
    public float swimWaterlineHeight = 1.2f;

    [Header("Look")]
    [Tooltip("Eye anchor the main camera parents to at runtime. Auto-found by name 'Eye'.")]
    public Transform eye;
    public float mouseSensitivity = 2.2f;
    public float pitchLimit = 80f;

    [Header("Safety")]
    [Tooltip("Falling below this world Y teleports the player back to the spawn point.")]
    public float killY = -60f;

    // ---- External input hook (used by automated MCP tests; also a seam for a
    //      future input-rebinding system). When enabled, keyboard/mouse is ignored.
    [HideInInspector] public bool useExternalInput;
    [HideInInspector] public Vector2 externalMove;
    [HideInInspector] public bool externalRun;
    [HideInInspector] public bool externalJump;
    [HideInInspector] public float externalSwimVertical;   // -1 dive .. +1 rise

    public PlayerState State { get; private set; } = PlayerState.Airborne;
    /// <summary>World-space velocity from the CharacterController (read by EnemyAI prediction).</summary>
    public Vector3 Velocity => controller != null ? controller.velocity : Vector3.zero;

    CharacterController controller;
    float verticalVelocity;
    float pitch;
    Vector3 spawnPosition;
    RaycastHit climbHit;

    // ------------------------------------------------------------------ //

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        spawnPosition = transform.position;
        if (eye == null)
        {
            Transform t = transform.Find("Eye");
            if (t != null) eye = t;
        }
    }

    bool cameraAdopted;

    void Start()
    {
        AdoptCamera();
        LockCursor(true);
    }

    // FPS camera: adopt a camera at runtime only — in edit mode the scene camera
    // stays free for level inspection. Retried from Update() until it succeeds so
    // script-order or late-created cameras can't leave the game in a non-FPS view,
    // and every OTHER active camera is disabled so exactly one camera renders.
    void AdoptCamera()
    {
        if (eye == null) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            Camera[] all = FindObjectsOfType<Camera>();
            if (all.Length > 0) cam = all[0];
        }
        if (cam == null) return;

        cam.transform.SetParent(eye, false);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;

        foreach (Camera other in FindObjectsOfType<Camera>())
        {
            if (other != cam && other.enabled)
            {
                other.enabled = false;
                Debug.Log($"[PlayerController] Disabled extra camera '{other.name}' — one active camera only.");
            }
        }

        cameraAdopted = true;
        Debug.Log($"[PlayerController] Camera '{cam.name}' attached to Eye (first-person).");
    }

    /// <summary>Called by WorldCharacterSpawner after each island generation.</summary>
    public void SetSpawn(Vector3 pos)
    {
        spawnPosition = pos;
        Teleport(pos);
    }

    public void Teleport(Vector3 pos)
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        controller.enabled = false;
        transform.position = pos;
        controller.enabled = true;
        verticalVelocity = 0f;
        State = PlayerState.Airborne;   // settle onto the ground next frame
    }

    // ------------------------------------------------------------------ //

    void Update()
    {
        if (!cameraAdopted && Time.frameCount % 30 == 0) AdoptCamera();

        HandleCursor();
        HandleLook();

        Vector2 move = ReadMove();
        bool run  = ReadRun();
        bool jump = ReadJump();

        // Entering water overrides every land state (chest below the surface).
        if (State != PlayerState.Swimming && IsUnderwater)
            State = PlayerState.Swimming;

        switch (State)
        {
            case PlayerState.Grounded: UpdateGrounded(move, run, jump); break;
            case PlayerState.Airborne: UpdateAirborne(move); break;
            case PlayerState.Climbing: UpdateClimbing(move, jump); break;
            case PlayerState.Swimming: UpdateSwimming(move); break;
        }

        if (transform.position.y < killY)
        {
            Debug.LogWarning("[PlayerController] Fell out of world — respawning.");
            Teleport(spawnPosition + Vector3.up * 2f);
        }
    }

    // ---- States ------------------------------------------------------- //

    void UpdateGrounded(Vector2 move, bool run, bool jump)
    {
        Vector3 dir   = MoveDirection(move);
        float   speed = walkSpeed * (run ? runMultiplier : 1f);

        verticalVelocity = -4f;   // downward bias keeps the controller glued on slopes
        if (jump)
            verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);

        MoveWithVertical(dir * speed);

        if (WantsToClimb(move))            State = PlayerState.Climbing;
        else if (!controller.isGrounded)   State = PlayerState.Airborne;
    }

    void UpdateAirborne(Vector2 move)
    {
        verticalVelocity -= gravity * Time.deltaTime;
        MoveWithVertical(MoveDirection(move) * walkSpeed * airControl);

        if (controller.isGrounded)
            State = PlayerState.Grounded;
        else if (verticalVelocity <= 2f && WantsToClimb(move))
            State = PlayerState.Climbing;   // grab a wall while falling past it
    }

    void UpdateClimbing(Vector2 move, bool jump)
    {
        verticalVelocity = 0f;   // gravity suspended while attached to the wall

        if (jump)
        {
            // Push off the wall: detach upward, player steers away with air control.
            verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight) * 0.8f;
            State = PlayerState.Airborne;
            return;
        }

        bool  wall  = FindClimbSurface(out climbHit);
        float angle = wall ? Vector3.Angle(climbHit.normal, Vector3.up) : 0f;

        if (!wall || angle < climbMinAngle)
        {
            // Topped out or the wall flattened — small mount boost over the ledge.
            controller.Move(Vector3.up * 0.6f + transform.forward * 0.5f);
            State = controller.isGrounded ? PlayerState.Grounded : PlayerState.Airborne;
            return;
        }

        // W/S move up/down the wall, A/D strafe slowly, constant pull sticks us to it.
        // Running is intentionally unavailable while climbing.
        Vector3 climb = Vector3.up * (move.y * climbSpeed)
                      + transform.right * (move.x * climbSpeed * 0.5f)
                      - climbHit.normal * 1.5f;
        controller.Move(climb * Time.deltaTime);

        if (controller.isGrounded && move.y < -0.1f)
            State = PlayerState.Grounded;   // climbed back down to walkable ground
    }

    void UpdateSwimming(Vector2 move)
    {
        verticalVelocity = 0f;   // water carries the body — no gravity

        float vert = ReadSwimVertical();
        Vector3 dir = MoveDirection(move) * swimSpeed;
        dir.y = vert * swimVerticalSpeed;

        // Don't swim above the surface — cap upward motion at the waterline.
        if (dir.y > 0f && transform.position.y + swimWaterlineHeight >= WaterY)
            dir.y = 0f;

        controller.Move(dir * Time.deltaTime);

        if (!IsUnderwater)
            State = controller.isGrounded ? PlayerState.Grounded : PlayerState.Airborne;
    }

    // ---- Helpers ------------------------------------------------------ //

    float WaterY => WaterPlane.Instance != null ? WaterPlane.Instance.SurfaceY : float.NegativeInfinity;

    bool IsUnderwater => transform.position.y + swimWaterlineHeight < WaterY;

    void MoveWithVertical(Vector3 horizontalVelocity)
    {
        Vector3 v = horizontalVelocity;
        v.y = verticalVelocity;
        controller.Move(v * Time.deltaTime);
    }

    Vector3 MoveDirection(Vector2 move)
    {
        Vector3 dir = transform.right * move.x + transform.forward * move.y;
        return dir.sqrMagnitude > 1f ? dir.normalized : dir;
    }

    bool WantsToClimb(Vector2 move)
    {
        if (move.y <= 0.1f) return false;   // must push toward the wall
        if (!FindClimbSurface(out climbHit)) return false;
        float angle = Vector3.Angle(climbHit.normal, Vector3.up);
        return angle >= climbMinAngle && angle <= 100f;   // wall, not ceiling
    }

    bool FindClimbSurface(out RaycastHit hit)
    {
        Vector3 origin = transform.position + Vector3.up * 1.0f;
        if (!Physics.Raycast(origin, transform.forward, out hit, climbCheckDistance))
            return false;
        // Only the terrain (MeshCollider) is climbable — prop BoxColliders are not.
        return hit.collider is MeshCollider;
    }

    // ---- Look & cursor ------------------------------------------------- //

    void HandleLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        transform.Rotate(0f, mx, 0f);                       // yaw turns the body
        pitch = Mathf.Clamp(pitch - my, -pitchLimit, pitchLimit);
        if (eye != null)
            eye.localRotation = Quaternion.Euler(pitch, 0f, 0f);   // pitch only the eye
    }

    void HandleCursor()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            LockCursor(true);
    }

    static void LockCursor(bool on)
    {
        Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !on;
    }

    // ---- Input (keyboard or external test hook) ------------------------ //

    Vector2 ReadMove() => useExternalInput
        ? externalMove
        : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

    bool ReadRun() => useExternalInput ? externalRun : Input.GetKey(KeyCode.LeftShift);

    // Swimming: Space rises, Shift dives (Shift is unused for running underwater).
    float ReadSwimVertical()
    {
        if (useExternalInput) return Mathf.Clamp(externalSwimVertical, -1f, 1f);
        float v = 0f;
        if (Input.GetKey(KeyCode.Space))     v += 1f;
        if (Input.GetKey(KeyCode.LeftShift)) v -= 1f;
        return v;
    }

    bool ReadJump()
    {
        if (useExternalInput)
        {
            bool j = externalJump;
            externalJump = false;   // consume so one request = one jump
            return j;
        }
        return Input.GetKeyDown(KeyCode.Space);
    }
}
