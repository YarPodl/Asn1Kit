namespace Asn1Kit.Runtime;

/// <summary>Structural operations used by generated DEFAULT comparisons.</summary>
public static class Asn1Collection
{
    /// <summary>Counts matching values without allocating temporary collections.</summary>
    public static int Count<T>(IReadOnlyList<T> values, Func<T, bool> predicate)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        var count = 0;
        for (var i = 0; i < values.Count; i++) if (predicate(values[i])) count++;
        return count;
    }
}
