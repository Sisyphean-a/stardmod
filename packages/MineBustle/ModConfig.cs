namespace MineBustle;

/// <summary>
/// 模组配置类，用于存储当前的怪物生成倍率
/// </summary>
public class ModConfig
{
    /// <summary>
    /// 当前的怪物生成倍率（1.0 - 10.0）
    /// 默认为 1.0，每天睡觉后重置
    /// </summary>
    public double CurrentMultiplier { get; set; } = 1.0;

    /// <summary>
    /// 是否启用祭坛功能
    /// 默认开启 (True)
    /// </summary>
    public bool EnableAltar { get; set; } = true;

    /// <summary>
    /// 是否为了给怪物腾出空间而减少石头生成
    /// 默认开启 (True)
    /// </summary>
    public bool ReduceStones { get; set; } = true;

    /// <summary>
    /// 基础献祭费用
    /// </summary>
    public int BaseFee { get; set; } = 500;

    /// <summary>
    /// 通胀系数（基于玩家总收入）
    /// </summary>
    public double InflationCoefficient { get; set; } = 0.001;

    /// <summary>
    /// 惩罚指数（倍率越高，费用增长越快）
    /// </summary>
    public double PenaltyExponent { get; set; } = 1.0;
}

