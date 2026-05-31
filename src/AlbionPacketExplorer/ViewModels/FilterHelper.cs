namespace AlbionPacketExplorer.ViewModels;

internal static class FilterHelper
{
    /// <summary>
    /// Tokens prefixed with '-' are exclusions; all others are inclusions.
    /// exact=true: inclusion tokens must match a haystack exactly (used for numeric codes).
    /// Empty/whitespace filter = pass.
    /// </summary>
    public static bool Matches(string filter, params string[] haystacks)
        => MatchesCore(filter, exact: false, haystacks);

    public static bool MatchesExact(string filter, params string[] haystacks)
        => MatchesCore(filter, exact: true, haystacks);

    private static bool MatchesCore(string filter, bool exact, IReadOnlyList<string> haystacks)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;

        var tokens = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (token.StartsWith('-') && token.Length > 1)
            {
                var term = token[1..];
                if (exact
                    ? haystacks.Any(h => h.Equals(term, StringComparison.OrdinalIgnoreCase))
                    : haystacks.Any(h => h.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
            else
            {
                if (exact
                    ? !haystacks.Any(h => h.Equals(token, StringComparison.OrdinalIgnoreCase))
                    : !haystacks.Any(h => h.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
        }
        return true;
    }

}
