# STranslate.Plugin.Ocr.SiliconFlow 设计文档

日期：2026-09-01
状态：已与用户逐段确认

## 1. 背景与目标

为 STranslate（WPF 翻译/OCR 工具）开发一个社区 OCR 插件，调用硅基流动（SiliconFlow）的免费视觉模型完成文字识别。

**目标**：

- 一个插件支持三个免费模型：
  - `PaddlePaddle/PaddleOCR-VL-1.5`（文档解析 SOTA，原生输出坐标）
  - `deepseek-ai/DeepSeek-OCR`（grounding 文档转 Markdown）
  - `Qwen/Qwen3.5-4B`（原生多模态，自由提示词）
- 支持混合排版：多语言、文本与 LaTeX 公式混杂
- LaTeX 公式以模型原生输出透传（`$...$`），由宿主 Markdown 渲染展示
- 深度适配每个模型的提示词协议与输出格式，不做暴力提示词

**非目标**：

- 不做公式翻译保护（公式送翻译引擎后的处理是宿主职责，超出插件权限）
- 不做本地推理（用户本地已装 Ollama 系插件，本插件专注云端硅基流动）
- 不支持任意自定义模型 ID 输入（避免误配非视觉模型）

## 2. 关键调研结论

### 2.1 硅基流动 API

- 端点：`POST https://api.siliconflow.cn/v1/chat/completions`（OpenAI 兼容）
- 认证：`Authorization: Bearer {ApiKey}`，Key 由设置页输入
- 图像输入：`content` 数组混排 `image_url`（base64 data URI）与 `text`
- 三个模型均免费（输入/输出均 ￥0/M Tokens）
- 错误码：401 Invalid token、429 TPM 限流、503 过载

### 2.2 模型协议差异（全部走同一 chat 接口，差异在提示词与输出）

| 模型 | 提示词 | 输出 | 坐标 |
|---|---|---|---|
| PaddleOCR-VL-1.5 | `OCR:` / `Spotting:` / `Table Recognition:` / `Formula Recognition:` | Markdown / 逐行文字+LOC token | ✅ 0~999 千分比，8 token 组四角框 |
| DeepSeek-OCR | `<image>\n<|grounding|>Convert the document to markdown.` 等六个官方模板 | Markdown | ❌ |
| Qwen3.5-4B | 自由提示词 | Markdown | ❌ |

**PaddleOCR-VL Spotting 输出格式**：

```
文字内容<|LOC_x1|><|LOC_y1|><|LOC_x2|><|LOC_y2|><|LOC_x3|><|LOC_y3|><|LOC_x4|><|LOC_y4|>
```

8 个 LOC 值 = 左上、右上、右下、左下四角，值域 0~999；反归一化：`px = LOC值 × PixelWidth / 1000`。

**DeepSeek-OCR 注意**：官方提示词中的 `<image>\n` 前缀是本地推理约定；硅基流动 chat 接口中图像已由 `image_url` 传入，适配器必须剥离 `<image>\n` 占位符只发正文。

### 2.3 STranslate 插件机制

- NuGet 包 `STranslate.Plugin` 1.0.14，`net10.0-windows` + WPF
- OCR 插件实现 `IOcrPlugin`；`SupportBoxPoints() => true` 才会进入图片翻译 OCR 下拉
- 纯文本结果由宿主本地 Smart 分段智能推断坐标（图片翻译仍可用，只是选区精度依赖宿主推断）
- 配置：`context.LoadSettingStorage<T>()` / `SaveSettingStorage<T>()`
- 日志：`Context.Logger`；HTTP：`Context.HttpService.PostAsync(url, body, options, ct)`
- 打包：csproj `EnableAutoPackage=true`，Release 编译产出 `.spkg`（zip 结构，根目录直接含 plugin.json），拖入插件页安装
- `plugin.json`：全新唯一 PluginID，`Version` 用 `System.Version` 可解析格式

## 3. 架构

### 3.1 目录结构

