namespace swag
{
    using System;
    using System.IO;
    using Amazon.S3;
    using System.Linq;
    using Amazon.S3.Model;
    using System.Diagnostics;
    using Amazon.S3.Transfer;
    using System.Threading.Tasks;
    using System.Threading;
    using System.Collections.Generic;

    /// <summary>
    /// Encapsulates logic of uploading/sweeping of one single bucket
    /// </summary>
    public class Bucket : IDisposable
    {
        public Action<string> OnFileUploaded;
        public Action<string> OnFileFailed;

        private string              m_BaseDirectory = "";
        private string              m_BucketName    = "";
        private bool                m_Validated     = false;
        private int                 m_Timeout       = 5000;
        private int                 m_MaxCount      = 5;
        private FileSystemWatcher   m_Watcher       = null;
        private List<Task<Task>>    m_PendingTasks  = new List<Task<Task>>();
        private List<string>        m_PendingFiles  = new List<string>();

        public Bucket(string base_path, uint timeout)
        {
            if (string.IsNullOrWhiteSpace(base_path) ||
                !Path.IsPathRooted(base_path) ||
                !File.GetAttributes(base_path).HasFlag(FileAttributes.Directory) ||
                !Directory.Exists(base_path))
            {
                Trace.TraceError("Bucket path supplied is invalid: {0}", base_path);
                return;
            }

            m_BaseDirectory = base_path;
            m_Timeout = (int)timeout * 1000;
            m_BucketName = m_BaseDirectory.Split(Path.DirectorySeparatorChar).Last();
            m_Validated = true;

            Trace.TraceInformation("Bucket is up and watching with name {0} and path {1}",
                m_BucketName, m_BaseDirectory);
        }

        /// <summary>
        /// Bucket name
        /// </summary>
        public string BucketName
        {
            get { return m_BucketName; }
        }

        /// <summary>
        /// Starts the bucket file watcher
        /// </summary>
        public void SetupWatcher()
        {
            if (!m_Validated)
                return;

            if (m_Watcher != null)
                return;

            m_Watcher = new FileSystemWatcher();
            m_Watcher.Path = m_BaseDirectory;
            m_Watcher.NotifyFilter =
                NotifyFilters.LastWrite |
                NotifyFilters.FileName;

            m_Watcher.Changed += WatcherCallback;
            m_Watcher.Renamed += WatcherCallback;
            m_Watcher.EnableRaisingEvents = true;
            m_Watcher.IncludeSubdirectories = true;
        }

        protected void FileUploadedCallback(string file)
        {
            if (m_PendingFiles.Count > 0)
                m_PendingFiles.Remove(file);

            if (OnFileUploaded != null)
                OnFileUploaded(file);
        }

        protected void FileFailedCallback(string file)
        {
            if (m_PendingFiles.Count > 0)
                m_PendingFiles.Remove(file);

            if (OnFileFailed != null)
                OnFileFailed(file);
        }

        private void WatcherCallback(object source, FileSystemEventArgs ev)
        {
            if (!File.Exists(ev.FullPath))
            {
                Trace.TraceError("File does not exist: {0}", ev.FullPath);
                return;
            }

            if (File.GetAttributes(ev.FullPath).HasFlag(FileAttributes.Directory))
            {
                Trace.TraceInformation("Bucket {0} has received an event from watcher: File {1} with reason {2}. {3}",
                    m_BucketName, ev.FullPath, ev.ChangeType.ToString(),
                    "Ignoring this since it's a directory.");
                return;
            }

            if (!File.Exists(ev.FullPath))
            {
                Trace.TraceInformation("Bucket {0} has received an event from watcher: File {1} with reason {2}. {3}",
                    m_BucketName, ev.FullPath, ev.ChangeType.ToString(),
                    "Ignoring this since it does not exist.");
                return;
            }

            Trace.TraceInformation("Bucket {0} has received an event from watcher: File {1} with reason {2}",
                m_BucketName, ev.FullPath, ev.ChangeType.ToString());
            
            Upload(ev.FullPath);
        }

