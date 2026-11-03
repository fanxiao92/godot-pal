p='src/Pal.Game/Scripts/Dialogue/TriggerEventLoader.cs'
s=open(p,encoding='utf-8').read()
old='''    /// <summary>The index plus every event file it lists, for native dependency-hash closures.</summary>
    private const char chr_backslash = '\\';

    public static IReadOnlyList<string> DependencyUris(string indexPath)
    {
        indexPath = indexPath.Replace('\', '/');
        string indexText = ReadUtf8(indexPath);
        using var index = JsonDocument.Parse(indexText);
        string directory = indexPath[..(indexPath.LastIndexOf('/') + 1)];
        var uris = new List<string> { indexPath };
        foreach (JsonElement value in index.RootElement.GetProperty("files").EnumerateArray())
            uris.Add(directory + value.GetString() + ".json");
        return uris;
    }
'''
new='''    /// <summary>The index plus every event file it lists, for native dependency-hash closures.
    /// The uri is already validated (forward slashes only) by the caller's profile validation.</summary>
    public static IReadOnlyList<string> DependencyUris(string indexPath)
    {
        string indexText = ReadUtf8(indexPath);
        using var index = JsonDocument.Parse(indexText);
        string directory = indexPath[..(indexPath.LastIndexOf('/') + 1)];
        var uris = new List<string> { indexPath };
        foreach (JsonElement value in index.RootElement.GetProperty("files").EnumerateArray())
            uris.Add(directory + value.GetString() + ".json");
        return uris;
    }
'''
assert old in s, 'block not found'
s=s.replace(old,new)
open(p,'w',encoding='utf-8').write(s)
print('ok')