```
STranslate.Plugin.Ocr.SiliconFlow/          # 仓库根（已建 git）
├── docs/superpowers/specs/                   # 本设计文档
├── LICENSE
├── .gitignore
├── STranslate.Plugin.Ocr.SiliconFlow/        # 插件项目
│   ├── Main.cs                    # 入口：IOcrPlugin + ILlm
│   ├── Settings.cs                # 配置模型
│   ├── SiliconFlowClient.cs       # OpenAI 兼容 HTTP 调用共用层
│   ├── Adapters/
│   │   ├── IOcrModelAdapter.cs   # 适配器接口
│   │   ├── PaddleOcrVlAdapter.cs  # PaddleOCR-VL-1.5
│   │   ├── DeepSeekOcrAdapter.cs  # DeepSeek-OCR
│   │   └── QwenAdapter.cs        # Qwen3.5-4B
│   ├── View/SettingsView.xaml(.cs)
│   ├── ViewModel/SettingsViewModel.cs
│   ├── Languages/                 # zh-cn / en 语言包
│   ├── plugin.json
│   ├── icon.png
│   └── STranslate.Plugin.Ocr.SiliconFlow.csproj
└── tests/
    ├── STranslate.Plugin.Ocr.SiliconFlow.Tests/   # xunit 适配器单元测试
    └── ConsoleTest/                                # 真实 API 冒烟（手动跑）
```

### 3.2 组件职责

**IOcrModelAdapter**（模型适配器接口）：

```csharp
string BuildPromptText(Settings settings);          // 返回发给模型的 text 段（固定模板/自由提示词）
OcrResult ParseResponse(string content, OcrRequest request);  // 模型输出 → OcrResult
bool SupportsCoordinates(Settings settings);         // 是否提供 BoxPoints
```

- `PaddleOcrVlAdapter`：按 `PaddleMode` 选模板；Spotting 模式解析 LOC token 为 Regions 结构；其余模式 Markdown 按行拆 `OcrContents`
- `DeepSeekOcrAdapter`：按 `DeepSeekTemplate` 选六个官方模板之一（发送前剥离 `<image>\n`）；输出按 Markdown 透传
- `QwenAdapter`：`QwenPrompt` 自由提示词；输出当纯文本
- `Main.SupportBoxPoints()` 委托当前 Adapter 的 `SupportsCoordinates`（实际只有 Paddle+Spotting 为 true）

**SiliconFlowClient**：组装请求体（model/messages/temperature/max_tokens）、`Options`（Bearer 头 + 超时）、调 `Context.HttpService.PostAsync`、解析 `choices[0].message.content`。三个模型共用，无模型特定逻辑。

**Main.cs**：`Init()` 加载配置并实例化 Adapter；`RecognizeAsync()` 编排：选 Adapter → Build → Post → Parse，不含模型特定逻辑。新增模型只需加一个 Adapter。

### 3.3 UI 原则：协议硬编码

模型协议的固定部分**硬编码在 Adapter 内，UI 不可见不可改**：

- PaddleOCR-VL 四个模板原文、DeepSeek 六个官方模板原文、`<image>`/`<|grounding|>` 处理——不提供编辑入口
- UI 只允许在官方约定的枚举间切换（下拉选择模式/模板），选中项以只读形式展示将发送的模板
- 唯一例外：Qwen3.5-4B 自由提示词可编辑（它本无官方 OCR 协议）

## 4. 配置模型（Settings）

| 字段 | 类型 | 默认值 | UI |
|---|---|---|---|
| ApiKey | string | 空 | 密码框 + 测试连接按钮 |
| Model | string | `PaddlePaddle/PaddleOCR-VL-1.5` | 下拉（三选一，友好名+完整ID） |
| PaddleMode | enum | Document | Document/Spotting/Table/Formula 下拉 |
| DeepSeekTemplate | enum | Markdown | 六官方模板下拉 |
| QwenPrompt | string | 预置默认 | 多行可编辑 |
| Temperature | double | 0.0 | 数值 |
| MaxTokens | int | 4096 | 数值 |
| TimeoutSeconds | int | 30 | 数值 |

