using AgentX.Core.Services.Screen;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Screen;

/// <summary>
/// Unit tests for <see cref="IdeWindowDetector"/>.
/// Covers IDE detection from window titles, language inference,
/// VS Code remote suffix stripping, and various edge cases.
/// </summary>
public sealed class IdeWindowDetectorTests
{
    // ── VS Code detection ─────────────────────────────────────────────────────

    [Fact]
    public void Detect_VsCodeTitle_ReturnsDetection()
    {
        // Arrange
        var title = "Program.cs - Agent-X - Visual Studio Code";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.IdeName.Should().Be("VS Code");
        result.ActiveFileName.Should().Be("Program.cs");
        result.ProjectName.Should().Be("Agent-X");
        result.Language.Should().Be("C#");
        result.RawTitle.Should().Be(title);
    }

    [Fact]
    public void Detect_VsCodeRemoteTitle_StripsRemoteSuffix()
    {
        // Arrange
        var title = "app.ts - my-app [SSH: dev-server] - Visual Studio Code";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.IdeName.Should().Be("VS Code");
        result.ActiveFileName.Should().Be("app.ts");
        result.ProjectName.Should().Be("my-app");
        result.Language.Should().Be("TypeScript");
    }

    [Fact]
    public void Detect_VsCodeSingleFile_NoProject()
    {
        // Arrange
        var title = "README.md - Visual Studio Code";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.IdeName.Should().Be("VS Code");
        result.ActiveFileName.Should().Be("README.md");
        result.ProjectName.Should().BeEmpty();
        result.Language.Should().Be("Markdown");
    }

    // ── Visual Studio detection ───────────────────────────────────────────────

    [Fact]
    public void Detect_VisualStudio2022_ReturnsDetection()
    {
        // Arrange
        var title = "Form1.cs - MyApp - Microsoft Visual Studio 2022";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.IdeName.Should().Be("Visual Studio");
        result.ActiveFileName.Should().Be("Form1.cs");
        result.ProjectName.Should().Be("MyApp");
        result.Language.Should().Be("C#");
    }

    [Fact]
    public void Detect_VisualStudioUnversioned_ReturnsDetection()
    {
        // Arrange
        var title = "Program.cs - Solution - Microsoft Visual Studio";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.IdeName.Should().Be("Visual Studio");
        result.ActiveFileName.Should().Be("Program.cs");
        result.ProjectName.Should().Be("Solution");
    }

    // ── JetBrains detection ────────────────────────────────────────────────────

    [Fact]
    public void Detect_RiderTitle_ReturnsDetection()
    {
        // Arrange — JetBrains Rider uses en-dash (U+2013) as separator
        var title = "Program.cs \u2013 Agent-X \u2013 JetBrains Rider";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.IdeName.Should().Be("JetBrains Rider");
        result.ActiveFileName.Should().Be("Program.cs");
        result.ProjectName.Should().Be("Agent-X");
    }

    [Fact]
    public void Detect_IntelliJTitle_ReturnsDetection()
    {
        // Arrange — IntelliJ IDEA uses en-dash (U+2013) as separator
        var title = "Main.java \u2013 my-app \u2013 IntelliJ IDEA";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.IdeName.Should().Be("IntelliJ IDEA");
        result.ActiveFileName.Should().Be("Main.java");
        result.ProjectName.Should().Be("my-app");
        result.Language.Should().Be("Java");
    }

    // ── Cursor and Zed detection ───────────────────────────────────────────────

    [Fact]
    public void Detect_CursorTitle_ReturnsDetection()
    {
        // Arrange
        var title = "App.tsx - my-project - Cursor";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.IdeName.Should().Be("Cursor");
        result.ActiveFileName.Should().Be("App.tsx");
        result.ProjectName.Should().Be("my-project");
        result.Language.Should().Be("TypeScript");
    }

    [Fact]
    public void Detect_ZedTitle_ReturnsDetection()
    {
        // Arrange — Zed uses em-dash (U+2014) as separator
        var title = "main.rs \u2014 Zed";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.IdeName.Should().Be("Zed");
        result.ActiveFileName.Should().Be("main.rs");
        result.ProjectName.Should().BeEmpty();
        result.Language.Should().Be("Rust");
    }

