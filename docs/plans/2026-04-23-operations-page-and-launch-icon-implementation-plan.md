# Operations Page And Launch Icon Implementation Plan

Date: 2026-04-23

## Objective

Add a dedicated `Operations` page backed by the shared operations overview service, wire Dashboard into it, and set the executable launch icon from `icons/agent_x.png`.

## Tasks

1. Add the new operations surface.
   - Create `OperationsViewModel` that loads `IOperationsOverviewService`.
   - Create `OperationsPage.xaml` and code-behind using the existing page/view-model pattern.

2. Register the page in app composition.
   - Add the view model and page to `App.xaml.cs`.
   - Register the page in `MainWindow` page mapping and navigation item mapping.

3. Wire discoverability.
   - Add an `Operations` shell item under `INTELLIGENCE`.
   - Add a dashboard `Open Operations` handoff.
   - Add keyboard/command discoverability through `ShortcutCatalog`.

4. Keep Dashboard compact.
   - Preserve the current operations overview cards.
   - Add a single clear dashboard button into the new Operations page rather than expanding dashboard further.

5. Wire the launch icon.
   - Use `icons/agent_x.png` as the source asset.
   - Generate or add the required `.ico` if the WinUI project needs it for `ApplicationIcon`.
   - Update `AgentX.App.csproj` so the executable uses the new icon.

6. Add focused tests.
   - Add `OperationsViewModelTests`.
   - Extend dashboard and shortcut/command coverage for the new Operations destination.
   - Link any new App-layer files into the test project as needed.

7. Verify.
   - Run targeted operations/dashboard/shortcut tests.
   - Run `dotnet build src\AgentX.App\AgentX.App.csproj -p:RuntimeIdentifier=win-x64 --no-restore`.