        /// <summary>
        /// Uploads a file to S3 bucket asynchronously.
        /// Use FinishPendingTasks to wait for everything to finish
        /// </summary>
        /// <param name="file"></param>
        public async void Upload(string file)
        {
            if (!m_Validated)
                return;

            if (m_PendingFiles.Contains(file))
            {
                Trace.TraceInformation("File is pending. Ignoring {0}", file);
                return;
            }

            if (m_PendingFiles.Count > m_MaxCount)
            {
                Trace.TraceInformation("Max count exceeded. Ignoring {0}", file);
                return;
            }

            try
            {
                m_PendingFiles.Add(file);
                Trace.TraceInformation("Upload began for {0}", file);

                using (TransferUtility file_transfer_utility =
                    new TransferUtility(
                        new AmazonS3Client(
                            Amazon.RegionEndpoint.USEast1)))
                {
                    TransferUtilityUploadRequest request =
                        new TransferUtilityUploadRequest
                        {
                            FilePath = file,
                            BucketName = m_BucketName,
                            Key = GetRelativePath(file, m_BaseDirectory)
                        };

                    CancellationTokenSource token = new CancellationTokenSource();
                    Task upload_task = file_transfer_utility.UploadAsync(request, token.Token);
                    Task<Task> pending_task = Task.WhenAny(upload_task, Task.Delay(m_Timeout));

                    m_PendingTasks.Add(pending_task);
                    Trace.TraceInformation("Task added for {0}", file);

                    try
                    {
                        Task completed_task = await pending_task;

                        if (completed_task == upload_task
                            && completed_task.IsCompleted
                            && !completed_task.IsFaulted)
                        {
                            Trace.TraceInformation("Upload completed {0}", file);
                            FileUploadedCallback(file);
                        }
                        else
                        {
                            token.Cancel();
                            throw new TimeoutException("Task timed out.");
                        }
                    }
                    finally
                    {
                        m_PendingTasks.Remove(pending_task);
                        Trace.TraceInformation("Task ended for {0}", file);
                    }
                }
            }
            catch(Exception ex)
            {
                Trace.TraceError("Uploade failed {0}: {1}", ex.Message, file);
                FileFailedCallback(file);
            }
            finally
            {
                m_PendingFiles.Remove(file);
                Trace.TraceInformation("Upload ended for {0}", file);
            }
        }

        /// <summary>
        /// Sweeps the entire bucket directory
        /// Does not care about file database
        /// </summary>
        public void Sweep()
        {
            if (!m_Validated)
                return;

            foreach (string file in ListFiles())
                Upload(file);
        }

        /// <summary>
        /// Database-aware version of Sweep. Only uploads
        /// files if they do not exist in either failed
        /// or succeed tables.
        /// </summary>
        /// <param name="db"></param>
        public void Sweep(Database db)
        {
            if (!m_Validated)
                return;

            foreach (string file in ListFiles())
                if (!db.Exists(file)) Upload(file);
        }

        /// <summary>
        /// Synchronously wait for all pending upload
        /// tasks to finish uploading to S3
        /// </summary>
        public void FinishPendingTasks()
        {
            if (m_PendingTasks.Count == 0)
                return;

            Trace.TraceInformation("Waiting for {0} pending tasks to finish."
                , m_PendingTasks.Count);

            m_PendingTasks.RemoveAll(item => item == null);
            Task.WaitAll(m_PendingTasks.ToArray());
        }

        private IEnumerable<string> ListFiles()
        {
            return Directory.EnumerateFiles(
                m_BaseDirectory,
                "*.*",
                SearchOption.AllDirectories);
        }

        private string GetRelativePath(string filespec, string folder)
        {
            Uri pathUri = new Uri(filespec);
            
            if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                folder += Path.DirectorySeparatorChar;
            }

            return Uri.UnescapeDataString(
                new Uri(folder)
                .MakeRelativeUri(pathUri)
                .ToString()
                .Replace('/', Path.DirectorySeparatorChar));
        }

        #region IDisposable Support
        private bool m_Disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!m_Disposed)
            {
                if (disposing)
                {
                    FinishPendingTasks();
                    if (m_Watcher != null) m_Watcher.Dispose();
                }

                m_Watcher = null;
                m_Disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
