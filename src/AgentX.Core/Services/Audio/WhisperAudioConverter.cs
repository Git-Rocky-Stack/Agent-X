using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AgentX.Core.Services.Audio;

/// <summary>
/// Prepares audio files for Whisper.net.
/// <para>
/// Whisper.net 1.5 reads only RIFF/WAVE integer PCM recorded at exactly 16 kHz; anything else
/// fails inside its wave parser ("Invalid wave file RIFF header" for MP3/M4A/FLAC, "Only 16KHz
/// sample rate is supported" for an ordinary 44.1 or 48 kHz WAV). This helper decodes the
/// source, mixes it down to mono, resamples it to 16 kHz and writes 16-bit PCM, so every format
/// the transcription service advertises reaches the model in the one shape it accepts.
/// </para>
/// <para>
/// WAV input is decoded by NAudio's managed reader on every platform. Compressed formats
/// (MP3, M4A/AAC, FLAC, and OGG/WEBM where the codec pack is installed) are decoded through
/// Windows Media Foundation.
/// </para>
/// </summary>
internal static class WhisperAudioConverter
{
    /// <summary>The only sample rate Whisper.net accepts.</summary>
    internal const int WhisperSampleRate = 16_000;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="path"/> is a WAV file Whisper.net can
    /// read as-is, so no conversion (and no temporary file) is needed. Voice dictation records in
    /// exactly this shape.
    /// </summary>
    internal static bool IsWhisperReadyWav(string path)
    {
        if (!IsWavPath(path))
        {
            return false;
        }

        try
        {
            using var reader = new WaveFileReader(path);
            return IsWhisperReadyFormat(reader.WaveFormat);
        }
        catch (FormatException)
        {
            // Not a parseable RIFF/WAVE file. Conversion will surface the real error.
            return false;
        }
    }

    /// <summary>
    /// Whether Whisper.net's wave parser accepts <paramref name="format"/> directly: integer PCM,
    /// 16 kHz, 8/16/24/32 bits per sample. Channels are averaged by Whisper.net itself.
    /// </summary>
    internal static bool IsWhisperReadyFormat(WaveFormat format) =>
        format.Encoding == WaveFormatEncoding.Pcm
        && format.SampleRate == WhisperSampleRate
        && format.BitsPerSample is 8 or 16 or 24 or 32;

    /// <summary>
    /// Decodes <paramref name="inputPath"/>, mixes it down to mono, resamples it to 16 kHz and
    /// writes 16-bit PCM WAV to <paramref name="outputPath"/>. Synchronous and CPU-bound; call it
    /// off the UI thread.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The file is a compressed format and this platform has no Media Foundation decoder.
    /// </exception>
    internal static void ConvertToWhisperWav(string inputPath, string outputPath, CancellationToken ct)
    {
        using var reader = OpenReader(inputPath);

        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels > 1)
        {
            samples = new MonoDownmixSampleProvider(samples);
        }

        if (samples.WaveFormat.SampleRate != WhisperSampleRate)
        {
            samples = new WdlResamplingSampleProvider(samples, WhisperSampleRate);
        }

        var pcm16 = new SampleToWaveProvider16(samples);

        using var writer = new WaveFileWriter(outputPath, pcm16.WaveFormat);
        var buffer = new byte[pcm16.WaveFormat.AverageBytesPerSecond];
        int read;
        while ((read = pcm16.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            writer.Write(buffer, 0, read);
        }
    }

    private static WaveStream OpenReader(string path)
    {
        if (IsWavPath(path))
        {
            var wav = new WaveFileReader(path);
            if (wav.WaveFormat.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible)
            {
                return wav;
            }

            // A compressed WAV payload (ADPCM, mu-law, ...): hand it to Media Foundation below.
            wav.Dispose();
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new NotSupportedException(
                $"Decoding '{Path.GetExtension(path)}' audio requires Windows Media Foundation.");
        }

        return new MediaFoundationReader(path);
    }

    private static bool IsWavPath(string path) =>
        string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Averages any number of interleaved channels into one. NAudio's own
    /// <see cref="StereoToMonoSampleProvider"/> handles exactly two channels, and a 5.1 M4A
    /// is a realistic input.
    /// </summary>
    private sealed class MonoDownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _sourceBuffer = [];

        public MonoDownmixSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var needed = count * _channels;
            if (_sourceBuffer.Length < needed)
            {
                _sourceBuffer = new float[needed];
            }

            var frames = _source.Read(_sourceBuffer, 0, needed) / _channels;
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                var first = frame * _channels;
                for (var channel = 0; channel < _channels; channel++)
                {
                    sum += _sourceBuffer[first + channel];
                }

                buffer[offset + frame] = sum / _channels;
            }

            return frames;
        }
    }
}
