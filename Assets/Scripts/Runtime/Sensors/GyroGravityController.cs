using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Publishes device or browser-fallback tilt as board gravity and detects shake-to-scramble input.</summary>
    public sealed class GyroGravityController : MonoBehaviour
    {
        [SerializeField] private float gravityScale = 0.6f;
        [SerializeField] private float deadZoneDegrees = 3.0f;
        [SerializeField] private float maxTiltDegrees = 35.0f;
        [SerializeField] private float shakeJerkThreshold = 6.5f;
        [SerializeField] private float shakeSampleWindow = 0.4f;
        [SerializeField] private float shakeCooldown = 8.0f;
        [SerializeField] private float scrambleForceRadius = 2.2f;
        [SerializeField, Range(0f, 1f)] private float mouseTiltSensitivity = 0.85f;
        [SerializeField, Min(0f)] private float gravityPublishEpsilon = 0.015f;

        private const int JerkBufferCapacity = 32;
        private readonly float[] jerkSamples = new float[JerkBufferCapacity];
        private readonly float[] jerkSampleTimes = new float[JerkBufferCapacity];
        private int jerkWriteIndex;
        private int jerkSampleCount;
        private float previousAccelerationMagnitude;
        private float previousAccelerationTime;
        private float lastShakeTime = float.NegativeInfinity;
        private Vector3 lastPublishedGravity = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        private Transform cameraTransform;
        private bool shakePending;

        /// <summary>Gets whether a hardware gyroscope is active; false in WebGL where fallback tilt remains functional.</summary>
        public bool GyroAvailable { get; private set; }
        /// <summary>Gets the number of shake-scramble charges that can still be consumed.</summary>
        public int ScrambleChargesRemaining { get; private set; }

        private void Awake()
        {
            Camera mainCamera = Camera.main;
            cameraTransform = mainCamera != null ? mainCamera.transform : null;
            previousAccelerationTime = Time.unscaledTime;
#if !UNITY_WEBGL || UNITY_EDITOR
            GyroAvailable = SystemInfo.supportsGyroscope;
            if (GyroAvailable)
            {
                Input.gyro.enabled = true;
                previousAccelerationMagnitude = Input.acceleration.magnitude;
            }
#else
            GyroAvailable = false;
#endif
        }

        private void Update()
        {
            Vector2 rawTilt;
#if !UNITY_WEBGL || UNITY_EDITOR
            if (GyroAvailable)
            {
                Vector3 deviceGravity = Input.gyro.gravity;
                rawTilt = new Vector2(deviceGravity.x, deviceGravity.y);
                SampleJerk();
                if (DetectShake())
                {
                    shakePending = true;
                }
            }
            else
            {
                rawTilt = ReadFallbackTilt();
            }
#else
            rawTilt = ReadFallbackTilt();
#endif

            if (!GyroAvailable && Application.isFocused && Input.GetKeyDown(KeyCode.Space) &&
                Time.unscaledTime - lastShakeTime >= shakeCooldown)
            {
                lastShakeTime = Time.unscaledTime;
                shakePending = true;
            }

            Vector3 gravity = ConvertTiltToGravity(rawTilt);
            if ((gravity - lastPublishedGravity).sqrMagnitude >= gravityPublishEpsilon * gravityPublishEpsilon)
            {
                lastPublishedGravity = gravity;
                WorldStateBus.RaiseGravityVectorChanged(gravity);
            }
        }

        private void SampleJerk()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            float now = Time.unscaledTime;
            float deltaTime = now - previousAccelerationTime;
            if (deltaTime <= 0.0001f)
            {
                return;
            }

            float magnitude = Input.acceleration.magnitude;
            float jerk = Mathf.Abs(magnitude - previousAccelerationMagnitude) / deltaTime;
            jerkSamples[jerkWriteIndex] = jerk;
            jerkSampleTimes[jerkWriteIndex] = now;
            jerkWriteIndex = (jerkWriteIndex + 1) % JerkBufferCapacity;
            if (jerkSampleCount < JerkBufferCapacity)
            {
                jerkSampleCount++;
            }
            previousAccelerationMagnitude = magnitude;
            previousAccelerationTime = now;
#endif
        }

        private bool DetectShake()
        {
            if (!Application.isFocused || Time.unscaledTime - lastShakeTime < shakeCooldown)
            {
                return false;
            }

            int sustainedSamples = 0;
            float cutoff = Time.unscaledTime - shakeSampleWindow;
            for (int i = 0; i < jerkSampleCount; i++)
            {
                if (jerkSampleTimes[i] >= cutoff && jerkSamples[i] > shakeJerkThreshold)
                {
                    sustainedSamples++;
                    if (sustainedSamples >= 3)
                    {
                        lastShakeTime = Time.unscaledTime;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Consumes one charge only after a hardware shake or fallback Space key trigger.</summary>
        public bool TryConsumeScrambleCharge(out Vector3 explosionOrigin)
        {
            explosionOrigin = transform.position + Vector3.up * scrambleForceRadius;
            if (!shakePending || ScrambleChargesRemaining <= 0)
            {
                return false;
            }

            shakePending = false;
            ScrambleChargesRemaining--;
            return true;
        }

        /// <summary>Adds a non-negative number of charges while saturating at the integer maximum.</summary>
        public void GrantScrambleCharge(int amount)
        {
            if (amount <= 0)
            {
                return;
            }
            ScrambleChargesRemaining = amount > int.MaxValue - ScrambleChargesRemaining
                ? int.MaxValue
                : ScrambleChargesRemaining + amount;
        }

        private Vector2 ReadFallbackTilt()
        {
            float width = Mathf.Max(1f, Screen.width);
            float height = Mathf.Max(1f, Screen.height);
            Vector3 mouse = Input.mousePosition;
            float horizontal = ((mouse.x / width) * 2f - 1f) * mouseTiltSensitivity;
            float vertical = ((mouse.y / height) * 2f - 1f) * mouseTiltSensitivity;

            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) horizontal -= 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) horizontal += 1f;
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) vertical -= 1f;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) vertical += 1f;
            return Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
        }

        private Vector3 ConvertTiltToGravity(Vector2 rawTilt)
        {
            float rawMagnitude = Mathf.Clamp01(rawTilt.magnitude);
            float tiltDegrees = rawMagnitude * Mathf.Max(0f, maxTiltDegrees);
            if (tiltDegrees <= Mathf.Max(0f, deadZoneDegrees) || rawMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            float usableRange = Mathf.Max(0.001f, maxTiltDegrees - deadZoneDegrees);
            float normalizedStrength = Mathf.Clamp01((tiltDegrees - deadZoneDegrees) / usableRange);
            Vector2 direction = rawTilt / rawMagnitude;
            Vector3 boardDirection = new Vector3(direction.x, 0f, direction.y);
            if (GyroAvailable && cameraTransform != null)
            {
                Vector3 cameraRelative = cameraTransform.TransformDirection(boardDirection);
                boardDirection = Vector3.ProjectOnPlane(cameraRelative, Vector3.up).normalized;
            }
            return boardDirection * (Physics.gravity.magnitude * gravityScale * normalizedStrength);
        }
    }
}
