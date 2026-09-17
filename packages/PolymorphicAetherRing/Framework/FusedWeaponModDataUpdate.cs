using StardewValley;

namespace PolymorphicAetherRing.Framework;

internal interface IFusedWeaponModDataStore
{
    bool TryGetValue(string key, out string? value);

    void SetValue(string key, string value);

    void Remove(string key);
}

/// <summary>已完成序列化与验证、可一次应用的熔铸数据更新。</summary>
internal sealed class FusedWeaponModDataUpdate
{
    private readonly IReadOnlyDictionary<string, string> _values;
    private readonly IReadOnlyCollection<string> _removedKeys;

    public FusedWeaponModDataUpdate(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyCollection<string> removedKeys)
    {
        _values = values;
        _removedKeys = removedKeys;
    }

    public void ApplyTo(Item item)
    {
        ApplyTo(new ItemModDataStore(item));
    }

    /// <summary>
    /// Guarantee: 写入任一键失败时恢复本次涉及的全部原值，不留下混合格式数据。
    /// </summary>
    internal void ApplyTo(IFusedWeaponModDataStore store)
    {
        string[] affectedKeys = _values.Keys
            .Concat(_removedKeys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var previousValues = new Dictionary<string, (bool Exists, string? Value)>();

        foreach (string key in affectedKeys)
        {
            bool exists = store.TryGetValue(key, out string? value);
            previousValues[key] = (exists, value);
        }

        try
        {
            foreach ((string key, string value) in _values)
                store.SetValue(key, value);
            foreach (string key in _removedKeys)
                store.Remove(key);
        }
        catch (Exception writeException)
        {
            try
            {
                foreach ((string key, (bool exists, string? value)) in previousValues)
                {
                    if (exists)
                        store.SetValue(key, value!);
                    else
                        store.Remove(key);
                }
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "Failed to write fused weapon data and restore its previous values.",
                    writeException,
                    rollbackException);
            }

            throw;
        }
    }

    private sealed class ItemModDataStore : IFusedWeaponModDataStore
    {
        private readonly Item _item;

        public ItemModDataStore(Item item)
        {
            _item = item;
        }

        public bool TryGetValue(string key, out string? value)
        {
            return _item.modData.TryGetValue(key, out value);
        }

        public void SetValue(string key, string value)
        {
            _item.modData[key] = value;
        }

        public void Remove(string key)
        {
            _item.modData.Remove(key);
        }
    }
}
