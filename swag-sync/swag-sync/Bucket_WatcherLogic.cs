namespace swag
{
    using System.IO;

    public partial class Bucket
    {
        /// <summary>
        /// Starts the bucket file watcher
        /// </summary>
        public void SetupWatcher()
        {
            if (!Ready)
                return;

            ShutdownWatcher();
            m_Watcher = new RecursiveFileWatcher(BucketPath, WatcherCallback);

            Log.Write("Bucket {0} is watching at directory {1}.", BucketName, BucketPath);
        }

        /// <summary>
        /// Shuts down the file watcher
        /// No-op if watcher does not exist
        /// </summary>
        public void ShutdownWatcher()
        {
            if (!Ready)
                return;

            if (m_Watcher != null)
            {
                Log.Write("Bucket {0} is no longer being watched.", BucketName);

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
            if (!Ready)
                return;

            if (!File.Exists(file))
            {
                Log.Error("File does not exist for this callback: {0}.", file);
                return;
            }

            if (Directory.Exists(file))
            {
                Log.Info("Ignoring watcher callback for directory {0}.", file);
                return;
            }

            EnqueueUpload(file);
        }
    }
}
