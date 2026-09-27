using AgentX.Core.AI.Context;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Chat;

/// <summary>
/// The chat context inspector's story, chips and explanations are built in Core, and were
/// English in every language. Core words them through FormatHelper.LocalizedText, which the app
/// sets at startup; without it the English is exactly what it was. Setting it changes shared
/// state, so these run in the collection that runs on its own.
/// </summary>
[Collection(nameof(FormatHelperLocalizedTextCollection))]
public sealed class ChatContextInspectionWordingTests
{
    [Fact]
    public void Without_the_apps_resources_the_wording_is_the_english_it_always_was()
    {
        var limited = ChatContextInspectionSnapshot.CreateLimited(7, "question", "provider_disconnected");
        limited.ContextStoryText.Should().Be(
            "This response used a limited-visibility path, so only partial chat context details are available.");
        limited.ContextStorySourceChips.Select(chip => chip.Label).Should().Equal("Limited Visibility");
        limited.AssemblyExplanation.Should().Be(
            "Agent-X generated a response without the full context assembly pipeline.");
        limited.RecallExplanation.Should().Be("Durable recall details are unavailable for this response path.");

        var stale = StaleSnapshotWithRecallAndOverflow();
        stale.ContextStoryText.Should().Be(
            "Using a stale durable summary with 2 newer messages still outside it, " +
            "2 recalled messages from other conversations, and compressed overflow context.");
        stale.ContextStorySourceChips.Select(chip => chip.Label)
            .Should().Equal("Stale Summary", "2 Recall Matches", "Compressed Overflow");

        (stale with { Summary = stale.Summary! with { PendingMessageCount = 1 }, RecallMatches = [Recall(1)] })
            .ContextStoryText.Should().Be(
                "Using a stale durable summary with 1 newer message still outside it, " +
                "1 recalled message from another conversation, and compressed overflow context.");

        ChatService.BuildAssemblyExplanation(new ContextAssemblyDiagnostics { OverflowMessageCount = 2 })
            .Should().Be("Agent-X selected a bounded subset of the thread and evaluated overflow context against the remaining budget.");
        ChatService.BuildCompressionExplanation(new ContextAssemblyDiagnostics { CompressionSkipReason = "some_new_reason" })
            .Should().Be("No overflow summary was added (some new reason).");
        ChatService.BuildRecallExplanation(new ContextAssemblyDiagnostics { AddedDurableRecall = true, RecalledMessageCount = 3 })
            .Should().Be("Agent-X added 3 recalled messages from other conversations as supporting context.");
        ChatService.BuildRecallExplanation(new ContextAssemblyDiagnostics { DurableRecallSkipReason = "no_recall_matches" })
            .Should().Be("Durable recall found no relevant cross-conversation matches for this response.");
    }

    [Fact]
    public void With_the_apps_resources_the_story_chips_and_explanations_are_in_the_users_language()
    {
        var previous = FormatHelper.LocalizedText;
        FormatHelper.LocalizedText = ReswLocalization.For("de").GetString;
        try
        {
            var limited = ChatContextInspectionSnapshot.CreateLimited(7, "question", "provider_disconnected");
            limited.ContextStoryText.Should().Be(
                "Diese Antwort wurde über einen Weg mit eingeschränkter Einsicht erzeugt, " +
                "daher sind nur Teile der Chat-Kontextdetails verfügbar.");
            limited.ContextStorySourceChips.Select(chip => chip.Label).Should().Equal("Eingeschränkte Einsicht");
            limited.AssemblyExplanation.Should().Be(
                "Agent-X hat eine Antwort ohne die vollständige Pipeline zur Kontextzusammenstellung erzeugt.");

            var stale = StaleSnapshotWithRecallAndOverflow();
            stale.ContextStoryText.Should().Be(
                "Grundlage: eine veraltete dauerhafte Zusammenfassung (2 neuere Nachrichten noch nicht eingearbeitet), " +
                "2 abgerufene Nachrichten aus anderen Unterhaltungen und komprimierter Überlaufkontext.");
            stale.ContextStorySourceChips.Select(chip => chip.Label)
                .Should().Equal("Veraltete Zusammenfassung", "2 Recall-Treffer", "Komprimierter Überlauf");

            ChatService.BuildRecallExplanation(new ContextAssemblyDiagnostics { AddedDurableRecall = true, RecalledMessageCount = 3 })
                .Should().Be("Agent-X hat 3 abgerufene Nachrichten aus anderen Unterhaltungen als unterstützenden Kontext hinzugefügt.");
            ChatService.BuildCompressionExplanation(new ContextAssemblyDiagnostics { CompressionSkipReason = "some_new_reason" })
                .Should().Be("Es wurde keine Überlaufzusammenfassung hinzugefügt (some new reason).");
        }
        finally
        {
            FormatHelper.LocalizedText = previous;
        }
    }

    private static ChatContextInspectionSnapshot StaleSnapshotWithRecallAndOverflow() => new()
    {
        ConversationId = 7,
        CapturedAt = DateTime.UtcNow,
        Diagnostics = new ContextAssemblyDiagnostics { AddedOverflowSummary = true },
        Summary = new ConversationSummaryInspection { ConversationId = 7, IsStale = true, PendingMessageCount = 2 },
        RecallMatches = [Recall(1), Recall(2)]
    };

    private static ChatContextRecallInspectionItem Recall(long messageId) => new()
    {
        ConversationId = 3,
        MessageId = messageId,
        ConversationTitle = "Earlier thread",
        Role = "user",
        ContentPreview = "A recalled message"
    };
}
