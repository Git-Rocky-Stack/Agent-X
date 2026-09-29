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

        // The combos showed the enum member names ("PlainText", "ResearchReport") and an
        // English "(None)"; each choice now carries its localized name and file extension.
        var localization = App.GetService<ILocalizationService>();
        FormatCombo.ItemsSource = Enum.GetValues<ExportFormat>()
            .Select(format => new Choice<ExportFormat>(format, FormatLabel(localization, format)))
            .ToList();
        FormatCombo.SelectedIndex = 0;

        var templates = new List<Choice<ExportTemplateId?>>
        {
            new(null, localization.GetString("ExportDlg_TemplateNone"))
        };
        templates.AddRange(Enum.GetValues<ExportTemplateId>()
            .Select(template => new Choice<ExportTemplateId?>(template, TemplateLabel(localization, template))));
        TemplateCombo.ItemsSource = templates;
        TemplateCombo.SelectedIndex = 0;

        // Templates produce Markdown, so they apply to Markdown exports only (the export
        // service rejects a template with any other format rather than ignoring it).
        FormatCombo.SelectionChanged += (s, e) =>
        {
            var fmt = SelectedFormat;
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
        Title = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            App.GetService<ILocalizationService>().GetString("ExportDlg_TitleFormat"),
            title);
    }

    private ExportFormat SelectedFormat =>
        FormatCombo.SelectedItem is Choice<ExportFormat> choice ? choice.Value : ExportFormat.Markdown;

    private ExportTemplateId? SelectedTemplate =>
        TemplateCombo.SelectedItem is Choice<ExportTemplateId?> choice ? choice.Value : null;

    private static string FormatLabel(ILocalizationService localization, ExportFormat format) => format switch
    {
        ExportFormat.Markdown => localization.GetString("ExportDlg_FormatMarkdown"),
        ExportFormat.Html => localization.GetString("ExportDlg_FormatHtml"),
        ExportFormat.Pdf => localization.GetString("ExportDlg_FormatPdf"),
        ExportFormat.Json => localization.GetString("ExportDlg_FormatJson"),
        ExportFormat.PlainText => localization.GetString("ExportDlg_FormatPlainText"),
        ExportFormat.Csv => localization.GetString("ExportDlg_FormatCsv"),
        ExportFormat.Docx => localization.GetString("ExportDlg_FormatDocx"),
        ExportFormat.Pptx => localization.GetString("ExportDlg_FormatPptx"),
        _ => format.ToString()
    };

    private static string TemplateLabel(ILocalizationService localization, ExportTemplateId template) => template switch
    {
        ExportTemplateId.ResearchReport => localization.GetString("ExportDlg_TemplateResearchReport"),
        ExportTemplateId.ExecutiveSummary => localization.GetString("ExportDlg_TemplateExecutiveSummary"),
        ExportTemplateId.AnnotatedBibliography => localization.GetString("ExportDlg_TemplateAnnotatedBibliography"),
        _ => template.ToString()
    };

    /// <summary>A combo entry: the value it stands for and the name shown for it.</summary>
    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Copies the conversation to the clipboard as Markdown. This is the only export
    /// route that produces no file, so it runs from its own button rather than through
    /// the format-and-save flow.
    /// </summary>
    private async void OnCopyAsMarkdownClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.CopyConversationAsMarkdownCommand.ExecuteAsync(_conversationId);

        // The outcome comes from the view model's state: the message is translated, so its
        // wording cannot tell a failure from a success.
        StatusInfoBar.Message = _viewModel.StatusMessage;
        StatusInfoBar.Severity = _viewModel.LastExportSucceeded
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Error;
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
            var format = SelectedFormat;
            var template = SelectedTemplate;

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
                    var failed = !_viewModel.LastExportSucceeded;
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
