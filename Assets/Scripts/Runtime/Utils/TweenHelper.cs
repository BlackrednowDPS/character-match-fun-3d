using System;
using System.Collections;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Small coroutine-based tween collection requiring no third-party package.</summary>
    public static class TweenHelper
    {
        private static readonly AnimationCurve DefaultCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        /// <summary>Moves a transform between local positions over unscaled or scaled time.</summary>
        public static IEnumerator MoveLocal(Transform target, Vector3 from, Vector3 to, float duration,
            AnimationCurve curve = null, bool unscaledTime = false, Action completed = null)
        {
            if (target == null)
            {
                yield break;
            }

            AnimationCurve easing = curve ?? DefaultCurve;
            float elapsed = 0f;
            float safeDuration = Mathf.Max(0.0001f, duration);
            target.localPosition = from;
            while (elapsed < safeDuration)
            {
                elapsed += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                target.localPosition = Vector3.LerpUnclamped(from, to,
                    easing.Evaluate(Mathf.Clamp01(elapsed / safeDuration)));
                yield return null;
            }

            target.localPosition = to;
            completed?.Invoke();
        }

        /// <summary>Scales a transform between values over unscaled or scaled time.</summary>
        public static IEnumerator Scale(Transform target, Vector3 from, Vector3 to, float duration,
            AnimationCurve curve = null, bool unscaledTime = false, Action completed = null)
        {
            if (target == null)
            {
                yield break;
            }

            AnimationCurve easing = curve ?? DefaultCurve;
            float elapsed = 0f;
            float safeDuration = Mathf.Max(0.0001f, duration);
            target.localScale = from;
            while (elapsed < safeDuration)
            {
                elapsed += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                target.localScale = Vector3.LerpUnclamped(from, to,
                    easing.Evaluate(Mathf.Clamp01(elapsed / safeDuration)));
                yield return null;
            }

            target.localScale = to;
            completed?.Invoke();
        }

        /// <summary>Rotates a transform between local orientations over scaled time.</summary>
        public static IEnumerator RotateLocal(Transform target, Quaternion from, Quaternion to, float duration,
            AnimationCurve curve = null, Action completed = null)
        {
            if (target == null)
            {
                yield break;
            }

            AnimationCurve easing = curve ?? DefaultCurve;
            float elapsed = 0f;
            float safeDuration = Mathf.Max(0.0001f, duration);
            target.localRotation = from;
            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                target.localRotation = Quaternion.SlerpUnclamped(from, to,
                    easing.Evaluate(Mathf.Clamp01(elapsed / safeDuration)));
                yield return null;
            }

            target.localRotation = to;
            completed?.Invoke();
        }
    }
}
