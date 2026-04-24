# Operations Imported Document Health Pill Implementation Plan

Date: 2026-04-24

## Task 1

Extend `OperationsImportedDocumentPreview` with an optional `HealthStatus` field and a computed `HasHealthStatus` helper so the empty state can omit the pill.

## Task 2

Inject `IDocumentService` into `OperationsOverviewService` and resolve the recent imported document records for the top imported inbox items.

## Task 3

Add imported-document health derivation helpers in `OperationsOverviewService`:

- `Searchable`
- `Processing`
- `Needs Attention`

Also update the imported-document detail line so it reflects the searchable/processing/review state in plain language.

## Task 4

Update `OperationsPage.xaml`:

- register the shared status color converter
- keep the source label visible
- add the compact health pill beside the source label
- hide the pill when `HasHealthStatus == false`

## Task 5

Expand focused tests:

- `OperationsOverviewServiceTests`
  - completed searchable document
  - pending/processing document
  - failed or broken document
- `OperationsViewModelTests`
  - verify `HealthStatus` survives snapshot mapping

## Task 6

Run verification:

- focused `dotnet test` for Operations overview/viewmodel slices
- `dotnet build src\AgentX.App\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`
