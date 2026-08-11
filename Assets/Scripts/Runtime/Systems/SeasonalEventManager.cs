using System;
using System.Globalization;
using UnityEngine;

namespace CMF3D.Runtime
{
    /// <summary>Authored UTC window and optional region filter for one holiday overlay.</summary>
    [Serializable]
    public sealed class SeasonalEventWindow
    {
        [SerializeField] private HolidayEvent holidayEvent;
        [SerializeField] private string regionCode = "*";
        [SerializeField, Range(1, 12)] private int startMonth = 1;
        [SerializeField, Range(1, 31)] private int startDay = 1;
        [SerializeField, Range(1, 12)] private int endMonth = 1;
        [SerializeField, Range(1, 31)] private int endDay = 1;

        /// <summary>Gets the holiday value published while this window is active.</summary>
        public HolidayEvent HolidayEvent => holidayEvent;

        /// <summary>Checks the authored month/day window and region without allocating collections.</summary>
        public bool IsActive(DateTime utcNow, string currentRegionCode)
        {
            if (!string.Equals(regionCode, "*", StringComparison.Ordinal) &&
                !string.Equals(regionCode, currentRegionCode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            int current = utcNow.Month * 100 + utcNow.Day;
            int start = startMonth * 100 + startDay;
            int end = endMonth * 100 + endDay;
            return start <= end ? current >= start && current <= end : current >= start || current <= end;
        }
    }

    /// <summary>Data-driven seasonal holiday calendar evaluated by the runtime manager.</summary>
    [CreateAssetMenu(menuName = "CMF3D/Seasonal Calendar", fileName = "SeasonalCalendar")]
    public sealed class SeasonalCalendarSO : ScriptableObject
    {
        [SerializeField] private SeasonalEventWindow[] events;

        /// <summary>Returns the first authored active holiday, or None when no window matches.</summary>
        public HolidayEvent ResolveHoliday(DateTime utcNow, string regionCode)
        {
            if (events == null)
            {
                return HolidayEvent.None;
            }
            for (int i = 0; i < events.Length; i++)
            {
                SeasonalEventWindow candidate = events[i];
                if (candidate != null && candidate.IsActive(utcNow, regionCode))
                {
                    return candidate.HolidayEvent;
                }
            }
            return HolidayEvent.None;
        }
    }

    /// <summary>Publishes hemisphere-aware season and calendar holiday state on start and application resume.</summary>
    public sealed class SeasonalEventManager : MonoBehaviour
    {
        [SerializeField] private SeasonalCalendarSO calendar;
        [SerializeField] private bool southernHemisphere;

        private SeasonType currentSeason;
        private HolidayEvent currentHoliday;
        private string regionCode = "*";
        private bool hasPublished;

        /// <summary>Gets the most recently resolved season.</summary>
        public SeasonType CurrentSeason => currentSeason;
        /// <summary>Gets the most recently resolved holiday overlay.</summary>
        public HolidayEvent CurrentHoliday => currentHoliday;

        private void Awake()
        {
            regionCode = ResolveRegionCode();
        }

        private void OnEnable()
        {
            ResolveAndPublish();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                ResolveAndPublish();
            }
        }

        /// <summary>Re-evaluates the system clock and publishes only when season or holiday changed.</summary>
        public void ResolveAndPublish()
        {
            DateTime utcNow = DateTime.UtcNow;
            SeasonType resolvedSeason = ResolveSeason(utcNow.Month, southernHemisphere);
            HolidayEvent resolvedHoliday = calendar == null
                ? HolidayEvent.None
                : calendar.ResolveHoliday(utcNow, regionCode);
            if (!hasPublished || resolvedSeason != currentSeason || resolvedHoliday != currentHoliday)
            {
                currentSeason = resolvedSeason;
                currentHoliday = resolvedHoliday;
                hasPublished = true;
                WorldStateBus.RaiseSeasonChanged(currentSeason, currentHoliday);
            }
        }

        /// <summary>Maps a month and hemisphere to the four broad seasons.</summary>
        public static SeasonType ResolveSeason(int month, bool isSouthernHemisphere)
        {
            int normalizedMonth = Mathf.Clamp(month, 1, 12);
            SeasonType northern;
            if (normalizedMonth >= 3 && normalizedMonth <= 5)
            {
                northern = SeasonType.Spring;
            }
            else if (normalizedMonth >= 6 && normalizedMonth <= 8)
            {
                northern = SeasonType.Summer;
            }
            else if (normalizedMonth >= 9 && normalizedMonth <= 11)
            {
                northern = SeasonType.Fall;
            }
            else
            {
                northern = SeasonType.Winter;
            }

            if (!isSouthernHemisphere)
            {
                return northern;
            }
            return northern == SeasonType.Spring ? SeasonType.Fall
                : northern == SeasonType.Summer ? SeasonType.Winter
                : northern == SeasonType.Fall ? SeasonType.Spring : SeasonType.Summer;
        }

        private static string ResolveRegionCode()
        {
            try
            {
                RegionInfo region = new RegionInfo(CultureInfo.CurrentCulture.Name);
                return region.TwoLetterISORegionName;
            }
            catch (Exception)
            {
                return "*";
            }
        }
    }
}
