namespace swag
{
    using System;
    using System.IO;
    using Amazon.S3;
    using System.Linq;
    using Amazon.S3.Model;
    using System.Threading;
    using System.Diagnostics;
    using Amazon.S3.Transfer;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Collections.Concurrent;

    /// <summary>
    /// Encapsulates logic of uploading/sweeping of one single bucket
    /// </summary>
    public class Bucket : IDisposable
    {
        #region public fields

        /// <summary>
        /// Event emitted on successful upload of a file
        /// </summary>
        public Action<string> OnFileUploaded;

        /// <summary>
        /// Event emitted on unsuccessful upload of a file
        /// </summary>
        public Action<string> OnFileFailed;

        /// <summary>
        /// Bucket name
        /// </summary>
        public string BucketName
        {
            get { return m_BucketName; }
        }

        /// <summary>
        /// Bucket path
        /// </summary>
        public string BucketPath
        {
            get { return m_BucketPath; }
        }

        /// <summary>
        /// Answers true if this instance is ready to upload
        /// </summary>
        public bool Valid
        {
            get { return !m_Disposed && m_Validated; }
        }

        /// <summary>
        /// Returns true if queue for "active" uploads is full.
        /// </summary>
        public bool IsFull
        {
            get { return m_CurrentUploads.Count > m_options.BucketMax; }
        }

        #endregion

        #region private fields

        private string                  m_BucketPath        = "";
        private string                  m_BucketName        = "";
        private bool                    m_Validated         = false;
        private RecursiveFileWatcher    m_Watcher           = null;
        private InternetService         m_Internet          = null;
        private Options                 m_options           = null;
        private TransferUtility         m_XferUtility       = null;
        private ConcurrentQueue<string> m_PendingUploads    = new ConcurrentQueue<string>();
        private ConcurrentDictionary<string, Task<Task>>
                                        m_CurrentUploads    = new ConcurrentDictionary<string, Task<Task>>();

        #endregion

        public Bucket(string base_path, Options opts, InternetService internet)
        {
            if (opts == null)
            {
                Trace.TraceError("Options supplied to Bucket is empty.");
                return;
            }

            if (internet == null)
            {
                Trace.TraceError("Internet supplied to Bucket is empty.");
                return;
            }

            if (!CheckBucketPath(base_path))
            {
                Trace.TraceError("Bucket path supplied is invalid: {0}.", BucketPath);
                return;
            }

            m_options = opts;
            m_Internet = internet;
            m_BucketPath = base_path;
            m_BucketName = BucketPath.Split(Path.DirectorySeparatorChar).Last();

            if (m_BucketName.Contains(Path.DirectorySeparatorChar))
            {
                Trace.TraceError("Unable to extract a valid bucket name: {0}.", m_BucketName);
                return;
            }

            m_Validated = SetupTransferUtility();
        }

        /// <summary>
        /// Starts the bucket file watcher
        /// </summary>
        public void SetupWatcher()
        {
            if (!Valid)
                return;

            lock(this)
            {
                ShutdownWatcher();
                m_Watcher = new RecursiveFileWatcher(BucketPath, WatcherCallback);
                Trace.TraceInformation("Bucket {0} is watching at directory {1}.", BucketName, BucketPath);
            }
        }

        /// <summary>
        /// Shuts down the file watcher
        /// No-op if watcher does not exist
        /// </summary>
        public void ShutdownWatcher()
        {
            if (!Valid)
                return;

            lock (this)
            {
                if (m_Watcher != null)
                {
                    Trace.TraceWarning("Bucket {0} is no longer being watched.", BucketName);

                    m_Watcher.Dispose();
                    m_Watcher = null;
                }
            }
        }

        /// <summary>
        /// Add an upload to the "inactive" queue.
        /// File might and might not start to upload immediately.
        /// </summary>
        /// <param name="file">file to be uploaded</param>
        public void EnqueueUpload(string file)
        {
            PullUpload();

            if (m_PendingUploads.Contains(file) || m_CurrentUploads.ContainsKey(file))
                return;

            m_PendingUploads.Enqueue(file);
        }

        /// <summary>
        /// Pull an upload request out of "inactive" queue into "active" queue.
        /// No-op if this is not possible (queues are empty or etc.)
        /// </summary>
        public void PullUpload()
        {
            if (!IsFull)
            {
                string pulled;
                if (DequeueUpload(out pulled))
                    Upload(pulled);
            }
        }

        /// <summary>
        /// Attempt to dequeue a file to upload
        /// </summary>
        /// <param name="file">container for dequeued file</param>
        /// <returns>true if dequeuing is successful</returns>
        private bool DequeueUpload(out string file)
        {
            file = string.Empty;

            lock (this)
            {
                if (m_PendingUploads.TryDequeue(out file))
                    return true;
                else
                    return false;
            }
        }

