namespace AgentX.App.ViewModels.Sync;

// =============================================================================
// TYPE ALIAS — SyncHistoryItem
//
// The SyncSettingsPage.xaml DataTemplate declares x:DataType="vm:SyncHistoryItem".
// This subclass provides the expected type name without duplicating any logic.
// =============================================================================

/// <summary>
/// Type alias used in SyncSettingsPage.xaml DataTemplate
/// (<c>x:DataType="vm:SyncHistoryItem"</c>).
/// </summary>
public class SyncHistoryItem : SyncLogDisplayItem { }
