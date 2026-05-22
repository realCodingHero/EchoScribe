using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using EchoScribe.ViewModels;
using Wpf.Ui.Controls;

namespace EchoScribe.Views;

public partial class MainWindow : FluentWindow
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel = DataContext as MainViewModel;
        if (_viewModel != null)
        {
            // 自动滚动到最新转录
            _viewModel.TranscriptionEntries.CollectionChanged += OnTranscriptionChanged;

            // 初始化
            await _viewModel.InitializeAsync();
        }
    }

    private void OnTranscriptionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && TranscriptionList.Items.Count > 0)
        {
            // 滚动到底部
            TranscriptionList.ScrollIntoView(TranscriptionList.Items[^1]);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel?.Dispose();
    }
}
