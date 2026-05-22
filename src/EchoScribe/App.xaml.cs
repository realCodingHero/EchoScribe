using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace EchoScribe;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 应用深色主题 + Mica 效果
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica);
    }
}
