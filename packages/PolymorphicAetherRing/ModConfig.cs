namespace PolymorphicAetherRing;

/// <summary>模组配置类</summary>
public class ModConfig
{
    public const float MinimumDamageMultiplier = 0.1f;
    public const float MaximumDamageMultiplier = 5.0f;
    public const float MinimumRangeMultiplier = 0.5f;
    public const float MaximumRangeMultiplier = 3.0f;
    public const float MinimumCooldownMultiplier = 0.1f;
    public const float MaximumCooldownMultiplier = 2.0f;
    public const int MinimumAndroidLongPressMs = 200;
    public const int MaximumAndroidLongPressMs = 1500;

    /// <summary>伤害倍率 (0.1 - 5.0)</summary>
    public float DamageMultiplier { get; set; } = 1.0f;

    /// <summary>范围倍率 (0.5 - 3.0)</summary>
    public float RangeMultiplier { get; set; } = 1.0f;

    /// <summary>冷却倍率 (0.1 - 2.0)</summary>
    public float CooldownMultiplier { get; set; } = 1.0f;

    /// <summary>熔铸时是否返还旧武器</summary>
    public bool ReturnFusedWeapon { get; set; } = false;

    /// <summary>安卓端长按触发阈值（毫秒），默认500ms</summary>
    public int AndroidLongPressMs { get; set; } = 500;

    internal void Validate()
    {
        ValidateRange(DamageMultiplier, MinimumDamageMultiplier, MaximumDamageMultiplier, nameof(DamageMultiplier));
        ValidateRange(RangeMultiplier, MinimumRangeMultiplier, MaximumRangeMultiplier, nameof(RangeMultiplier));
        ValidateRange(CooldownMultiplier, MinimumCooldownMultiplier, MaximumCooldownMultiplier, nameof(CooldownMultiplier));

        if (AndroidLongPressMs is < MinimumAndroidLongPressMs or > MaximumAndroidLongPressMs)
        {
            throw new InvalidDataException(
                $"{nameof(AndroidLongPressMs)} must be between {MinimumAndroidLongPressMs} and {MaximumAndroidLongPressMs}.");
        }
    }

    internal void ResetToDefaults()
    {
        var defaults = new ModConfig();
        DamageMultiplier = defaults.DamageMultiplier;
        RangeMultiplier = defaults.RangeMultiplier;
        CooldownMultiplier = defaults.CooldownMultiplier;
        ReturnFusedWeapon = defaults.ReturnFusedWeapon;
        AndroidLongPressMs = defaults.AndroidLongPressMs;
    }

    private static void ValidateRange(float value, float minimum, float maximum, string propertyName)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new InvalidDataException(
                $"{propertyName} must be a finite value between {minimum} and {maximum}.");
        }
    }
}
