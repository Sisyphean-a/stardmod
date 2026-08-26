using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;

namespace PortableLoadingOptimizer.Services;

internal sealed class SaveMenuDelayOptimizer
{
    private static SaveMenuDelayOptimizer? instance;

    private readonly ModConfig config;
    private readonly ConcurrentQueue<WorkerMessage> messages = new();
    private MemberInfo? activateDelayMember;
    private long slotsAdjusted;
    private long removedMilliseconds;
    private int patchedConstructors;
    private int adjustmentFailureLogged;

    internal SaveMenuDelayOptimizer(ModConfig config)
    {
        this.config = config;
    }

    internal void Apply(Harmony harmony)
    {
        instance = this;
        if (!config.RemoveSaveSelectionDelay)
            return;

        Type? loadGameMenuType = AccessTools.TypeByName("StardewValley.Menus.LoadGameMenu");
        Type? slotType = loadGameMenuType?.GetNestedType(
            "SaveFileSlot",
            BindingFlags.Public | BindingFlags.NonPublic)
            ?? AccessTools.TypeByName("StardewValley.Menus.SaveFileSlot");
        if (slotType is null)
        {
            messages.Enqueue(new WorkerMessage("[SAVE DELAY] 找不到 SaveFileSlot，已保留原生等待。", LogLevel.Warn));
            return;
        }

        activateDelayMember = FindActivateDelayMember(slotType);
        if (activateDelayMember is null)
        {
            messages.Enqueue(new WorkerMessage("[SAVE DELAY] 找不到 ActivateDelay，已保留原生等待。", LogLevel.Warn));
            return;
        }

        MethodInfo postfix = AccessTools.Method(typeof(SaveMenuDelayOptimizer), nameof(ConstructorPostfix))!;
        foreach (ConstructorInfo constructor in slotType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            try
            {
                harmony.Patch(constructor, postfix: new HarmonyMethod(postfix));
                patchedConstructors++;
            }
            catch (Exception ex)
            {
                messages.Enqueue(new WorkerMessage($"[SAVE DELAY] 跳过不兼容构造函数：{ex.GetBaseException().Message}", LogLevel.Trace));
            }
        }

        if (patchedConstructors == 0)
            messages.Enqueue(new WorkerMessage("[SAVE DELAY] 没有可补丁的 SaveFileSlot 构造函数，已保留原生等待。", LogLevel.Warn));
    }

    internal string GetStatus()
    {
        return $"[SAVE DELAY] enabled={config.RemoveSaveSelectionDelay}, patched={patchedConstructors}, slots={Interlocked.Read(ref slotsAdjusted)}, removed={Interlocked.Read(ref removedMilliseconds)}ms";
    }

    internal bool TryDequeueMessage(out WorkerMessage message) => messages.TryDequeue(out message);

    private static MemberInfo? FindActivateDelayMember(Type slotType)
    {
        for (Type? type = slotType; type is not null; type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty("ActivateDelay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property?.CanRead == true && property.CanWrite && property.PropertyType == typeof(int))
                return property;

            FieldInfo? field = type.GetField("ActivateDelay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field?.FieldType == typeof(int))
                return field;
        }

        return null;
    }

    private static void ConstructorPostfix(object __instance)
    {
        SaveMenuDelayOptimizer? current = instance;
        if (current is null || !current.config.RemoveSaveSelectionDelay || current.activateDelayMember is null)
            return;

        try
        {
            int original;
            switch (current.activateDelayMember)
            {
                case PropertyInfo property:
                    original = Math.Max(0, (int)(property.GetValue(__instance) ?? 0));
                    property.SetValue(__instance, 0);
                    break;
                case FieldInfo field:
                    original = Math.Max(0, (int)(field.GetValue(__instance) ?? 0));
                    field.SetValue(__instance, 0);
                    break;
                default:
                    return;
            }

            Interlocked.Increment(ref current.slotsAdjusted);
            Interlocked.Add(ref current.removedMilliseconds, original);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref current.adjustmentFailureLogged, 1) == 0)
            {
                current.messages.Enqueue(new WorkerMessage(
                    $"[SAVE DELAY] 调整选档等待失败，后续保持原生行为：{ex.GetBaseException().Message}",
                    LogLevel.Warn));
            }
        }
    }
}
