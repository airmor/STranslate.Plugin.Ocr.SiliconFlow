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
    /// 内置 64×64 PNG 测试图（A 形图案，程序化生成确保校验和合法）。
    /// 尺寸红线：DeepSeek-OCR 要求宽高均 &gt;28px（实测 8×8 报 code 20015）。
    /// </summary>
    private static readonly byte[] TestImage = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAIAAAAlC+aJAAAA3ElEQVR4nO2WQQ7DIAwE8/9PU6mqenAqsDegBXX2SJi1J7nkaofnaocHAXcQcAcBdxBwB4HjBK7FQQABBBBAYGOB1dsLDnO+QLWkU1jGtxJQ8H0ERNwr8LzEKTCn5OHU6UEAAQQcAvenHTB5nt2nDIxeW/5w+Ci1TxlIfPdw3l9xR4Gf14aF9eXf+CKB/G/CvgLfy8nC6iYffKlAqVDE5XkIxEIRl+chEAtFXJ6HQCwUcXkeArFQxOV5CMRCEZfnIRALRVyeh0AsFPG/E9gtCLiDgDsIuIOAOwg0c16/r/8XAXtWJQAAAABJRU5ErkJggg==");

    /// <summary>三模型选项（显示名 → 完整 ID）+ 自定义项</summary>
    public ObservableCollection<ModelOption> Models { get; } =
    [
        new(OcrModelRegistry.Adapters[0].ModelId, OcrModelRegistry.Adapters[0].DisplayName + "（文档解析 SOTA·带坐标）"),
        new(OcrModelRegistry.Adapters[1].ModelId, OcrModelRegistry.Adapters[1].DisplayName + "（grounding 文档转 MD·带坐标）"),
        new(OcrModelRegistry.Adapters[2].ModelId, OcrModelRegistry.Adapters[2].DisplayName + "（自由提示词）"),
        new(OcrModelRegistry.CustomModelId, "自定义模型（任意多模态 LLM）")
    ];

    public bool IsCustomSelected => SelectedModel.Id == OcrModelRegistry.CustomModelId;

    /// <summary>自定义多模态模型 ID（任意视觉 LLM，配合自由提示词使用）</summary>
    public string CustomModel
    {
        get => _customModel;
        set
        {
            if (SetProperty(ref _customModel, value))
            {
                _settings.CustomModel = value;
                Save();
            }
        }
    }
    private string _customModel = string.Empty;

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
        CustomModel = _settings.CustomModel;
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
                OnPropertyChanged(nameof(IsCustomSelected));
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
            var request = new OcrRequest(TestImage, LangEnum.Auto, 64, 64);
            var result = await SiliconFlowClient.RecognizeAsync(_context, _settings, adapter, request, CancellationToken.None);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.ErrorMessage);

            var modelLabel = adapter is QwenAdapter q && _settings.Model.Trim() is OcrModelRegistry.CustomModelId
                ? $"自定义 · {_settings.CustomModel.Trim()}"
                : adapter.DisplayName;
            ValidateResult = $"连接成功 · {modelLabel}";
        }
        catch (Exception exception)
        {
            _context.Logger.LogError(exception, "SiliconFlow validation failed");
            var failure = _context.GetTranslation("ValidationFailure");
            ValidateResult = $"{(failure == "ValidationFailure" ? "验证失败" : failure)}：{SiliconFlowClient.Redact(exception.Message, _settings.ApiKey)}";
        }
    }

    /// <summary>
    /// 重新打开设置页时刷新显示值（兜底：防 PasswordBox 关窗清空曾把空串写回）
    /// </summary>
    public void RefreshDisplay()
    {
        ApiKey = _settings.ApiKey;
        OnPropertyChanged(nameof(ApiKey));
    }

    private void Save() => _context.SaveSettingStorage<Settings>();

    public void Dispose() { }
}

public record ModelOption(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}
