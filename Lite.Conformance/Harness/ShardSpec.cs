namespace Lite.Conformance.Harness;

/// <summary>A stable zero-based shard selection applied after paths are sorted.</summary>
internal readonly record struct ShardSpec(int Index, int Count)
{
    public static ShardSpec All => new(0, 1);

    public static bool TryParse(string value, out ShardSpec shard)
    {
        shard = All;
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var index) ||
            !int.TryParse(parts[1], out var count) ||
            count < 1 || index < 0 || index >= count)
            return false;
        shard = new ShardSpec(index, count);
        return true;
    }

    public IEnumerable<T> Apply<T>(IEnumerable<T> source)
    {
        if (Count == 1) return source;
        var index = Index;
        var count = Count;
        return source.Where((_, position) => position % count == index);
    }

    public override string ToString() => $"{Index}/{Count}";
}
