using HarmonyLib;
using StardewModdingAPI;
using StardewValley.Locations;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace MineBustle;

/// <summary>
/// MineShaft 类的 Harmony 补丁
/// 使用 IL Transpiler 修改怪物生成概率
/// </summary>
[HarmonyPatch(typeof(MineShaft), "populateLevel")]
public class MineShaftPatches
{
    /// <summary>
    /// 获取怪物生成倍率 (用于乘法)
    /// </summary>
    public static double GetSpawnMultiplier()
    {
        double multiplier = ModEntry.Config.CurrentMultiplier;
        return multiplier > 0 ? multiplier : 1.0;
    }

    /// <summary>
    /// 获取石头生成的除数 (用于除法)
    /// 如果配置开启了"减少石头"，则返回倍率；否则返回 1.0 (不减少)
    /// </summary>
    public static double GetStoneDivisor()
    {
        // 如果配置开启，返回倍率（例如 10.0），让石头概率 / 10
        if (ModEntry.Config.ReduceStones)
        {
            double multiplier = ModEntry.Config.CurrentMultiplier;
            return multiplier > 0 ? multiplier : 1.0;
        }
        // 如果配置关闭，返回 1.0，石头概率 / 1，即不变
        return 1.0;
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        try
        {
            // 1. 定位 adjustLevelChances 调用
            int callIndex = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Call && codes[i].operand?.ToString()?.Contains("adjustLevelChances") == true)
                {
                    callIndex = i;
                    break;
                }
            }

            if (callIndex == -1)
            {
                ModEntry.ModMonitor.Log("无法定位 adjustLevelChances，补丁失败。", LogLevel.Error);
                return instructions;
            }

            // 2. 获取变量索引
            // 倒数第3个是 monsterChance (Call -> Gem -> Item -> Monster)
            int monsterChanceLoadIndex = callIndex - 3;
            // 倒数第4个是 stoneChance (Call -> Gem -> Item -> Monster -> Stone)
            int stoneChanceLoadIndex = callIndex - 4;

            var monsterInstruction = codes[monsterChanceLoadIndex];
            var stoneInstruction = codes[stoneChanceLoadIndex];

            // 验证指令
            if (!IsLoadLocalAddress(monsterInstruction) || !IsLoadLocalAddress(stoneInstruction))
            {
                ModEntry.ModMonitor.Log("变量加载指令不匹配，补丁失败。", LogLevel.Error);
                return instructions;
            }

            object monsterVarIndex = monsterInstruction.operand;
            object stoneVarIndex = stoneInstruction.operand;

            // 3. 注入逻辑
            var newInstructions = new List<CodeInstruction>();

            // --- A: 提升怪物概率 (monster * Multiplier) ---
            newInstructions.Add(new CodeInstruction(OpCodes.Ldloc_S, monsterVarIndex));
            newInstructions.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MineShaftPatches), nameof(GetSpawnMultiplier))));
            newInstructions.Add(new CodeInstruction(OpCodes.Mul));
            newInstructions.Add(new CodeInstruction(OpCodes.Stloc_S, monsterVarIndex));

            // --- B: 降低石头概率 (stone / Divisor) ---
            // 这里调用 GetStoneDivisor，根据配置决定是除以 10 还是除以 1
            newInstructions.Add(new CodeInstruction(OpCodes.Ldloc_S, stoneVarIndex));
            newInstructions.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MineShaftPatches), nameof(GetStoneDivisor))));
            newInstructions.Add(new CodeInstruction(OpCodes.Div));
            newInstructions.Add(new CodeInstruction(OpCodes.Stloc_S, stoneVarIndex));

            // 插入代码
            codes.InsertRange(callIndex + 1, newInstructions);

            ModEntry.ModMonitor.Log($"成功注入概率修改代码。石头变量: {stoneVarIndex}, 怪物变量: {monsterVarIndex}", LogLevel.Debug);
        }
        catch (System.Exception ex)
        {
            ModEntry.ModMonitor.Log($"Transpiler 异常: {ex.Message}", LogLevel.Error);
            return instructions;
        }

        return codes;
    }

    private static bool IsLoadLocalAddress(CodeInstruction instruction)
    {
        return instruction.opcode == OpCodes.Ldloca || instruction.opcode == OpCodes.Ldloca_S;
    }
}