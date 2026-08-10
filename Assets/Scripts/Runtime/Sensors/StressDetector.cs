using System;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Coarse player-input stress level inferred from recent tap cadence and mistakes.</summary>
    public enum StressTier
    {
        /// <summary>Less than 1.5 taps per second with no rapid repeated mistakes.</summary>
        Calm,
        /// <summary>Between 1.5 and 3.5 taps per second.</summary>
        Focused,
        /// <summary>More than 3.5 taps per second or at least two recent invalid taps.</summary>
        Frantic
    }

    /// <summary>Measures a fixed rolling input window and pairs empathetic humor with a breather offer.</summary>
    public sealed class StressDetector : MonoBehaviour
    {
        [SerializeField] private float frenticTapsPerSecond = 3.5f;
        [SerializeField] private float focusedTapsPerSecond = 1.5f;
        [SerializeField] private float rollingWindowSeconds = 5f;
        [SerializeField, Range(0f, 1f)] private float metaHumorTriggerChance = 0.15f;
        [SerializeField] private float metaHumorCooldownSeconds = 45f;
        [SerializeField, Range(5f, 10f)] private float evaluationsPerSecond = 5f;
        [SerializeField] private DialogueDatabase dialogueDatabase;
        [SerializeField] private MonoBehaviour assistSystemProvider;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private LayerMask matchTargetLayerMask = ~0;

        private const int TapBufferCapacity = 128;
        private readonly float[] tapTimes = new float[TapBufferCapacity];
        private readonly Vector2[] tapPositions = new Vector2[TapBufferCapacity];
        private readonly bool[] tapValidity = new bool[TapBufferCapacity];
        private readonly RaycastHit[] raycastHits = new RaycastHit[8];
        private int tapWriteIndex;
        private int tapCount;
        private float nextEvaluationTime;
        private float lastMetaHumorTime = float.NegativeInfinity;
        private IPlayerAssistSystem assistSystem;

        /// <summary>Gets the latest throttled stress tier.</summary>
        public StressTier CurrentTier { get; private set; } = StressTier.Calm;
        /// <summary>Gets the average screen-space distance between taps in the active rolling window.</summary>
        public float AverageTouchDistance { get; private set; }
        /// <summary>Raised only after a paired breather offer succeeds, carrying a pre-authored line reference.</summary>
        public event Action<string> OnMetaHumorLineRequested;

        private void Awake()
        {
            assistSystem = assistSystemProvider as IPlayerAssistSystem;
            if (inputCamera == null)
            {
                inputCamera = Camera.main;
            }
            nextEvaluationTime = Time.unscaledTime;
        }

        private void Update()
        {
            int touchCount = Input.touchCount;
            for (int i = 0; i < touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    RegisterTap(touch.position, IsMatchTarget(touch.position));
                }
            }
            if (touchCount == 0 && Input.GetMouseButtonDown(0))
            {
                Vector2 mousePosition = Input.mousePosition;
                RegisterTap(mousePosition, IsMatchTarget(mousePosition));
            }

            if (Time.unscaledTime >= nextEvaluationTime)
            {
                nextEvaluationTime = Time.unscaledTime + 1f / Mathf.Clamp(evaluationsPerSecond, 5f, 10f);
                EvaluateTier();
            }
        }

        /// <summary>Lets a dedicated input layer report a tap and its match-target validity without allocating.</summary>
        public void ReportTap(Vector2 screenPos, bool wasValidMatchTarget)
        {
            RegisterTap(screenPos, wasValidMatchTarget);
        }

        private void RegisterTap(Vector2 screenPos, bool wasValidMatchTarget)
        {
            tapTimes[tapWriteIndex] = Time.unscaledTime;
            tapPositions[tapWriteIndex] = screenPos;
            tapValidity[tapWriteIndex] = wasValidMatchTarget;
            tapWriteIndex = (tapWriteIndex + 1) % TapBufferCapacity;
            if (tapCount < TapBufferCapacity)
            {
                tapCount++;
            }
        }

        private void EvaluateTier()
        {
            float now = Time.unscaledTime;
            float window = Mathf.Max(0.1f, rollingWindowSeconds);
            float cutoff = now - window;
            float mistakeCutoff = now - 2f;
            int activeTapCount = 0;
            int recentMistakes = 0;
            float totalDistance = 0f;
            int distanceSamples = 0;
            Vector2 previousPosition = default;
            bool hasPrevious = false;

            int oldestIndex = (tapWriteIndex - tapCount + TapBufferCapacity) % TapBufferCapacity;
            for (int offset = 0; offset < tapCount; offset++)
            {
                int index = (oldestIndex + offset) % TapBufferCapacity;
                float timestamp = tapTimes[index];
                if (timestamp < cutoff)
                {
                    continue;
                }

                activeTapCount++;
                Vector2 position = tapPositions[index];
                if (hasPrevious)
                {
                    totalDistance += Vector2.Distance(previousPosition, position);
                    distanceSamples++;
                }
                previousPosition = position;
                hasPrevious = true;
                if (!tapValidity[index] && timestamp >= mistakeCutoff)
                {
                    recentMistakes++;
                }
            }

            AverageTouchDistance = distanceSamples > 0 ? totalDistance / distanceSamples : 0f;
            float tapsPerSecond = activeTapCount / window;
            StressTier evaluated = tapsPerSecond > frenticTapsPerSecond || recentMistakes >= 2
                ? StressTier.Frantic
                : tapsPerSecond >= focusedTapsPerSecond ? StressTier.Focused : StressTier.Calm;
            if (evaluated == CurrentTier)
            {
                return;
            }

            CurrentTier = evaluated;
            WorldStateBus.RaisePlayerStressLevelChanged(evaluated);
            if (evaluated == StressTier.Frantic)
            {
                TryFireMetaHumorLine();
            }
        }

        private void TryFireMetaHumorLine()
        {
            float now = Time.unscaledTime;
            if (now - lastMetaHumorTime < metaHumorCooldownSeconds ||
                UnityEngine.Random.value > metaHumorTriggerChance || dialogueDatabase == null || assistSystem == null)
            {
                return;
            }

            string line;
            if (!dialogueDatabase.TryGetLine("MetaHumor_Stress", out line) ||
                !assistSystem.TryOfferBreatherHint())
            {
                return;
            }

            lastMetaHumorTime = now;
            OnMetaHumorLineRequested?.Invoke(line);
        }

        private bool IsMatchTarget(Vector2 screenPosition)
        {
            if (inputCamera == null)
            {
                return false;
            }
            Ray ray = inputCamera.ScreenPointToRay(screenPosition);
            int hitCount = Physics.RaycastNonAlloc(ray, raycastHits, 100f, matchTargetLayerMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                if (raycastHits[i].collider.GetComponentInParent<EggPiece>() != null)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
