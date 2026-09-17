using System.Runtime.ExceptionServices;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.Tools;
using PolymorphicAetherRing;

namespace PolymorphicAetherRing.Framework;

/// <summary>战斗光环管理器 - 处理360度自动攻击</summary>
public class RingCombatManager
{
    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private readonly ModConfig _config;

    /// <summary>上次攻击后经过的毫秒数</summary>
    private double _timeSinceLastAttack;

    /// <summary>当前的攻击冷却时间</summary>
    private int _currentCooldownMs;

    /// <summary>缓存的熔铸数据</summary>
    private FusedWeaponData? _cachedFusionData;

    /// <summary>缓存的熔铸数据签名</summary>
    private string? _cachedFusionSignature;

    /// <summary>按熔铸数据重建、只在光环攻击中临时持有的武器。</summary>
    private MeleeWeapon? _cachedFusionWeapon;

    public RingCombatManager(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        _helper = helper;
        _monitor = monitor;
        _config = config;
        _timeSinceLastAttack = 0;
        _currentCooldownMs = 400; // 默认冷却
    }

    /// <summary>每帧更新</summary>
    public void Update()
    {
        var player = Game1.player;
        if (player == null || !Context.IsPlayerFree)
            return;

        // 检查玩家是否装备了我们的戒指
        var ring = GetEquippedAetherRing(player);
        if (ring == null)
        {
            _cachedFusionData = null;
            _cachedFusionSignature = null;
            _cachedFusionWeapon = null;
            return;
        }

        // 按熔铸数据判断是否变化，不能依赖物品对象引用。
        // 组合戒指等场景可能每帧返回不同实例，但其熔铸数据并未改变。
        var fusionSignature = FusedWeaponData.GetModDataSignature(ring);
        if (!string.Equals(fusionSignature, _cachedFusionSignature, StringComparison.Ordinal))
        {
            _cachedFusionWeapon = RefreshFusionDataCache(
                ring,
                fusionSignature,
                ref _cachedFusionSignature,
                ref _cachedFusionData);

            if (_cachedFusionData != null)
            {
                // 应用冷却倍率
                _currentCooldownMs = (int)(_cachedFusionData.GetAttackIntervalMs() * _config.CooldownMultiplier);
                // 确保至少有 100ms
                if (_currentCooldownMs < 100) _currentCooldownMs = 100;

                _monitor.Log($"Loaded fusion data: {_cachedFusionData.WeaponName}, cooldown: {_currentCooldownMs}ms (Mult: {_config.CooldownMultiplier})", LogLevel.Debug);
            }
        }

        // 如果没有熔铸数据，不执行攻击
        if (_cachedFusionData == null || !_cachedFusionData.IsValid)
            return;

        // 累计时间
        _timeSinceLastAttack += Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;

        // 检查冷却
        // 只有当时间足够，并且成功执行了攻击（命中了目标）时，才扣除冷却时间
        if (_timeSinceLastAttack >= _currentCooldownMs)
        {
            if (ExecuteAuraAttack(player, _cachedFusionData, _cachedFusionWeapon!))
            {
                // 使用减法而不是置0，以保持长期平均频率准确
                _timeSinceLastAttack -= _currentCooldownMs;

                // 如果累积时间仍然远大于冷却（例如卡顿后），重置为0以避免瞬间爆发多次攻击
                if (_timeSinceLastAttack > _currentCooldownMs)
                    _timeSinceLastAttack = 0;
            }
            // 如果没命中，保持 _timeSinceLastAttack 不变（满能量状态），下一帧继续尝试
        }
    }

    /// <summary>获取玩家装备的特定戒指</summary>
    private Item? GetEquippedAetherRing(Farmer player)
    {
        // 检查左手
        var left = FindAetherRing(player.leftRing.Value);
        if (left != null) return left;

        // 检查右手
        var right = FindAetherRing(player.rightRing.Value);
        if (right != null) return right;

        return null;
    }