        /// <summary>
        /// Sometimes zombie tasks remain in the queue due to the way
        /// ConcurrentCollections work in C#. This kills all zombies.
        /// </summary>
        private void CleanCurrentQueue()
        {
            if (m_CurrentUploads.IsEmpty)
                return;

            foreach (var current_upload in m_CurrentUploads)
            {
                if (current_upload.Value != null && current_upload.Value.IsCompleted)
                {
                    Trace.TraceWarning("A zombie task is found for {0}!", current_upload.Key);

                    Task<Task> dead_task;
                    if (!m_CurrentUploads.TryRemove(current_upload.Key, out dead_task))
                    {
                        Trace.TraceWarning("Unable to remove the zombie task for {0}!", current_upload.Key);
                    }
                }
            }
        }

        /// <summary>
        /// Sets up bucket's transfer utility based on its end-point
        /// </summary>
        private bool SetupTransferUtility()
        {
            try
            {
                using (IAmazonS3 client = Amazon.AWSClientFactory.CreateAmazonS3Client
                    (m_options.AwsAccessKey, m_options.AwsSecretKey, new AmazonS3Config { ServiceURL = "https://s3.amazonaws.com" }))
                {
                    var bucket = client.ListBuckets().Buckets.Find(b =>
                    {
                        return (BucketName == b.BucketName);
                    });

                    if (bucket != null)
                    {
                        var location = client.GetBucketLocation(bucket.BucketName);

                        if (location != null)
                        {
                            if (m_XferUtility != null)
                            {
                                m_XferUtility.Dispose();
                                m_XferUtility = null;
                            }

                            // documented at: http://docs.aws.amazon.com/AmazonS3/latest/API/RESTBucketGETlocation.html
                            // basically if it's in US-East-1 region, location string is NULL!
                            string bucket_location = (string.IsNullOrWhiteSpace(location.Location.Value) ? "us-east-1" : location.Location.Value);

                            m_XferUtility = new TransferUtility
                                (new AmazonS3Client
                                ((Amazon.RegionEndpoint.GetBySystemName(bucket_location))));
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Trace.TraceError("Unable to setup the bucket {0}: {1}.", BucketName, ex.Message);
                m_XferUtility = null;
            }

            return (m_XferUtility != null);
        }

        /// <summary>
        /// Answers true if bucket path is valid
        /// </summary>
        /// <param name="path">bucket path</param>
        /// <returns>validity of bucket path</returns>
        private bool CheckBucketPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) ||
                    !Path.IsPathRooted(path) ||
                    !File.GetAttributes(path).HasFlag(FileAttributes.Directory) ||
                    !Directory.Exists(path))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError("Unable to check bucket path : {0}.", ex.Message);
            }

            return true;
        }

        /// <summary>
        /// Callback called by Upload() method after a successful upload
        /// </summary>
        /// <param name="file">file that was uploaded</param>
        protected virtual void FileUploadedCallback(string file)
        {
            if (!Valid)
                return;

            if (OnFileUploaded != null)
                OnFileUploaded(file);
        }

        /// <summary>
        /// Callback called by Upload() method after a failed upload
        /// </summary>
        /// <param name="file">file that was failed to upload</param>
        protected virtual void FileFailedCallback(string file)
        {
            if (!Valid)
                return;

            if (OnFileFailed != null)
                OnFileFailed(file);
        }

        /// <summary>
        /// Callback called by FS watcher if it's setup.
        /// </summary>
        /// <param name="file">file passed by FS watcher</param>
        private void WatcherCallback(string file)
        {
            if (!Valid)
                return;

            if (!File.Exists(file))
            {
                Trace.TraceError("File does not exist for this callback: {0}.", file);
                return;
            }

            if (Directory.Exists(file))
            {
                Trace.TraceInformation("Ignoring watcher callback for directory {0}.", file);
                return;
            }

            EnqueueUpload(file);
        }

