namespace AgentX.Core.Services.Shortcuts;

/// <summary>
/// Context that owns a group of shortcuts. <see cref="Global"/> shortcuts fire
/// from anywhere; per-page scopes only fire when that page is the active
/// navigation frame.
/// </summary>
public sealed record ShortcutScope(string Name)
{
    public static readonly ShortcutScope Global = new("Global");

    public bool IsGlobal => Name == Global.Name;
}
