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
        void OnFileRenamed(string oldPath, string newPath);
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
            _watcher.EnableRaisingEvents = true;
            _isMonitoring = true;
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

        private void ProcessQueuedEvents(object? sender, ElapsedEventArgs e)
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
                        _handler.OnFileRenamed(renameEvent.OldFullPath, renameEvent.FullPath);
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
