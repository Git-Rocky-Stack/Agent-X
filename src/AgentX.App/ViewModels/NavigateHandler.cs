namespace AgentX.App.ViewModels;

/// <summary>
/// Raised by a view model to ask the shell to show another page, optionally handing that
/// page the thing the user actually picked.
/// <para>
/// The optional <paramref name="parameter"/> is what separates "go to Search" from "go to
/// Search for <c>quarterly revenue</c>". Without it a view model can only name a
/// destination, so any identity the user selected — a query, a document, a conversation —
/// is dropped at the page boundary and they arrive at an empty list.
/// </para>
/// <para>
/// Declared as a delegate rather than <c>Action&lt;string, object?&gt;</c> so the
/// parameter can be optional: existing single-argument call sites keep working unchanged.
/// </para>
/// </summary>
/// <param name="pageKey">The destination page's key in the shell's page map.</param>
/// <param name="parameter">
/// Optional payload forwarded to the destination page's <c>OnNavigatedTo</c>.
/// </param>
public delegate void NavigateHandler(string pageKey, object? parameter = null);
