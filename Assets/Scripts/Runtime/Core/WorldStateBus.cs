using System;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Identifies the broad season selected for the current player region.</summary>
    public enum SeasonType
    {
        /// <summary>March-to-May northern season.</summary>
        Spring,
        /// <summary>June-to-August northern season.</summary>
        Summer,
        /// <summary>September-to-November northern season.</summary>
        Fall,
        /// <summary>December-to-February northern season.</summary>
        Winter
    }

    /// <summary>Identifies an optional live holiday overlay without carrying scene references.</summary>
    public enum HolidayEvent
    {
        /// <summary>No active holiday overlay.</summary>
        None,
        /// <summary>Halloween Jack-o'-Lantern Egg overlay.</summary>
        HalloweenJackOLanternEgg,
        /// <summary>Winter Santa Yolk overlay.</summary>
        WinterSantaYolk
    }

    /// <summary>Immutable, allocation-free copy of the latest world inputs.</summary>
    public readonly struct WorldStateSnapshot
    {
        /// <summary>Creates a complete world-state value.</summary>
        public WorldStateSnapshot(Vector3 gravityVector, SeasonType season, WeatherState weather,
            float temperatureCelsius, StressTier playerStress)
        {
            GravityVector = gravityVector;
            Season = season;
            Weather = weather;
            TemperatureCelsius = temperatureCelsius;
            PlayerStress = playerStress;
        }

        /// <summary>Gets the latest board-space gravity vector.</summary>
        public readonly Vector3 GravityVector;
        /// <summary>Gets the latest resolved season.</summary>
        public readonly SeasonType Season;
        /// <summary>Gets the latest resolved weather.</summary>
        public readonly WeatherState Weather;
        /// <summary>Gets the latest observed temperature in Celsius.</summary>
        public readonly float TemperatureCelsius;
        /// <summary>Gets the latest inferred player stress tier.</summary>
        public readonly StressTier PlayerStress;
    }

    /// <summary>Central event aggregator for cross-system runtime state.</summary>
    public static class WorldStateBus
    {
        /// <summary>Raised only when the board-space gravity vector materially changes.</summary>
        public static event Action<Vector3> OnGravityVectorChanged;
        /// <summary>Raised when the regional season or holiday changes.</summary>
        public static event Action<SeasonType, HolidayEvent> OnSeasonChanged;
        /// <summary>Raised when the current weather category changes.</summary>
        public static event Action<WeatherState> OnWeatherChanged;
        /// <summary>Raised once for each hot or cold threshold crossing.</summary>
        public static event Action<float, ThresholdDirection> OnTemperatureThresholdCrossed;
        /// <summary>Raised when the inferred player stress tier changes.</summary>
        public static event Action<StressTier> OnPlayerStressLevelChanged;

        private static Vector3 gravityVector = Vector3.back;
        private static SeasonType season = SeasonType.Spring;
        private static WeatherState weather = WeatherState.Clear;
        private static float temperatureCelsius;
        private static StressTier playerStress = StressTier.Calm;

        /// <summary>Gets a value-copy snapshot without allocating.</summary>
        public static WorldStateSnapshot Snapshot => new WorldStateSnapshot(
            gravityVector, season, weather, temperatureCelsius, playerStress);

        /// <summary>Publishes a new gravity vector synchronously without allocating.</summary>
        public static void RaiseGravityVectorChanged(Vector3 gravity)
        {
            gravityVector = gravity;
            OnGravityVectorChanged?.Invoke(gravity);
        }

        /// <summary>Publishes a new season and holiday synchronously without allocating.</summary>
        public static void RaiseSeasonChanged(SeasonType value, HolidayEvent activeEvent)
        {
            season = value;
            OnSeasonChanged?.Invoke(value, activeEvent);
        }

        /// <summary>Publishes a weather category synchronously without allocating.</summary>
        public static void RaiseWeatherChanged(WeatherState value)
        {
            weather = value;
            OnWeatherChanged?.Invoke(value);
        }

        /// <summary>Publishes a temperature threshold crossing synchronously without allocating.</summary>
        public static void RaiseTemperatureThresholdCrossed(float celsius, ThresholdDirection direction)
        {
            temperatureCelsius = celsius;
            OnTemperatureThresholdCrossed?.Invoke(celsius, direction);
        }

        /// <summary>Updates the cached temperature without raising a crossing event.</summary>
        public static void SetTemperature(float celsius)
        {
            temperatureCelsius = celsius;
        }

        /// <summary>Publishes a player stress tier synchronously without allocating.</summary>
        public static void RaisePlayerStressLevelChanged(StressTier tier)
        {
            playerStress = tier;
            OnPlayerStressLevelChanged?.Invoke(tier);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnGravityVectorChanged = null;
            OnSeasonChanged = null;
            OnWeatherChanged = null;
            OnTemperatureThresholdCrossed = null;
            OnPlayerStressLevelChanged = null;
            gravityVector = Vector3.back;
            season = SeasonType.Spring;
            weather = WeatherState.Clear;
            temperatureCelsius = 0f;
            playerStress = StressTier.Calm;
        }
    }
}
