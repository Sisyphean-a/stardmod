using StardewModdingAPI;

namespace FishingBarGrowth;

/// <summary>
/// Generic Mod Config Menu API接口
/// </summary>
public interface IGenericModConfigMenuApi
{
    /// <summary>
    /// 注册mod配置
    /// </summary>
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

    /// <summary>
    /// 取消注册mod配置
    /// </summary>
    void Unregister(IManifest mod);

    /// <summary>
    /// 添加布尔选项
    /// </summary>
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);

    /// <summary>
    /// 添加整数选项
    /// </summary>
    void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, string? fieldId = null);

    /// <summary>
    /// 添加文本选项
    /// </summary>
    void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string>? tooltip = null, string[]? allowedValues = null, string? fieldId = null);

    /// <summary>
    /// 添加分页标题
    /// </summary>
    void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);

    /// <summary>
    /// 添加段落文本
    /// </summary>
    void AddParagraph(IManifest mod, Func<string> text);

    /// <summary>
    /// 添加分页
    /// </summary>
    void AddPage(IManifest mod, string pageId, Func<string>? pageTitle = null);

    /// <summary>
    /// 添加页面链接
    /// </summary>
    void AddPageLink(IManifest mod, string pageId, Func<string> text, Func<string>? tooltip = null);
}

