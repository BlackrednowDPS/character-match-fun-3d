using System;
using System.Collections.Generic;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Receives lifecycle notifications from an object pool.</summary>
    public interface IPoolable
    {
        /// <summary>Resets transient state immediately before an object is activated.</summary>
        void OnRentFromPool();
        /// <summary>Resets transient state immediately before an object is deactivated.</summary>
        void OnReturnToPool();
    }

    /// <summary>Provides allocation-free reuse of a fixed, pre-warmed object set.</summary>
    public interface IObjectPool<T> where T : class
    {
        /// <summary>Gets an inactive object, or null when the pre-warmed pool is exhausted.</summary>
        T Get();
        /// <summary>Returns an object to the pool; callers must not retain it as active state.</summary>
        void Return(T item);
        /// <summary>Creates the requested fixed capacity during initialization, never in gameplay hot paths.</summary>
        void Prewarm(int capacity);
        /// <summary>Gets the total number of objects created during pre-warming.</summary>
        int Capacity { get; }
        /// <summary>Gets the number of objects currently available.</summary>
        int AvailableCount { get; }
    }

    /// <summary>Unity component pool that instantiates only while explicitly pre-warming.</summary>
    public class ComponentObjectPool<T> : MonoBehaviour, IObjectPool<T> where T : Component, IPoolable
    {
        [SerializeField] private T prefab;
        [SerializeField, Min(0)] private int prewarmCount;
        [SerializeField] private Transform poolRoot;

        private Stack<T> available;
        private int capacity;

        /// <summary>Gets the total number of component instances created by pre-warming.</summary>
        public int Capacity => capacity;
        /// <summary>Gets the number of inactive component instances available without allocation.</summary>
        public int AvailableCount => available == null ? 0 : available.Count;

        /// <summary>Prepares the backing collection and optional inspector-defined capacity.</summary>
        protected virtual void Awake()
        {
            if (available == null)
            {
                available = new Stack<T>(Mathf.Max(1, prewarmCount));
            }
            if (prewarmCount > 0)
            {
                Prewarm(prewarmCount);
            }
        }

        /// <summary>Creates instances during initialization; calling this during active play may hitch.</summary>
        public void Prewarm(int requestedCapacity)
        {
            if (requestedCapacity <= capacity || prefab == null)
            {
                return;
            }

            if (available == null)
            {
                available = new Stack<T>(requestedCapacity);
            }

            Transform parent = poolRoot != null ? poolRoot : transform;
            while (capacity < requestedCapacity)
            {
                T instance = Instantiate(prefab, parent);
                instance.gameObject.SetActive(false);
                available.Push(instance);
                capacity++;
            }
        }

        /// <summary>Gets and activates a pre-warmed instance, or null when exhausted.</summary>
        public T Get()
        {
            if (available == null || available.Count == 0)
            {
                return null;
            }

            T item = available.Pop();
            item.OnRentFromPool();
            item.gameObject.SetActive(true);
            return item;
        }

        /// <summary>Resets, reparents, and deactivates a component for later reuse.</summary>
        public void Return(T item)
        {
            if (item == null || !item.gameObject.activeSelf)
            {
                return;
            }

            item.OnReturnToPool();
            item.transform.SetParent(poolRoot != null ? poolRoot : transform, false);
            item.gameObject.SetActive(false);
            available.Push(item);
        }
    }

    /// <summary>Concrete inspector-attachable pool for egg pieces.</summary>
    public sealed class EggPiecePool : ComponentObjectPool<EggPiece> { }
}
