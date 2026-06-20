using AgentX.Core.DTOs;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.DTOs;

public class ConversationDtoTests
{
    [Fact]
    public void ConversationDto_HasBranchingFields()
    {
        var dto = new ConversationDto
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            UpdatedAtUtc = DateTime.UtcNow,
            MessageCount = 5,
            IsPinned = false,
            ParentConversationId = 5,
            BranchPointMessageId = 42,
            BranchLabel = "Alt approach",
            BranchCount = 2
        };

        dto.ParentConversationId.Should().Be(5);
        dto.BranchPointMessageId.Should().Be(42);
        dto.BranchLabel.Should().Be("Alt approach");
        dto.BranchCount.Should().Be(2);
    }

    [Fact]
    public void ConversationDto_DefaultBranchingFields_AreNull()
    {
        var dto = new ConversationDto
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            UpdatedAtUtc = DateTime.UtcNow,
            MessageCount = 0,
            IsPinned = false
        };

        dto.ParentConversationId.Should().BeNull();
        dto.BranchPointMessageId.Should().BeNull();
        dto.BranchLabel.Should().BeNull();
        dto.BranchCount.Should().Be(0);
    }
}
