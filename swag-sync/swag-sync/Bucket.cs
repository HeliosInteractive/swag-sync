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
                return;

            Trace.TraceInformation("Attempting to upload {0}", file);

            try
            {
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
                            Key = file
                                .Replace(m_BaseDirectory, string.Empty)
                                .Trim(Path.DirectorySeparatorChar)
                                .Replace(Path.DirectorySeparatorChar, '/')
                        };

                    CancellationTokenSource token = new CancellationTokenSource();
                    Task upload_task = file_transfer_utility.UploadAsync(request, token.Token);
                    Task<Task> pending_task = Task.WhenAny(upload_task, Task.Delay(m_Timeout));

                    m_PendingTasks.Add(pending_task);
                    m_PendingFiles.Add(file);

                    Task completed_task = await pending_task;

                    m_PendingFiles.Remove(file);
                    m_PendingTasks.Remove(pending_task);

                    if (completed_task == upload_task)
                    {
                        if (Exists(request, file_transfer_utility.S3Client))
                        {
                            Trace.TraceInformation("Upload complete {0}", file);
                            FileUploadedCallback(file);
                        }
                        else
                        {
                            Trace.TraceInformation("Upload failed {0}", file);
                            FileFailedCallback(file);
                        }
                    }
                    else
                    {
                        token.Cancel();
                        {
                            Trace.TraceInformation("Upload timed out {0}", file);
                            FileFailedCallback(file);
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Trace.TraceError("Uploading {0} failed due to exception: {1}",
                    ex.Message, file);
                FileFailedCallback(file);
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

            foreach (string file in Directory.EnumerateFiles(
                m_BaseDirectory, "*.*", SearchOption.AllDirectories))
                Upload(file);
        }

        /// <summary>
        /// Database-aware version of Sweep. Only uploads
        /// files if they do not exist in either failed
        /// ot succeed tables.
        /// </summary>
        /// <param name="db"></param>
        public void Sweep(Database db)
        {
            if (!m_Validated)
                return;

            foreach (string file in Directory.EnumerateFiles(
                m_BaseDirectory, "*.*", SearchOption.AllDirectories))
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

        private bool Exists(TransferUtilityUploadRequest request, IAmazonS3 client)
        {
            try
            {
                var response = client.GetObjectMetadata(
                    new GetObjectMetadataRequest
                    {
                        BucketName = request.BucketName,
                        Key = request.Key
                    });

                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError("Unable to query S3 for file existence ({0}): {1}/{2}",
                    ex.Message , request.BucketName, request.Key);

                return false;
            }
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
