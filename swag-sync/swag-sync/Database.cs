namespace swag
{
    using System;
    using System.Data;
    using Mono.Data.Sqlite;
    using System.Diagnostics;
    using System.Collections.Generic;

    /// <summary>
    /// Used to store records of what has been uploaded
    /// and what has been failed per swag-sync runs.
    /// </summary>
    public class Database : IDisposable
    {
        private IDbConnection   m_Connection;
        private IDbCommand      m_Command;

        public Database()
        {
            try
            {
                m_Connection = new SqliteConnection("URI=file:swag.db");
                m_Connection.Open();
            }
            catch(Exception ex)
            {
                Trace.TraceError("Database connection failed: ", ex.Message);
                m_Connection = null;
                return;
            }

            try
            {
                m_Command = m_Connection.CreateCommand();

                m_Command.CommandText = "CREATE TABLE IF NOT EXISTS failed (id INTEGER PRIMARY KEY, path VARCHAR(4096) UNIQUE)";
                m_Command.ExecuteNonQuery();

                m_Command.CommandText = "CREATE TABLE IF NOT EXISTS succeed (id INTEGER PRIMARY KEY, path VARCHAR(4096) UNIQUE)";
                m_Command.ExecuteNonQuery();
            }
            catch (InvalidOperationException) { Dispose(); }
        }

        /// <summary>
        /// answers true if underlying resources
        /// managing the Sqlite database are valid.
        /// </summary>
        public bool IsValid
        {
            get
            {
                return m_Command != null &&
                    m_Connection != null;
            }
        }

        /// <summary>
        /// Pushes a failed upload file into database
        /// This is a no-op if file already exists.
        /// </summary>
        /// <param name="file">failed file</param>
        public void PushFailed(string file)
        {
            if (!IsValid)
                return;

            lock(this)
            {
                m_Command.CommandText = string.Format("INSERT OR IGNORE INTO failed (path) VALUES ('{0}')", file);
                try { m_Command.ExecuteNonQuery(); }
                catch (InvalidOperationException) { Dispose(); }
            }
        }

        /// <summary>
        /// Pushes a successfully uploaded file into database
        /// Also removes the file from "failed" table if it exists
        /// </summary>
        /// <param name="file">succeeded file</param>
        public void PushSucceed(string file)
        {
            if (!IsValid)
                return;

            lock (this)
            {
                string query1 = string.Format("DELETE FROM failed WHERE path='{0}'", file);
                string query2 = string.Format("INSERT OR IGNORE INTO succeed (path) VALUES ('{0}')", file);
                m_Command.CommandText = string.Format("{0};{1}", query1, query2);
                try { m_Command.ExecuteNonQuery(); }
                catch(InvalidOperationException) { Dispose(); }
            }
        }

        /// <summary>
        /// Returns (pops) last failed files.
        /// </summary>
        /// <param name="files">container to accepts path to failed files</param>
        /// <param name="count">max nmber of files to pop.</param>
        public void PopFailed(out List<string> files, uint count)
        {
            files = new List<string>();

            if (!IsValid)
                return;

            lock (this)
            {
                m_Command.CommandText = string.Format("SELECT path FROM failed LIMIT {0}", count);
                try
                {
                    using (IDataReader reader = m_Command.ExecuteReader())
                    {
                        while (reader.Read()) files.Add(reader.GetString(0));
                    }
                }
                catch(InvalidOperationException) { Dispose(); }
            }
        }

        #region IDisposable Support
        private bool m_Disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!m_Disposed)
            {
                if (disposing)
                {
                    Trace.TraceWarning("Disposing the database.");
                    if (m_Connection != null) m_Connection.Dispose();
                    if (m_Command != null) m_Command.Dispose();
                }

                m_Connection = null;
                m_Command = null;
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
