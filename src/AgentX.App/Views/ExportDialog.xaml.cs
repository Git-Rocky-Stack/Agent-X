using AgentX.App.ViewModels;
using AgentX.Core.Services.Export.Models;
using AgentX.Core.Services.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

/// <summary>
/// ContentDialog for configuring and executing conversation exports.
/// Supports all 8 export formats and, for Markdown, 3 built-in templates.
/// </summary>
/// <remarks>
/// "Include citations" lists the web sources saved with each answer, and "Include model info"
/// names the model that wrote it. "Include branches" is not offered: no exporter includes
/// branch conversations, so the switch would change nothing.
/// </remarks>
public sealed partial class ExportDialog : ContentDialog
{
    private readonly ExportViewModel _viewModel;
    private long _conversationId;

    public ExportDialog(ExportViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        // Populate format combo with all ExportFormat values
        FormatCombo.ItemsSource = Enum.GetValues<ExportFormat>();
        FormatCombo.SelectedIndex = 0;

        // Populate template combo: "(None)" + template names
        var templates = new List<string> { "(None)" };
        templates.AddRange(Enum.GetNames<ExportTemplateId>());
        TemplateCombo.ItemsSource = templates;
        TemplateCombo.SelectedIndex = 0;

        // Templates produce Markdown, so they apply to Markdown exports only (the export
        // service rejects a template with any other format rather than ignoring it).
        FormatCombo.SelectionChanged += (s, e) =>
        {
            var fmt = (ExportFormat)FormatCombo.SelectedItem!;
            TemplateCombo.IsEnabled = fmt is ExportFormat.Markdown;
            if (!TemplateCombo.IsEnabled)
            {
                TemplateCombo.SelectedIndex = 0;
            }
        };
    }

    /// <summary>
    /// Sets the conversation to export and updates the dialog title.
    /// </summary>
    public void SetConversation(long conversationId, string title)
    {
        _conversationId = conversationId;
        Title = $"Export: {title}";
    }

    /// <summary>
    /// Copies the conversation to the clipboard as Markdown. This is the only export
    /// route that produces no file, so it runs from its own button rather than through
    /// the format-and-save flow.
    /// </summary>
    private async void OnCopyAsMarkdownClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.CopyConversationAsMarkdownCommand.ExecuteAsync(_conversationId);

        StatusInfoBar.Message = _viewModel.StatusMessage;
        StatusInfoBar.Severity = _viewModel.StatusMessage.StartsWith("Copy failed", StringComparison.Ordinal)
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The dialog stays open: the export's result is shown in the InfoBar, which the dialog
        // used to close over as soon as the export returned. The operator closes it after
        // reading the result (and can retry a failed export in place).
        args.Cancel = true;
        var deferral = args.GetDeferral();
        IsPrimaryButtonEnabled = false;
        try
        {
            var format = (ExportFormat)FormatCombo.SelectedItem!;
            var templateIdx = TemplateCombo.SelectedIndex - 1; // -1 because index 0 is "(None)"
            var template = templateIdx >= 0 ? (ExportTemplateId?)templateIdx : null;

            var options = new ExportOptions
            {
                Format = format,
                IncludeCitations = IncludeCitationsToggle.IsOn,
                IncludeModelInfo = IncludeModelInfoToggle.IsOn,
                IncludeMetadata = IncludeMetadataToggle.IsOn,
                IncludeTimestamps = IncludeTimestampsToggle.IsOn,
                TemplateId = template
            };

            var request = new ExportConversationRequest(_conversationId, options);

            await _viewModel.ExportConversationCommand.ExecuteAsync(request);

            // Show result in InfoBar
            if (!string.IsNullOrEmpty(_viewModel.StatusMessage))
            {
                StatusInfoBar.Message = _viewModel.StatusMessage;
                StatusInfoBar.IsOpen = true;

                if (_viewModel.IsExporting)
                {
                    StatusInfoBar.Severity = InfoBarSeverity.Informational;
                }
                else
                {
                    var failed = _viewModel.StatusMessage.StartsWith("Export failed");
                    StatusInfoBar.Severity = failed
                        ? InfoBarSeverity.Error
                        : InfoBarSeverity.Success;

                    if (!failed)
                    {
                        // Nothing is left to cancel once the file is written.
                        CloseButtonText = App.GetService<ILocalizationService>().GetString("ExportDlg_Close");
                    }
                }
            }
        }
        finally
        {
            IsPrimaryButtonEnabled = true;
            deferral.Complete();
        }
    }
}
