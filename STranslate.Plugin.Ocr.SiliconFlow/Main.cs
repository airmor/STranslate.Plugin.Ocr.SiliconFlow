using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using STranslate.Plugin.Ocr.SiliconFlow.Adapters;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace STranslate.Plugin.Ocr.SiliconFlow;

public partial class Main : ObservableObject, IOcrPlugin, ILlm
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    public IEnumerable<LangEnum> SupportedLanguages => Enum.GetValues<LangEnum>();

    public ObservableCollection<Prompt> Prompts { get; set; } = [];

    public Prompt? SelectedPrompt
    {
        get => Prompts.FirstOrDefault(p => p.IsEnabled);
        set => SelectPrompt(value);
    }

    public void SelectPrompt(Prompt? prompt)
    {
        if (prompt == null) return;

        foreach (var p in Prompts)
        {
            p.IsEnabled = p == prompt;
        }

        OnPropertyChanged(nameof(SelectedPrompt));
    }

    public void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();
        // Prompts 为各模型固定模板的只读展示（协议硬编码，不提供编辑）
        Prompts.Clear();
        foreach (var prompt in PromptCatalog.BuildDefaultPrompts())
        {
            Prompts.Add(prompt);
        }
    }

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings, this);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Dispose() => _viewModel?.Dispose();

    public bool SupportBoxPoints() =>
        OcrModelRegistry.Resolve(Settings).SupportsCoordinates(Settings);

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var adapter = OcrModelRegistry.Resolve(Settings);
            var result = await SiliconFlowClient.RecognizeAsync(Context, Settings, adapter, request, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Context.Logger.LogError(exception, "SiliconFlow OCR failed");
            return new OcrResult().Fail(SiliconFlowClient.Redact(exception.Message, Settings.ApiKey));
        }
    }
}