按模型显示对应区域（Visibility 绑定），未选中模型的区域不占空间。

**测试连接**：发送内置 1×1 像素 PNG 给当前模型；成功显示"连接成功 · 模型名"；失败显示脱敏错误（ApiKey 绝不出现在错误信息，参考 Bailian 插件 Redact 模式）。

## 5. 数据流

```
用户截图/贴图
→ 宿主调用 RecognizeAsync(OcrRequest{ImageData, PixelWidth, PixelHeight})
→ Main 按 Settings.Model 取 Adapter
→ Adapter.BuildPromptText()（固定模板，UI 枚举选择）
→ SiliconFlowClient: POST api.siliconflow.cn/v1/chat/completions
   body: {model, messages:[{role:"user", content:[image_url(base64), text]}],
          temperature, max_tokens}
   headers: Authorization Bearer；CancellationToken 全程穿透
→ choices[0].message.content
→ Adapter.ParseResponse():
   · Spotting: 逐行正则 <\|LOC_(\d+)\|> 提取 8 值 → 四角框 → ×PixelWidth/1000 反归一化
     → OcrRegion→OcrParagraph→逐行 OcrContent(BoxPoints)
   · 其它: Markdown 按行拆 OcrContents（LaTeX 原样保留在文本中）
→ OcrResult 返回宿主 → Markdown 渲染（公式直接展示）/ 送翻译引擎
```

Spotting 模式若 `PixelWidth/Height ≤ 0`（旧宿主），坐标按原始 LOC 值返回不反归一化（与现有 PaddleOCRVL 插件一致的兜底）。

## 6. 错误处理（三层）

| 层 | 处理 | 用户所见 |
|---|---|---|
| HTTP/网络 | 捕获后 `result.Fail(msg)`，ApiKey 脱敏 | "请求失败：timeout after 30s" 等 |
| API 业务错 | 解析 error.message 透传 | 401 Invalid token / 429 限流 / 503 过载 |
| 模型输出异常 | Spotting 无 LOC 行 → Fail("未检测到文字")；空 content → Fail | 明确提示而非空结果 |

`OperationCanceledException` 且 ct 已取消时原样上抛（尊重 ESC 取消），其余异常一律 Fail 不抛。

## 7. 测试策略

1. **适配器单元测试**（xunit，脱离宿主）：
   - LOC 解析：正常行/空行/<8 token 坏行 → Regions 与坐标换算断言
   - `<image>\n` 剥离、模板选择、Markdown 拆行、空输出
2. **真实 API 冒烟**（`tests/ConsoleTest`，手动）：本地图片 → 三模型各模式 → 打印结果核对
3. **手动验证清单**（spkg 拖入本机 STranslate）：
   - 截图 OCR：三模型识别中英混排+公式图，Markdown/LaTeX 渲染正常
   - 图片翻译：Paddle+Spotting 出现在图片翻译 OCR 下拉、区域高亮正确
   - ESC 取消、错误 Key 报错、测试连接按钮

## 8. 打包与发布

- csproj：NuGet `STranslate.Plugin` 1.0.14；`net10.0-windows`；`EnableAutoPackage=true`
- Release 编译 → `.artifacts/plugins/STranslate.Plugin.Ocr.SiliconFlow.spkg`
- 拖入 STranslate 插件页安装（走与市场相同的安装逻辑）
- 后续迭代：GitHub Actions 自动 release（参考 Fan-chou 仓库 release.yml），不在本期范围

## 9. 开发环境

- 仅 NuGet SDK 独立编译（用户选择，不 clone 主程序源码）
- 宿主：用户本机正式版 `C:\Users\airmor\AppData\Local\STranslate\current\STranslate.exe`
- 需要 .NET 10 SDK（Windows targeting 已启用）
