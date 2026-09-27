using AgentX.App.Helpers;
using AgentX.App.ViewModels;
using AgentX.Core.Services.Export.Models;
using AgentX.Core.Services.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using Windows.ApplicationModel.DataTransfer;

namespace AgentX.App.Views;

public sealed partial class WorkflowBuilderPage : Page
{
    private static readonly ExportFormat[] WorkflowResultExportFormats =
    [
        ExportFormat.Markdown,
        ExportFormat.PlainText,
        ExportFormat.Html,
        ExportFormat.Json
    ];

    public WorkflowBuilderViewModel ViewModel { get; }

    public WorkflowBuilderPage()
    {
        ViewModel = PageViewModelFactory.Create<WorkflowBuilderViewModel>();
        ViewModel.NavigateRequested = NavigateToPage;
        InitializeComponent();

        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Log.Debug("WorkflowBuilderPage loaded");
        await ViewModel.InitializeAsync();
    }

    private void NavigateToPage(string pageTag, object? parameter = null)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage(pageTag, parameter);
        }
    }

    private void CopyOutputToClipboard(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.RunOutput))
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(ViewModel.RunOutput);
            Clipboard.SetContent(dataPackage);
            ViewModel.StatusMessage = App.GetService<ILocalizationService>().GetString("WfBuilder_OutputCopied");
        }
    }

    private async void ExportWorkflowToClipboard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: long workflowId } || workflowId <= 0)
        {
            return;
        }

        var json = await ViewModel.GetWorkflowExportJsonAsync(workflowId);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(json);
        Clipboard.SetContent(dataPackage);
        ViewModel.StatusMessage = App.GetService<ILocalizationService>()
            .GetString("WfBuilder_WorkflowCopied", ViewModel.SelectedWorkflowName);
    }

    private async void ImportWorkflow_Click(object sender, RoutedEventArgs e)
    {
        var localization = App.GetService<ILocalizationService>();
        var importBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 220,
            MaxHeight = 420,
            PlaceholderText = localization.GetString("WfBuilder_ImportDialogPlaceholder")
        };

        var clipboardText = await TryGetClipboardTextAsync();
        if (!string.IsNullOrWhiteSpace(clipboardText))
        {
            importBox.Text = clipboardText;
        }

        var dialog = new ContentDialog
        {
            Title = localization.GetString("WfBuilder_ImportDialogTitle"),
            PrimaryButtonText = localization.GetString("WfBuilder_ImportDialogImport"),
            CloseButtonText = localization.GetString("WfBuilder_DialogCancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(importBox.Text),
            XamlRoot = this.XamlRoot,
            Content = new StackPanel
            {
                Spacing = 10,
                MinWidth = 520,
                Children =
                {
                    new TextBlock
                    {
                        Text = localization.GetString("WfBuilder_ImportDialogInstructions"),
                        TextWrapping = TextWrapping.Wrap
                    },
                    importBox
                }
            }
        };

        importBox.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(importBox.Text);
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ImportWorkflowCommand.ExecuteAsync(importBox.Text);
        }
    }

    private async void ExportCurrentResult_Click(object sender, RoutedEventArgs e)
    {
        await ShowWorkflowResultExportDialogAsync(
            App.GetService<ILocalizationService>().GetString("WfBuilder_ExportResultTitle"),
            options => ViewModel.ExportCurrentResultAsync(options));
    }

    private async void ExportHistoricalRun_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkflowRunHistoryDisplayItem run })
        {
            return;
        }

        await ShowWorkflowResultExportDialogAsync(
            App.GetService<ILocalizationService>().GetString("WfBuilder_ExportStoredRunTitle", run.StartedAtText),
            options => ViewModel.ExportHistoricalRunAsync(run, options));
    }

    private async Task ShowWorkflowResultExportDialogAsync(
        string title,
        Func<ExportOptions, Task<ExportResult>> exportAction)
    {
        var localization = App.GetService<ILocalizationService>();

        // The combo showed the enum member names ("PlainText"); each choice now carries the
        // localized name the conversation export dialog shows for that format.
        var formatChoices = WorkflowResultExportFormats
            .Select(format => new FormatChoice(format, FormatLabel(localization, format)))
            .ToList();
        var formatCombo = new ComboBox
        {
            ItemsSource = formatChoices,
            SelectedItem = formatChoices.First(candidate => candidate.Value == ExportFormat.Markdown),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var includeMetadataToggle = new ToggleSwitch
        {
            Header = localization.GetString("WfBuilder_ExportDialogIncludeMetadata"),
            IsOn = true
        };

        var dialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = localization.GetString("WfBuilder_ExportDialogExport"),
            CloseButtonText = localization.GetString("WfBuilder_DialogCancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
            Content = new StackPanel
            {
                Spacing = 12,
                MinWidth = 420,
                Children =
                {
                    new TextBlock
                    {
                        Text = localization.GetString("WfBuilder_ExportDialogInstructions"),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = localization.GetString("WfBuilder_ExportDialogFormat") },
                            formatCombo
                        }
                    },
                    includeMetadataToggle
                }
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var options = new ExportOptions
            {
                Format = formatCombo.SelectedItem is FormatChoice choice ? choice.Value : ExportFormat.Markdown,
                IncludeMetadata = includeMetadataToggle.IsOn
            };

            await exportAction(options);
        }
    }

    /// <summary>The name shown for an export format: the one the conversation export dialog shows.</summary>
    private static string FormatLabel(ILocalizationService localization, ExportFormat format) => format switch
    {
        ExportFormat.Markdown => localization.GetString("ExportDlg_FormatMarkdown"),
        ExportFormat.PlainText => localization.GetString("ExportDlg_FormatPlainText"),
        ExportFormat.Html => localization.GetString("ExportDlg_FormatHtml"),
        ExportFormat.Json => localization.GetString("ExportDlg_FormatJson"),
        _ => format.ToString()
    };

    /// <summary>A format combo entry: the format it stands for and the name shown for it.</summary>
    private sealed record FormatChoice(ExportFormat Value, string Label)
    {
        public override string ToString() => Label;
    }

    private static async Task<string?> TryGetClipboardTextAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                return await content.GetTextAsync();
            }
        }
        catch
        {
            // Clipboard access can fail in some edge cases; importing still works with manual paste.
        }

        return null;
    }

    /// <summary>
    /// Helper for DataTemplate visibility binding — shows element when int > 0.
    /// </summary>
    public static Visibility IntToVisibility(int value) =>
        value > 0 ? Visibility.Visible : Visibility.Collapsed;
}