        /// <summary>
        /// Uploads a file to S3 bucket asynchronously.
        /// Use FinishPendingTasks to wait for everything to finish
        /// </summary>
        /// <param name="file">file to be uploaded</param>
        private async void Upload(string file)
        {
            if (!Valid)
                return;

            if (!m_Internet.IsUp)
            {
                Trace.TraceInformation("Internet seems to be down. Ignoring {0}.", file);
                return;
            }

            CleanCurrentQueue();
            if (m_CurrentUploads.ContainsKey(file))
            {
                Trace.TraceInformation("File is pending. Ignoring {0}.", file);
                return;
            }

            try
            {
                TransferUtilityUploadRequest request =
                    new TransferUtilityUploadRequest
                    {
                        FilePath = file,
                        BucketName = BucketName,
                        Key = GetRelativePath(file, BucketPath)
                    };

                using (CancellationTokenSource token = new CancellationTokenSource())
                using (Task upload_task = m_XferUtility.UploadAsync(request, token.Token))
                using (Task timeout_task = Task.Delay(TimeSpan.FromSeconds(m_options.Timeout), token.Token))
                {
                    Task<Task> pending_task = Task.WhenAny(upload_task, timeout_task);

                    m_CurrentUploads[file] = pending_task;
                    Trace.TraceInformation("Upload began for {0}.", file);

                    Task completed_task = await pending_task;
                    token.Cancel();

                    if (completed_task == upload_task
                        && completed_task.IsCompleted
                        && !completed_task.IsFaulted)
                    {
                        if (Exists(request))
                        {
                            Trace.TraceInformation("Upload finished {0}.", file);
                            FileUploadedCallback(file);
                        }
                        else
                        {
                            throw new FileNotFoundException("S3 double check failed.");
                        }
                    }
                    else
                    {
                        throw new TimeoutException("Task timed out.");
                    }

                    CleanupTasks(timeout_task, upload_task);
                }
            }
            catch(Exception ex)
            {
                Trace.TraceError("Upload failed {0}: {1}.", ex.Message, file);
                FileFailedCallback(file);
            }
            finally
            {
                Task<Task> task;
                if (!m_CurrentUploads.TryRemove(file, out task))
                    Trace.TraceWarning("Unable to remove {0}.", file);

                Trace.TraceInformation("Upload ended for {0}.", file);

                // chain uploads together.
                PullUpload();
            }
        }

        /// <summary>
        /// Sweeps the entire bucket directory
        /// Does not care about file database
        /// </summary>
        public void Sweep()
        {
            if (!Valid)
                return;

            foreach (string file in ListFiles())
                EnqueueUpload(file);
        }

        /// <summary>
        /// Database-aware version of Sweep. Only uploads
        /// files if they do not exist in either failed
        /// or succeed tables.
        /// </summary>
        /// <param name="db">database instance</param>
        public void Sweep(Database db)
        {
            if (!Valid || db == null)
                return;

            foreach (string file in ListFiles())
                if (!db.Exists(file)) EnqueueUpload(file);
        }

        /// <summary>
        /// Synchronously wait for all pending upload
        /// tasks to finish uploading to S3
        /// </summary>
        public void FinishPendingTasks()
        {
            if (m_CurrentUploads.IsEmpty)
                return;

            CleanCurrentQueue();
            Trace.TraceInformation("Waiting for {0} pending tasks to finish."
                , m_CurrentUploads.Count);

            Task.WaitAll(m_CurrentUploads.Values.AsEnumerable().ToArray());
        }

        /// <summary>
        /// Double checks with AWS to see if a file uploaded
        /// previously via Upload() exists or not.
        /// </summary>
        /// <param name="request">param coming in from Upload</param>
        /// <returns></returns>
        private bool Exists(TransferUtilityUploadRequest request)
        {
            if (!Valid || request == null || !m_options.CheckEnabled)
                return true;

            bool exists = false;

            using (CancellationTokenSource cts = new CancellationTokenSource())
            using (Task timeout = Task.Delay(TimeSpan.FromMilliseconds(m_options.CheckTimeout), cts.Token))
            using (Task exist_check = Task.Run(() =>
            {
                try
                {
                    var response = m_XferUtility.S3Client.GetObjectMetadata(
                        new GetObjectMetadataRequest
                        {
                            BucketName = request.BucketName,
                            Key = request.Key
                        });

                    exists = true;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Unable to query S3 for file existence ({0}): {1}/{2}",
                        ex.Message, request.BucketName, request.Key);

                    exists = false;
                }
            }, cts.Token))
            {
                Task.WaitAny(timeout, exist_check);
                cts.Cancel();

                CleanupTasks(timeout, exist_check);
            }

            return exists;
        }

        /// <summary>
        /// Forcefully cleans up tasks.
        /// This should be called after CancellationToken is invoked
        /// </summary>
        /// <param name="tasks">tasks to wait for</param>
        private void CleanupTasks(params Task[] tasks)
        {
            if (tasks == null || tasks.Length == 0)
                return;

            foreach (Task task in tasks)
            {
                try { if (task != null && !task.IsCompleted) task.Wait(); }
                catch { Trace.TraceWarning("Unable to put Task out of its misery."); }
            }
        }

        /// <summary>
        /// Lists all files in the bucket directory
        /// </summary>
        /// <returns></returns>
        private IEnumerable<string> ListFiles()
        {
            return Directory.EnumerateFiles(
                m_BucketPath, "*.*",
                SearchOption.AllDirectories);
        }

        /// <summary>
        /// Gets relative path of file based on an absolute path of a directory
        /// </summary>
        /// <param name="filespec">path to file</param>
        /// <param name="folder">path to one of filespec's parent directories</param>
        /// <returns></returns>
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
                .Replace(Path.DirectorySeparatorChar, '/'));
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
