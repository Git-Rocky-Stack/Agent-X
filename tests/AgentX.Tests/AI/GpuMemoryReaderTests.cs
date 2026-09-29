using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class GpuMemoryReaderTests
{
    private const long Gb = 1_073_741_824L;

    [Fact]
    public void Registry_qwMemorySize_wins_over_the_4GB_capped_AdapterRAM()
    {
        // A 24 GB card: WMI AdapterRAM saturates at 4 GB (0xFFF00000), the driver publishes 24 GB.
        var registry = new[]
        {
            new GpuMemoryReader.RegistryAdapter("NVIDIA GeForce RTX 4090", @"pci\ven_10de&dev_2684", 24 * Gb)
        };

        var vram = GpuMemoryReader.SelectVramBytes(
            "NVIDIA GeForce RTX 4090",
            @"PCI\VEN_10DE&DEV_2684&SUBSYS_16F310DE&REV_A1\4&2F1A2C3&0&0008",
            GpuMemoryReader.ParseAdapterRam(0xFFF00000u),
            registry);

        vram.Should().Be(24 * Gb);
        new HardwareCapability { GpuName = "NVIDIA GeForce RTX 4090", GpuVramBytes = vram }
            .RecommendedGpuLayers.Should().Be(33, "a 24 GB card must not be capped at the 4 GB tier");
    }

    [Fact]
    public void Adapter_is_matched_by_name_when_the_device_id_is_missing()
    {
        var registry = new[] { new GpuMemoryReader.RegistryAdapter("AMD Radeon RX 7900 XTX", null, 24 * Gb) };

        GpuMemoryReader.SelectVramBytes("AMD Radeon RX 7900 XTX", null, 4 * Gb, registry)
            .Should().Be(24 * Gb);
    }

    [Fact]
    public void AdapterRAM_is_the_fallback_when_the_driver_publishes_no_size()
    {
        var registry = new[] { new GpuMemoryReader.RegistryAdapter("Intel(R) UHD Graphics 770", null, 0) };

        GpuMemoryReader.SelectVramBytes("Intel(R) UHD Graphics 770", null, 128 * 1_048_576L, registry)
            .Should().Be(128 * 1_048_576L);
        GpuMemoryReader.SelectVramBytes("Unlisted GPU", null, 2 * Gb, Array.Empty<GpuMemoryReader.RegistryAdapter>())
            .Should().Be(2 * Gb);
    }

    [Fact]
    public void AdapterRAM_is_read_as_unsigned()
    {
        GpuMemoryReader.ParseAdapterRam(null).Should().Be(0);
        GpuMemoryReader.ParseAdapterRam(unchecked((int)0xFFF00000u)).Should().Be(0xFFF00000L);
        GpuMemoryReader.ParseAdapterRam(2147483648u).Should().Be(2147483648L);
        GpuMemoryReader.ParseAdapterRam("not a number").Should().Be(0);
    }

    [Fact]
    public void Registry_memory_values_are_parsed_from_qword_dword_and_binary()
    {
        GpuMemoryReader.ParseRegistryMemorySize(12 * Gb).Should().Be(12 * Gb);
        GpuMemoryReader.ParseRegistryMemorySize(unchecked((int)0x80000000u)).Should().Be(2 * Gb);
        GpuMemoryReader.ParseRegistryMemorySize(BitConverter.GetBytes(16 * Gb)).Should().Be(16 * Gb);
        GpuMemoryReader.ParseRegistryMemorySize(BitConverter.GetBytes(0x40000000u)).Should().Be(Gb);
        GpuMemoryReader.ParseRegistryMemorySize(null).Should().Be(0);
        GpuMemoryReader.ParseRegistryMemorySize(new byte[2]).Should().Be(0);
    }

    [Fact]
    public void ReadAdapters_is_empty_rather_than_throwing_on_non_Windows_hosts()
    {
        if (OperatingSystem.IsWindows())
            return; // Real WMI probe on Windows; covered by the environment-tolerant GPU tier test.

        GpuMemoryReader.ReadAdapters(Log.Logger).Should().BeEmpty();
    }
}
