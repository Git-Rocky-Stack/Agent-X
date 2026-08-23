using AgentX.App.ViewModels;
using AgentX.Core.Services.Export.Models;
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
        ViewModel = App.GetService<WorkflowBuilderViewModel>();
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
            ViewModel.StatusMessage = "Output copied to clipboard";
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
        ViewModel.StatusMessage = $"Workflow \"{ViewModel.SelectedWorkflowName}\" copied to clipboard";
    }

    private async void ImportWorkflow_Click(object sender, RoutedEventArgs e)
    {
        var importBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 220,
            MaxHeight = 420,
            PlaceholderText = "Paste exported workflow JSON here"
        };

        var clipboardText = await TryGetClipboardTextAsync();
        if (!string.IsNullOrWhiteSpace(clipboardText))
        {
            importBox.Text = clipboardText;
        }

        var dialog = new ContentDialog
        {
            Title = "Import Workflow",
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
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
                        Text = "Paste a workflow export below. Clipboard text is loaded automatically when available.",
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
            "Export Workflow Result",
            options => ViewModel.ExportCurrentResultAsync(options));
    }

    private async void ExportHistoricalRun_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkflowRunHistoryDisplayItem run })
        {
            return;
        }

        await ShowWorkflowResultExportDialogAsync(
            $"Export Stored Run ({run.StartedAtText})",
            options => ViewModel.ExportHistoricalRunAsync(run, options));
    }

    private async Task ShowWorkflowResultExportDialogAsync(
        string title,
        Func<ExportOptions, Task<ExportResult>> exportAction)
    {
        var formatCombo = new ComboBox
        {
            ItemsSource = WorkflowResultExportFormats,
            SelectedItem = ExportFormat.Markdown,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var includeMetadataToggle = new ToggleSwitch
        {
            Header = "Include metadata",
            IsOn = true
        };

        var dialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = "Export",
            CloseButtonText = "Cancel",
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
                        Text = "Export this workflow result to the default Agent-X export directory.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = "Format" },
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
                Format = (ExportFormat)formatCombo.SelectedItem!,
                IncludeMetadata = includeMetadataToggle.IsOn
            };

            await exportAction(options);
        }
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
