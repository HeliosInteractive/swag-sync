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

        public string BucketName
        {
            get { return m_BucketName; }
        }

        public void SetupWatcher()
        {
            if (!m_Validated)
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
            if (OnFileUploaded != null)
                OnFileUploaded(file);
        }

        protected void FileFailedCallback(string file)
        {
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

        public async void Upload(string file)
        {
            if (!m_Validated)
                return;

            Trace.TraceInformation("Attempting to upload {0}", file);

            using (TransferUtility file_transfer_utility =
                new TransferUtility(
                    new AmazonS3Client(
                        Amazon.RegionEndpoint.USWest1)))
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

                request.UploadProgressEvent +=
                    new EventHandler<UploadProgressArgs>
                        (UploadProgressCallback);

                CancellationTokenSource token = new CancellationTokenSource();
                Task upload_task = file_transfer_utility.UploadAsync(request, token.Token);
                Task<Task> pending_task = Task.WhenAny(upload_task, Task.Delay(m_Timeout));

                m_PendingTasks.Add(pending_task);
                Task completed_task = await pending_task;
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

        public void Sweep()
        {
            if (!m_Validated)
                return;

            foreach (string file in Directory.EnumerateFiles(
                m_BaseDirectory, "*.*", SearchOption.AllDirectories))
                Upload(file);
        }

        public void FinishPendingTasks()
        {
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

            catch (AmazonS3Exception ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return false;

                throw;
            }
        }

        private void UploadProgressCallback(object sender, UploadProgressArgs e)
        {
            Trace.TraceInformation("Upload progress from bucket {0} and File {1}: {2}/{3} bytes.",
                m_BucketName, e.FilePath, e.TransferredBytes, e.TotalBytes);
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
                    m_Watcher.Dispose();
                }

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
