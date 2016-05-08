namespace swag
{
    using System.IO;
    using System.Linq;
    using System.Collections.Generic;

    class SynchronizeService : Service
    {
        InternetService m_Internet;
        List<Bucket>    m_Buckets;
        Database        m_Database;
        Options         m_Options;

        public SynchronizeService(
            InternetService internet,
            List<Bucket> buckets,
            Database db,
            Options opt)
        {
            m_Internet = internet;
            m_Database = db;
            m_Options = opt;
            m_Buckets = buckets;
        }

        protected override void Run()
        {
            if (m_Internet == null ||
                m_Database == null ||
                m_Options == null ||
                m_Buckets == null)
                return;

            if (m_Internet.IsUp)
            {
                Log.Info("Running synchronization service.");

                m_Buckets.ForEach(b => { b.Sweep(m_Database); });

                List<string> failed_files;
                m_Database.PopFailed(out failed_files, m_Options.SweepCount);

                failed_files.ForEach(file =>
                {
                    string bucket_name = file
                        .Replace(m_Options.RootDirectory, string.Empty)
                        .Trim(Path.DirectorySeparatorChar)
                        .Split(Path.DirectorySeparatorChar)
                        .First();

                    Bucket bucket = m_Buckets.Find(b => b.BucketName == bucket_name);
                    if (bucket != null) bucket.EnqueueUpload(file);
                });
            }
            else
            {
                Log.Info("Internet is down, will check back in {0} seconds.", m_Options.SweepInterval);
            }
        }
    }
}