    // ── Edge cases ─────────────────────────────────────────────────────────────

    [Fact]
    public void Detect_NullTitle_ReturnsNull()
    {
        // Act
        var result = IdeWindowDetector.Detect(null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Detect_EmptyTitle_ReturnsNull()
    {
        // Act
        var result = IdeWindowDetector.Detect(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Detect_WhitespaceTitle_ReturnsNull()
    {
        // Act
        var result = IdeWindowDetector.Detect("   \t\n  ");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Detect_UnknownIde_ReturnsNull()
    {
        // Arrange — a title that doesn't match any known IDE suffix
        var title = "Some random window title";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Google - Chrome")]
    [InlineData("Stack Overflow - Mozilla Firefox")]
    [InlineData("Welcome - Microsoft Edge")]
    public void Detect_BrowserTitle_ReturnsNull(string browserTitle)
    {
        // Act
        var result = IdeWindowDetector.Detect(browserTitle);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Detect_TitleWithNoFileExtension_LanguageEmpty()
    {
        // Arrange — "Dockerfile" has no dot-extension but is handled as a special case
        // This test covers a file name that is NOT Dockerfile and has no extension
        var title = "Makefile - my-project - Visual Studio Code";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.ActiveFileName.Should().Be("Makefile");
        result.Language.Should().BeEmpty();
    }

    [Fact]
    public void Detect_DockerfileSpecialCase_LanguageDocker()
    {
        // Arrange
        var title = "Dockerfile - my-project - Visual Studio Code";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.ActiveFileName.Should().Be("Dockerfile");
        result.Language.Should().Be("Docker");
    }

    [Fact]
    public void Detect_DockerfileWithSuffix_LanguageDocker()
    {
        // Arrange — Dockerfile.dev is also recognized
        var title = "Dockerfile.dev - my-app - Visual Studio Code";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.ActiveFileName.Should().Be("Dockerfile.dev");
        result.Language.Should().Be("Docker");
    }

    [Fact]
    public void Detect_UnrecognizedExtension_LanguageEmpty()
    {
        // Arrange
        var title = "data.xyz - my-project - Visual Studio Code";

        // Act
        var result = IdeWindowDetector.Detect(title);

        // Assert
        result.Should().NotBeNull();
        result!.Language.Should().BeEmpty();
    }

    // ── Language inference ─────────────────────────────────────────────────────

    [Fact]
    public void InferLanguage_CsFile_ReturnsCSharp()
    {
        var result = IdeWindowDetector.Detect("Calculator.cs - MyApp - Visual Studio Code");
        result!.Language.Should().Be("C#");
    }

    [Fact]
    public void InferLanguage_PyFile_ReturnsPython()
    {
        var result = IdeWindowDetector.Detect("script.py - DataProject - Visual Studio Code");
        result!.Language.Should().Be("Python");
    }

    [Fact]
    public void InferLanguage_TsFile_ReturnsTypeScript()
    {
        var result = IdeWindowDetector.Detect("index.ts - WebApp - Visual Studio Code");
        result!.Language.Should().Be("TypeScript");
    }

    [Fact]
    public void InferLanguage_RsFile_ReturnsRust()
    {
        var result = IdeWindowDetector.Detect("lib.rs - rust-project - Visual Studio Code");
        result!.Language.Should().Be("Rust");
    }

    [Fact]
    public void InferLanguage_GoFile_ReturnsGo()
    {
        var result = IdeWindowDetector.Detect("main.go - goservice - Visual Studio Code");
        result!.Language.Should().Be("Go");
    }

    [Fact]
    public void InferLanguage_XamlFile_ReturnsXAML()
    {
        var result = IdeWindowDetector.Detect("MainWindow.xaml - WpfApp - Visual Studio Code");
        result!.Language.Should().Be("XAML");
    }

    [Fact]
    public void InferLanguage_RazorFile_ReturnsBlazor()
    {
        var result = IdeWindowDetector.Detect("Index.razor - BlazorApp - Visual Studio Code");
        result!.Language.Should().Be("Blazor");
    }

    [Fact]
    public void InferLanguage_Dockerfile_ReturnsDocker()
    {
        var result = IdeWindowDetector.Detect("Dockerfile - my-project - Visual Studio Code");
        result!.Language.Should().Be("Docker");
    }

    // ── Additional language mappings ───────────────────────────────────────────

    [Theory]
    [InlineData("app.jsx - frontend - Visual Studio Code", "React JSX")]
    [InlineData("style.scss - frontend - Visual Studio Code", "CSS")]
    [InlineData("App.vue - frontend - Visual Studio Code", "")]
    [InlineData("handler.go - backend - Visual Studio Code", "Go")]
    [InlineData("main.swift - ios-app - Visual Studio Code", "Swift")]
    [InlineData("build.gradle.kts - android-app - Visual Studio Code", "")]
    [InlineData("config.yaml - infra - Visual Studio Code", "YAML")]
    [InlineData("query.sql - database - Visual Studio Code", "SQL")]
    [InlineData("script.sh - ops - Visual Studio Code", "Shell")]
    [InlineData("script.bash - ops - Visual Studio Code", "Shell")]
    [InlineData("profile.ps1 - ops - Visual Studio Code", "PowerShell")]
    [InlineData("page.html - web - Visual Studio Code", "HTML")]
    [InlineData("app.css - web - Visual Studio Code", "CSS")]
    [InlineData("data.json - config - Visual Studio Code", "JSON")]
    [InlineData("data.xml - config - Visual Studio Code", "XML")]
    [InlineData("app.mjs - frontend - Visual Studio Code", "JavaScript")]
    [InlineData("program.fs - fsharp-app - Visual Studio Code", "F#")]
    [InlineData("module.vb - vb-app - Visual Studio Code", "Visual Basic")]
    [InlineData("header.hpp - cpp-lib - Visual Studio Code", "C/C++ Header")]
    [InlineData("header.h - c-lib - Visual Studio Code", "C/C++ Header")]
    [InlineData("main.cpp - cpp-app - Visual Studio Code", "C++")]
    [InlineData("main.c - c-app - Visual Studio Code", "C")]
    [InlineData("app.cc - cpp-app - Visual Studio Code", "C++")]
    [InlineData("app.cxx - cpp-app - Visual Studio Code", "C++")]
    [InlineData("model.scala - bigdata - Visual Studio Code", "Scala")]
    [InlineData("view.kt - android - Visual Studio Code", "Kotlin")]
    [InlineData("page.rb - rails-app - Visual Studio Code", "Ruby")]
    [InlineData("index.php - web-app - Visual Studio Code", "PHP")]
    [InlineData("README.md - docs - Visual Studio Code", "Markdown")]
    public void Detect_VariousFileExtensions_ReturnsExpectedLanguage(string title, string expectedLanguage)
    {
        var result = IdeWindowDetector.Detect(title);
        result!.Language.Should().Be(expectedLanguage);
    }

    // ── RawTitle preservation ──────────────────────────────────────────────────

    [Fact]
    public void Detect_PreservesRawTitle()
    {
        var title = "app.tsx - my-project - Visual Studio Code";
        var result = IdeWindowDetector.Detect(title);
        result!.RawTitle.Should().Be(title);
    }

    [Fact]
    public void Detect_PreservesRawTitle_WithUnicodeSeparators()
    {
        var title = "Program.cs \u2013 Agent-X \u2013 JetBrains Rider";
        var result = IdeWindowDetector.Detect(title);
        result!.RawTitle.Should().Be(title);
    }

    // ── VS Code remote suffix variations ───────────────────────────────────────

    [Theory]
    [InlineData("file.ts - workspace [SSH: user@host] - Visual Studio Code", "workspace")]
    [InlineData("file.ts - workspace [Dev Container: my-container] - Visual Studio Code", "workspace")]
    [InlineData("file.ts - workspace [WSL: Ubuntu] - Visual Studio Code", "workspace")]
    public void Detect_VsCodeRemoteVariations_StripsRemoteSuffix(string title, string expectedProject)
    {
        var result = IdeWindowDetector.Detect(title);
        result!.ProjectName.Should().Be(expectedProject);
    }
}
