namespace swag
{
    using System;
    using System.Data;
    using Mono.Data.Sqlite;
    using System.Collections.Generic;

    public class Database : IDisposable
    {
        private IDbConnection   m_Connection;
        private IDbCommand      m_Command;

        public Database()
        {
            m_Connection = new SqliteConnection("URI=file:swag.db");
            m_Connection.Open();
            m_Command = m_Connection.CreateCommand();

            m_Command.CommandText = "CREATE TABLE IF NOT EXISTS failed (id INTEGER PRIMARY KEY, path VARCHAR(4096) UNIQUE)";
            m_Command.ExecuteNonQuery();

            m_Command.CommandText = "CREATE TABLE IF NOT EXISTS succeed (id INTEGER PRIMARY KEY, path VARCHAR(4096) UNIQUE)";
            m_Command.ExecuteNonQuery();
        }

        public void PushFailed(string file)
        {
            lock(this)
            {
                m_Command.CommandText = string.Format("INSERT OR IGNORE INTO failed (path) VALUES ('{0}')", file);
                m_Command.ExecuteNonQuery();
            }
        }

        public void PushSucceed(string file)
        {
            lock (this)
            {
                string query1 = string.Format("DELETE FROM failed WHERE path='{0}'", file);
                string query2 = string.Format("INSERT OR IGNORE INTO succeed (path) VALUES ('{0}')", file);
                m_Command.CommandText = string.Format("{0};{1}", query1, query2);
                m_Command.ExecuteNonQuery();
            }
        }

        public void PopFailed(out List<string> files, uint count)
        {
            files = new List<string>();

            m_Command.CommandText = string.Format("SELECT path FROM failed LIMIT {0}", count);
            using (IDataReader reader = m_Command.ExecuteReader())
            {
                files.Add(reader.GetString(0));
            }
        }

        #region IDisposable Support
        private bool m_DisposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!m_DisposedValue)
            {
                if (disposing)
                {
                    m_Connection.Dispose();
                    m_Command.Dispose();
                }

                m_DisposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
