using System.Globalization;
using System.Reflection;
using StardewModdingAPI;

namespace Toolbox;

/// <summary>
/// Optional GMCM bridge which avoids making the desktop-only config menu a load-time dependency.
/// </summary>
internal sealed class GenericModConfigMenuAdapter
{
    private const string ModId = "spacechase0.GenericModConfigMenu";

    private readonly object api;
    private readonly IMonitor monitor;

    private GenericModConfigMenuAdapter(object api, IMonitor monitor)
    {
        this.api = api;
        this.monitor = monitor;
    }

    internal static GenericModConfigMenuAdapter? TryCreate(IModRegistry modRegistry, IMonitor monitor)
    {
        try
        {
            object? api = modRegistry.GetApi<object>(ModId);
            return api is null ? null : new GenericModConfigMenuAdapter(api, monitor);
        }
        catch (Exception ex)
        {
            monitor.Log($"无法加载可选的 GMCM 配置桥接：{ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    internal void Register(IManifest manifest, Action reset, Action save)
    {
        Invoke("Register", manifest, reset, save, false);
    }

    internal void AddBoolOption(
        IManifest manifest,
        Func<bool> getValue,
        Action<bool> setValue,
        Func<string> name,
        Func<string> tooltip)
    {
        Invoke("AddBoolOption", manifest, getValue, setValue, name, tooltip, null);
    }

    internal void AddNumberOption(
        IManifest manifest,
        Func<int> getValue,
        Action<int> setValue,
        Func<string> name,
        Func<string> tooltip,
        int? min = null,
        int? max = null,
        int? interval = null)
    {
        Invoke("AddNumberOption", manifest, getValue, setValue, name, tooltip, min, max, interval, null, null);
    }

    internal void AddNumberOption(
        IManifest manifest,
        Func<float> getValue,
        Action<float> setValue,
        Func<string> name,
        Func<string> tooltip,
        float? min = null,
        float? max = null,
        float? interval = null)
    {
        Invoke("AddNumberOption", manifest, getValue, setValue, name, tooltip, min, max, interval, null, null);
    }

    internal void AddKeybindList(
        IManifest manifest,
        Func<StardewModdingAPI.Utilities.KeybindList> getValue,
        Action<StardewModdingAPI.Utilities.KeybindList> setValue,
        Func<string> name,
        Func<string> tooltip)
    {
        Invoke("AddKeybindList", manifest, getValue, setValue, name, tooltip, null);
    }

    private void Invoke(string methodName, params object?[] arguments)
    {
        MethodInfo? method = FindMethod(methodName, arguments);
        if (method is null)
        {
            monitor.Log($"GMCM 未提供兼容的 {methodName} 方法，已跳过该配置项。", LogLevel.Warn);
            return;
        }

        ParameterInfo[] parameters = method.GetParameters();
        object?[] convertedArguments = new object?[parameters.Length];
        for (int index = 0; index < arguments.Length; index++)
            convertedArguments[index] = ConvertArgument(arguments[index], parameters[index].ParameterType);
        for (int index = arguments.Length; index < parameters.Length; index++)
            convertedArguments[index] = Type.Missing;

        try
        {
            method.Invoke(api, convertedArguments);
        }
        catch (TargetInvocationException ex)
        {
            monitor.Log($"GMCM 调用 {methodName} 失败：{ex.InnerException?.Message ?? ex.Message}", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            monitor.Log($"GMCM 调用 {methodName} 失败：{ex.Message}", LogLevel.Warn);
        }
    }

    private MethodInfo? FindMethod(string methodName, object?[] arguments)
    {
        return api.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == methodName)
            .OrderBy(method => method.GetParameters().Length)
            .FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < arguments.Length
                    || parameters.Skip(arguments.Length).Any(parameter => !parameter.IsOptional))
                {
                    return false;
                }

                return arguments.Select((argument, index) => IsCompatible(argument, parameters[index].ParameterType)).All(value => value);
            });
    }

    private static bool IsCompatible(object? argument, Type parameterType)
    {
        if (argument is null)
            return !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) is not null;

        if (parameterType.IsInstanceOfType(argument))
            return true;

        Type? nullableType = Nullable.GetUnderlyingType(parameterType);
        if (nullableType is not null)
            return IsCompatible(argument, nullableType);

        return argument is IConvertible && typeof(IConvertible).IsAssignableFrom(parameterType);
    }

    private static object? ConvertArgument(object? argument, Type parameterType)
    {
        if (argument is null || parameterType.IsInstanceOfType(argument))
            return argument;

        Type targetType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (targetType.IsEnum)
            return Enum.Parse(targetType, argument.ToString()!, ignoreCase: true);

        if (argument is IConvertible)
            return Convert.ChangeType(argument, targetType, CultureInfo.InvariantCulture);

        return argument;
    }
}
