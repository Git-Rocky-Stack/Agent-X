using AgentX.Core.Services.Audio;
using FluentAssertions;
using NAudio.Wave;
using Xunit;

namespace AgentX.Tests.Services.Audio;

/// <summary>
/// Whisper.net 1.5 parses only 16 kHz integer-PCM WAV. These tests pin the conversion that lets
/// the transcription service accept the ordinary 44.1/48 kHz stereo WAVs and float WAVs people
/// actually have, and the fast path that leaves a dictation-shaped WAV untouched.
/// </summary>
public sealed class WhisperAudioConverterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agentx-whisper-conv-" + Guid.NewGuid().ToString("N"));

    public WhisperAudioConverterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void IsWhisperReadyWav_AcceptsTheDictationShape()
    {
        var path = WriteTone("dictation.wav", new WaveFormat(16_000, 16, 1), seconds: 1);

        WhisperAudioConverter.IsWhisperReadyWav(path).Should().BeTrue();
    }

    [Theory]
    [InlineData(44_100, 2)]
    [InlineData(48_000, 1)]
    public void IsWhisperReadyWav_RejectsOtherSampleRates(int sampleRate, int channels)
    {
        var path = WriteTone("cd.wav", new WaveFormat(sampleRate, 16, channels), seconds: 1);

        WhisperAudioConverter.IsWhisperReadyWav(path).Should().BeFalse();
    }

    [Fact]
    public void IsWhisperReadyWav_RejectsFloatPcmEvenAt16Khz()
    {
        var path = WriteTone("float.wav", WaveFormat.CreateIeeeFloatWaveFormat(16_000, 1), seconds: 1);

        WhisperAudioConverter.IsWhisperReadyWav(path).Should().BeFalse();
    }

    [Fact]
    public void IsWhisperReadyWav_ReturnsFalseForNonWavOrCorruptFiles()
    {
        var mp3 = Path.Combine(_dir, "song.mp3");
        File.WriteAllBytes(mp3, [0xFF, 0xFB, 0x90, 0x00]);
        var badWav = Path.Combine(_dir, "bad.wav");
        File.WriteAllText(badWav, "definitely not RIFF");

        WhisperAudioConverter.IsWhisperReadyWav(mp3).Should().BeFalse();
        WhisperAudioConverter.IsWhisperReadyWav(badWav).Should().BeFalse();
    }

    [Theory]
    [InlineData(44_100, 2, 16)]
    [InlineData(48_000, 6, 16)]
    [InlineData(22_050, 1, 24)]
    public void ConvertToWhisperWav_ProducesMono16KhzPcm16OfTheSameDuration(int sampleRate, int channels, int bits)
    {
        var source = WriteTone("in.wav", new WaveFormat(sampleRate, bits, channels), seconds: 2);
        var output = Path.Combine(_dir, "out.wav");

        WhisperAudioConverter.ConvertToWhisperWav(source, output, CancellationToken.None);

        using var reader = new WaveFileReader(output);
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.Pcm);
        reader.WaveFormat.SampleRate.Should().Be(16_000);
        reader.WaveFormat.Channels.Should().Be(1);
        reader.WaveFormat.BitsPerSample.Should().Be(16);
        reader.TotalTime.TotalSeconds.Should().BeApproximately(2.0, 0.05);
        WhisperAudioConverter.IsWhisperReadyWav(output).Should().BeTrue();
    }

    [Fact]
    public void ConvertToWhisperWav_ConvertsFloatWav()
    {
        var source = WriteTone("float.wav", WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2), seconds: 1);
        var output = Path.Combine(_dir, "out.wav");

        WhisperAudioConverter.ConvertToWhisperWav(source, output, CancellationToken.None);

        using var reader = new WaveFileReader(output);
        WhisperAudioConverter.IsWhisperReadyFormat(reader.WaveFormat).Should().BeTrue();
        reader.TotalTime.TotalSeconds.Should().BeApproximately(1.0, 0.05);
    }

    [Fact]
    public void ConvertToWhisperWav_KeepsTheSignal()
    {
        // A full-scale tone on the left channel only must survive the downmix at half amplitude,
        // not vanish (which is what dropping channels instead of averaging them would do).
        var source = WriteTone("left.wav", new WaveFormat(44_100, 16, 2), seconds: 1, leftOnly: true);
        var output = Path.Combine(_dir, "out.wav");

        WhisperAudioConverter.ConvertToWhisperWav(source, output, CancellationToken.None);

        using var reader = new WaveFileReader(output);
        var provider = reader.ToSampleProvider();
        var samples = new float[16_000];
        var read = provider.Read(samples, 0, samples.Length);
        var peak = samples.Take(read).Select(Math.Abs).Max();
        peak.Should().BeInRange(0.35f, 0.55f);
    }

    [Fact]
    public void ConvertToWhisperWav_HonoursCancellation()
    {
        var source = WriteTone("long.wav", new WaveFormat(44_100, 16, 2), seconds: 5);
        var output = Path.Combine(_dir, "out.wav");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => WhisperAudioConverter.ConvertToWhisperWav(source, output, cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    private string WriteTone(string name, WaveFormat format, int seconds, bool leftOnly = false)
    {
        var path = Path.Combine(_dir, name);
        using var writer = new WaveFileWriter(path, format);
        var frames = format.SampleRate * seconds;
        for (var i = 0; i < frames; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * 440 * i / format.SampleRate) * 0.9f;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                writer.WriteSample(leftOnly && channel > 0 ? 0f : value);
            }
        }

        return path;
    }
}
