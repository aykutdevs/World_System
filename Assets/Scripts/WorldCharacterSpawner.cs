using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Places the player and the yamyam on sensible dry ground after every island
/// generation. Runs as the last pipeline step in MapGenerator.GenerateMap()
/// (after the NavMesh bake, so the enemy can be snapped onto the fresh mesh).
/// Player start: centre of the first settlement zone (flat, dry, reserved);
/// falls back to any NavMesh point if no zone was found this island.
/// </summary>
public class WorldCharacterSpawner : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    public PlayerController player;
    public EnemyAI enemy;
    public SettlementZoneFinder zones;

    [Header("Placement")]
    [Tooltip("Spawn height above the ground hit point (lets the controller settle).")]
    public float dropHeight = 1.5f;
    [Tooltip("Enemy starts this far from the player (min..max, world units).")]
    public Vector2 enemyDistanceRange = new Vector2(60f, 130f);

    public void PlaceCharacters()
    {
        if (player == null) player = FindObjectOfType<PlayerController>();
        if (enemy == null) enemy = FindObjectOfType<EnemyAI>();
        if (zones == null) zones = FindObjectOfType<SettlementZoneFinder>();

        if (!TryGetPlayerSpawn(out Vector3 playerPos))
        {
            Debug.LogWarning("[WorldCharacterSpawner] No valid player spawn found — characters not moved.");
            return;
        }

        if (player != null)
        {
            Vector3 spawn = playerPos + Vector3.up * dropHeight;
            if (Application.isPlaying) player.SetSpawn(spawn);
            else player.transform.position = spawn;
            Debug.Log($"[WorldCharacterSpawner] Player spawned at {spawn}.");
        }

        if (enemy != null && TryGetEnemySpawn(playerPos, out Vector3 enemyPos))
        {
            enemy.PlaceAt(enemyPos);
            Debug.Log($"[WorldCharacterSpawner] Yamyam spawned at {enemyPos} " +
                      $"({Vector3.Distance(playerPos, enemyPos):F0} units from player).");
        }
    }

    // ------------------------------------------------------------------ //

    bool TryGetPlayerSpawn(out Vector3 pos)
    {
        // Preferred: first settlement zone — guaranteed flat, dry, off the beach.
        if (zones != null && zones.zones.Count > 0)
        {
            pos = zones.zones[0].center;
            if (SnapToGround(ref pos)) return true;
        }

        // Fallback: any point on the baked NavMesh near the island centre.
        if (NavMesh.SamplePosition(new Vector3(0f, 60f, 0f), out NavMeshHit hit, 400f, NavMesh.AllAreas))
        {
            pos = hit.position;
            return true;
        }

        pos = Vector3.zero;
        return false;
    }

    bool TryGetEnemySpawn(Vector3 playerPos, out Vector3 pos)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector2 dir  = Random.insideUnitCircle.normalized;
            float   dist = Random.Range(enemyDistanceRange.x, enemyDistanceRange.y);
            Vector3 candidate = playerPos + new Vector3(dir.x, 0f, dir.y) * dist;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 25f, NavMesh.AllAreas))
            {
                pos = hit.position;
                return true;
            }
        }

        // Last resort: anywhere on the NavMesh at least min distance away.
        if (NavMesh.SamplePosition(playerPos + Vector3.forward * enemyDistanceRange.x,
                                   out NavMeshHit far, 200f, NavMesh.AllAreas))
        {
            pos = far.position;
            return true;
        }

        pos = Vector3.zero;
        return false;
    }

    static bool SnapToGround(ref Vector3 pos)
    {
        Vector3 origin = pos + Vector3.up * 500f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1000f) &&
            hit.collider is MeshCollider)
        {
            pos = hit.point;
            return true;
        }
        return false;
    }
}
