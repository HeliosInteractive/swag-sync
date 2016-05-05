namespace swag
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Diagnostics;
    using System.Collections.Generic;

    /// <summary>
    /// This class exists because FileSystemWatcher is BROKEN
    /// on Linux and cannot watch sub-directories.
    /// </summary>
    public class RecursiveFileWatcher : IDisposable
    {
        private bool                        m_Disposed = false;
        private Action<string>              m_Handler;
        private FileSystemWatcher           m_Watcher;
        private List<RecursiveFileWatcher>  m_Watchers;

        public RecursiveFileWatcher(string base_path, Action<string> handler)
        {
            m_Watchers = new List<RecursiveFileWatcher>();
            m_Handler = handler;

            m_Watcher = new FileSystemWatcher();
            m_Watcher.Path = base_path;

            m_Watcher.NotifyFilter =
                NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite |
                NotifyFilters.FileName |
                NotifyFilters.Size;

            try
            {
                m_Watcher.EnableRaisingEvents = true;
            }
            catch (FileNotFoundException ex)
            {
                Trace.TraceInformation("Watcher spawned too soon. Waiting for 750 ms: {0}", ex.Message);

                Thread.Sleep(FileCreationLatency);
                m_Watcher.EnableRaisingEvents = true;
            }
            catch
            {
                Trace.TraceInformation("Watcher failed.");
                return;
            }

            m_Watcher.Changed += OnChanged;
            m_Watcher.Renamed += OnRenamed;
            m_Watcher.Deleted += OnDeleted;
            m_Watcher.Created += OnCreated;

            Trace.TraceInformation("Watcher is watching directory: {0}", base_path);

            foreach (var subdir in Directory.EnumerateDirectories(base_path))
            {
                m_Watchers.Add(new RecursiveFileWatcher(subdir, m_Handler));
            }
        }

        private static bool IsValid(RecursiveFileWatcher watcher)
        {
            return watcher != null && watcher.m_Watcher != null;
        }

        private static int FileCreationLatency
        {
            get { return 750; /* 750 ms as suggested by Mono */ }
        }

        private void OnDeleted(object source, FileSystemEventArgs args)
        {
            try
            {
                lock (this)
                {
                    var found = m_Watchers.FindAll(w => { return IsValid(w) && w.m_Watcher.Path == args.FullPath; });
                    found.ForEach(w =>
                    {
                        Trace.TraceInformation("Watcher removed directory: {0}", args.FullPath);
                        m_Watchers.Remove(w);
                        w.Dispose();
                    });
                }
            }
            catch(Exception ex)
            {
                Trace.TraceError("Watcher encountered and error: {0}", ex.Message);
            }
        }

        private void OnCreated(object source, FileSystemEventArgs args)
        {
            try
            {
                lock (this)
                {
                    if (Directory.Exists(args.FullPath))
                    {
                        if (m_Watchers.Find(w => { return IsValid(w) && w.m_Watcher.Path == args.FullPath; }) == null)
                        {
                            m_Watchers.Add(new RecursiveFileWatcher(args.FullPath, m_Handler));
                            Trace.TraceInformation("Watcher added directory: {0}", args.FullPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError("Watcher encountered and error: {0}", ex.Message);
            }
        }

        private void OnChanged(object source, FileSystemEventArgs args)
        {
            try
            {
                lock (this)
                {
                    if (Directory.Exists(args.FullPath))
                    {
                        if (m_Watchers.Find(w => { return IsValid(w) && w.m_Watcher.Path == args.FullPath; }) == null)
                        {
                            m_Watchers.Add(new RecursiveFileWatcher(args.FullPath, m_Handler));
                            Trace.TraceInformation("Watcher added directory: {0}", args.FullPath);
                        }
                    }
                    else
                    {
                        if (m_Handler != null)
                            m_Handler(args.FullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError("Watcher encountered and error: {0}", ex.Message);
            }
        }

        private void OnRenamed(object source, RenamedEventArgs args)
        {
            try
            {
                lock (this)
                {
                    if (Directory.Exists(args.FullPath))
                    {
                        m_Watchers.Add(new RecursiveFileWatcher(args.FullPath, m_Handler));
                        Trace.TraceInformation("Watcher added directory: {0}", args.FullPath);
                    }
                    else
                    {
                        if (m_Handler != null)
                            m_Handler(args.FullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError("Watcher encountered and error: {0}", ex.Message);
            }
        }

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            lock (this)
            {
                if (!m_Disposed)
                {
                    if (disposing)
                    {
                        if (m_Watchers != null)
                            m_Watchers.ForEach(w => { if (IsValid(w)) w.Dispose(); });

                        if (m_Watcher != null)
                            m_Watcher.Dispose();
                    }

                    m_Watchers = null;
                    m_Watcher = null;
                    m_Disposed = true;
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