    /// <summary>递归查找目标戒指</summary>
    private Item? FindAetherRing(Item? item)
    {
        if (item == null) return null;

        // 1. 直接匹配
        if (item.QualifiedItemId == ModEntry.QualifiedRingId || item.ItemId == ModEntry.RingId)
            return item;

        // 2. 检查组合戒指
        if (item is StardewValley.Objects.CombinedRing combinedRing)
        {
            foreach (var child in combinedRing.combinedRings)
            {
                var found = FindAetherRing(child);
                if (found != null) return found;
            }
        }

        return null;
    }

    /// <summary>执行光环攻击</summary>
    /// <returns>是否命中任何目标</returns>
    /// <summary>
    /// Failure: 损坏的新签名只报告一次并清空数据，绝不继续使用上一枚戒指的缓存。
    /// </summary>
    internal static MeleeWeapon? RefreshFusionDataCache(
        Item ring,
        string fusionSignature,
        ref string? cachedSignature,
        ref FusedWeaponData? cachedData)
    {
        try
        {
            FusedWeaponData? loadedData = FusedWeaponData.FromModData(ring);
            MeleeWeapon? loadedWeapon = loadedData is null
                ? null
                : FusedWeaponRestorer.CreateCombatWeapon(loadedData);

            cachedSignature = fusionSignature;
            cachedData = loadedData;
            return loadedWeapon;
        }
        catch
        {
            cachedSignature = fusionSignature;
            cachedData = null;
            throw;
        }
    }

    private bool ExecuteAuraAttack(
        Farmer player,
        FusedWeaponData fusionData,
        MeleeWeapon fusionWeapon)
    {
        var location = player.currentLocation;
        if (location == null)
            return false;

        var playerCenter = player.getStandingPosition();

        // 应用范围倍率
        var attackRadius = fusionData.GetAttackRadius() * _config.RangeMultiplier;

        var radiusSquared = attackRadius * attackRadius;

        // 收集范围内的所有怪物
        var targetsHit = new List<Monster>();

        foreach (var character in location.characters)
        {
            if (character is not Monster monster)
                continue;

            // 跳过已死亡的怪物
            if (monster.Health <= 0)
                continue;

            // 计算距离（使用平方避免开根号）
            var monsterCenter = monster.getStandingPosition();
            var distanceSquared = Vector2.DistanceSquared(playerCenter, monsterCenter);

            if (distanceSquared <= radiusSquared)
            {
                targetsHit.Add(monster);
            }
        }

        // 如果没有命中任何目标，不播放效果，返回false
        if (targetsHit.Count == 0)
            return false;

        // Flow: 让熔铸武器临时成为 CurrentTool，但不修改玩家当前快捷栏中的物品。
        // Guarantee: 无论攻击或清理如何失败，都会还原既有临时物品，并尽力移除全部临时附魔。
        var equippedEnchantments = new List<BaseEnchantment>(fusionWeapon.enchantments.Count);
        Exception? attackFailure = null;
        try
        {
            WithTemporaryCombatWeapon(player, fusionWeapon, () =>
            {
                foreach (BaseEnchantment enchantment in fusionWeapon.enchantments)
                {
                    equippedEnchantments.Add(enchantment);
                    enchantment.OnEquip(player);
                }

                PlayAttackSound(fusionData.WeaponType, location);

                foreach (Monster monster in targetsHit)
                    DealDamageToMonster(player, monster, fusionData, playerCenter);
            });
        }
        catch (Exception exception)
        {
            attackFailure = exception;
            throw;
        }
        finally
        {
            Exception? cleanupFailure = null;
            WithTemporaryCombatWeapon(player, fusionWeapon, () =>
            {
                for (int index = equippedEnchantments.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        equippedEnchantments[index].OnUnequip(player);
                    }
                    catch (Exception exception)
                    {
                        cleanupFailure ??= exception;
                    }
                }
            });

            if (cleanupFailure != null)
            {
                if (attackFailure != null)
                {
                    _monitor.Log($"Aura enchantment cleanup failed after an attack error: {cleanupFailure}", LogLevel.Error);
                }
                else
                {
                    ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
                }
            }
        }

