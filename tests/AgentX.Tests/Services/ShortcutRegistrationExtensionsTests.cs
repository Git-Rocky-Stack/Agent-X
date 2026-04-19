using System.Linq;
using System.Threading.Tasks;
using AgentX.App.Helpers;
using AgentX.Core.Services.Shortcuts;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services;

public class ShortcutRegistrationExtensionsTests
{
    [Fact]
    public void RegisterShortcuts_registers_all_descriptors_and_disposes_them_together()
    {
        var registry = new ShortcutRegistry();
        var first = Descriptor("docs.refresh", VirtualKeyCode.F5);
        var second = Descriptor("docs.search", VirtualKeyCode.F);

        var scope = registry.RegisterShortcuts(first, second);

        registry.All().Select(d => d.Id).Should().BeEquivalentTo("docs.refresh", "docs.search");

        scope.Dispose();

        registry.All().Should().BeEmpty();
    }

    [Fact]
    public void Composite_registration_dispose_is_idempotent()
    {
        var registry = new ShortcutRegistry();
        var scope = registry.RegisterShortcuts(Descriptor("settings.save", VirtualKeyCode.S));

        scope.Dispose();
        scope.Dispose();

        registry.All().Should().BeEmpty();
    }

    private static ShortcutDescriptor Descriptor(string id, VirtualKeyCode key)
    {
        return new ShortcutDescriptor(
            id,
            id,
            new ShortcutScope("TestPage"),
            new[] { new KeyChord(KeyModifiers.Ctrl, key) },
            _ => Task.CompletedTask,
            "Test");
    }
}
