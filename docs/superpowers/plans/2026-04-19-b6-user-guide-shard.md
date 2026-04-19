# B6: UserGuidePage Shard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Shard `UserGuidePage.xaml` (2,555 LOC) into sections using ContentControl + DataTemplateSelector, enabling localization pagination and maintainable section editing.

**Architecture:** Create section model, DataTemplateSelector, and individual DataTemplates for each section. Replace the 21-row Grid monolith with an ItemsControl bound to a section collection. Add localization keys for all strings.

**Tech Stack:** C#, .NET 8, WinUI 3, XAML, xUnit

---

### Task 1: UserGuideSection Model + DataTemplateSelector

**Files:**
- Create: `src/AgentX.App/Models/UserGuideSection.cs`
- Create: `src/AgentX.App/Selectors/UserGuideSectionSelector.cs`
- Create: `src/AgentX.App/ViewModels/UserGuideViewModel.cs`
- Create: `tests/AgentX.Tests/ViewModels/UserGuideViewModelTests.cs`

- [ ] **Step 1: Define UserGuideSection model**

```csharp
public record UserGuideSection(
    string TitleKey,      // Localization key for title
    string IconGlyph,     // Segoe MDL2 icon
    string TemplateKey,   // Maps to DataTemplate key
    int DisplayOrder);    // Sort order

public enum UserGuideSectionType
{
    Welcome, GettingStarted, KnowledgeVault, Chat, Search,
    AiModels, SystemPrompts, Export, VoiceInput, WebImport,
    Sync, KeyboardShortcuts, Privacy, Settings, Troubleshooting,
    TipsAndTricks, Changelog, About
}
```

- [ ] **Step 2: Create UserGuideSectionSelector**

```csharp
public class UserGuideSectionSelector : DataTemplateSelector
{
    public DataTemplate? WelcomeTemplate { get; set; }
    public DataTemplate? GettingStartedTemplate { get; set; }
    // ... one per section type

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item is UserGuideSection section
            ? GetTemplate(section.TemplateKey)
            : null;
    }
}
```

- [ ] **Step 3: Create UserGuideViewModel**

Exposes `ObservableCollection<UserGuideSection> Sections` populated with all 21 sections in display order.

- [ ] **Step 4: Write tests**

Tests: ViewModel populates correct number of sections, sections are in display order, each section has valid TemplateKey.

---

### Task 2: Extract Sections into ResourceDictionary DataTemplates

**Files:**
- Create: `src/AgentX.App/Styles/UserGuideSections.xaml` (ResourceDictionary with all DataTemplates)
- Create: `src/AgentX.App/Styles/UserGuideSections.Welcome.xaml` (first batch)
- Create: `src/AgentX.App/Styles/UserGuideSections.Features.xaml` (second batch)
- Create: `src/AgentX.App/Styles/UserGuideSections.Advanced.xaml` (third batch)

- [ ] **Step 1: Create ResourceDictionary with DataTemplate per section**

Each section becomes a `DataTemplate x:Key="WelcomeTemplate"` containing the existing Border/CardStyle content from UserGuidePage.xaml. Extract in batches:

- **Batch 1 (Welcome + Getting Started):** Rows 0-3 → WelcomeTemplate, GettingStartedTemplate, KnowledgeVaultTemplate
- **Batch 2 (Core Features):** Rows 4-10 → ChatTemplate, SearchTemplate, AiModelsTemplate, SystemPromptsTemplate, ExportTemplate, VoiceInputTemplate, WebImportTemplate
- **Batch 3 (Advanced):** Rows 11-20 → SyncTemplate, KeyboardShortcutsTemplate, PrivacyTemplate, SettingsTemplate, TroubleshootingTemplate, TipsTemplate, ChangelogTemplate, AboutTemplate

- [ ] **Step 2: Replace hardcoded strings with x:Uid references**

Add localization keys: `UserGuide_Welcome_Title`, `UserGuide_Welcome_Description`, etc. for all text content.

- [ ] **Step 3: Verify in designer / runtime**

Ensure all sections render identically to the original monolith.

---

### Task 3: Rebuild UserGuidePage with ItemsControl

**Files:**
- Rewrite: `src/AgentX.App/Views/UserGuidePage.xaml` (target: ~100 LOC)
- Modify: `src/AgentX.App/Views/UserGuidePage.xaml.cs` (add ViewModel wiring)
- Update: Localization files (6 locales) with all UserGuide keys

- [ ] **Step 1: Replace 21-row Grid with ItemsControl**

```xml
<ScrollViewer>
    <ItemsControl ItemsSource="{x:Bind ViewModel.Sections}"
                   ItemTemplateSelector="{StaticResource SectionSelector}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <StackPanel Spacing="16" />
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
    </ItemsControl>
</ScrollViewer>
```

Target: ~100 LOC total for UserGuidePage.xaml.

- [ ] **Step 2: Add all localization keys to 6 locale files**

Languages: en-US, es-ES, fr-FR, de-DE, ja-JP, zh-CN. Add all UserGuide_ prefixed keys.

- [ ] **Step 3: Run localization audit**

```bash
dotnet test AgentX.sln --filter "FullyQualifiedName~Locale" --blame-hang-timeout 60s
```

- [ ] **Step 4: Visual QA — verify all sections render correctly**

- [ ] **Step 5: Run full test suite**

```bash
dotnet test AgentX.sln --blame-hang-timeout 60s
```

---

## Verification Gate

UserGuidePage.xaml ≤ 100 LOC. All 6 locales have UserGuide keys. Locale audit 100% coverage. Visual QA passes.

## Commit Strategy

- `refactor(ui): UserGuideSection model + DataTemplateSelector`
- `refactor(ui): UserGuide sections extracted to ResourceDictionary DataTemplates`
- `refactor(ui): UserGuidePage rebuilt with ItemsControl + localized`
