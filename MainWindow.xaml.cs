using System;
using System.Windows;
using System.Windows.Input;
using AudioTranscriber.ViewModels;

namespace AudioTranscriber
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;

        public MainWindow()
        {
            try
            {
                App.LogInfo("初始化 MainWindow...");
                InitializeComponent();
                
                _viewModel = DataContext as MainViewModel;
                
                // 注册窗口关闭事件
                Closing += OnWindowClosing;
                
                // 键盘快捷键
                KeyDown += OnKeyDown;
                
                App.LogInfo("MainWindow 初始化完成");
            }
            catch (Exception ex)
            {
                App.LogInfo($"MainWindow 初始化失败: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"窗口初始化失败:\n{ex.Message}\n\n{ex.StackTrace}",
                    "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // 清理资源
                _viewModel?.Dispose();
            }
            catch (Exception ex)
            {
                App.LogInfo($"窗口关闭时出错: {ex.Message}");
            }
        }

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                // 空格键控制录音
                if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
                {
                    if (_viewModel?.CurrentState == Models.RecordingState.Recording)
                    {
                        _viewModel.StopRecordingCommand.Execute(null);
                    }
                    else
                    {
                        _viewModel?.StartRecordingCommand.Execute(null);
                    }
                    e.Handled = true;
                }
                
                // Ctrl+S 保存
                if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    _viewModel?.SaveTranscriptCommand.Execute(null);
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                App.LogInfo($"键盘快捷键处理失败: {ex.Message}");
            }
        }
    }
}
