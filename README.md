# EchoScribe 🎙️

**实时语音转文字桌面工具** — 捕获 Windows 应用程序音频并使用 AI 实时转录为中文文字。

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-blue)]()

## ✨ 功能特性

- 🎯 **单进程音频捕获** — 选择特定应用程序（如 Zoom、Chrome、播放器），仅捕获该程序的音频
- 🤖 **AI 实时转录** — 基于 SenseVoice 模型，中文识别精度极高
- 🚀 **GPU 加速** — 支持 NVIDIA CUDA GPU 加速推理
- 🎤 **麦克风支持** — 可选开启麦克风输入，转录过程中随时切换
- ⏱️ **时间戳** — 每条转录都带有精确时间标记
- 📥 **导出功能** — 支持导出为 `.srt` 字幕文件和 `.txt` 文本文件
- 🎨 **现代界面** — Windows 11 Fluent Design 深色主题

## 📋 系统要求

- **操作系统**: Windows 10 (Build 20348+) 或 Windows 11
- **运行时**: .NET 10 Runtime
- **GPU** (推荐): NVIDIA GPU + CUDA 12.8+（如 RTX 5070Ti）
- **内存**: 4GB+ RAM

## 🚀 快速开始

### 1. 安装 .NET 10

从 [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0) 下载安装 .NET 10 SDK。

### 2. 下载模型文件

从 [sherpa-onnx releases](https://github.com/k2-fsa/sherpa-onnx/releases) 下载以下模型：

```bash
# 搜索: sherpa-onnx-sense-voice-zh-en-ja-ko-yue
# 解压到项目根目录的 models/ 文件夹

models/
└── sherpa-onnx-sense-voice-zh-en-ja-ko-yue/
    ├── model.onnx        # ASR 模型 (必需)
    ├── tokens.txt         # 词表 (必需)
    └── silero_vad.onnx    # VAD 模型 (推荐)
```

### 3. 构建和运行

```bash
dotnet build
dotnet run --project src/EchoScribe/EchoScribe.csproj
```

## 🏗️ 技术架构

| 组件 | 技术 |
| :--- | :--- |
| **框架** | .NET 10 LTS |
| **GUI** | WPF + [WPF-UI](https://github.com/lepoco/wpfui) (Fluent Design) |
| **架构模式** | MVVM ([CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)) |
| **ASR 引擎** | [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) + SenseVoiceSmall |
| **VAD** | Silero VAD (via sherpa-onnx) |
| **音频捕获** | WASAPI Process Loopback API |
| **音频处理** | [NAudio](https://github.com/naudio/NAudio) |

## 📁 项目结构

```
src/EchoScribe/
├── Audio/                    # 音频捕获模块
│   ├── IAudioCapturer.cs     # 统一捕获接口
│   ├── ProcessLoopbackCapture.cs  # 单进程捕获
│   ├── SystemLoopbackCapture.cs   # 系统 loopback 备选
│   ├── MicrophoneCapture.cs  # 麦克风输入
│   ├── AudioResampler.cs     # 16kHz 重采样
│   ├── AudioProcessScanner.cs # 进程扫描
│   └── NativeInterop/        # COM 互操作
├── Asr/                      # ASR 语音识别模块
│   ├── SherpaOnnxEngine.cs   # ASR 引擎封装
│   ├── VadEngine.cs          # VAD 检测
│   ├── TranscriptionPipeline.cs  # 转录流水线
│   └── TextPostProcessor.cs  # 文字后处理
├── Export/                   # 导出模块
│   └── TranscriptionExporter.cs  # .txt / .srt 导出
├── Models/                   # 模型管理
│   └── ModelManager.cs
├── ViewModels/               # MVVM ViewModel
│   └── MainViewModel.cs
└── Views/                    # WPF 视图
    ├── MainWindow.xaml
    └── Controls/
        └── AudioLevelMeter.xaml
```

## 📄 许可证

MIT License
