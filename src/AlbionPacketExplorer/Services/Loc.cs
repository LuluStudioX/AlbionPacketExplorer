namespace AlbionPacketExplorer.Services;

/// <summary>Short static facade over <see cref="LocalizationService"/> for view-model code.</summary>
public static class Loc
{
    public static string T(string key) => LocalizationService.Instance[key];

    public static string Format(string key, params object?[] args) =>
        LocalizationService.Instance.Format(key, args);
}
