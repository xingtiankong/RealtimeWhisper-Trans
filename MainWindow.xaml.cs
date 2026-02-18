using AudioTranscriber.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Collections.Specialized;

namespace AudioTranscriber
{
    /// <summary>
    /// 悬浮字幕主窗口
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _isMaximized = false;
        private Rect _normalBounds;
        private SettingsWindow? _settingsWindow;

        public MainWindow()
        {
            InitializeComponent();

            // 确保ViewModel被正确初始化
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += ViewModel_PropertyChanged;

                // 监听字幕集合变化，自动滚动到最新
                vm.TranscriptSegments.CollectionChanged += TranscriptSegments_CollectionChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 当设置改变时更新UI
            if (e.PropertyName == nameof(MainViewModel.BackgroundBrush))
            {
                SubtitleContainer.Background = (DataContext as MainViewModel)?.BackgroundBrush;
            }
        }

        /// <summary>
        /// 字幕集合变化时自动滚动到最新
        /// </summary>
        private void TranscriptSegments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // 使用Dispatcher延迟执行，等待UI更新完成
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SubtitleScrollViewer?.ScrollToEnd();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// 窗口拖动
        /// </summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                // 单击拖动窗口
                DragMove();
            }
        }

        /// <summary>
        /// 双击最大化/还原
        /// </summary>
        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_isMaximized)
            {
                // 还原
                Left = _normalBounds.Left;
                Top = _normalBounds.Top;
                Width = _normalBounds.Width;
                Height = _normalBounds.Height;
                _isMaximized = false;
            }
            else
            {
                // 最大化（保存原位置）
                _normalBounds = new Rect(Left, Top, Width, Height);
                Left = 0;
                Top = 0;
                Width = SystemParameters.WorkArea.Width;
                Height = 200;
                _isMaximized = true;
            }
        }

        /// <summary>
        /// 打开设置窗口
        /// </summary>
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow == null || !_settingsWindow.IsVisible)
            {
                _settingsWindow = new SettingsWindow
                {
                    Owner = this,
                    DataContext = this.DataContext // 共享同一个ViewModel
                };
                _settingsWindow.Show();
            }
            else
            {
                _settingsWindow.Activate();
            }
        }

        /// <summary>
        /// 关闭窗口
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 关闭设置窗口（如果打开）
            _settingsWindow?.Close();
            Close();
        }
    }
}
