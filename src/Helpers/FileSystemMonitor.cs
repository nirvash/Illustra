using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Illustra.Helpers
{
    public interface IFileSystemChangeHandler
    {
        void OnFileCreated(string path);
        void OnFileDeleted(string path);
        Task OnChildFolderRenamed(string oldPath, string newPath); // 子要素がリネームされた場合に呼び出される
    }

    public class FileSystemMonitor : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly IFileSystemChangeHandler _handler;
        private readonly ConcurrentQueue<FileSystemEventArgs> _eventQueue;
        private readonly Timer _processTimer;
        private bool _isMonitoring;
        private string _monitoredPath = string.Empty;

        public FileSystemMonitor(IFileSystemChangeHandler handler, bool isDirectoryMonitoring = false)
        {
            _handler = handler;
            _eventQueue = new ConcurrentQueue<FileSystemEventArgs>();
            _processTimer = new Timer(500) { AutoReset = false };
            _processTimer.Elapsed += ProcessQueuedEvents;

            _watcher = isDirectoryMonitoring ? new FileSystemWatcher
            {
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = false
            } : new FileSystemWatcher
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = false
            };

            _watcher.Created += (s, e) => QueueEvent(e);
            _watcher.Deleted += (s, e) => QueueEvent(e);
            _watcher.Renamed += (s, e) => QueueEvent(e);
        }

        public bool IsMonitoring => _isMonitoring;

        public void StartMonitoring(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    throw new DirectoryNotFoundException($"Directory not found: {path}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return;
            }
            StopMonitoring();

            _watcher.Path = path;
            try
            {
                _watcher.EnableRaisingEvents = true;
                _isMonitoring = true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.ComponentModel.Win32Exception)
            {
                // Win32Exception は、ネットワークドライブなど特定の状況で発生する可能性がある
                Debug.WriteLine($"Error starting file system monitoring for path '{path}': {ex.Message}");
                StopMonitoring(); // 監視を開始できなかった場合は停止状態に戻す
                // 必要に応じて、ユーザーへの通知やエラーハンドリングを追加
            }
        }

        public void StopMonitoring()
        {
            _watcher.EnableRaisingEvents = false;
            _eventQueue.Clear();
            _processTimer.Stop();
            _isMonitoring = false;
        }

        private void QueueEvent(FileSystemEventArgs e)
        {
            _eventQueue.Enqueue(e);
            _processTimer.Stop();
            _processTimer.Start();
        }

        private async void ProcessQueuedEvents(object? sender, ElapsedEventArgs e)
        {
            while (_eventQueue.TryDequeue(out var evt))
            {
                switch (evt)
                {
                    case FileSystemEventArgs createEvent when evt.ChangeType == WatcherChangeTypes.Created:
                        _handler.OnFileCreated(createEvent.FullPath);
                        break;

                    case FileSystemEventArgs deleteEvent when evt.ChangeType == WatcherChangeTypes.Deleted:
                        _handler.OnFileDeleted(deleteEvent.FullPath);
                        break;

                    case RenamedEventArgs renameEvent:
                        await _handler.OnChildFolderRenamed(renameEvent.OldFullPath, renameEvent.FullPath);
                        break;
                }
            }
        }

        public void Dispose()
        {
            StopMonitoring();
            _watcher.Dispose();
            _processTimer.Dispose();
        }
    }
}
