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
        private uint            m_FailedLimit = 5;

        public Database(uint fail_limit)
        {
            m_FailedLimit = fail_limit;

            try
            {
                m_Connection = new SqliteConnection("URI=file:swag.db");
                m_Connection.Open();
            }
            catch(DllNotFoundException)
            {
                Trace.TraceError("Database connection failed: Sqlite3 not found.");
                m_Connection = null;
                return;
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

                m_Command.CommandText = "CREATE TABLE IF NOT EXISTS failed (id INTEGER PRIMARY KEY, attempts INTEGER DEFAULT 0, path VARCHAR(4096) UNIQUE)";
                m_Command.ExecuteNonQuery();

                m_Command.CommandText = "CREATE TABLE IF NOT EXISTS succeed (id INTEGER PRIMARY KEY, path VARCHAR(4096) UNIQUE)";
                m_Command.ExecuteNonQuery();
            }
            catch (InvalidOperationException ex)
            {
                Trace.TraceError("Connection to database could not be established: ", ex.Message);
                if (m_Connection != null) m_Connection.Dispose();
                if (m_Command != null) m_Command.Dispose();
                m_Connection = null;
                m_Command = null;
            }
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
                m_Command.Parameters.Clear();
                m_Command.CommandText = "INSERT OR IGNORE INTO failed (path) VALUES (@file); UPDATE failed SET attempts=attempts+1 WHERE path=@file";
                m_Command.Parameters.Add(new SqliteParameter { ParameterName = "@file", Value = file });
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
                m_Command.Parameters.Clear();
                m_Command.CommandText = "DELETE FROM failed WHERE path=@file;INSERT OR IGNORE INTO succeed (path) VALUES (@file)";
                m_Command.Parameters.Add(new SqliteParameter { ParameterName = "@file", Value = file });
                try { m_Command.ExecuteNonQuery(); }
                catch(InvalidOperationException) { Dispose(); }
            }
        }

        /// <summary>
        /// Removes a file from both succeed and failed tables.
        /// </summary>
        /// <param name="file">desired file</param>
        public void Remove(string file)
        {
            if (!IsValid)
                return;

            lock (this)
            {
                m_Command.Parameters.Clear();
                m_Command.CommandText = "DELETE FROM failed WHERE path=@file; DELETE FROM succeed WHERE path=@file";
                m_Command.Parameters.Add(new SqliteParameter { ParameterName = "@file", Value = file });
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
                m_Command.Parameters.Clear();
                m_Command.CommandText = "SELECT path FROM failed WHERE attempts < @limit LIMIT @count";
                m_Command.Parameters.Add(new SqliteParameter { ParameterName = "@count", Value = count });
                m_Command.Parameters.Add(new SqliteParameter { ParameterName = "@limit", Value = m_FailedLimit });
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

        /// <summary>
        /// Pops all files from both succeed and failed tables
        /// </summary>
        /// <param name="files">container to accepts path to popped files</param>
        public void PopAll(out List<string> files)
        {
            files = new List<string>();

            if (!IsValid)
                return;

            lock (this)
            {
                m_Command.Parameters.Clear();
                m_Command.CommandText = "SELECT path FROM failed UNION ALL SELECT path FROM succeed";
                try
                {
                    using (IDataReader reader = m_Command.ExecuteReader())
                    {
                        while (reader.Read()) files.Add(reader.GetString(0));
                    }
                }
                catch (InvalidOperationException) { Dispose(); }
            }
        }

        /// <summary>
        /// Checks to see if a file exists in database
        /// Either in failed or succeed tables
        /// </summary>
        /// <param name="file">file to check</param>
        /// <returns>true on found</returns>
        public bool Exists(string file)
        {
            if (!IsValid)
                return false;

            lock (this)
            {
                m_Command.Parameters.Clear();
                m_Command.CommandText = "SELECT id FROM failed WHERE path=@file UNION ALL SELECT id FROM succeed WHERE path=@file";
                m_Command.Parameters.Add(new SqliteParameter { ParameterName = "@file", Value = file });
                try
                {
                    using (IDataReader reader = m_Command.ExecuteReader())
                    {
                        return reader.Read();
                    }
                }
                catch(InvalidOperationException) { Dispose(); }
            }

            return false;
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
