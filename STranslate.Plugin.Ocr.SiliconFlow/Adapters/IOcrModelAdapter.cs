namespace STranslate.Plugin.Ocr.SiliconFlow.Adapters;

/// <summary>
/// 模型适配器：封装单个模型的提示词协议与输出解析。
/// HTTP 不在此层（统一走 SiliconFlowClient）。
/// </summary>
public interface IOcrModelAdapter
{
    /// <summary>硅基流动模型完整 ID</summary>
    string ModelId { get; }

    /// <summary>设置页显示的友好名</summary>
    string DisplayName { get; }

    /// <summary>
    /// 构建发给模型的 text 段。
    /// 模型协议固定部分（官方模板原文、特殊 token）必须硬编码在此，UI 不可更改。
    /// </summary>
    string BuildPromptText(Settings settings);

    /// <summary>解析模型输出为 OcrResult</summary>
    OcrResult ParseResponse(string content, OcrRequest request);

    /// <summary>当前配置下是否提供文字坐标（BoxPoints）</summary>
    bool SupportsCoordinates(Settings settings);
}

/// <summary>按 Settings.Model 解析适配器；未知模型回退 PaddleOCR-VL</summary>
public static class OcrModelRegistry
{
    public static readonly IReadOnlyList<IOcrModelAdapter> Adapters =
    [
        new PaddleOcrVlAdapter(),
        new DeepSeekOcrAdapter(),
        new QwenAdapter()
    ];

    public static IOcrModelAdapter Resolve(Settings settings)
    {
        var model = settings.Model.Trim();
        return Adapters.FirstOrDefault(a => string.Equals(a.ModelId, model, StringComparison.OrdinalIgnoreCase))
            ?? Adapters[0];
    }
}
