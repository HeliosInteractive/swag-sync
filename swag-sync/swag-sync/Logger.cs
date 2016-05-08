namespace swag
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// A simple console logger with log levels
    /// </summary>
    class Log
    {
        private static ConsoleLogger s_Source;

        public static void Setup(Options opts)
        {
            if (s_Source != null)
                return;

            SourceLevels level = SourceLevels.Critical;

            if (opts.LogVerbosity.StartsWith("info"))
                level = SourceLevels.Information;
            else if (opts.LogVerbosity.StartsWith("warn"))
                level = SourceLevels.Warning;
            else if (opts.LogVerbosity.StartsWith("error"))
                level = SourceLevels.Error;

            s_Source = new ConsoleLogger(level);
        }

        public static void Error(string msg)
        {
            lock(s_Source)
            {
                if (s_Source == null)
                    return;

                s_Source.Write(SourceLevels.Error, msg);
            }
        }

        public static void Error(string fmt, params object[] args)
        {
            Error(string.Format(fmt, args));
        }

        public static void Warn(string msg)
        {
            lock (s_Source)
            {
                if (s_Source == null)
                    return;

                s_Source.Write(SourceLevels.Warning, msg);
            }
        }

        public static void Warn(string fmt, params object[] args)
        {
            Warn(string.Format(fmt, args));
        }

        public static void Info(string msg)
        {
            lock(s_Source)
            {
                if (s_Source == null)
                    return;

                s_Source.Write(SourceLevels.Information, msg);
            }
        }

        public static void Info(string fmt, params object[] args)
        {
            Info(string.Format(fmt, args));
        }

        public static void Write(string msg)
        {
            lock(s_Source)
            {
                if (s_Source == null)
                    return;

                s_Source.Write(SourceLevels.Critical, msg);
            }
        }

        public static void Write(string fmt, params object[] args)
        {
            Write(string.Format(fmt, args));
        }
    }

    internal class ConsoleLogger
    {
        SourceLevels m_SourceLevel = SourceLevels.Critical;

        public ConsoleLogger(SourceLevels lvl)
        {
            m_SourceLevel = lvl;
        }

        private static string now()
        {
            return DateTime.UtcNow.ToString();
        }

        public void Write(SourceLevels lvl, string message)
        {
            if (lvl > m_SourceLevel)
                return;

            lock (this)
            {
                Console.WriteLine("{0} | {1,-11} | {2}", now(), lvl.ToString().ToLower(), message);
            }
        }
    }
}
