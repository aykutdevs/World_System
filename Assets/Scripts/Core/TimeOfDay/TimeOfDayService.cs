using System;
using UnityEngine;

// ---- Core.TimeOfDay — day/night clock (reusable across projects) ----
// Pure time-keeping: a 0-24 hour value advancing at a configurable real-time
// day length, with hour/sunrise/sunset events. It does NOT touch lights,
// ambient or any game object — visual binders subscribe from the game side.

namespace Core.TimeOfDay
{
    public class TimeOfDayService : MonoBehaviour
    {
        /// <summary>Scene-singleton convenience accessor (set in OnEnable).</summary>
        public static TimeOfDayService Instance { get; private set; }

        [Header("Clock")]
        [Tooltip("Hour of day the clock starts at when entering Play mode.")]
        [Range(0f, 24f)] public float startHour = 9f;
        [Tooltip("Length of one full 24h in-game day, in REAL minutes.")]
        [Min(0.05f)] public float dayLengthMinutes = 12f;
        [Tooltip("Debug/test multiplier on top of day length (1 = normal). " +
                 "Crank it up to fast-forward through sunset/sunrise.")]
        [Min(0f)] public float timeScale = 1f;

        [Header("Sun events")]
        [Range(0f, 24f)] public float sunriseHour = 6f;
        [Range(0f, 24f)] public float sunsetHour = 18f;

        /// <summary>Current time of day, 0 (midnight) .. 24.</summary>
        public float Hour { get; private set; }
        /// <summary>Hour / 24 — handy for rotations and curve lookups.</summary>
        public float NormalizedTime => Hour / 24f;
        public bool IsDay => Hour >= sunriseHour && Hour < sunsetHour;

        /// <summary>Fired once per whole in-game hour crossed, with the new hour (0-23).</summary>
        public event Action<int> OnHourChanged;
        public event Action OnSunrise;
        public event Action OnSunset;

        void OnEnable()  { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        void Awake() => Hour = Mathf.Repeat(startHour, 24f);

        void Update()
        {
            float hoursPerSecond = 24f / (dayLengthMinutes * 60f);
            Advance(Time.deltaTime * hoursPerSecond * timeScale);
        }

        /// <summary>Jumps the clock (no events fired for the skipped span).</summary>
        public void SetHour(float hour) => Hour = Mathf.Repeat(hour, 24f);

        // Advances the clock and fires every event whose moment was crossed.
        // Works across the 24→0 wrap by unwrapping targets past 'prev'.
        void Advance(float deltaHours)
        {
            if (deltaHours <= 0f) return;

            float prev = Hour;
            float next = prev + deltaHours;

            if (OnHourChanged != null)
                for (int h = Mathf.FloorToInt(prev) + 1; h <= Mathf.FloorToInt(next); h++)
                    OnHourChanged.Invoke(((h % 24) + 24) % 24);

            if (Crossed(prev, next, sunriseHour)) OnSunrise?.Invoke();
            if (Crossed(prev, next, sunsetHour))  OnSunset?.Invoke();

            Hour = Mathf.Repeat(next, 24f);
        }

        static bool Crossed(float prev, float next, float target)
        {
            float t = target;
            while (t <= prev) t += 24f;   // first occurrence strictly after 'prev'
            return t <= next;
        }
    }
}
