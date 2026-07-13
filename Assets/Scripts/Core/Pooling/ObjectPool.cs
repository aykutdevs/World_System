using System;
using System.Collections.Generic;

// ---- Core.Pooling — object pooling (reusable across projects) ----
// ObjectPool<T> is pure C# (no UnityEngine); GameObjectPool (separate file)
// is the prefab-based Unity flavour.

namespace Core.Pooling
{
    /// <summary>
    /// Minimal generic pool: Get() pops a recycled instance or creates one via
    /// the factory, Release() returns it. Optional onGet/onRelease callbacks
    /// normalize instance state (reset, enable/disable, clear buffers...).
    /// </summary>
    public class ObjectPool<T> where T : class
    {
        readonly Func<T>   factory;
        readonly Action<T> onGet;
        readonly Action<T> onRelease;
        readonly Stack<T>  inactive = new Stack<T>();

        public int CountInactive => inactive.Count;

        public ObjectPool(Func<T> factory, Action<T> onGet = null, Action<T> onRelease = null,
                          int prewarmCount = 0)
        {
            this.factory   = factory ?? throw new ArgumentNullException(nameof(factory));
            this.onGet     = onGet;
            this.onRelease = onRelease;
            for (int i = 0; i < prewarmCount; i++)
                Release(factory());
        }

        public T Get()
        {
            T item = inactive.Count > 0 ? inactive.Pop() : factory();
            onGet?.Invoke(item);
            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;
            onRelease?.Invoke(item);
            inactive.Push(item);
        }
    }
}
