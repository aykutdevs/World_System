using UnityEngine;
using UnityEngine.AI;
using Core.FSM;

// ---- WILDCUT — Yamyam (cannibal) enemy prototype ----
//
// IMPORTANT: This is NOT machine learning. It is a scripted state machine
// (Patrol / Chase / Search) whose variety comes from fixed rules + runtime
// randomness: predictive interception of the player's projected position,
// randomly timed re-pathing with lateral (flanking) offsets, and truly random
// search patterns. It *reads* as smart; it does not learn anything.
//
// Locomotion: NavMeshAgent only — the yamyam cannot climb steep slopes the
// NavMesh excludes, so climbing a mountain is the player's tactical escape.
// All destination requests funnel through MoveTo(); replacing that single
// method with a climbing-capable mover is how option (a) would be added later.

public enum EnemyState { Patrol, Chase, Search }

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    [Header("Perception")]
    [Tooltip("Player inside this range triggers the chase.")]
    public float detectionRange = 18f;
    [Tooltip("Beyond this range the player counts as out of sight while chasing.")]
    public float loseSightRange = 32f;
    [Tooltip("Seconds without sight before giving up the chase and searching.")]
    public float lostSightDuration = 6f;

    [Header("Speeds")]
    public float patrolSpeed = 4f;
    [Tooltip("Kept below the player's run speed (6 × 1.8 = 10.8) so sprinting escapes, walking doesn't.")]
    public float chaseSpeed = 9.4f;

    [Header("Patrol")]
    public float patrolRadius = 70f;
    public Vector2 patrolWaitRange = new Vector2(1.5f, 4f);

    [Header("Chase variety (scripted, not learned)")]
    [Tooltip("Seconds ahead the player's velocity is projected for interception.")]
    public float predictionTime = 1.6f;
    [Tooltip("Random seconds between re-paths — breaks the straight-line pursuit.")]
    public Vector2 repathInterval = new Vector2(2f, 4f);
    [Tooltip("Max random sideways offset on the intercept point (soft flanking).")]
    public float flankOffset = 6f;

    [Header("Search")]
    public int searchPointCount = 3;
    public float searchRadius = 25f;

    [Header("Catch")]
    public float catchDistance = 2.2f;

    [Header("World events (Chapters 9-10 — documentary hooks)")]
    [Tooltip("Optional — chase start is broadcast here (the documentary camera " +
             "will subscribe to the same channel later).")]
    public WorldEventChannel eventChannel;
    public string chaseStartEventId = "yamyam_chase_start";

    [Header("Debug")]
    [Tooltip("Log every state transition (Core.FSM transition log).")]
    public bool logStateTransitions;

    public EnemyState State => fsm != null ? fsm.CurrentId : EnemyState.Patrol;
    /// <summary>Exposed for tests: last destination requested from the NavMesh.</summary>
    public Vector3 LastMoveTarget { get; private set; }

    NavMeshAgent agent;
    PlayerController player;
    StateMachine<EnemyState> fsm;
    float distToPlayer;          // per-frame snapshot the state ticks read
    Vector3 home;
    Vector3 lastKnownPos;
    float lastSeenTime;
    float nextRepathTime;
    float waitUntil;
    float nextCatchLogTime;
    int searchPointsLeft;

    // ------------------------------------------------------------------ //

    // States wrap the existing per-state methods; Chase/Search entry setup
    // (previously the EnterChase/EnterSearch prologues) runs via onEnter.
    void EnsureStateMachine()
    {
        if (fsm != null) return;
        fsm = new StateMachine<EnemyState>();
        fsm.Add(EnemyState.Patrol, new DelegateState(onTick: _ => PatrolUpdate(distToPlayer)));
        fsm.Add(EnemyState.Chase,  new DelegateState(onEnter: OnEnterChase, onTick: _ => ChaseUpdate(distToPlayer)));
        fsm.Add(EnemyState.Search, new DelegateState(onEnter: OnEnterSearch, onTick: _ => SearchUpdate(distToPlayer)));
        fsm.OnTransition += (from, to) =>
        {
            if (logStateTransitions) Debug.Log($"[EnemyAI] State {from} -> {to}");
        };
        fsm.SetState(EnemyState.Patrol);
    }

    void Awake()
    {
        EnsureStateMachine();
        agent = GetComponent<NavMeshAgent>();
        agent.height       = 2f;
        agent.radius       = 0.5f;
        agent.acceleration = 24f;
        agent.angularSpeed = 480f;
    }

    void Start()
    {
        home = transform.position;
        if (!agent.isOnNavMesh &&
            NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 40f, NavMesh.AllAreas))
            agent.Warp(hit.position);
    }

    /// <summary>Called by WorldCharacterSpawner after each island generation.</summary>
    public void PlaceAt(Vector3 pos)
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (Application.isPlaying && agent.isActiveAndEnabled)
            agent.Warp(pos);
        else
            transform.position = pos;
        home = pos;
        EnsureStateMachine();
        fsm.SetState(EnemyState.Patrol);
    }

    // ------------------------------------------------------------------ //

    void Update()
    {
        if (!agent.isOnNavMesh) return;

        if (player == null)
        {
            player = FindObjectOfType<PlayerController>();
            if (player == null) return;
        }

        distToPlayer = Vector3.Distance(transform.position, player.transform.position);
        fsm.Tick(Time.deltaTime);
    }

    // ---- Patrol -------------------------------------------------------- //

    void PatrolUpdate(float dist)
    {
        agent.speed = patrolSpeed;

        if (dist <= detectionRange) { fsm.SetState(EnemyState.Chase); return; }

        if (agent.pathPending || agent.remainingDistance > 1.5f) return;

        if (waitUntil <= 0f)
        {
            // Arrived — idle a moment before wandering on.
            waitUntil = Time.time + Random.Range(patrolWaitRange.x, patrolWaitRange.y);
        }
        else if (Time.time >= waitUntil)
        {
            waitUntil = 0f;
            MoveTo(RandomPointAround(home, patrolRadius));
        }
    }

    // ---- Chase --------------------------------------------------------- //

    void OnEnterChase()
    {
        lastSeenTime   = Time.time;
        lastKnownPos   = player.transform.position;
        nextRepathTime = 0f;   // re-path immediately

        if (eventChannel != null && !string.IsNullOrEmpty(chaseStartEventId))
            eventChannel.Raise(chaseStartEventId, transform.position);
    }

    void ChaseUpdate(float dist)
    {
        agent.speed = chaseSpeed;

        if (dist <= loseSightRange)
        {
            lastSeenTime = Time.time;
            lastKnownPos = player.transform.position;
        }

        if (dist <= catchDistance && Time.time >= nextCatchLogTime)
        {
            nextCatchLogTime = Time.time + 2f;
            Debug.Log("[EnemyAI] Yamyam caught the player!");
        }

        if (Time.time - lastSeenTime > lostSightDuration) { fsm.SetState(EnemyState.Search); return; }

        if (Time.time >= nextRepathTime)
        {
            // Close quarters: drop the fancy behaviour, re-path fast and aim straight
            // at the player, otherwise the flank offset keeps orbiting out of reach.
            bool closeQuarters = dist < 8f;
            nextRepathTime = Time.time + (closeQuarters
                ? 0.4f
                : Random.Range(repathInterval.x, repathInterval.y));

            // Predictive interception: aim where the player is HEADING, not where
            // they are — plus a random sideways nudge so pursuit lines vary.
            // Both fade out as the distance closes (full effect beyond ~20 units).
            float rangeFactor = Mathf.Clamp01((dist - 4f) / 16f);

            Vector3 vel = player.Velocity; vel.y = 0f;
            Vector3 predicted = player.transform.position + vel * predictionTime * rangeFactor;

            Vector3 toTarget = predicted - transform.position;
            toTarget.y = 0f;
            Vector3 lateral = Vector3.Cross(Vector3.up, toTarget.normalized)
                              * Random.Range(-flankOffset, flankOffset) * rangeFactor;

            MoveTo(predicted + lateral);
        }
    }

    // ---- Search -------------------------------------------------------- //

    void OnEnterSearch()
    {
        searchPointsLeft = searchPointCount;
        MoveTo(lastKnownPos);   // first stop: where the player was last seen
    }

    void SearchUpdate(float dist)
    {
        agent.speed = patrolSpeed * 1.4f;

        if (dist <= detectionRange) { fsm.SetState(EnemyState.Chase); return; }

        if (agent.pathPending || agent.remainingDistance > 2f) return;

        if (searchPointsLeft > 0)
        {
            searchPointsLeft--;
            // UnityEngine.Random — real per-session randomness, deliberately NOT
            // the island seed, so every playthrough gets a different search sweep.
            MoveTo(RandomPointAround(lastKnownPos, searchRadius));
        }
        else
        {
            home      = transform.position;   // adopt the area as the new patrol centre
            waitUntil = 0f;
            fsm.SetState(EnemyState.Patrol);
        }
    }

    // ---- Locomotion (single choke point — swap for a climbing mover to get option (a)) ---- //

    void MoveTo(Vector3 worldPos)
    {
        if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, 14f, NavMesh.AllAreas))
        {
            LastMoveTarget = hit.position;
            agent.SetDestination(hit.position);
        }
    }

    Vector3 RandomPointAround(Vector3 center, float radius)
    {
        Vector2 r = Random.insideUnitCircle * radius;
        return center + new Vector3(r.x, 0f, r.y);
    }
}
