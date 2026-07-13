using System.Collections.Generic;
using UnityEngine;

namespace Core.Pooling
{
    /// <summary>
    /// Prefab-based pool: Get() activates a recycled instance (or instantiates
    /// a new one), Release() deactivates and shelves it. Instances live under
    /// an optional parent transform to keep the hierarchy tidy. Depends only
    /// on UnityEngine — no game code.
    /// </summary>
    public class GameObjectPool
    {
        readonly GameObject prefab;
        readonly Transform  parent;
        readonly Stack<GameObject> inactive = new Stack<GameObject>();

        public int CountInactive => inactive.Count;

        /// <param name="prewarmCount">Instances created up-front (spawn spikes avoided).</param>
        public GameObjectPool(GameObject prefab, int prewarmCount = 0, Transform parent = null)
        {
            this.prefab = prefab;
            this.parent = parent;
            for (int i = 0; i < prewarmCount; i++)
                Release(Object.Instantiate(prefab, parent));
        }

        public GameObject Get(Vector3 position, Quaternion rotation)
        {
            GameObject go = inactive.Count > 0
                ? inactive.Pop()
                : Object.Instantiate(prefab, parent);
            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null) return;   // destroyed externally (scene unload) — drop it
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            inactive.Push(go);
        }
    }
}
