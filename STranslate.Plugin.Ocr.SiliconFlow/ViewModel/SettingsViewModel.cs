using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using STranslate.Plugin.Ocr.SiliconFlow.Adapters;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace STranslate.Plugin.Ocr.SiliconFlow;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;

    /// <summary>
    /// 内置 8×8 PNG 测试图（黑底 T 形图案，程序化生成确保校验和合法——
    /// 曾用 1×1 图实测被硅基流动判定 broken PNG）
    /// </summary>
    private static readonly byte[] TestImage = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAIAAABLbSncAAAAG0lEQVR4nGP4jwMwQCkkQJwEXA7dKOpKoDkXALX7j3G63SEQAAAAAElFTkSuQmCC");

    /// <summary>三模型选项（显示名 → 完整 ID）</summary>
    public ObservableCollection<ModelOption> Models { get; } =
    [
        new(OcrModelRegistry.Adapters[0].ModelId, OcrModelRegistry.Adapters[0].DisplayName + "（文档解析 SOTA·带坐标）"),
        new(OcrModelRegistry.Adapters[1].ModelId, OcrModelRegistry.Adapters[1].DisplayName + "（grounding 文档转 MD）"),
        new(OcrModelRegistry.Adapters[2].ModelId, OcrModelRegistry.Adapters[2].DisplayName + "（自由提示词）")
    ];

    public ObservableCollection<string> PaddleModes { get; } =
    [
        nameof(PaddleOcrMode.Document),
        nameof(PaddleOcrMode.Spotting),
        nameof(PaddleOcrMode.Table),
        nameof(PaddleOcrMode.Formula)
    ];

    public ObservableCollection<string> DeepSeekTemplates { get; } =
    [
        nameof(DeepSeekOcrTemplate.Markdown),
        nameof(DeepSeekOcrTemplate.Ocr),
        nameof(DeepSeekOcrTemplate.FreeOcr),
        nameof(DeepSeekOcrTemplate.ParseFigure),
        nameof(DeepSeekOcrTemplate.Describe)
    ];

    public SettingsViewModel(IPluginContext context, Settings settings, Main main)
    {
        _context = context;
        _settings = settings;
        Main = main;

        ApiKey = _settings.ApiKey;
        _selectedModel = Models.FirstOrDefault(m => m.Id == _settings.Model.Trim()) ?? Models[0];
        _paddleMode = PaddleModes.Contains(_settings.PaddleMode) ? _settings.PaddleMode : PaddleModes[0];
        _deepSeekTemplate = DeepSeekTemplates.Contains(_settings.DeepSeekTemplate) ? _settings.DeepSeekTemplate : DeepSeekTemplates[0];
        QwenPrompt = _settings.QwenPrompt;
        Temperature = _settings.Temperature;
        MaxTokens = _settings.MaxTokens;
        TimeoutSeconds = _settings.TimeoutSeconds;
    }

    public Main Main { get; }

    public bool IsPaddleSelected => SelectedModel.Id == OcrModelRegistry.Adapters[0].ModelId;
    public bool IsDeepSeekSelected => SelectedModel.Id == OcrModelRegistry.Adapters[1].ModelId;
    public bool IsQwenSelected => SelectedModel.Id == OcrModelRegistry.Adapters[2].ModelId;
    public bool IsSpotting => IsPaddleSelected && _paddleMode == nameof(PaddleOcrMode.Spotting);

    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (SetProperty(ref _apiKey, value))
            {
                _settings.ApiKey = value;
                Save();
            }
        }
    }
    private string _apiKey = string.Empty;

    public ModelOption SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (value != null && SetProperty(ref _selectedModel, value))
            {
                _settings.Model = value.Id;
                Save();
                OnPropertyChanged(nameof(IsPaddleSelected));
                OnPropertyChanged(nameof(IsDeepSeekSelected));
                OnPropertyChanged(nameof(IsQwenSelected));
                OnPropertyChanged(nameof(IsSpotting));
            }
        }
    }
    private ModelOption _selectedModel = null!;

    public string PaddleMode
    {
        get => _paddleMode;
        set
        {
            if (value != null && SetProperty(ref _paddleMode, value))
            {
                _settings.PaddleMode = value;
                Save();
                OnPropertyChanged(nameof(IsSpotting));
            }
        }
    }
    private string _paddleMode = null!;

    public string DeepSeekTemplate
    {
        get => _deepSeekTemplate;
        set
        {
            if (value != null && SetProperty(ref _deepSeekTemplate, value))
            {
                _settings.DeepSeekTemplate = value;
                Save();
            }
        }
    }
    private string _deepSeekTemplate = null!;

    public string QwenPrompt
    {
        get => _qwenPrompt;
        set
        {
            if (SetProperty(ref _qwenPrompt, value))
            {
                _settings.QwenPrompt = value;
                Save();
            }
        }
    }
    private string _qwenPrompt = string.Empty;

    public double Temperature
    {
        get => _temperature;
        set
        {
            if (SetProperty(ref _temperature, value))
            {
                _settings.Temperature = value;
                Save();
            }
        }
    }
    private double _temperature;

    public int MaxTokens
    {
        get => _maxTokens;
        set
        {
            if (SetProperty(ref _maxTokens, value))
            {
                _settings.MaxTokens = value;
                Save();
            }
        }
    }
    private int _maxTokens;

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set
        {
            if (SetProperty(ref _timeoutSeconds, value))
            {
                _settings.TimeoutSeconds = value;
                Save();
            }
        }
    }
    private int _timeoutSeconds;

    [ObservableProperty]
    private string _validateResult = string.Empty;

    [RelayCommand]
    private async Task ValidateAsync()
    {
        ValidateResult = _context.GetTranslation("Validating") is { Length: > 0 } v && v != "Validating" ? v : "验证中...";
        try
        {
            var adapter = OcrModelRegistry.Resolve(_settings);
            var request = new OcrRequest(TestImage, LangEnum.Auto, 1, 1);
            var result = await SiliconFlowClient.RecognizeAsync(_context, _settings, adapter, request, CancellationToken.None);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.ErrorMessage);

            ValidateResult = $"连接成功 · {adapter.DisplayName}";
        }
        catch (Exception exception)
        {
            _context.Logger.LogError(exception, "SiliconFlow validation failed");
            var failure = _context.GetTranslation("ValidationFailure");
            ValidateResult = $"{(failure == "ValidationFailure" ? "验证失败" : failure)}：{SiliconFlowClient.Redact(exception.Message, _settings.ApiKey)}";
        }
    }

    private void Save() => _context.SaveSettingStorage<Settings>();

    public void Dispose() { }
}

public record ModelOption(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}
