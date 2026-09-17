using System.IO;
using System.Text.Json;

namespace PolymorphicAetherRing.Framework;

/// <summary>可持久化的武器附魔状态。</summary>
public sealed class FusedEnchantmentData
{
    /// <summary>附魔类型的完整名称。</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>定义附魔类型的程序集简单名称。</summary>
    public string? AssemblyName { get; set; }

    /// <summary>附魔等级。必须由持久化数据或提取流程显式提供。</summary>
    public int Level { get; set; }
}

internal static class FusedEnchantmentDataCodec
{
    public static string Serialize(IReadOnlyCollection<FusedEnchantmentData> enchantments)
    {
        Validate(enchantments);
        return JsonSerializer.Serialize(enchantments);
    }

    public static List<FusedEnchantmentData> Deserialize(string json)
    {
        try
        {
            List<FusedEnchantmentData>? enchantments =
                JsonSerializer.Deserialize<List<FusedEnchantmentData?>>(json)
                    ?.Select((enchantment, index) => enchantment
                        ?? throw new InvalidDataException($"Enchantment entry {index} is null."))
                    .ToList();
            if (enchantments is null)
                throw new InvalidDataException("Enchantment data is null.");

            Validate(enchantments);
            return enchantments;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Enchantment data is not valid JSON.", exception);
        }
    }

    public static List<FusedEnchantmentData> DeserializeLegacy(string typeNames)
    {
        return typeNames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(typeName => new FusedEnchantmentData
            {
                TypeName = typeName,
                Level = 1
            })
            .ToList();
    }

    private static void Validate(IEnumerable<FusedEnchantmentData> enchantments)
    {
        foreach (FusedEnchantmentData enchantment in enchantments)
        {
            if (string.IsNullOrWhiteSpace(enchantment.TypeName))
                throw new InvalidDataException("Enchantment type name is missing.");
            if (enchantment.Level < 1)
                throw new InvalidDataException($"Enchantment '{enchantment.TypeName}' has invalid level {enchantment.Level}.");
        }
    }
}
