namespace AgentX.App.ViewModels;

/// <summary>
/// A question a view model asks before a destructive action: the dialog title, the message,
/// and the texts of the confirm and cancel buttons. The page shows it (a ContentDialog whose
/// default button is Cancel) and answers true only when the user confirms.
/// </summary>
public sealed record ConfirmationRequest(string Title, string Message, string ConfirmText, string CancelText);
