using AgentX.Core.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentX.App.ViewModels;

/// <summary>
/// The Built-in LLM block of the AI Providers section in Settings: how many layers of the built-in
/// model run on the GPU, saved as AppSettings.LocalGpuLayers together with the rest of the page.
/// <para>
/// Automatic is the saved value 0: when the model loads, LocalLlmProvider looks for an NVIDIA GPU
/// and puts 16, 28 or 33 layers on it by its video memory. Otherwise the switch is off and
/// <see cref="GpuLayers"/> is the count, where 0 keeps the model on the CPU (saved as -1, because 0
/// already means Automatic). The layers reach the GPU only when the NVIDIA CUDA 12 Toolkit is
/// installed, since LLamaSharp's CUDA backend needs its runtime; otherwise the model runs on the CPU.
/// </para>
/// <para>
/// Saving settings re-initializes the AI service, which rebuilds the built-in provider when this
/// value changed, so the model is reloaded with it: at once when it is the active provider,
/// otherwise the next time it is used. No restart is needed.
/// </para>
/// </summary>
public sealed partial class BuiltInModelSettingsViewModel : ObservableObject
{
    /// <summary>The largest count the Settings page accepts.</summary>
    public const int MaximumGpuLayers = 999;

    /// <summary>The saved value for "no layers on the GPU".</summary>
    private const int CpuOnlyGpuLayers = -1;

    [ObservableProperty] private bool _isGpuLayersAutomatic = true;

    /// <summary>Layers on the GPU while <see cref="IsGpuLayersAutomatic"/> is off; 0 is CPU only.</summary>
    [ObservableProperty] private int _gpuLayers;

    /// <summary>Shows the saved setting.</summary>
    public void Load(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IsGpuLayersAutomatic = settings.LocalGpuLayers == 0;
        GpuLayers = Math.Clamp(settings.LocalGpuLayers, 0, MaximumGpuLayers);
    }

    /// <summary>Writes the choice into <paramref name="settings"/>, which the page then saves.</summary>
    public void ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (IsGpuLayersAutomatic)
        {
            settings.LocalGpuLayers = 0;
            return;
        }

        var layers = Math.Clamp(GpuLayers, 0, MaximumGpuLayers);
        settings.LocalGpuLayers = layers == 0 ? CpuOnlyGpuLayers : layers;
    }

    /// <summary>Back to Automatic, the default.</summary>
    public void Reset()
    {
        IsGpuLayersAutomatic = true;
        GpuLayers = 0;
    }
}
