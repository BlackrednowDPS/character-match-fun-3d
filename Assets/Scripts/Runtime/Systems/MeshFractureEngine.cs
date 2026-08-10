using System;
using System.Collections;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Identifies the pre-fractured mesh variant selected for an impact.</summary>
    public enum FractureSeverity
    {
        /// <summary>Uses the four-to-six shard light variant.</summary>
        Light,
        /// <summary>Uses the eight-to-twelve shard medium variant.</summary>
        Medium,
        /// <summary>Uses the sixteen-to-twenty shard shatter variant.</summary>
        Shatter
    }

    /// <summary>Pooled rigid shard with cached renderer state for allocation-free fades.</summary>
    public sealed class FractureShard : MonoBehaviour, IPoolable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MeshRenderer[] renderers;
        private MaterialPropertyBlock[] propertyBlocks;
        private Color[] baseColors;

        /// <summary>Gets the cached Rigidbody used for explosion impulses.</summary>
        public Rigidbody Body { get; private set; }

        internal FractureShardPool OwnerPool { get; set; }

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            renderers = GetComponentsInChildren<MeshRenderer>(true);
            propertyBlocks = new MaterialPropertyBlock[renderers.Length];
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                propertyBlocks[i] = new MaterialPropertyBlock();
                Material shared = renderers[i].sharedMaterial;
                if (shared != null && shared.HasProperty(BaseColorId))
                {
                    baseColors[i] = shared.GetColor(BaseColorId);
                }
                else if (shared != null && shared.HasProperty(ColorId))
                {
                    baseColors[i] = shared.GetColor(ColorId);
                }
                else
                {
                    baseColors[i] = Color.white;
                }
            }
        }

        /// <summary>Resets visual and physics state before pool activation.</summary>
        public void OnRentFromPool()
        {
            SetFade(1f);
            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.isKinematic = false;
            }
        }

        /// <summary>Clears transient physics state before the shard is deactivated.</summary>
        public void OnReturnToPool()
        {
            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.isKinematic = true;
            }
            SetFade(1f);
            OwnerPool = null;
        }

        /// <summary>Applies alpha through cached MaterialPropertyBlocks without material instantiation.</summary>
        public void SetFade(float alpha)
        {
            float clampedAlpha = Mathf.Clamp01(alpha);
            for (int i = 0; i < renderers.Length; i++)
            {
                Color color = baseColors[i];
                color.a *= clampedAlpha;
                MaterialPropertyBlock block = propertyBlocks[i];
                block.Clear();
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                renderers[i].SetPropertyBlock(block);
            }
        }
    }

    /// <summary>Inspector-attachable pre-warmed shard pool.</summary>
    public sealed class FractureShardPool : ComponentObjectPool<FractureShard> { }

    /// <summary>Maps an archetype and severity to a fixed shard pool and spawn budget.</summary>
    [Serializable]
    public sealed class FracturePoolBinding
    {
        [SerializeField] private string archetypeName;
        [SerializeField] private FractureSeverity severity;
        [SerializeField] private FractureShardPool pool;
        [SerializeField, Range(1, 20)] private int shardsPerEvent = 6;

        /// <summary>Gets the egg archetype name associated with this pool.</summary>
        public string ArchetypeName => archetypeName;
        /// <summary>Gets the impact severity associated with this pool.</summary>
        public FractureSeverity Severity => severity;
        /// <summary>Gets the component pool containing the variant's shards.</summary>
        public FractureShardPool Pool => pool;
        /// <summary>Gets the maximum number of shards rented for one event.</summary>
        public int ShardsPerEvent => shardsPerEvent;
    }

    /// <summary>Selects pre-fractured variants, applies impulses, fades shards, and enforces the six-event budget.</summary>
    public sealed class MeshFractureEngine : MonoBehaviour
    {
        [SerializeField] private int maxConcurrentFractureEvents = 6;
        [SerializeField] private float shardLifetimeSeconds = 1.4f;
        [SerializeField] private float shardFadeStartSeconds = 1.1f;
        [SerializeField, Range(1, 20)] private int maxShardsPerEvent = 20;
        [SerializeField] private FracturePoolBinding[] fracturePools;

        private ActiveFractureEvent[] activeFractureEvents;
        private int activeEventCount;
        private int spawnSlotIndex = -1;

        private sealed class ActiveFractureEvent
        {
            internal FractureShard[] Shards;
            internal Coroutine Routine;
            internal float StartedAt;
            internal bool Active;
        }

        private void Awake()
        {
            maxConcurrentFractureEvents = Mathf.Max(1, maxConcurrentFractureEvents);
            maxShardsPerEvent = Mathf.Clamp(maxShardsPerEvent, 1, 20);
            activeFractureEvents = new ActiveFractureEvent[maxConcurrentFractureEvents];
            for (int i = 0; i < activeFractureEvents.Length; i++)
            {
                activeFractureEvents[i] = new ActiveFractureEvent
                {
                    Shards = new FractureShard[maxShardsPerEvent]
                };
            }

            if (fracturePools == null)
            {
                return;
            }

            for (int i = 0; i < fracturePools.Length; i++)
            {
                FracturePoolBinding binding = fracturePools[i];
                if (binding != null && binding.Pool != null)
                {
                    binding.Pool.Prewarm(binding.ShardsPerEvent * maxConcurrentFractureEvents);
                }
            }
        }

        /// <summary>Spawns a force-selected pooled fracture event, culling the oldest event at the concurrency cap.</summary>
        public void TriggerFracture(string archetypeName, Vector3 worldPos, float normalizedImpactForce)
        {
            if (activeFractureEvents == null || string.IsNullOrEmpty(archetypeName))
            {
                return;
            }

            if (activeEventCount >= activeFractureEvents.Length)
            {
                CullOldestEvent();
            }

            spawnSlotIndex = FindFreeEventSlot();
            if (spawnSlotIndex < 0)
            {
                return;
            }

            float impact = Mathf.Clamp01(normalizedImpactForce);
            FractureSeverity severity = impact < 0.35f
                ? FractureSeverity.Light
                : impact < 0.7f ? FractureSeverity.Medium : FractureSeverity.Shatter;
            FractureShard[] shards = SpawnShardsFromPool(archetypeName, severity, worldPos);
            if (!ContainsShard(shards))
            {
                spawnSlotIndex = -1;
                return;
            }

            ActiveFractureEvent activeEvent = activeFractureEvents[spawnSlotIndex];
            activeEvent.Active = true;
            activeEvent.StartedAt = Time.unscaledTime;
            activeEventCount++;
            ApplyExplosionImpulse(shards, worldPos, impact);
            activeEvent.Routine = StartCoroutine(DespawnAfterLifetime(shards));
            spawnSlotIndex = -1;
        }

        private FractureShard[] SpawnShardsFromPool(string archetypeName, FractureSeverity severity, Vector3 worldPos)
        {
            if (spawnSlotIndex < 0 || fracturePools == null)
            {
                return null;
            }

            ActiveFractureEvent activeEvent = activeFractureEvents[spawnSlotIndex];
            ClearShardReferences(activeEvent.Shards);
            FracturePoolBinding binding = FindBinding(archetypeName, severity);
            if (binding == null || binding.Pool == null)
            {
                return activeEvent.Shards;
            }

            int count = Mathf.Min(binding.ShardsPerEvent, activeEvent.Shards.Length);
            for (int i = 0; i < count; i++)
            {
                FractureShard shard = binding.Pool.Get();
                if (shard == null)
                {
                    break;
                }

                shard.OwnerPool = binding.Pool;
                shard.transform.SetParent(transform, true);
                shard.transform.position = worldPos + UnityEngine.Random.insideUnitSphere * 0.08f;
                shard.transform.rotation = UnityEngine.Random.rotation;
                activeEvent.Shards[i] = shard;
            }
            return activeEvent.Shards;
        }

        private void ApplyExplosionImpulse(FractureShard[] shards, Vector3 origin, float force)
        {
            if (shards == null)
            {
                return;
            }

            float impulse = Mathf.Lerp(2.5f, 11f, Mathf.Clamp01(force));
            for (int i = 0; i < shards.Length; i++)
            {
                FractureShard shard = shards[i];
                if (shard != null && shard.Body != null)
                {
                    shard.Body.AddExplosionForce(impulse, origin - Vector3.up * 0.05f, 1.8f, 0.15f,
                        ForceMode.Impulse);
                }
            }
        }

        private IEnumerator DespawnAfterLifetime(FractureShard[] shards)
        {
            float elapsed = 0f;
            float lifetime = Mathf.Max(0.01f, shardLifetimeSeconds);
            float fadeStart = Mathf.Clamp(shardFadeStartSeconds, 0f, lifetime);
            float fadeDuration = Mathf.Max(0.01f, lifetime - fadeStart);
            while (elapsed < lifetime)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= fadeStart)
                {
                    float alpha = 1f - Mathf.Clamp01((elapsed - fadeStart) / fadeDuration);
                    for (int i = 0; i < shards.Length; i++)
                    {
                        if (shards[i] != null)
                        {
                            shards[i].SetFade(alpha);
                        }
                    }
                }
                yield return null;
            }

            ReturnShards(shards);
            CompleteEvent(shards);
        }

        private FracturePoolBinding FindBinding(string archetypeName, FractureSeverity severity)
        {
            for (int i = 0; i < fracturePools.Length; i++)
            {
                FracturePoolBinding binding = fracturePools[i];
                if (binding != null && binding.Severity == severity &&
                    string.Equals(binding.ArchetypeName, archetypeName, StringComparison.OrdinalIgnoreCase))
                {
                    return binding;
                }
            }
            return null;
        }

        private int FindFreeEventSlot()
        {
            for (int i = 0; i < activeFractureEvents.Length; i++)
            {
                if (!activeFractureEvents[i].Active)
                {
                    return i;
                }
            }
            return -1;
        }

        private void CullOldestEvent()
        {
            int oldest = -1;
            float oldestTime = float.MaxValue;
            for (int i = 0; i < activeFractureEvents.Length; i++)
            {
                ActiveFractureEvent activeEvent = activeFractureEvents[i];
                if (activeEvent.Active && activeEvent.StartedAt < oldestTime)
                {
                    oldest = i;
                    oldestTime = activeEvent.StartedAt;
                }
            }

            if (oldest < 0)
            {
                return;
            }

            ActiveFractureEvent oldestEvent = activeFractureEvents[oldest];
            if (oldestEvent.Routine != null)
            {
                StopCoroutine(oldestEvent.Routine);
            }
            ReturnShards(oldestEvent.Shards);
            oldestEvent.Routine = null;
            oldestEvent.Active = false;
            activeEventCount--;
        }

        private void CompleteEvent(FractureShard[] shards)
        {
            for (int i = 0; i < activeFractureEvents.Length; i++)
            {
                ActiveFractureEvent activeEvent = activeFractureEvents[i];
                if (ReferenceEquals(activeEvent.Shards, shards))
                {
                    activeEvent.Routine = null;
                    if (activeEvent.Active)
                    {
                        activeEvent.Active = false;
                        activeEventCount--;
                    }
                    return;
                }
            }
        }

        private static void ReturnShards(FractureShard[] shards)
        {
            for (int i = 0; i < shards.Length; i++)
            {
                FractureShard shard = shards[i];
                if (shard == null)
                {
                    continue;
                }
                FractureShardPool owner = shard.OwnerPool;
                if (owner != null)
                {
                    owner.Return(shard);
                }
                shards[i] = null;
            }
        }

        private static void ClearShardReferences(FractureShard[] shards)
        {
            for (int i = 0; i < shards.Length; i++)
            {
                shards[i] = null;
            }
        }

        private static bool ContainsShard(FractureShard[] shards)
        {
            if (shards == null)
            {
                return false;
            }
            for (int i = 0; i < shards.Length; i++)
            {
                if (shards[i] != null)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
