using StardewModdingAPI;
using StardewValley;
using StardewValley.Enchantments;

namespace PolymorphicAetherRing.Framework;

internal static class FusedWeaponTooltip
{
    public static string AppendToDescription(
        Item item,
        string description,
        ITranslationHelper translations)
    {
        return string.Join(
            Environment.NewLine,
            description,
            string.Empty,
            GetDetails(item, translations));
    }

    public static string GetDetails(Item item, ITranslationHelper translations)
    {
        try
        {
            FusedWeaponData? fusionData = FusedWeaponData.FromModData(item);
            if (fusionData is null)
                return translations.Get("tooltip.fusion.empty").ToString();

            return string.Join(
                Environment.NewLine,
                translations.Get("tooltip.fusion.heading", new { weapon = fusionData.WeaponName }),
                translations.Get("tooltip.fusion.damage", new
                {
                    min = fusionData.MinDamage,
                    max = fusionData.MaxDamage,
                    type = GetWeaponType(fusionData.WeaponType, translations)
                }),
                translations.Get("tooltip.fusion.speed", new
                {
                    speed = fusionData.Speed,
                    interval = fusionData.GetAttackIntervalMs()
                }),
                translations.Get("tooltip.fusion.critical", new
                {
                    chance = fusionData.CritChance * 100,
                    multiplier = fusionData.CritMultiplier
                }),
                translations.Get("tooltip.fusion.range", new
                {
                    knockback = fusionData.Knockback,
                    range = fusionData.GetAttackRadius()
                }),
                translations.Get("tooltip.fusion.enchantments", new
                {
                    enchantments = GetEnchantmentSummary(fusionData, translations)
                }));
        }
        catch (InvalidDataException)
        {
            return translations.Get("tooltip.fusion.corrupt").ToString();
        }
    }

    private static string GetWeaponType(int weaponType, ITranslationHelper translations)
    {
        string key = weaponType switch
        {
            1 => "tooltip.fusion.type.dagger",
            2 => "tooltip.fusion.type.club",
            _ => "tooltip.fusion.type.sword"
        };
        return translations.Get(key).ToString();
    }

    private static string GetEnchantmentSummary(
        FusedWeaponData fusionData,
        ITranslationHelper translations)
    {
        if (fusionData.Enchantments.Count == 0)
            return translations.Get("tooltip.fusion.enchantments.none").ToString();

        return string.Join(
            ", ",
            FusedWeaponRestorer.CreateEnchantments(fusionData.Enchantments)
                .Select(enchantment => FormatEnchantment(enchantment, translations)));
    }

    private static string FormatEnchantment(
        BaseEnchantment enchantment,
        ITranslationHelper translations)
    {
        return translations.Get("tooltip.fusion.enchantment", new
        {
            name = enchantment.GetDisplayName(),
            level = enchantment.Level
        }).ToString();
    }
}
