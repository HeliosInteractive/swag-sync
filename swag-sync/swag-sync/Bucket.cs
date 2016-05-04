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
        public string BucketDirectory
        {
            get { return m_BucketDirectory; }
            set { m_BucketDirectory = value; OnBucketDirectoryChanged(); }
        }

        #endregion

        #region private fields

        private string              m_BucketDirectory   = "";
        private string              m_BucketName        = "";
        private bool                m_Validated         = false;
        private FileSystemWatcher   m_Watcher           = null;
        private List<Task<Task>>    m_PendingTasks      = new List<Task<Task>>();
        private List<string>        m_PendingFiles      = new List<string>();
        private InternetService     m_Internet          = null;
        private Options             m_options           = null;
        private TransferUtility     m_TransferUtility   = null;

        #endregion

        public Bucket(string base_path, Options opts, InternetService internet)
        {
            m_options = opts;
            m_Internet = internet;
            BucketDirectory = base_path;

            if (string.IsNullOrWhiteSpace(BucketName))
            {
                Trace.TraceError("Extracted bucket name is invalid.");
                return;
            }

            if (opts == null)
            {
                Trace.TraceError("Options supplied to Bucket is empty.");
                return;
            }

            if (internet == null)
            {
                Trace.TraceError("Options supplied to Bucket is empty.");
                return;
            }
        }

        /// <summary>
        /// Starts the bucket file watcher
        /// </summary>
        public void SetupWatcher()
        {
            if (!m_Validated)
                return;

            ShutdownWatcher();

            m_Watcher = new FileSystemWatcher();
            m_Watcher.Path = m_BucketDirectory;
            m_Watcher.NotifyFilter =
                NotifyFilters.LastWrite |
                NotifyFilters.FileName;

            m_Watcher.Changed += WatcherCallback;
            m_Watcher.Renamed += WatcherCallback;
            m_Watcher.EnableRaisingEvents = true;
            m_Watcher.IncludeSubdirectories = true;

            Trace.TraceInformation("Bucket {0} is watching directory {1}", BucketName, BucketDirectory);
        }

        /// <summary>
        /// Sets up bucket based on its end-point
        /// </summary>
        private void SetupBucket()
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
                            if (m_TransferUtility != null)
                            {
                                m_TransferUtility.Dispose();
                                m_TransferUtility = null;
                            }

                            // documented at: http://docs.aws.amazon.com/AmazonS3/latest/API/RESTBucketGETlocation.html
                            // basically if it's in US-East-1 region, location string is NULL!
                            string bucket_location = (string.IsNullOrWhiteSpace(location.Location.Value) ? "us-east-1" : location.Location.Value);

                            m_TransferUtility = new TransferUtility
                                (new AmazonS3Client
                                ((Amazon.RegionEndpoint.GetBySystemName(bucket_location))));
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Trace.TraceError("Unable to setup the bucket {0}: {1}", BucketName, ex.Message);
                m_TransferUtility = null;
                return;
            }

            Trace.TraceInformation("Bucket {0} is setup.", BucketName);
        }

        /// <summary>
        /// Shuts down the file watcher
        /// No-op if watcher does not exist
        /// </summary>
        public void ShutdownWatcher()
        {
            if (m_Watcher != null)
            {
                m_Watcher.Dispose();
                m_Watcher = null;
            }
        }

        /// <summary>
        /// Internal event emitted when BucketDirectory changes.
        /// Sets m_Validated to false in case of bad input.
        /// </summary>
        private void OnBucketDirectoryChanged()
        {
            FinishPendingTasks();

            m_BucketName    = string.Empty;
            bool succeed    = true;
            bool watching   = (m_Watcher != null);

            ShutdownWatcher();

            if (!CheckBucketPath(BucketDirectory))
            {
                Trace.TraceError("Bucket path supplied is invalid: {0}.", BucketDirectory);
                succeed = false;
                return;
            }

            m_BucketName = m_BucketDirectory.Split(Path.DirectorySeparatorChar).Last();

            if (m_BucketName.Contains(Path.DirectorySeparatorChar))
            {
                Trace.TraceError("Unable to extract a valid bucket name: {0}.", m_BucketName);
                succeed = false;
                return;
            }

            if (!succeed)
            {
                m_Validated = false;
                m_BucketName = string.Empty;
            }
            else if (m_options != null && m_Internet != null)
            {
                if (watching)
                    SetupWatcher();

                SetupBucket();

                m_Validated = (m_TransferUtility != null);
            }
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
                Trace.TraceError("Unable to check bucket path : {0}", ex.Message);
            }

            return true;
        }

        /// <summary>
        /// Callback called by Upload() method after a successful upload
        /// </summary>
        /// <param name="file">file that was uploaded</param>
        protected virtual void FileUploadedCallback(string file)
        {
            if (!m_Validated)
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
            if (!m_Validated)
                return;

            if (OnFileFailed != null)
                OnFileFailed(file);
        }

        /// <summary>
        /// Callback called by FS watcher if it's setup.
        /// </summary>
        /// <param name="source">passed by FS watcher, can be safely casted to Bucket</param>
        /// <param name="ev">event args passed by FS watcher</param>
        private void WatcherCallback(object source, FileSystemEventArgs ev)
        {
            if (!File.Exists(ev.FullPath))
            {
                Trace.TraceError("File does not exist for this callback: {0}.", ev.FullPath);
                return;
            }

            if (File.GetAttributes(ev.FullPath).HasFlag(FileAttributes.Directory))
            {
                Trace.TraceInformation("Ignoring watcher callback for directory {0}.", ev.FullPath);
                return;
            }

            Trace.TraceInformation("FS Event for {0} bucket: File {1} with reason {2}",
                BucketName, ev.FullPath, ev.ChangeType.ToString());

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

            if (m_PendingFiles.Count > m_options.BucketMax)
            {
                Trace.TraceInformation("Max count exceeded. Ignoring {0}", file);
                return;
            }

            if (!m_Internet.IsUp)
            {
                Trace.TraceInformation("Internet seems to be down. Ignoring {0}", file);
                return;
            }

            try
            {
                m_PendingFiles.Add(file);
                Trace.TraceInformation("Upload began for {0}", file);

                TransferUtilityUploadRequest request =
                    new TransferUtilityUploadRequest
                    {
                        FilePath = file,
                        BucketName = BucketName,
                        Key = GetRelativePath(file, BucketDirectory)
                    };

                using (CancellationTokenSource token = new CancellationTokenSource())
                using (Task timeout_task = Task.Delay(TimeSpan.FromSeconds(m_options.Timeout), token.Token))
                using (Task upload_task = m_TransferUtility.UploadAsync(request, token.Token))
                {
                    Task<Task> pending_task = Task.WhenAny(upload_task, timeout_task);

                    m_PendingTasks.Add(pending_task);
                    Trace.TraceInformation("Task added for {0}", file);

                    try
                    {
                        Task completed_task = await pending_task;
                        token.Cancel();

                        if (completed_task == upload_task
                            && completed_task.IsCompleted
                            && !completed_task.IsFaulted)
                        {
                            if (Exists(request))
                            {
                                Trace.TraceInformation("Upload completed {0}", file);
                                FileUploadedCallback(file);
                            }
                            else
                            {
                                throw new FileNotFoundException("S3 double check failed");
                            }
                        }
                        else
                        {
                            throw new TimeoutException("Task timed out.");
                        }
                    }
                    finally
                    {
                        m_PendingTasks.Remove(pending_task);
                        Trace.TraceInformation("Task ended for {0}", file);
                    }

                    if (!timeout_task.IsCompleted)
                        timeout_task.Wait();

                    if (!upload_task.IsCompleted)
                        upload_task.Wait();
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

        /// <summary>
        /// Double checks with AWS to see if a file uploaded
        /// previously via Upload() exists or not.
        /// </summary>
        /// <param name="request">param coming in from Upload</param>
        /// <returns></returns>
        private bool Exists(TransferUtilityUploadRequest request)
        {
            if (!m_Validated || !m_options.CheckEnabled)
                return true;

            bool exists = false;

            using (CancellationTokenSource cts = new CancellationTokenSource())
            using (Task timeout = Task.Delay(TimeSpan.FromMilliseconds(m_options.CheckTimeout), cts.Token))
            using (Task exist_check = Task.Run(() =>
            {
                try
                {
                    var response = m_TransferUtility.S3Client.GetObjectMetadata(
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

                if (!exist_check.IsCompleted)
                    exist_check.Wait();

                if (!timeout.IsCompleted)
                    timeout.Wait();
            }

            return exists;
        }

        /// <summary>
        /// Lists all files in the bucket directory
        /// </summary>
        /// <returns></returns>
        private IEnumerable<string> ListFiles()
        {
            return Directory.EnumerateFiles(
                m_BucketDirectory,
                "*.*",
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
