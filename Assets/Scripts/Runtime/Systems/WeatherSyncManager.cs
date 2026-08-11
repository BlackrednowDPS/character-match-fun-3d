using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace CMF3D.Runtime
{
    /// <summary>Coarse weather category used by gameplay systems.</summary>
    public enum WeatherState
    {
        /// <summary>Baseline conditions with no gameplay penalty.</summary>
        Clear,
        /// <summary>Rain conditions that reduce board friction.</summary>
        Rain,
        /// <summary>Snow conditions that reduce gravity and enable frozen tags.</summary>
        Snow
    }

    /// <summary>Direction in which a configured temperature threshold was crossed.</summary>
    public enum ThresholdDirection
    {
        /// <summary>Temperature moved upward through a threshold.</summary>
        Rising,
        /// <summary>Temperature moved downward through a threshold.</summary>
        Falling
    }

    /// <summary>Resolves location without blocking, polls Open-Meteo, and retains a small offline cache.</summary>
    public sealed class WeatherSyncManager : MonoBehaviour
    {
        [SerializeField] private float pollIntervalMinutes = 15f;
        [SerializeField] private float hotThresholdCelsius = 30f;
        [SerializeField] private float coldThresholdCelsius = 0f;
        [SerializeField] private float fallbackLatitude = 19.4326f;
        [SerializeField] private float fallbackLongitude = -99.1332f;
        [SerializeField, Min(1f)] private float requestTimeoutSeconds = 12f;
        [SerializeField, Min(1f)] private float locationTimeoutSeconds = 10f;

        private const string CacheFileName = "weather_cache.json";
        private float latitude;
        private float longitude;
        private bool hasTemperature;
        private string cachePath;
        private float initialPollDelaySeconds;

        /// <summary>Gets the last known-good weather, defaulting to Clear.</summary>
        public WeatherState CurrentWeather { get; private set; } = WeatherState.Clear;
        /// <summary>Gets the last known-good temperature in Celsius.</summary>
        public float CurrentTemperatureCelsius { get; private set; }

        [Serializable]
        private sealed class OpenMeteoResponse
        {
            /// <summary>Current conditions returned by Open-Meteo.</summary>
            public OpenMeteoCurrent current;
        }

        [Serializable]
        private sealed class OpenMeteoCurrent
        {
            /// <summary>Current temperature in Celsius.</summary>
            public float temperature_2m;
            /// <summary>WMO weather interpretation code.</summary>
            public int weather_code;
        }

        [Serializable]
        private sealed class WeatherCacheRecord
        {
            /// <summary>Round-trip UTC timestamp for rate-limit age checks.</summary>
            public string fetchedUtc;
            /// <summary>Last valid raw Open-Meteo response.</summary>
            public string responseJson;
        }

        private IEnumerator Start()
        {
            latitude = fallbackLatitude;
            longitude = fallbackLongitude;
            cachePath = Path.Combine(Application.persistentDataPath, CacheFileName);
            ApplyCachedResponse();
            ResolveTimezoneFallback();
            yield return LocationRoutine();
            yield return PollRoutine();
        }

        private IEnumerator LocationRoutine()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (!Input.location.isEnabledByUser)
            {
                yield break;
            }

            Input.location.Start(100f, 100f);
            float deadline = Time.realtimeSinceStartup + locationTimeoutSeconds;
            while (Input.location.status == LocationServiceStatus.Initializing &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (Input.location.status == LocationServiceStatus.Running)
            {
                LocationInfo location = Input.location.lastData;
                latitude = location.latitude;
                longitude = location.longitude;
            }
            Input.location.Stop();
#else
            yield return null;
#endif
        }

        private IEnumerator PollRoutine()
        {
            if (initialPollDelaySeconds > 0f)
            {
                float firstPollAt = Time.realtimeSinceStartup + initialPollDelaySeconds;
                while (enabled && Time.realtimeSinceStartup < firstPollAt)
                {
                    yield return null;
                }
            }

            while (enabled)
            {
                string url = string.Format(CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0:F4}&longitude={1:F4}&current=temperature_2m,weather_code&timezone=UTC",
                    latitude, longitude);
                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = Mathf.CeilToInt(requestTimeoutSeconds);
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        string json = request.downloadHandler.text;
                        if (TryApplyResponse(json))
                        {
                            WriteCache(json);
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[WeatherSyncManager] Open-Meteo request failed; retaining last known-good weather.", this);
                    }
                }

                float waitSeconds = Mathf.Max(1f, pollIntervalMinutes * 60f);
                float resumeAt = Time.realtimeSinceStartup + waitSeconds;
                while (enabled && Time.realtimeSinceStartup < resumeAt)
                {
                    yield return null;
                }
            }
        }

        private void ApplyWeather(WeatherState state, float tempCelsius)
        {
            float previous = CurrentTemperatureCelsius;
            bool hadPrevious = hasTemperature;
            CurrentTemperatureCelsius = tempCelsius;
            hasTemperature = true;
            WorldStateBus.SetTemperature(tempCelsius);

            if (state != CurrentWeather)
            {
                CurrentWeather = state;
                WorldStateBus.RaiseWeatherChanged(state);
            }

            if (!hadPrevious)
            {
                return;
            }
            RaiseCrossing(previous, tempCelsius, coldThresholdCelsius);
            RaiseCrossing(previous, tempCelsius, hotThresholdCelsius);
        }

        private void RaiseCrossing(float previous, float current, float threshold)
        {
            if (previous <= threshold && current > threshold)
            {
                WorldStateBus.RaiseTemperatureThresholdCrossed(current, ThresholdDirection.Rising);
            }
            else if (previous >= threshold && current < threshold)
            {
                WorldStateBus.RaiseTemperatureThresholdCrossed(current, ThresholdDirection.Falling);
            }
        }

        private bool TryApplyResponse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }
            try
            {
                OpenMeteoResponse response = JsonUtility.FromJson<OpenMeteoResponse>(json);
                if (response == null || response.current == null)
                {
                    return false;
                }
                ApplyWeather(MapWeatherCode(response.current.weather_code), response.current.temperature_2m);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[WeatherSyncManager] Weather response could not be parsed: " + exception.Message, this);
                return false;
            }
        }

        private void ApplyCachedResponse()
        {
            try
            {
                if (!File.Exists(cachePath))
                {
                    return;
                }
                string cacheJson = File.ReadAllText(cachePath);
                WeatherCacheRecord cache = JsonUtility.FromJson<WeatherCacheRecord>(cacheJson);
                if (cache != null && !string.IsNullOrEmpty(cache.responseJson))
                {
                    TryApplyResponse(cache.responseJson);
                    DateTime fetchedUtc;
                    if (DateTime.TryParse(cache.fetchedUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out fetchedUtc))
                    {
                        double ageSeconds = Math.Max(0d, (DateTime.UtcNow - fetchedUtc.ToUniversalTime()).TotalSeconds);
                        initialPollDelaySeconds = Mathf.Max(0f,
                            pollIntervalMinutes * 60f - (float)ageSeconds);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[WeatherSyncManager] Offline weather cache was ignored: " + exception.Message, this);
            }
        }

        private void WriteCache(string responseJson)
        {
            try
            {
                WeatherCacheRecord cache = new WeatherCacheRecord
                {
                    fetchedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    responseJson = responseJson
                };
                File.WriteAllText(cachePath, JsonUtility.ToJson(cache));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[WeatherSyncManager] Weather was applied but its offline cache could not be written: " +
                    exception.Message, this);
            }
        }

        private void ResolveTimezoneFallback()
        {
            try
            {
                double offsetHours = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalHours;
                longitude = Mathf.Clamp((float)(offsetHours * 15d), -180f, 180f);
            }
            catch (Exception)
            {
                latitude = fallbackLatitude;
                longitude = fallbackLongitude;
            }
        }

        private static WeatherState MapWeatherCode(int code)
        {
            if ((code >= 71 && code <= 77) || code == 85 || code == 86)
            {
                return WeatherState.Snow;
            }
            if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82) ||
                (code >= 95 && code <= 99))
            {
                return WeatherState.Rain;
            }
            return WeatherState.Clear;
        }
    }
}
