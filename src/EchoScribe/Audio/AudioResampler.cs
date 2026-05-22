using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EchoScribe.Audio;

/// <summary>
/// 音频重采样器：将任意格式音频转换为 16kHz 单通道 float32 PCM
/// </summary>
public class AudioResampler
{
    private readonly WaveFormat _sourceFormat;
    private readonly int _targetSampleRate;

    public AudioResampler(WaveFormat sourceFormat, int targetSampleRate = 16000)
    {
        _sourceFormat = sourceFormat;
        _targetSampleRate = targetSampleRate;
    }

    /// <summary>
    /// 将原始音频字节重采样为 16kHz 单通道 float32
    /// </summary>
    public float[] Resample(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded == 0) return [];

        // 1. 包装为 WaveBuffer
        using var sourceStream = new RawSourceWaveStream(
            new MemoryStream(buffer, 0, bytesRecorded),
            _sourceFormat);

        ISampleProvider sampleProvider = sourceStream.ToSampleProvider();

        // 2. 如果是多通道，转为单通道
        if (_sourceFormat.Channels > 1)
        {
            sampleProvider = sampleProvider.ToMono();
        }

        // 3. 如果采样率不同，重采样
        if (_sourceFormat.SampleRate != _targetSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, _targetSampleRate);
        }

        // 4. 读取所有重采样后的样本
        // 估算输出大小
        int estimatedSamples = (int)((long)bytesRecorded * _targetSampleRate /
            (_sourceFormat.SampleRate * _sourceFormat.BlockAlign)) + 1024;

        var outputBuffer = new float[estimatedSamples];
        int samplesRead = sampleProvider.Read(outputBuffer, 0, outputBuffer.Length);

        if (samplesRead <= 0) return [];

        // 裁剪到实际大小
        if (samplesRead < outputBuffer.Length)
        {
            var result = new float[samplesRead];
            Array.Copy(outputBuffer, result, samplesRead);
            return result;
        }

        return outputBuffer;
    }
}
