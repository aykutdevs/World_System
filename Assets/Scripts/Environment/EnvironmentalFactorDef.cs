using UnityEngine;

/// <summary>
/// WILDCUT Chapter 7 / Table 12 — one environmental factor the world exposes
/// (temperature, radiation, humidity...). Pure data: a new factor — or a new
/// climate like a "Cold Region" — is just another asset with different values,
/// assigned to an IslandArchetype's factor list. No code.
/// Create via: Assets ▸ Create ▸ WILDCUT ▸ Environment ▸ Environmental Factor.
/// </summary>
[CreateAssetMenu(menuName = "WILDCUT/Environment/Environmental Factor", fileName = "Factor_New")]
public class EnvironmentalFactorDef : ScriptableObject
{
    public enum Calculation
    {
        Constant,          // same value everywhere
        AltitudeGradient   // baseValue at sea level, changes linearly with altitude
    }

    [Tooltip("Key the health system queries, e.g. 'temperature', 'radiation'.")]
    public string factorId = "temperature";

    public Calculation calculation = Calculation.AltitudeGradient;

    [Tooltip("Value at sea level (the water surface).")]
    public float baseValue = 25f;

    [Tooltip("Change per world meter ABOVE sea level (AltitudeGradient only). " +
             "Negative = drops with altitude, e.g. -0.07 °C/m ≈ 25° shore → ~5° peak.")]
    public float changePerAltitudeMeter = -0.07f;

    [Tooltip("Final value is clamped to this range.")]
    public float minValue = -100f;
    public float maxValue = 100f;

    /// <param name="altitudeAboveSea">World meters above the water surface
    /// (underwater positions evaluate at sea level).</param>
    public float Evaluate(float altitudeAboveSea)
    {
        float v = baseValue;
        if (calculation == Calculation.AltitudeGradient)
            v += Mathf.Max(0f, altitudeAboveSea) * changePerAltitudeMeter;
        return Mathf.Clamp(v, minValue, maxValue);
    }

    void OnValidate()
    {
        if (maxValue < minValue) maxValue = minValue;
    }
}
