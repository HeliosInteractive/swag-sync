namespace swag
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;

    class DatabaseService : Service
    {
        private Database    m_Database;
        private Options     m_Options;

        public DatabaseService(Database db, Options opt)
        {
            m_Database = db;
            m_Options = opt;
        }

        protected override void Run()
        {
            if (m_Database == null)
                return;

            List<string> all_files;
            m_Database.PopAll(out all_files);

            all_files.ForEach(file =>
            {
                if (!File.Exists(file) ||
                    (m_Options != null && !file.Contains(m_Options.RootDirectory)))
                {
                    Trace.TraceWarning("Invalid file found in database: {0}", file);
                    m_Database.Remove(file);
                }
            });
        }
    }
}
