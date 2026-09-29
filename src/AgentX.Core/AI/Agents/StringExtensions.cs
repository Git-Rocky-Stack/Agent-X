namespace AgentX.Core.AI.Agents;

/// <summary>
/// String helpers for the agent orchestration code.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Returns at most the first <paramref name="maxLength"/> characters of <paramref name="value"/>.
    /// </summary>
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        return value.Substring(0, maxLength);
    }
}
