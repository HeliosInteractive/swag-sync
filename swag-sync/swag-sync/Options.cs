namespace swag
{
    using System;
    using System.IO;
    using Mono.Options;

    /// <summary>
    /// Encapsulates command line options which can be provided
    /// to swag-sync. Use Parse to grab options from command.
    /// </summary>
    public class Options
    {
        /// <summary>
        /// Pass args[] from Main to this so it can parse args
        /// </summary>
        /// <param name="args">args[] passed to Main</param>
        /// <returns>parsed options (or their default values)</returns>
        public static Options Parse(string[] args)
        {
            Options opts = new Options();

            OptionSet set = new OptionSet()
            {
                "Usage: swag-sync [OPTIONS]",
                "Synchronizes given directory with an S3 directory.",
                "",
                "Options:\n",
                {
                    "i|interval=",
                    "the number of seconds for upload interval." +
                    "this must be an unsigned integer (number)."+
                    "specify zero to disable sweeping.",
                    (uint v) => { opts.m_SweepInterval = v; }
                },
                {
                    "c|count=",
                    "the number files to pop in every sweep PER BUCKET." +
                    "this must be an unsigned integer (number)."+
                    "specify zero to disable sweeping.",
                    (uint v) => { opts.m_SweepCount = v; }
                },
                {
                    "r|root=",
                    "The root directory to watch." +
                    "Sub directories will be used as bucket names.",
                    (string v) => { opts.m_RootDirectory = v; }
                },
                {
                    "t|timeout",
                    "Timeout in seconds for upload operations." +
                    "this must be an unsigned integer (number).",
                    (uint v) => { opts.m_Timeout = v; }
                },
                {
                    "s|sweep",
                    "Sweep once and quit (Ignores database).",
                    v => { opts.m_SweepOnce = (v != null); }
                },
                {
                    "h|help",
                    "show this message and exit",
                    v => { opts.m_ShowHelp = (v != null); }
                },
            };

            try
            {
                set.Parse(args);
            }
            catch (OptionException ex)
            {
                Console.WriteLine(string.Format("Invalid options supplied: {0}", ex.Message));
                Console.WriteLine("Try: swag-sync --help");
                opts.m_ShowHelp = true;
            }

            if (opts.m_ShowHelp || string.IsNullOrWhiteSpace(opts.m_RootDirectory))
            {
                set.WriteOptionDescriptions(Console.Out);
                opts.m_ShowHelp = true;
            }

            return opts;
        }

        /// <summary>
        /// Returns true if options are valid.
        /// Specifically if root directory exists.
        /// </summary>
        public bool IsValid
        {
            get
            {
                // note that this returns false if root is not read-able
                return Directory.Exists(m_RootDirectory);
            }
        }

        /// <summary>
        /// Path to the root directory to watch
        /// or none if invalid path supplied
        /// </summary>
        public string RootDirectory
        {
            get
            {
                if (IsValid)
                    return m_RootDirectory;
                else
                    return string.Empty;
            }
        }

        /// <summary>
        /// Number of seconds to wait before waking up
        /// the up-loader thread to upload failed files
        /// </summary>
        public uint SweepInterval
        {
            get
            {
                return m_SweepInterval;
            }
        }

        /// <summary>
        /// Number of files to upload in every sweep attempt
        /// </summary>
        public uint SweepCount
        {
            get
            {
                return m_SweepCount;
            }
        }

        /// <summary>
        /// Returns true if weeping is enabled
        /// </summary>
        public bool SweepEnabled
        {
            get
            {
                return SweepCount > 0 && SweepInterval > 0;
            }
        }

        /// <summary>
        /// returns true if a help box has already been shown
        /// </summary>
        public bool ShowHelp
        {
            get
            {
                return m_ShowHelp;
            }
        }

        /// <summary>
        /// Should we sweep everything and exit?
        /// </summary>
        public bool SweepOnce
        {
            get
            {
                return m_SweepOnce;
            }
        }

        /// <summary>
        /// Timeout in seconds before we give up on uploading
        /// </summary>
        public uint Timeout
        {
            get
            {
                return m_Timeout;
            }
        }

        private string  m_RootDirectory = "";
        private uint    m_SweepInterval = 10;
        private uint    m_SweepCount    = 10;
        private uint    m_Timeout       = 10;
        private bool    m_ShowHelp      = false;
        private bool    m_SweepOnce     = false;
    }
}
