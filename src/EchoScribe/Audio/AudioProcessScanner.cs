using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace EchoScribe.Audio;

/// <summary>
/// 音频进程信息
/// </summary>
public class AudioProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public string DisplayName => string.IsNullOrEmpty(WindowTitle)
        ? $"{ProcessName} (PID: {ProcessId})"
        : $"{WindowTitle} — {ProcessName}";

    public override string ToString() => DisplayName;
}

/// <summary>
/// 扫描当前正在输出音频的进程
/// </summary>
public class AudioProcessScanner
{
    /// <summary>
    /// 获取所有当前有音频会话的进程
    /// </summary>
    public List<AudioProcessInfo> GetAudioProcesses()
    {
        var result = new List<AudioProcessInfo>();
        var seenPids = new HashSet<int>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessionManager = device.AudioSessionManager;
            var sessions = sessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];

                try
                {
                    var pid = (int)session.GetProcessID;

                    // 跳过系统进程和重复进程
                    if (pid == 0 || !seenPids.Add(pid)) continue;

                    var process = Process.GetProcessById(pid);

                    result.Add(new AudioProcessInfo
                    {
                        ProcessId = pid,
                        ProcessName = process.ProcessName,
                        WindowTitle = process.MainWindowTitle
                    });
                }
                catch (ArgumentException)
                {
                    // 进程可能已退出
                }
                catch (InvalidOperationException)
                {
                    // 进程信息不可用
                }
            }
        }
        catch (Exception)
        {
            // 音频设备不可用
        }

        return result.OrderBy(p => p.ProcessName).ToList();
    }
}
