namespace swag
{
    using System;
    using System.IO;
    using System.Linq;
    using Amazon.S3.Model;
    using System.Threading;
    using System.Diagnostics;
    using Amazon.S3.Transfer;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Collections.Concurrent;

    /// <summary>
    /// All methods which handle network logic of Bucket
    /// </summary>
    public partial class Bucket
    {
        private ConcurrentQueue<string> m_PendingUploads = new ConcurrentQueue<string>();
        private ConcurrentDictionary<string, KeyValuePair<Task<Task>, CancellationTokenSource>>
                                        m_CurrentUploads = new ConcurrentDictionary<string, KeyValuePair<Task<Task>, CancellationTokenSource>>();
        /// <summary>
        /// Returns true if the queue for "active" uploads is full.
        /// </summary>
        public bool IsFull
        {
            get { return m_CurrentUploads.Count > m_options.BucketMax; }
        }

        /// <summary>
        /// Add an upload to the "inactive" queue.
        /// File might and might not start to upload immediately.
        /// </summary>
        /// <param name="file">file to be uploaded</param>
        public void EnqueueUpload(string file)
        {
            DequeueUpload();

            if (m_PendingUploads.Contains(file) || m_CurrentUploads.ContainsKey(file))
                return;

            m_PendingUploads.Enqueue(file);

            Log.Info("{0} enqueued for uploading.", file);
        }

        /// <summary>
        /// Pull an upload request out of the "inactive" queue into "active" queue.
        /// No-op if this is not possible (queues are empty or etc.)
        /// </summary>
        public void DequeueUpload()
        {
            if (!IsFull && !m_PendingUploads.IsEmpty)
            {
                string pulled;

                if (m_PendingUploads.TryDequeue(out pulled))
                {
                    Log.Info("{0} dequeued for uploading.", pulled);
                    Upload(pulled);
                }
                else
                {
                    Log.Warn("Unable to dequeue.");

                    Thread.Sleep(TimeSpan.FromMilliseconds(1));
                    DequeueUpload();
                }
            }
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

            if (m_CurrentUploads.ContainsKey(file))
            {
                Log.Warn("File is pending. Ignoring {0}.", file);
                return;
            }

            if (!m_Internet.IsUp)
            {
                Log.Warn("Internet seems to be down. Enqueuing {0}.", file);
                EnqueueUpload(file);
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

                    m_CurrentUploads[file] = new KeyValuePair<Task<Task>, CancellationTokenSource>
                        (pending_task, token);

                    Log.Write("Upload began {0}.", file);

                    Task completed_task = await pending_task;
                    token.Cancel();

                    if (completed_task == upload_task
                        && completed_task.IsCompleted
                        && !completed_task.IsFaulted)
                    {
                        if (Exists(request))
                        {
                            Log.Write("Upload succeed {0}.", file);
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
            catch (Exception ex)
            {
                Log.Write("Upload failed {0}: {1}.", ex.Message, file);
                FileFailedCallback(file);
            }
            finally
            {
                RemoveFile(file);
                DequeueUpload();
            }
        }

        /// <summary>
        /// Remove a file from "current" queue
        /// </summary>
        /// <param name="file">file to be removed</param>
        private void RemoveFile(string file)
        {
            if (!m_CurrentUploads.ContainsKey(file))
                return;

            KeyValuePair<Task<Task>, CancellationTokenSource> entry;
            if (!m_CurrentUploads.TryRemove(file, out entry))
            {
                Log.Warn("Unable to remove {0}. Retrying...", file);

                Thread.Sleep(TimeSpan.FromMilliseconds(100));
                RemoveFile(file);
            }
        }

        /// <summary>
        /// Synchronously wait for all pending upload
        /// tasks to finish uploading to S3
        /// </summary>
        public void FinishPendingTasks()
        {
            if (m_CurrentUploads.IsEmpty && m_PendingUploads.IsEmpty)
                return;

            Log.Write("Waiting for {0} pending tasks to finish."
                , m_CurrentUploads.Count + m_PendingUploads.Count);

            Task.WaitAll(m_CurrentUploads.Select(el=> { return el.Value.Key; }).ToArray());

            while(!m_PendingUploads.IsEmpty)
            {
                DequeueUpload();
                FinishPendingTasks();
            }
        }

        public void CancelPendingTasks()
        {
            if (m_CurrentUploads.IsEmpty)
                return;

            if (!m_PendingUploads.IsEmpty)
            {
                // really, the fastest way to clear pending items
                m_PendingUploads = new ConcurrentQueue<string>();
            }

            Log.Write("Cancelling {0} current tasks."
                , m_CurrentUploads.Count);

            foreach(var pending in m_CurrentUploads)
            {
                try
                {
                    pending.Value.Value.Cancel();
                    CleanupTasks(pending.Value.Key);
                }
                catch(Exception ex)
                {
                    Log.Error("Unable to cancel {0}: {1}.",
                        pending.Key, ex.Message);
                }
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

            foreach (string file in EnumerateLocalFiles())
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

            foreach (string file in EnumerateLocalFiles())
                if (!db.Exists(file)) EnqueueUpload(file);
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
                try { if (task != null && !task.IsCompleted) task.Wait(5000); }
                catch { Log.Error("Unable to put Task out of its misery."); }
            }
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
                    Log.Error("Unable to query S3 for file existence ({0}): {1}/{2}",
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
    }
}
