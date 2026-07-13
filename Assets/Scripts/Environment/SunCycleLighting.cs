using Core.TimeOfDay;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// WILDCUT binder for Core.TimeOfDay — drives the scene's directional light
/// from the clock. Deliberately simple and cheap (no volumetrics/clouds):
///   • sun pitch rotates with the hour (06:00 horizon → 12:00 zenith → 18:00 horizon)
///   • intensity follows sun elevation (0 at night)
///   • ambient light lerps to a faint blue at night so the island stays readable.
/// Also logs OnSunrise/OnSunset — the hook the sleep state (teammate's chapter)
/// and future night threats will subscribe to.
/// </summary>
public class SunCycleLighting : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    public TimeOfDayService timeOfDay;
    public Light sun;

    [Header("Sun")]
    [Tooltip("Peak intensity of the directional light at noon.")]
    public float dayIntensity = 1.15f;
    [Tooltip("Fixed compass heading of the sun path (degrees).")]
    public float sunHeading = 30f;

    [Header("Ambient")]
    public Color dayAmbient   = new Color(0.55f, 0.57f, 0.60f);
    [Tooltip("Faint blue night ambient (design: 'gece hafif mavi').")]
    public Color nightAmbient = new Color(0.09f, 0.12f, 0.22f);

    [Header("Debug")]
    public bool logSunEvents = true;
    public bool logEveryHour;

    bool subscribed;

    void Start() { TryResolve(); }

    void OnDestroy()
    {
        if (subscribed && timeOfDay != null)
        {
            timeOfDay.OnSunrise     -= HandleSunrise;
            timeOfDay.OnSunset      -= HandleSunset;
            timeOfDay.OnHourChanged -= HandleHour;
        }
    }

    void TryResolve()
    {
        if (timeOfDay == null) timeOfDay = TimeOfDayService.Instance;
        if (sun == null)
        {
            foreach (Light l in FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) { sun = l; break; }
        }

        if (!subscribed && timeOfDay != null)
        {
            timeOfDay.OnSunrise     += HandleSunrise;
            timeOfDay.OnSunset      += HandleSunset;
            timeOfDay.OnHourChanged += HandleHour;
            RenderSettings.ambientMode = AmbientMode.Flat;   // flat colour we can drive
            subscribed = true;
        }
    }

    void LateUpdate()
    {
        if (timeOfDay == null || sun == null) { TryResolve(); if (timeOfDay == null || sun == null) return; }

        // 06:00 → 0° (horizon), 12:00 → 90° (zenith), 18:00 → 180°, night below.
        float sunPitch = (timeOfDay.Hour - 6f) / 24f * 360f;
        sun.transform.rotation = Quaternion.Euler(sunPitch, sunHeading, 0f);

        float elevation = Mathf.Clamp01(Mathf.Sin(sunPitch * Mathf.Deg2Rad));
        sun.intensity = elevation * dayIntensity;

        // Ambient reaches full day colour quickly after sunrise (x2 ramp).
        RenderSettings.ambientLight = Color.Lerp(nightAmbient, dayAmbient,
                                                 Mathf.Clamp01(elevation * 2f));
    }

    void HandleSunrise()      { if (logSunEvents) Debug.Log($"[SunCycleLighting] Gün doğdu (OnSunrise, saat {timeOfDay.Hour:F1})."); }
    void HandleSunset()       { if (logSunEvents) Debug.Log($"[SunCycleLighting] Gün battı (OnSunset, saat {timeOfDay.Hour:F1})."); }
    void HandleHour(int hour) { if (logEveryHour) Debug.Log($"[SunCycleLighting] Saat {hour:00}:00."); }
}
