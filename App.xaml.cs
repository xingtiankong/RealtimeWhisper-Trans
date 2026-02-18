using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AudioTranscriber
{
    public partial class App : System.Windows.Application
    {
        private string _logPath;

        public App()
        {
            // 设置日志路径
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioTranscriber");
            
            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);
            
            _logPath = Path.Combine(appDataPath, "error.log");

            // 全局异常处理
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            LogException(exception, "UnhandledException");
            
            System.Windows.MessageBox.Show(
                $"发生严重错误，程序即将关闭。\n\n错误信息：{exception?.Message}\n\n日志已保存到：{_logPath}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException(e.Exception, "DispatcherUnhandledException");
            
            System.Windows.MessageBox.Show(
                $"发生错误：{e.Exception.Message}\n\n堆栈跟踪：{e.Exception.StackTrace}",
                "错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            
            e.Handled = true;
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        }

        private void LogException(Exception? ex, string type)
        {
            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{type}]\n";
                logEntry += $"Message: {ex?.Message}\n";
                logEntry += $"StackTrace: {ex?.StackTrace}\n";
                logEntry += $"InnerException: {ex?.InnerException?.Message}\n";
                logEntry += new string('=', 50) + "\n\n";
                
                File.AppendAllText(_logPath, logEntry);
            }
            catch { }
        }

        public static void LogInfo(string message)
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AudioTranscriber");
                var logPath = Path.Combine(appDataPath, "app.log");
                
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] {message}\n";
                File.AppendAllText(logPath, logEntry);
            }
            catch { }
        }
    }
}
