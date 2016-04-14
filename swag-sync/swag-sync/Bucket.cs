namespace swag
{
    using System;
    using System.IO;
    using Amazon.S3;
    using System.Linq;
    using System.Diagnostics;
    using Amazon.S3.Transfer;

    public class Bucket
    {
        public Action<string> OnFileUploaded;
        public Action<string> OnFileFailed;

        private string              m_BaseDirectory = "";
        private string              m_BucketName    = "";
        private bool                m_Validated     = false;
        private FileSystemWatcher   m_Watcher       = null;

        public Bucket(string base_path)
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
            m_BucketName = m_BaseDirectory.Split(Path.DirectorySeparatorChar).Last();

            m_Watcher = new FileSystemWatcher();
            m_Watcher.Path = m_BaseDirectory;
            m_Watcher.NotifyFilter =
                NotifyFilters.LastWrite |
                NotifyFilters.FileName |
                NotifyFilters.DirectoryName;

            m_Watcher.Changed += WatcherCallback;
            m_Watcher.Created += WatcherCallback;
            m_Watcher.Renamed += WatcherCallback;
            m_Validated = true;

            Trace.TraceInformation("Bucket is up and watching with name {0} and path {1}",
                m_BucketName, m_BaseDirectory);
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
            Trace.TraceInformation("Bucket {0} has received an event from watcher: File {1} with reason {2}",
                m_BaseDirectory, ev.FullPath, ev.ChangeType.ToString());
            Upload(ev.FullPath);
        }

        public void Upload(string file)
        {
            Trace.TraceInformation("Attempting to upload {0}", file);

            TransferUtility fileTransferUtility =
                new TransferUtility(new AmazonS3Client());

            TransferUtilityUploadRequest uploadRequest =
                new TransferUtilityUploadRequest
                {
                    BucketName = m_BucketName,
                    FilePath = file.Replace(m_BaseDirectory, string.Empty)
                };

            uploadRequest.UploadProgressEvent +=
                new EventHandler<UploadProgressArgs>
                    (UploadProgressCallback);

            fileTransferUtility.UploadAsync(uploadRequest);
        }

        private void UploadProgressCallback(object sender, UploadProgressArgs e)
        {
            Trace.TraceInformation("Upload progress from bucket {0} and File {1}: {2}/{3} bytes ({4}%)",
                m_BucketName, e.FilePath, e.TransferredBytes, e.TotalBytes, e.PercentDone);
        }
    }
}
