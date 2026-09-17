using System.IO;
using System.Reflection;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Tools;

namespace PolymorphicAetherRing.Framework;

internal static class FusedWeaponRestorer
{
    /// <summary>
    /// Flow: 按物品 ID 创建武器，按原版复制语义恢复附魔列表，再用熔铸快照校准战斗属性。
    /// Failure: 任一附魔无法完整恢复时抛出异常，禁止调用方覆盖仍保存在饰品中的旧数据。
    /// </summary>
    public static MeleeWeapon CreateWeapon(FusedWeaponData data)
    {
        if (!data.IsValid)
            throw new InvalidDataException("Fused weapon ID is missing.");

        Item item = ItemRegistry.Create(data.WeaponId);
        if (item is not MeleeWeapon weapon)
            throw new InvalidDataException($"Fused item '{data.WeaponId}' is not a melee weapon.");

        RestoreWeaponState(weapon, data);
        return weapon;
    }

    /// <summary>
    /// Flow: 临时战斗武器沿用原武器 ID 与完整战斗状态，使原版依赖武器身份的规则保持生效。
    /// </summary>
    internal static MeleeWeapon CreateCombatWeapon(FusedWeaponData data)
    {
        return CreateWeapon(data);
    }

    internal static IReadOnlyList<BaseEnchantment> CreateEnchantments(
        IEnumerable<FusedEnchantmentData> savedEnchantments)
    {
        return savedEnchantments.Select(CreateEnchantment).ToList();
    }

    internal static void RestoreEnchantment(MeleeWeapon weapon, FusedEnchantmentData saved)
    {
        BaseEnchantment enchantment = CreateEnchantment(saved);
        weapon.enchantments.Add(enchantment);
        enchantment.ApplyTo(weapon);
    }

    internal static void RestoreWeaponState(MeleeWeapon weapon, FusedWeaponData data)
    {
        data.Validate();
        foreach (FusedEnchantmentData savedEnchantment in data.Enchantments)
            RestoreEnchantment(weapon, savedEnchantment);

        RestoreCombatStats(weapon, data);
    }

    private static BaseEnchantment CreateEnchantment(FusedEnchantmentData saved)
    {
        Type type = FindEnchantmentType(saved)
            ?? throw new InvalidDataException($"Enchantment type '{saved.TypeName}' is not loaded.");

        if (type.IsAbstract || !typeof(BaseEnchantment).IsAssignableFrom(type))
            throw new InvalidDataException($"Type '{saved.TypeName}' is not a concrete enchantment.");

        BaseEnchantment enchantment;
        try
        {
            enchantment = Activator.CreateInstance(type) as BaseEnchantment
                ?? throw new InvalidDataException($"Enchantment '{saved.TypeName}' could not be created.");
        }
        catch (Exception exception) when (exception is MissingMethodException
                                          or MemberAccessException
                                          or TargetInvocationException)
        {
            throw new InvalidDataException(
                $"Enchantment '{saved.TypeName}' could not be created.",
                exception);
        }

        // Preserve the level captured from the original weapon. A mod may raise an
        // enchantment above the implementation's default maximum, and the weapon
        // must still be returned with the same observable state.
        if (saved.Level < 1)
            throw new InvalidDataException(
                $"Enchantment '{saved.TypeName}' has invalid level {saved.Level}.");

        enchantment.Level = saved.Level;
        return enchantment;
    }

    private static Type? FindEnchantmentType(FusedEnchantmentData saved)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        if (!string.IsNullOrWhiteSpace(saved.AssemblyName))
        {
            Assembly? owner = assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, saved.AssemblyName, StringComparison.Ordinal));
            return owner?.GetType(saved.TypeName, throwOnError: false, ignoreCase: false);
        }

        if (saved.TypeName.Contains('.', StringComparison.Ordinal))
        {
            return SelectUnambiguousType(
                saved.TypeName,
                assemblies
                    .Select(assembly => assembly.GetType(saved.TypeName, throwOnError: false, ignoreCase: false))
                    .OfType<Type>()
                    .Where(type => typeof(BaseEnchantment).IsAssignableFrom(type)));
        }

        Assembly gameAssembly = typeof(Game1).Assembly;
        Type? vanillaType = GetLoadableTypes(gameAssembly).FirstOrDefault(type =>
            type.Name == saved.TypeName && typeof(BaseEnchantment).IsAssignableFrom(type));
        if (vanillaType != null)
            return vanillaType;

        return SelectUnambiguousType(
            saved.TypeName,
            assemblies
                .Where(assembly => assembly != gameAssembly)
                .SelectMany(GetLoadableTypes)
                .Where(type =>
                    type.Name == saved.TypeName
                    && typeof(BaseEnchantment).IsAssignableFrom(type)));
    }

    private static Type? SelectUnambiguousType(string savedName, IEnumerable<Type> candidates)
    {
        Type[] matches = candidates.Distinct().ToArray();
        if (matches.Length <= 1)
            return matches.SingleOrDefault();

        string identities = string.Join(", ", matches.Select(type => type.AssemblyQualifiedName));
        throw new InvalidDataException(
            $"Legacy enchantment name '{savedName}' is ambiguous between: {identities}.");
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static void RestoreCombatStats(MeleeWeapon weapon, FusedWeaponData data)
    {
        weapon.minDamage.Value = data.MinDamage;
        weapon.maxDamage.Value = data.MaxDamage;
        weapon.speed.Value = data.Speed;
        weapon.critChance.Value = data.CritChance;
        weapon.critMultiplier.Value = data.CritMultiplier;
        weapon.knockback.Value = data.Knockback;
        weapon.addedAreaOfEffect.Value = data.AreaOfEffect;
        weapon.type.Value = data.WeaponType;
    }
}
