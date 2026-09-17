using System.Globalization;
using System.IO;
using PolymorphicAetherRing;
using PolymorphicAetherRing.Framework;
using StardewValley.Enchantments;
using StardewValley.Tools;
using Xunit;

namespace PolymorphicAetherRing.Tests;

public class FusedEnchantmentDataCodecTests
{
    [Fact]
    public void RoundTripPreservesGalaxySoulLevelAndTypeIdentity()
    {
        var source = new List<FusedEnchantmentData>
        {
            new()
            {
                TypeName = "StardewValley.Enchantments.GalaxySoulEnchantment",
                AssemblyName = "Stardew Valley",
                Level = 2
            }
        };

        string json = FusedEnchantmentDataCodec.Serialize(source);
        FusedEnchantmentData restored = Assert.Single(FusedEnchantmentDataCodec.Deserialize(json));

        Assert.Equal(source[0].TypeName, restored.TypeName);
        Assert.Equal(source[0].AssemblyName, restored.AssemblyName);
        Assert.Equal(2, restored.Level);
    }

    [Fact]
    public void LegacyTypeNamesRemainReadableAtKnownDefaultLevel()
    {
        List<FusedEnchantmentData> restored =
            FusedEnchantmentDataCodec.DeserializeLegacy("RubyEnchantment,GalaxySoulEnchantment");

        Assert.Collection(
            restored,
            ruby =>
            {
                Assert.Equal("RubyEnchantment", ruby.TypeName);
                Assert.Null(ruby.AssemblyName);
                Assert.Equal(1, ruby.Level);
            },
            soul =>
            {
                Assert.Equal("GalaxySoulEnchantment", soul.TypeName);
                Assert.Null(soul.AssemblyName);
                Assert.Equal(1, soul.Level);
            });
    }

