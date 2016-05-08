namespace swag
{
    using System.IO;
    using System.Collections.Generic;

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

            Log.Info("Running database service.");

            List<string> all_files;
            m_Database.PopAll(out all_files);

            all_files.ForEach(file =>
            {
                if (!File.Exists(file) ||
                    (m_Options != null && !file.Contains(m_Options.RootDirectory)))
                {
                    Log.Warn("Invalid file found in database: {0}", file);
                    m_Database.Remove(file);
                }
            });
        }
    }
}