        // _monitor.Log($"Aura hit {targetsHit.Count} targets", LogLevel.Trace);
        return true;
    }

    /// <summary>
    /// Guarantee: 熔铸武器只作为临时物品暴露给原版附魔逻辑，玩家当前快捷栏和已有临时物品均会复原。
    /// </summary>
    internal static void WithTemporaryCombatWeapon(Farmer player, MeleeWeapon fusionWeapon, Action action)
    {
        WithTemporaryItem(
            () => player.TemporaryItem,
            item => player.TemporaryItem = item,
            fusionWeapon,
            action);
    }

    internal static void WithTemporaryItem<T>(
        Func<T?> getTemporaryItem,
        Action<T?> setTemporaryItem,
        T temporaryItem,
        Action action)
        where T : class
    {
        T? previousTemporaryItem = getTemporaryItem();
        try
        {
            setTemporaryItem(temporaryItem);
            action();
        }
        finally
        {
            setTemporaryItem(previousTemporaryItem);
        }
    }

    /// <summary>对单个怪物造成伤害</summary>
    private void DealDamageToMonster(Farmer player, Monster monster, FusedWeaponData fusionData, Vector2 playerCenter)
    {
        var random = Game1.random;

        // 计算基础伤害
        int damage = random.Next(fusionData.MinDamage, fusionData.MaxDamage + 1);

        // 应用伤害倍率
        damage = (int)(damage * _config.DamageMultiplier);

        // 暴击判定
        bool isCrit = random.NextDouble() < fusionData.CritChance;
        if (isCrit)
        {
            damage = (int)(damage * fusionData.CritMultiplier);
        }

        // 计算击退方向（从玩家向外辐射）
        var monsterCenter = monster.getStandingPosition();
        var knockbackDirection = monsterCenter - playerCenter;
        if (knockbackDirection != Vector2.Zero)
            knockbackDirection.Normalize();

        var knockbackForce = fusionData.Knockback;
        int xTrajectory = (int)(knockbackDirection.X * knockbackForce * 10);
        int yTrajectory = (int)(knockbackDirection.Y * knockbackForce * 10);

        // 应用伤害
        var location = player.currentLocation;

        // 使用 damageMonster 方法（更完整的伤害流程）
        var hitBox = new Rectangle(
            (int)monsterCenter.X - 1,
            (int)monsterCenter.Y - 1,
            2,
            2
        );

        location.damageMonster(
            areaOfEffect: hitBox,
            minDamage: damage,
            maxDamage: damage,
            isBomb: false,
            knockBackModifier: knockbackForce,
            addedPrecision: 0,
            critChance: 0f, // 我们已经处理了暴击
            critMultiplier: 1f,
            triggerMonsterInvincibleTimer: true,
            who: player
        );

        // 播放命中特效
        PlayHitEffect(location, monsterCenter, isCrit);
    }

    /// <summary>播放攻击音效</summary>
    private void PlayAttackSound(int weaponType, GameLocation location)
    {
        string soundName = weaponType switch
        {
            1 => "daggerswipe", // 匕首
            2 => "clubswipe",   // 锤子
            _ => "swordswipe"   // 剑
        };

        location.playSound(soundName);
    }

    /// <summary>播放命中特效</summary>
    private void PlayHitEffect(GameLocation location, Vector2 position, bool isCrit)
    {
        // 添加临时动画精灵
        var hitSprite = new TemporaryAnimatedSprite(
            textureName: "TileSheets\\animations",
            sourceRect: new Rectangle(0, 0, 64, 64),
            animationInterval: 50f,
            animationLength: 6,
            numberOfLoops: 0,
            position: position - new Vector2(32, 32),
            flicker: false,
            flipped: false
        )
        {
            scale = isCrit ? 1.5f : 1f,
            alpha = 0.75f
        };

        location.temporarySprites.Add(hitSprite);

        // 暴击额外特效
        if (isCrit)
        {
            location.playSound("crit");
        }
    }
}
