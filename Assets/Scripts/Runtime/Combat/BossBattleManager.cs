using System;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Identifies a boss collision region used by pure damage modifiers.</summary>
    public enum HitZone
    {
        /// <summary>Standard body region.</summary>
        Body,
        /// <summary>Armored shell region.</summary>
        Shell,
        /// <summary>Exposed high-damage region.</summary>
        WeakPoint
    }

    /// <summary>Identifies the board operation authored by a counter-attack asset.</summary>
    public enum CounterAttackType
    {
        /// <summary>Shuffles a percentage of board cells.</summary>
        WhiskScramble,
        /// <summary>Locks random tiles for a number of turns.</summary>
        HotOilLock,
        /// <summary>Marks random timed hazard tiles.</summary>
        FryingPanBurn
    }

    /// <summary>Serializable HP threshold, hit-zone, and pacing data for one boss phase.</summary>
    [Serializable]
    public sealed class BossPhase
    {
        [SerializeField, Range(0f, 1f)] private float healthThreshold = 1f;
        [SerializeField, Min(1f)] private float enrageTimerSeconds = 90f;
        [SerializeField] private HitZone activeHitZone = HitZone.Body;

        /// <summary>Gets the normalized HP threshold at which this phase becomes active.</summary>
        public float HealthThreshold => healthThreshold;
        /// <summary>Gets the pacing timer that accelerates a counter attack without causing instant failure.</summary>
        public float EnrageTimerSeconds => enrageTimerSeconds;
        /// <summary>Gets the damage hit zone exposed during this phase.</summary>
        public HitZone ActiveHitZone => activeHitZone;
    }

    /// <summary>Data-authored boss counter attack and its tunable board effect.</summary>
    [CreateAssetMenu(menuName = "CMF3D/Boss Counter Attack", fileName = "CounterAttack")]
    public sealed class CounterAttackSO : ScriptableObject
    {
        [SerializeField] private CounterAttackType attackType;
        [SerializeField, Min(1f)] private float cooldownSeconds = 15f;
        [SerializeField, Min(0)] private int minimumPhaseIndex;
        [SerializeField, Range(0f, 1f)] private float scramblePercentage = 0.4f;
        [SerializeField, Min(0)] private int tileCount = 3;
        [SerializeField, Min(1)] private int turnDuration = 2;
        [SerializeField, Min(0f)] private float hazardDurationSeconds = 8f;

        /// <summary>Gets the board effect type.</summary>
        public CounterAttackType AttackType => attackType;
        /// <summary>Gets the interval between executions.</summary>
        public float CooldownSeconds => cooldownSeconds;
        /// <summary>Gets the earliest zero-based phase in which this counter can execute.</summary>
        public int MinimumPhaseIndex => minimumPhaseIndex;
        /// <summary>Gets the fraction of cells shuffled by Whisk Scramble.</summary>
        public float ScramblePercentage => scramblePercentage;
        /// <summary>Gets the requested lock or hazard tile count.</summary>
        public int TileCount => tileCount;
        /// <summary>Gets the Hot Oil Lock duration in successful turns.</summary>
        public int TurnDuration => turnDuration;
        /// <summary>Gets the Frying Pan Burn lifetime in seconds.</summary>
        public float HazardDurationSeconds => hazardDurationSeconds;
    }

    /// <summary>Boss encounter data containing HP, phase, counter, and base attack tuning.</summary>
    [CreateAssetMenu(menuName = "CMF3D/Boss Profile", fileName = "BossProfile")]
    public sealed class BossProfile : ScriptableObject
    {
        [SerializeField, Min(1)] private int maxHP = 1000;
        [SerializeField, Min(1)] private int playerBaseDamage = 50;
        [SerializeField] private BossPhase[] phases;
        [SerializeField] private CounterAttackSO[] counterAttacks;

        /// <summary>Gets the encounter's maximum HP.</summary>
        public int MaxHP => maxHP;
        /// <summary>Gets the base damage supplied to the pure combo calculation.</summary>
        public int PlayerBaseDamage => playerBaseDamage;
        /// <summary>Gets the authored phase array without allocating a copy.</summary>
        public BossPhase[] Phases => phases;
        /// <summary>Gets the authored counter-attack array without allocating a copy.</summary>
        public CounterAttackSO[] CounterAttacks => counterAttacks;
    }

    /// <summary>Pure deterministic boss combat calculations suitable for edit-mode unit tests.</summary>
    public static class BossCombatMath
    {
        /// <summary>Calculates total attack damage from match shape, size, cascade, and hit zone.</summary>
        public static int CalculateDamage(int baseDamage, int matchSize, MatchShape shape,
            int cascadeLinkIndex, HitZone zone)
        {
            float matchMultiplier;
            if (matchSize >= 5 || shape == MatchShape.LShape || shape == MatchShape.TShape)
            {
                matchMultiplier = 2.4f;
            }
            else if (matchSize == 4)
            {
                matchMultiplier = 1.6f;
            }
            else
            {
                matchMultiplier = 1f;
            }

            float chainMultiplier = Mathf.Min(4f, 1f + Mathf.Max(0, cascadeLinkIndex) * 0.25f);
            float zoneMultiplier = zone == HitZone.WeakPoint ? 1.5f : zone == HitZone.Shell ? 0.75f : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, baseDamage) * matchMultiplier *
                chainMultiplier * zoneMultiplier));
        }

        /// <summary>Returns the projectile count for the match-generated attack type.</summary>
        public static int GetProjectileCount(int matchSize, MatchShape shape)
        {
            if (matchSize >= 5 || shape == MatchShape.LShape || shape == MatchShape.TShape)
            {
                return 1;
            }
            return matchSize == 4 ? 5 : 1;
        }
    }

    /// <summary>Pooled projectile that reports a hit on reaching its target or entering a boss hit-zone collider.</summary>
    public sealed class BossProjectile : MonoBehaviour, IPoolable
    {
        [SerializeField, Min(0.1f)] private float hitDistance = 0.35f;

        private BossBattleManager owner;
        private Vector3 targetPosition;
        private int damage;
        private HitZone zone;
        private bool armed;

        /// <summary>Gets the cached Rigidbody used to cap projectile velocity.</summary>
        public Rigidbody Body { get; private set; }

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (armed && (transform.position - targetPosition).sqrMagnitude <= hitDistance * hitDistance)
            {
                ReportHit();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (armed && other.GetComponentInParent<BossHitZone>() != null)
            {
                ReportHit();
            }
        }

        /// <summary>Resets projectile state before activation.</summary>
        public void OnRentFromPool()
        {
            armed = false;
            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.isKinematic = false;
            }
        }

        /// <summary>Clears references and physics before deactivation.</summary>
        public void OnReturnToPool()
        {
            armed = false;
            owner = null;
            damage = 0;
            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.isKinematic = true;
            }
        }

        /// <summary>Arms the projectile using caller-owned state without allocating.</summary>
        public void Launch(BossBattleManager battleOwner, Vector3 startPosition, Vector3 target,
            int hitDamage, HitZone hitZone, float speed)
        {
            owner = battleOwner;
            damage = hitDamage;
            zone = hitZone;
            targetPosition = target;
            transform.position = startPosition;
            Vector3 direction = (target - startPosition).normalized;
            float cappedSpeed = Mathf.Min(22f, Mathf.Max(0.1f, speed));
            if (Body != null)
            {
                Body.linearVelocity = direction * cappedSpeed;
            }
            else
            {
                transform.position = target;
            }
            armed = true;
        }

        private void ReportHit()
        {
            if (!armed)
            {
                return;
            }
            armed = false;
            BossBattleManager battleOwner = owner;
            if (battleOwner != null)
            {
                battleOwner.HandleProjectileHit(this, damage, zone);
            }
        }
    }

    /// <summary>Optional collider marker that identifies a boss hit zone.</summary>
    public sealed class BossHitZone : MonoBehaviour
    {
        [SerializeField] private HitZone zone = HitZone.Body;

        /// <summary>Gets the hit zone represented by this collider.</summary>
        public HitZone Zone => zone;
    }

    /// <summary>Inspector-attachable pre-warmed boss projectile pool.</summary>
    public sealed class ProjectilePool : ComponentObjectPool<BossProjectile> { }

    /// <summary>Coordinates combo attacks, boss HP phases, pooled projectiles, and telegraphed counter attacks.</summary>
    public sealed class BossBattleManager : MonoBehaviour
    {
        [SerializeField] private BossProfile activeBoss;
        [SerializeField] private ProjectilePool projectilePool;
        [SerializeField] private GridManager3D gridManager;
        [SerializeField] private Transform bossSpawnPoint;
        [SerializeField, Min(0.1f)] private float projectileSpeed = 16f;
        [SerializeField, Min(1)] private int projectilePrewarmCount = 16;
        [SerializeField, Min(0f)] private float counterTelegraphSeconds = 1.2f;

        private float[] nextCounterTimes;
        private bool[] counterTelegraphed;
        private float phaseElapsed;
        private bool encounterActive;
        private int attackStartHP;

        /// <summary>Gets the boss's remaining hit points.</summary>
        public int CurrentHP { get; private set; }
        /// <summary>Gets the zero-based active boss phase.</summary>
        public int CurrentPhaseIndex { get; private set; }

        /// <summary>Raised synchronously after a phase threshold is crossed.</summary>
        public event Action<int> OnBossPhaseChanged;
        /// <summary>Raised once when boss HP first reaches zero.</summary>
        public event Action OnBossDefeated;
        /// <summary>Raised 1.2 seconds before a scheduled counter attack executes.</summary>
        public event Action<CounterAttackSO> OnBossCounterAttackTelegraphed;

        private void Awake()
        {
            if (bossSpawnPoint == null)
            {
                GameObject spawnObject = GameObject.Find("BossSpawnPoint");
                if (spawnObject != null)
                {
                    bossSpawnPoint = spawnObject.transform;
                }
            }
            if (bossSpawnPoint == null)
            {
                Debug.LogError("[BossBattleManager] Missing scene object named 'BossSpawnPoint'. SceneAutoBuilder must create it before an encounter.", this);
            }
            if (projectilePool != null)
            {
                projectilePool.Prewarm(projectilePrewarmCount);
            }
        }

        private void Update()
        {
            if (!encounterActive)
            {
                return;
            }
            TickEnrageTimer(Time.deltaTime);
            ScheduleCounterAttacks();
        }

        /// <summary>Resets runtime encounter state from an authored boss profile.</summary>
        public void InitializeEncounter(BossProfile profile)
        {
            if (profile == null)
            {
                Debug.LogError("[BossBattleManager] InitializeEncounter requires a BossProfile.", this);
                return;
            }

            activeBoss = profile;
            CurrentHP = Mathf.Max(1, profile.MaxHP);
            attackStartHP = CurrentHP;
            CurrentPhaseIndex = 0;
            phaseElapsed = 0f;
            encounterActive = true;
            CounterAttackSO[] counters = profile.CounterAttacks;
            int counterCount = counters == null ? 0 : counters.Length;
            nextCounterTimes = new float[counterCount];
            counterTelegraphed = new bool[counterCount];
            float now = Time.time;
            for (int i = 0; i < counterCount; i++)
            {
                CounterAttackSO counter = counters[i];
                nextCounterTimes[i] = now + (counter == null ? 15f : Mathf.Max(1f, counter.CooldownSeconds));
            }
            OnBossPhaseChanged?.Invoke(CurrentPhaseIndex);
        }

        /// <summary>Maps a resolved match to pooled projectiles and pure damage math.</summary>
        public void ReceiveMatchAttack(int matchSize, MatchShape shape, int cascadeLinkIndex)
        {
            if (!encounterActive || activeBoss == null || matchSize < 3)
            {
                return;
            }

            HitZone zone = GetCurrentHitZone();
            int totalDamage = BossCombatMath.CalculateDamage(activeBoss.PlayerBaseDamage, matchSize, shape,
                cascadeLinkIndex, zone);
            int projectileCount = BossCombatMath.GetProjectileCount(matchSize, shape);
            attackStartHP = CurrentHP;
            if (projectilePool == null || bossSpawnPoint == null)
            {
                ApplyDamage(totalDamage, zone);
                return;
            }

            Vector3 target = bossSpawnPoint.position;
            Vector3 start = transform.position;
            int baseProjectileDamage = totalDamage / projectileCount;
            int remainder = totalDamage % projectileCount;
            for (int i = 0; i < projectileCount; i++)
            {
                BossProjectile projectile = projectilePool.Get();
                int damage = baseProjectileDamage + (i < remainder ? 1 : 0);
                if (projectile == null)
                {
                    ApplyDamage(damage, zone);
                    continue;
                }
                projectile.Launch(this, start, target, damage, zone, projectileSpeed);
            }
        }

        internal void HandleProjectileHit(BossProjectile projectile, int damage, HitZone zone)
        {
            ApplyDamage(damage, zone);
            if (projectilePool != null && projectile != null)
            {
                projectilePool.Return(projectile);
            }
        }

        private void ApplyDamage(int amount, HitZone zone)
        {
            if (!encounterActive || amount <= 0)
            {
                return;
            }

            CurrentHP = Mathf.Max(0, CurrentHP - amount);
            if (activeBoss != null && attackStartHP - CurrentHP >= Mathf.CeilToInt(activeBoss.MaxHP * 0.2f))
            {
                ExpediteCounter(CounterAttackType.WhiskScramble);
            }

            if (CurrentHP == 0)
            {
                encounterActive = false;
                OnBossDefeated?.Invoke();
                return;
            }
            EvaluatePhaseTransition();
        }

        private void EvaluatePhaseTransition()
        {
            BossPhase[] phases = activeBoss == null ? null : activeBoss.Phases;
            if (phases == null || phases.Length == 0)
            {
                return;
            }

            float hpRatio = CurrentHP / (float)Mathf.Max(1, activeBoss.MaxHP);
            while (CurrentPhaseIndex + 1 < phases.Length)
            {
                BossPhase next = phases[CurrentPhaseIndex + 1];
                if (next == null || hpRatio > next.HealthThreshold)
                {
                    break;
                }
                CurrentPhaseIndex++;
                phaseElapsed = 0f;
                OnBossPhaseChanged?.Invoke(CurrentPhaseIndex);
            }
        }

        private void TickEnrageTimer(float deltaTime)
        {
            phaseElapsed += Mathf.Max(0f, deltaTime);
            BossPhase[] phases = activeBoss == null ? null : activeBoss.Phases;
            if (phases == null || CurrentPhaseIndex < 0 || CurrentPhaseIndex >= phases.Length ||
                phases[CurrentPhaseIndex] == null)
            {
                return;
            }

            float enrageAt = Mathf.Max(1f, phases[CurrentPhaseIndex].EnrageTimerSeconds);
            if (phaseElapsed >= enrageAt)
            {
                phaseElapsed = 0f;
                ExpediteFirstAvailableCounter();
            }
        }

        private void ScheduleCounterAttacks()
        {
            CounterAttackSO[] counters = activeBoss == null ? null : activeBoss.CounterAttacks;
            if (counters == null || nextCounterTimes == null)
            {
                return;
            }

            float now = Time.time;
            for (int i = 0; i < counters.Length; i++)
            {
                CounterAttackSO counter = counters[i];
                if (counter == null || counter.MinimumPhaseIndex > CurrentPhaseIndex)
                {
                    continue;
                }
                if (!counterTelegraphed[i] && now >= nextCounterTimes[i] - counterTelegraphSeconds)
                {
                    counterTelegraphed[i] = true;
                    OnBossCounterAttackTelegraphed?.Invoke(counter);
                }
                if (now >= nextCounterTimes[i])
                {
                    ExecuteCounterAttack(counter);
                    nextCounterTimes[i] = now + Mathf.Max(1f, counter.CooldownSeconds);
                    counterTelegraphed[i] = false;
                }
            }
        }

        private void ExecuteCounterAttack(CounterAttackSO counter)
        {
            if (counter == null || gridManager == null)
            {
                return;
            }

            switch (counter.AttackType)
            {
                case CounterAttackType.WhiskScramble:
                    gridManager.ApplyWhiskScramble(counter.ScramblePercentage);
                    break;
                case CounterAttackType.HotOilLock:
                    gridManager.ApplyHotOilLock(counter.TileCount, counter.TurnDuration);
                    break;
                case CounterAttackType.FryingPanBurn:
                    gridManager.ApplyHazardTiles(counter.TileCount, counter.HazardDurationSeconds);
                    break;
            }
        }

        private HitZone GetCurrentHitZone()
        {
            BossPhase[] phases = activeBoss == null ? null : activeBoss.Phases;
            if (phases == null || CurrentPhaseIndex < 0 || CurrentPhaseIndex >= phases.Length ||
                phases[CurrentPhaseIndex] == null)
            {
                return HitZone.Body;
            }
            return phases[CurrentPhaseIndex].ActiveHitZone;
        }

        private void ExpediteCounter(CounterAttackType type)
        {
            CounterAttackSO[] counters = activeBoss == null ? null : activeBoss.CounterAttacks;
            if (counters == null || nextCounterTimes == null)
            {
                return;
            }
            for (int i = 0; i < counters.Length; i++)
            {
                CounterAttackSO counter = counters[i];
                if (counter != null && counter.AttackType == type && counter.MinimumPhaseIndex <= CurrentPhaseIndex)
                {
                    float desired = Time.time + counterTelegraphSeconds;
                    if (nextCounterTimes[i] > desired)
                    {
                        nextCounterTimes[i] = desired;
                        counterTelegraphed[i] = false;
                    }
                    return;
                }
            }
        }

        private void ExpediteFirstAvailableCounter()
        {
            CounterAttackSO[] counters = activeBoss == null ? null : activeBoss.CounterAttacks;
            if (counters == null || nextCounterTimes == null)
            {
                return;
            }
            for (int i = 0; i < counters.Length; i++)
            {
                CounterAttackSO counter = counters[i];
                if (counter != null && counter.MinimumPhaseIndex <= CurrentPhaseIndex)
                {
                    float desired = Time.time + counterTelegraphSeconds;
                    if (nextCounterTimes[i] > desired)
                    {
                        nextCounterTimes[i] = desired;
                        counterTelegraphed[i] = false;
                    }
                    return;
                }
            }
        }
    }
}
