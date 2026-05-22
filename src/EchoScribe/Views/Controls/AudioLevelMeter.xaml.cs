using System.Windows;
using System.Windows.Controls;

namespace EchoScribe.Views.Controls;

/// <summary>
/// 音频电平指示器控件
/// 显示绿→黄→红渐变的音量条
/// </summary>
public partial class AudioLevelMeter : UserControl
{
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(
            nameof(Level),
            typeof(double),
            typeof(AudioLevelMeter),
            new PropertyMetadata(0.0, OnLevelChanged));

    /// <summary>
    /// 音频电平值（0.0 - 1.0）
    /// </summary>
    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public AudioLevelMeter()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateBar();
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AudioLevelMeter meter)
        {
            meter.UpdateBar();
        }
    }

    private void UpdateBar()
    {
        var level = Math.Clamp(Level, 0.0, 1.0);
        var totalWidth = BackgroundBorder.ActualWidth;

        if (totalWidth > 0)
        {
            LevelBar.Width = totalWidth * level;
        }
    }
}
