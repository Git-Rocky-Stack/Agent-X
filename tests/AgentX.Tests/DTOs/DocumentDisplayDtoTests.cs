using AgentX.Core.DTOs;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.DTOs;

/// <summary>
/// Covers the display projection every document list binds to. The icon selector is a
/// wide switch over file extensions, so a typo in one arm shows the wrong glyph for a
/// whole file class and nothing else in the suite notices.
/// </summary>
public sealed class DocumentDisplayDtoTests
{
    private const string DocumentGlyph = "\uE8A5";
    private const string TextGlyph = "\uE8A4";
    private const string PdfGlyph = "\uEA90";
    private const string CodeGlyph = "\uE943";
    private const string ImageGlyph = "\uEB9F";
    private const string AudioGlyph = "\uE8D6";

    [Theory]
    [InlineData(".pdf", PdfGlyph)]
    [InlineData(".docx", DocumentGlyph)]
    [InlineData(".doc", DocumentGlyph)]
    [InlineData(".txt", TextGlyph)]
    [InlineData(".md", CodeGlyph)]
    [InlineData(".markdown", CodeGlyph)]
    [InlineData(".cs", CodeGlyph)]
    [InlineData(".js", CodeGlyph)]
    [InlineData(".ts", CodeGlyph)]
    [InlineData(".py", CodeGlyph)]
    [InlineData(".java", CodeGlyph)]
    [InlineData(".cpp", CodeGlyph)]
    [InlineData(".c", CodeGlyph)]
    [InlineData(".go", CodeGlyph)]
    [InlineData(".rs", CodeGlyph)]
    [InlineData(".png", ImageGlyph)]
    [InlineData(".jpg", ImageGlyph)]
    [InlineData(".jpeg", ImageGlyph)]
    [InlineData(".gif", ImageGlyph)]
    [InlineData(".bmp", ImageGlyph)]
    [InlineData(".webp", ImageGlyph)]
    [InlineData(".mp3", AudioGlyph)]
    [InlineData(".wav", AudioGlyph)]
    [InlineData(".flac", AudioGlyph)]
    [InlineData(".ogg", AudioGlyph)]
    public void GetFileTypeIcon_MapsEveryKnownExtension(string extension, string expected)
    {
        DocumentDisplayDto.GetFileTypeIcon(extension).Should().Be(expected);
    }

    [Theory]
    [InlineData(".PDF", PdfGlyph)]
    [InlineData(".DocX", DocumentGlyph)]
    [InlineData(".JPEG", ImageGlyph)]
    public void GetFileTypeIcon_IsCaseInsensitive(string extension, string expected)
    {
        DocumentDisplayDto.GetFileTypeIcon(extension).Should().Be(expected);
    }

    [Theory]
    [InlineData(".xyz")]
    [InlineData("")]
    [InlineData("pdf")]          // no leading dot, so not a match
    [InlineData(".pdf.bak")]
    public void GetFileTypeIcon_FallsBackToTheTextGlyph(string extension)
    {
        DocumentDisplayDto.GetFileTypeIcon(extension).Should().Be(TextGlyph);
    }

    [Fact]
    public void GetFileTypeIcon_NullExtension_FallsBackInsteadOfThrowing()
    {
        DocumentDisplayDto.GetFileTypeIcon(null!).Should().Be(TextGlyph);
    }

    [Fact]
    public void FileTypeIcon_ProjectsTheFileTypeProperty()
    {
        Dto(fileType: ".png").FileTypeIcon.Should().Be(ImageGlyph);
    }

    [Fact]
    public void FileSizeFormatted_RendersHumanReadableBytes()
    {
        // 1 MiB. The exact unit string comes from FormatHelper; what this pins is that
        // the DTO formats the byte count rather than echoing the raw number.
        var formatted = Dto(fileSizeBytes: 1024L * 1024L).FileSizeFormatted;

        formatted.Should().NotBe("1048576");
        formatted.Should().Contain("1");
        formatted.Should().MatchRegex("[A-Za-z]");
    }

    [Fact]
    public void ImportedAgo_DescribesTheImportTimeRelatively()
    {
        var ago = Dto(importedAtUtc: DateTime.UtcNow.AddHours(-3)).ImportedAgo;

        ago.Should().NotBeNullOrWhiteSpace();
        ago.Should().NotBe(DateTime.UtcNow.AddHours(-3).ToString());
    }

    [Fact]
    public void Tags_DefaultToEmptyRatherThanNull()
    {
        Dto().Tags.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void CollectionName_IsOptional()
    {
        Dto().CollectionName.Should().BeNull();
    }

    [Fact]
    public void RecordEquality_ComparesByValue()
    {
        var id = Guid.NewGuid();
        var imported = new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc);

        Dto(id: id, importedAtUtc: imported).Should().Be(Dto(id: id, importedAtUtc: imported));
    }

    private static DocumentDisplayDto Dto(
        Guid? id = null,
        string fileType = ".txt",
        long fileSizeBytes = 1024,
        DateTime? importedAtUtc = null) => new()
        {
            Id = id ?? Guid.Empty,
            FileName = "report.txt",
            FilePath = @"C:\docs\report.txt",
            FileType = fileType,
            FileSizeBytes = fileSizeBytes,
            ImportedAtUtc = importedAtUtc ?? new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc),
            IndexingStatus = "indexed",
            ChunkCount = 4,
            WordCount = 900,
            PageCount = 2,
        };
}