    [Fact]
    public void InvalidPersistedLevelIsRejected()
    {
        const string json = "[{\"TypeName\":\"GalaxySoulEnchantment\",\"Level\":0}]";

        Assert.Throws<InvalidDataException>(() => FusedEnchantmentDataCodec.Deserialize(json));
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"TypeName\":\"GalaxySoulEnchantment\"}]")]
    public void IncompleteEnchantmentEntryIsRejectedAsInvalidData(string json)
    {
        Assert.Throws<InvalidDataException>(() => FusedEnchantmentDataCodec.Deserialize(json));
    }

    [Fact]
    public void ModDataRoundTripPreservesGalaxySoulLevel()
    {
        var holder = new MeleeWeapon();
        var source = new FusedWeaponData
        {
            WeaponId = "(W)4",
            WeaponName = "Galaxy Sword",
            Enchantments = new List<FusedEnchantmentData>
            {
                CreateSavedEnchantment(typeof(GalaxySoulEnchantment), 2)
            }
        };

        source.SaveToModData(holder);
        FusedWeaponData restored = Assert.IsType<FusedWeaponData>(FusedWeaponData.FromModData(holder));

        Assert.Equal("(W)4", restored.WeaponId);
        Assert.Equal(2, Assert.Single(restored.Enchantments).Level);
        Assert.False(restored.HasLegacyEnchantmentData);
    }

    [Fact]
    public void InvalidNewDataDoesNotPartiallyOverwriteExistingModData()
    {
        const string prefix = "xixifu.AetherTrinket/";
        var holder = new MeleeWeapon();
        holder.modData[prefix + "WeaponId"] = "(W)4";
        holder.modData[prefix + "WeaponName"] = "Existing weapon";
        var invalid = new FusedWeaponData
        {
            WeaponId = "(W)62",
            WeaponName = "Replacement",
            Enchantments = new List<FusedEnchantmentData>
            {
                CreateSavedEnchantment(typeof(GalaxySoulEnchantment), 0)
            }
        };

        Assert.Throws<InvalidDataException>(() => invalid.SaveToModData(holder));

        Assert.Equal("(W)4", holder.modData[prefix + "WeaponId"]);
        Assert.Equal("Existing weapon", holder.modData[prefix + "WeaponName"]);
        Assert.False(holder.modData.ContainsKey(prefix + "EnchantmentsV2"));
    }

    [Fact]
    public void IncompleteFusionDataIsRejected()
    {
        const string prefix = "xixifu.AetherTrinket/";
        var holder = new MeleeWeapon();
        holder.modData[prefix + "WeaponId"] = "(W)4";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            FusedWeaponData.FromModData(holder));

        Assert.Contains("WeaponName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrphanedFusionFieldWithoutWeaponIdIsRejected()
    {
        var holder = new MeleeWeapon();
        holder.modData["xixifu.AetherTrinket/EnchantmentsV2"] = "[]";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            FusedWeaponData.FromModData(holder));

        Assert.Contains("ID is missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptNewSignatureClearsPreviousCombatCache()
    {
        const string prefix = "xixifu.AetherTrinket/";
        var holder = new MeleeWeapon();
        new FusedWeaponData
        {
            WeaponId = "(W)4",
            WeaponName = "Galaxy Sword"
        }.SaveToModData(holder);
        holder.modData[prefix + "EnchantmentsV2"] = "{broken";
        string? cachedSignature = "previous-signature";
        FusedWeaponData? cachedData = new FusedWeaponData
        {
            WeaponId = "(W)62",
            WeaponName = "Previous weapon"
        };

        Assert.Throws<InvalidDataException>(() => RingCombatManager.RefreshFusionDataCache(
            holder,
            "corrupt-signature",
            ref cachedSignature,
            ref cachedData));

        Assert.Equal("corrupt-signature", cachedSignature);
        Assert.Null(cachedData);
    }

    [Fact]
    public void RestorerPreservesGalaxySoulLevel()
    {
        var weapon = new MeleeWeapon();

        FusedWeaponRestorer.RestoreEnchantment(
            weapon,
            CreateSavedEnchantment(typeof(GalaxySoulEnchantment), 2));

        BaseEnchantment restored = Assert.Single(weapon.enchantments);
        Assert.IsType<GalaxySoulEnchantment>(restored);
        Assert.Equal(2, restored.Level);
    }

    [Fact]
    public void RestorerPreservesHighEnchantmentLevelAndSpeed()
    {
        var weapon = new MeleeWeapon();
        var data = new FusedWeaponData
        {
            WeaponId = "(W)62",
            Speed = 12,
            Enchantments = new List<FusedEnchantmentData>
            {
                CreateSavedEnchantment(typeof(TestForgeEnchantment), 4)
            }
        };

        FusedWeaponRestorer.RestoreWeaponState(weapon, data);

        Assert.Equal(12, weapon.speed.Value);
        Assert.Equal(4, Assert.Single(weapon.enchantments).Level);
    }

    [Fact]
    public void CombatWeaponRestoresSavedEffectsLevelsAndType()
    {
        var combatWeapon = new MeleeWeapon();
        var fusionData = new FusedWeaponData
        {
            WeaponId = "(W)4",
            WeaponType = 1,
            Enchantments = new List<FusedEnchantmentData>
            {
                CreateSavedEnchantment(typeof(CrusaderEnchantment), 1),
                CreateSavedEnchantment(typeof(GalaxySoulEnchantment), 2),
                CreateSavedEnchantment(typeof(TestForgeEnchantment), 3)
            }
        };

        FusedWeaponRestorer.RestoreWeaponState(combatWeapon, fusionData);

        Assert.Equal(1, combatWeapon.type.Value);
        Assert.True(combatWeapon.hasEnchantmentOfType<CrusaderEnchantment>());
        Assert.Collection(
            combatWeapon.enchantments,
            crusader => Assert.Equal(1, Assert.IsType<CrusaderEnchantment>(crusader).Level),
            galaxySoul => Assert.Equal(2, Assert.IsType<GalaxySoulEnchantment>(galaxySoul).Level),
            forge => Assert.Equal(3, Assert.IsType<TestForgeEnchantment>(forge).Level));
    }

    [Fact]
    public void FusionDataUsesInvariantCultureAndReadsLegacyCommaDecimal()
    {
        var source = new FusedWeaponData
        {
            WeaponId = "(W)4",
            CritChance = 0.15f,
            CritMultiplier = 3.5f,
            Knockback = 1.25f
        };
        var holder = new MeleeWeapon();

        using (new CultureScope("de-DE"))
            source.SaveToModData(holder);

        Assert.Equal("0.15", holder.modData["xixifu.AetherTrinket/CritChance"]);
        holder.modData["xixifu.AetherTrinket/CritChance"] = "0,15";
        holder.modData["xixifu.AetherTrinket/CritMultiplier"] = "3,5";
        holder.modData["xixifu.AetherTrinket/Knockback"] = "1,25";

        using (new CultureScope("en-US"))
        {
            FusedWeaponData restored = Assert.IsType<FusedWeaponData>(FusedWeaponData.FromModData(holder));
            Assert.Equal(0.15f, restored.CritChance);
            Assert.Equal(3.5f, restored.CritMultiplier);
            Assert.Equal(1.25f, restored.Knockback);
        }
    }

    [Fact]
    public void InvalidFusionDamageRangeIsRejectedBeforeCombat()
    {
        var holder = new MeleeWeapon();
        holder.modData["xixifu.AetherTrinket/WeaponId"] = "(W)4";
        holder.modData["xixifu.AetherTrinket/MinDamage"] = "100";
        holder.modData["xixifu.AetherTrinket/MaxDamage"] = "50";

        Assert.Throws<InvalidDataException>(() => FusedWeaponData.FromModData(holder));
    }

    [Fact]
    public void ConfigResetPreservesReferenceAndRestoresDefaults()
    {
        var config = new ModConfig
        {
            DamageMultiplier = 2.5f,
            RangeMultiplier = 2f,
            CooldownMultiplier = 0.5f,
            ReturnFusedWeapon = true,
            AndroidLongPressMs = 1_000
        };
        ModConfig reference = config;

        config.ResetToDefaults();

        Assert.Same(reference, config);
        Assert.Equal(1f, config.DamageMultiplier);
        Assert.Equal(1f, config.RangeMultiplier);
        Assert.Equal(1f, config.CooldownMultiplier);
        Assert.False(config.ReturnFusedWeapon);
        Assert.Equal(500, config.AndroidLongPressMs);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(0f)]
    [InlineData(5.1f)]
    public void InvalidDamageMultiplierIsRejected(float value)
    {
        var config = new ModConfig { DamageMultiplier = value };

        Assert.Throws<InvalidDataException>(config.Validate);
    }

    [Fact]
    public void InvalidRangeCooldownAndLongPressSettingsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            new ModConfig { RangeMultiplier = -1f }.Validate());
        Assert.Throws<InvalidDataException>(() =>
            new ModConfig { CooldownMultiplier = float.PositiveInfinity }.Validate());
        Assert.Throws<InvalidDataException>(() =>
            new ModConfig { AndroidLongPressMs = 199 }.Validate());
    }

    [Fact]
    public void TemporaryCombatWeaponRestoresExistingTemporaryItem()
    {
        object? temporaryItem = new object();
        object previousTemporaryItem = temporaryItem;
        object fusionWeapon = new object();

        RingCombatManager.WithTemporaryItem(
            () => temporaryItem,
            item => temporaryItem = item,
            fusionWeapon,
            () => Assert.Same(fusionWeapon, temporaryItem));

        Assert.Same(previousTemporaryItem, temporaryItem);
    }

    [Fact]
    public void TemporaryCombatWeaponRestoresExistingTemporaryItemAfterFailure()
    {
        object? temporaryItem = new object();
        object previousTemporaryItem = temporaryItem;

        Assert.Throws<InvalidOperationException>(() => RingCombatManager.WithTemporaryItem(
            () => temporaryItem,
            item => temporaryItem = item,
            new object(),
            () => throw new InvalidOperationException("Injected combat failure.")));

        Assert.Same(previousTemporaryItem, temporaryItem);
    }

    [Fact]
    public void RestorerDoesNotMergeDuplicateForgeEntries()
    {
        var weapon = new MeleeWeapon();

        FusedWeaponRestorer.RestoreEnchantment(
            weapon,
            CreateSavedEnchantment(typeof(TestForgeEnchantment), 2));
        FusedWeaponRestorer.RestoreEnchantment(
            weapon,
            CreateSavedEnchantment(typeof(TestForgeEnchantment), 1));

        Assert.Collection(
            weapon.enchantments,
            first => Assert.Equal(2, Assert.IsType<TestForgeEnchantment>(first).Level),
            second => Assert.Equal(1, Assert.IsType<TestForgeEnchantment>(second).Level));
    }

    [Fact]
    public void RestorerDoesNotReplaceMultiplePrimaryEnchantments()
    {
        var weapon = new MeleeWeapon();

        FusedWeaponRestorer.RestoreEnchantment(
            weapon,
            CreateSavedEnchantment(typeof(ArtfulEnchantment), 1));
        FusedWeaponRestorer.RestoreEnchantment(
            weapon,
            CreateSavedEnchantment(typeof(VampiricEnchantment), 1));

        Assert.Collection(
            weapon.enchantments,
            first => Assert.IsType<ArtfulEnchantment>(first),
            second => Assert.IsType<VampiricEnchantment>(second));
    }

    [Fact]
    public void EnchantmentWithoutDefaultConstructorIsRejectedAsInvalidData()
    {
        var weapon = new MeleeWeapon();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            FusedWeaponRestorer.RestoreEnchantment(
                weapon,
                CreateSavedEnchantment(typeof(ConstructorOnlyEnchantment), 1)));

        Assert.IsType<MissingMethodException>(exception.InnerException);
        Assert.Empty(weapon.enchantments);
    }

    [Fact]
    public void AmbiguousLegacyModEnchantmentIsRejected()
    {
        var weapon = new MeleeWeapon();
        var saved = new FusedEnchantmentData
        {
            TypeName = "AmbiguousEnchantment",
            Level = 1
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            FusedWeaponRestorer.RestoreEnchantment(weapon, saved));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(weapon.enchantments);
    }

    [Fact]
    public void MidWriteFailureRestoresAllPreviousValues()
    {
        var initial = new Dictionary<string, string>
        {
            ["WeaponId"] = "(W)4",
            ["WeaponName"] = "Existing",
            ["EnchantmentIds"] = "GalaxySoulEnchantment"
        };
        var store = new FailingModDataStore(initial, failOnMutation: 2);
        var update = new FusedWeaponModDataUpdate(
            new Dictionary<string, string>
            {
                ["WeaponId"] = "(W)62",
                ["WeaponName"] = "Replacement",
                ["EnchantmentsV2"] = "[]"
            },
            new[] { "EnchantmentIds" });

        Assert.Throws<InvalidOperationException>(() => update.ApplyTo(store));

        Assert.Equal(
            initial.OrderBy(pair => pair.Key),
            store.Values.OrderBy(pair => pair.Key));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string name)
        {
            var culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }

    private static FusedEnchantmentData CreateSavedEnchantment(Type type, int level)
    {
        return new FusedEnchantmentData
        {
            TypeName = type.FullName!,
            AssemblyName = type.Assembly.GetName().Name,
            Level = level
        };
    }

    private sealed class FailingModDataStore : IFusedWeaponModDataStore
    {
        private readonly Dictionary<string, string> _values;
        private readonly int _failOnMutation;
        private int _mutationCount;
        private bool _failureRaised;

        public FailingModDataStore(Dictionary<string, string> values, int failOnMutation)
        {
            _values = new Dictionary<string, string>(values);
            _failOnMutation = failOnMutation;
        }

        public IReadOnlyDictionary<string, string> Values => _values;

        public bool TryGetValue(string key, out string? value)
        {
            return _values.TryGetValue(key, out value);
        }

        public void SetValue(string key, string value)
        {
            FailIfRequested();
            _values[key] = value;
        }

        public void Remove(string key)
        {
            FailIfRequested();
            _values.Remove(key);
        }

        private void FailIfRequested()
        {
            _mutationCount++;
            if (!_failureRaised && _mutationCount == _failOnMutation)
            {
                _failureRaised = true;
                throw new InvalidOperationException("Injected write failure.");
            }
        }
    }
}

public sealed class TestForgeEnchantment : BaseEnchantment
{
    public override bool IsForge() => true;

    public override int GetMaximumLevel() => 3;
}

public sealed class ConstructorOnlyEnchantment : BaseEnchantment
{
    public ConstructorOnlyEnchantment(int value)
    {
    }
}
