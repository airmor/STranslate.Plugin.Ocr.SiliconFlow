using CommunityToolkit.Mvvm.ComponentModel;

namespace STranslate.Plugin.Ocr.SiliconFlow;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;

    public SettingsViewModel(IPluginContext context, Settings settings, Main main)
    {
        _context = context;
        _settings = settings;
        Main = main;
    }

    public Main Main { get; }

    public void Dispose()
    {
    }
}
