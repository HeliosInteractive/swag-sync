﻿namespace swag
{
    using System;
    using System.IO;
    using Amazon.S3;
    using System.Linq;
    using System.Diagnostics;
    using Amazon.S3.Transfer;
    using System.Collections.Generic;

    /// <summary>
    /// Encapsulates logic of S3 bucket synchronizing
    /// </summary>
    public partial class Bucket : IDisposable
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

        #endregion

        #region private fields

        private string                  m_BucketPath        = "";
        private string                  m_BucketName        = "";
        private bool                    m_Validated         = false;
        private RecursiveFileWatcher    m_Watcher           = null;
        private InternetService         m_Internet          = null;
        private Options                 m_options           = null;
        private TransferUtility         m_XferUtility       = null;

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
        /// Lists all files in the bucket directory
        /// </summary>
        /// <returns></returns>
        private IEnumerable<string> EnumerateLocalFiles()
        {
            return Directory.EnumerateFiles(
                m_BucketPath, "*.*",
                SearchOption.AllDirectories);
        }

        /// <summary>
        /// Gets relative path of a file based on an absolute path of a directory
        /// </summary>
        /// <param name="file">path to file</param>
        /// <param name="folder">path to one of file's parent directories</param>
        /// <returns></returns>
        private string GetRelativePath(string file, string folder)
        {
            Uri pathUri = new Uri(file);
            
            if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                folder += Path.DirectorySeparatorChar;
            }

            string relative_path = Uri.UnescapeDataString(
                new Uri(folder)
                .MakeRelativeUri(pathUri)
                .ToString());

            if (Path.DirectorySeparatorChar != '/')
                return relative_path.Replace(Path.DirectorySeparatorChar, '/');
            else
                return relative_path;
        }

        #region IDisposable Support
        private bool m_Disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!m_Disposed)
            {
                if (disposing)
                {
                    CancelPendingTasks();

                    if (m_Watcher != null)
                        m_Watcher.Dispose();
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
