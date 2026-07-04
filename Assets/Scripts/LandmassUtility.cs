using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WILDCUT — single-landmass guarantee ("Ensure Single Landmass" pipeline step).
/// Flood-fills the above-water cells of a heightmap into connected components.
/// The largest component is the island; every other fragment is either
///  • bridged: a low sandbar (just above water level) is raised along the
///    shortest line between the fragment's and the main island's coastlines
///    (looks like a natural sand spit), or
///  • sunk: fragments too small to deserve a bridge are pushed below water.
/// Result: every seed/archetype variant yields exactly ONE connected island.
/// (A real archipelago will later be its own MapConcept, not an accident.)
/// </summary>
public static class LandmassUtility
{
    public static string EnsureSingleLandmass(float[,] map, float waterLevel, int bridgeRadiusCells)
    {
        int w = map.GetLength(0);
        int h = map.GetLength(1);

        List<List<Vector2Int>> parts = FindLandmasses(map, waterLevel, w, h);
        if (parts.Count <= 1)
            return $"Landmass check: {parts.Count} landmass detected — already single, no action.";

        parts.Sort((a, b) => b.Count.CompareTo(a.Count));
        List<Vector2Int> main = parts[0];

        int totalLand = 0;
        foreach (var p in parts) totalLand += p.Count;
        int minBridgeSize = Mathf.Max(12, totalLand / 200);   // <0.5% of land = islet → sink

        int bridged = 0, sunk = 0;
        for (int i = 1; i < parts.Count; i++)
        {
            List<Vector2Int> frag = parts[i];
            if (frag.Count < minBridgeSize)
            {
                foreach (Vector2Int c in frag)
                    map[c.x, c.y] = Mathf.Min(map[c.x, c.y], waterLevel - 0.03f);
                sunk++;
            }
            else
            {
                BuildSandbar(map, waterLevel, main, frag, bridgeRadiusCells, w, h);
                main.AddRange(frag);   // fragment now belongs to the island for later bridges
                bridged++;
            }
        }

        int after = FindLandmasses(map, waterLevel, w, h).Count;
        return $"Landmass check: {parts.Count} fragments detected → bridged {bridged}, " +
               $"sank {sunk} islet(s); now {after} landmass.";
    }

    // ------------------------------------------------------------------ //

    static List<List<Vector2Int>> FindLandmasses(float[,] map, float waterLevel, int w, int h)
    {
        var parts   = new List<List<Vector2Int>>();
        var visited = new bool[w, h];
        var queue   = new Queue<Vector2Int>();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (visited[x, y] || map[x, y] < waterLevel) continue;

                var cells = new List<Vector2Int>();
                visited[x, y] = true;
                queue.Enqueue(new Vector2Int(x, y));

                while (queue.Count > 0)
                {
                    Vector2Int c = queue.Dequeue();
                    cells.Add(c);
                    TryVisit(c.x - 1, c.y);
                    TryVisit(c.x + 1, c.y);
                    TryVisit(c.x, c.y - 1);
                    TryVisit(c.x, c.y + 1);
                }
                parts.Add(cells);

                void TryVisit(int cx, int cy)
                {
                    if (cx < 0 || cy < 0 || cx >= w || cy >= h) return;
                    if (visited[cx, cy] || map[cx, cy] < waterLevel) return;
                    visited[cx, cy] = true;
                    queue.Enqueue(new Vector2Int(cx, cy));
                }
            }
        }
        return parts;
    }

    static void BuildSandbar(float[,] map, float waterLevel,
                             List<Vector2Int> main, List<Vector2Int> frag,
                             int radius, int w, int h)
    {
        List<Vector2Int> mainCoast = CoastCells(map, waterLevel, main, w, h);
        List<Vector2Int> fragCoast = CoastCells(map, waterLevel, frag, w, h);
        if (mainCoast.Count == 0 || fragCoast.Count == 0) return;

        // Closest coast-to-coast pair (subsampled so huge coastlines stay cheap).
        int stepM = Mathf.Max(1, mainCoast.Count / 4000);
        int stepF = Mathf.Max(1, fragCoast.Count / 4000);
        float bestSq = float.MaxValue;
        Vector2Int a = mainCoast[0], b = fragCoast[0];
        for (int i = 0; i < mainCoast.Count; i += stepM)
            for (int j = 0; j < fragCoast.Count; j += stepF)
            {
                float d = (mainCoast[i] - fragCoast[j]).sqrMagnitude;
                if (d < bestSq) { bestSq = d; a = mainCoast[i]; b = fragCoast[j]; }
            }

        // Raise a sandbar along the line: highest at the spine (~water+0.05, the Sand
        // colour band), tapering toward the sides so it reads as a natural spit.
        int steps = Mathf.CeilToInt(Mathf.Sqrt(bestSq)) * 2 + 2;
        for (int s = 0; s <= steps; s++)
        {
            Vector2 p = Vector2.Lerp(new Vector2(a.x, a.y), new Vector2(b.x, b.y), s / (float)steps);
            int px = Mathf.RoundToInt(p.x);
            int py = Mathf.RoundToInt(p.y);

            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int cx = px + dx, cy = py + dy;
                    if (cx < 1 || cy < 1 || cx >= w - 1 || cy >= h - 1) continue;
                    float distNorm = Mathf.Sqrt(dx * dx + dy * dy) / (radius + 1f);
                    if (distNorm > 1f) continue;

                    float target = waterLevel + Mathf.Lerp(0.05f, 0.015f, distNorm);
                    if (map[cx, cy] < target) map[cx, cy] = target;
                }
        }
    }

    // Land cells that touch water — the coastline of a component.
    static List<Vector2Int> CoastCells(float[,] map, float waterLevel,
                                       List<Vector2Int> cells, int w, int h)
    {
        var coast = new List<Vector2Int>();
        foreach (Vector2Int c in cells)
        {
            bool edge =
                (c.x > 0     && map[c.x - 1, c.y] < waterLevel) ||
                (c.x < w - 1 && map[c.x + 1, c.y] < waterLevel) ||
                (c.y > 0     && map[c.x, c.y - 1] < waterLevel) ||
                (c.y < h - 1 && map[c.x, c.y + 1] < waterLevel);
            if (edge) coast.Add(c);
        }
        return coast;
    }
}
