namespace swag
{
    using System.IO;
    using System.Diagnostics;

    public partial class Bucket
    {
        /// <summary>
        /// Starts the bucket file watcher
        /// </summary>
        public void SetupWatcher()
        {
            if (!Valid)
                return;

            ShutdownWatcher();
            m_Watcher = new RecursiveFileWatcher(BucketPath, WatcherCallback);

            Trace.TraceInformation("Bucket {0} is watching at directory {1}.", BucketName, BucketPath);
        }

        /// <summary>
        /// Shuts down the file watcher
        /// No-op if watcher does not exist
        /// </summary>
        public void ShutdownWatcher()
        {
            if (!Valid)
                return;

            if (m_Watcher != null)
            {
                Trace.TraceWarning("Bucket {0} is no longer being watched.", BucketName);

                m_Watcher.Dispose();
                m_Watcher = null;
            }
        }

        /// <summary>
        /// Callback called by FS watcher if it's setup.
        /// </summary>
        /// <param name="file">file passed by FS watcher</param>
        protected virtual void WatcherCallback(string file)
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
    }
}
