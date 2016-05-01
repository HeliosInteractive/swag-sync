﻿namespace swag
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Security;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    
    class Program
    {
        static int Main(string[] args)
        {
            Options opts = Options.Parse(args);

            if (!opts.IsValid)
            {
                if (!opts.ShowHelp)
                {
                    Console.WriteLine("Invalid options supplied.");
                    Console.WriteLine("Try: swag-sync --help");
                }

                return 1;
            }

            Trace.Listeners.Add(new ConsoleTraceListener());

            string access_key = string.Empty;
            string secret_key = string.Empty;

            try
            {
                access_key = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
                secret_key = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
            }
            catch(SecurityException ex)
            {
                Trace.TraceError("Unable to access environment variables: {0}", ex.Message);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(access_key) ||
                string.IsNullOrWhiteSpace(secret_key))
            {
                Trace.TraceError("You need to define {0} and {1} environment variables!",
                    "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY");
                return 1;
            }

            Greet(opts);
            Run(opts);
            return 0;
        }

        static void Run(Options opts)
        {
            List<Bucket> buckets = new List<Bucket>();

            foreach (string bucket_path in Directory.GetDirectories(opts.RootDirectory))
                buckets.Add(new Bucket(bucket_path, opts.Timeout, opts.BucketMax));

            if (opts.SweepOnce)
            {
                Trace.TraceInformation("About to sweep...");
                buckets.ForEach(b => { b.Sweep(); });
                buckets.ForEach(b => { b.FinishPendingTasks(); });
                return;
            }
            else
            {
                Database db = new Database(opts.FailLimit);
                Trace.TraceInformation("About to watch...");
                buckets.ForEach(b =>
                {
                    b.Sweep(db);
                    b.OnFileUploaded += f => db.PushSucceed(f);
                    b.OnFileFailed += f => db.PushFailed(f);
                    b.SetupWatcher();
                });
                UploadFailedFiles(opts, db, buckets);
            }
        }

        static void UploadFailedFiles(Options opts, Database db, List<Bucket> buckets)
        {
            Trace.TraceInformation("Checking for failed files...");
            buckets.ForEach(b => { b.Sweep(db); });

            List<string> failed_files;
            db.PopFailed(out failed_files, opts.SweepCount);

            failed_files.ForEach(file =>
            {
                string bucket_name = file
                    .Replace(opts.RootDirectory, string.Empty)
                    .Trim(Path.DirectorySeparatorChar)
                    .Split(Path.DirectorySeparatorChar)
                    .First();

                Bucket bucket = buckets.Find(b => b.BucketName == bucket_name);
                if (bucket != null) bucket.Upload(file);
            });

            Task
                .Delay((int)opts.SweepInterval * 1000)
                .ContinueWith(task => { UploadFailedFiles(opts, db, buckets); })
                .Wait();
        }

        static void Greet(Options options)
        {
            const string greetings =
                "\n" +
                "███████╗██╗    ██╗ █████╗  ██████╗       ███████╗██╗   ██╗███╗   ██╗ ██████╗\n" +
                "██╔════╝██║    ██║██╔══██╗██╔════╝       ██╔════╝╚██╗ ██╔╝████╗  ██║██╔════╝\n" +
                "███████╗██║ █╗ ██║███████║██║  ███╗█████╗███████╗ ╚████╔╝ ██╔██╗ ██║██║     \n" +
                "╚════██║██║███╗██║██╔══██║██║   ██║╚════╝╚════██║  ╚██╔╝  ██║╚██╗██║██║     \n" +
                "███████║╚███╔███╔╝██║  ██║╚██████╔╝      ███████║   ██║   ██║ ╚████║╚██████╗\n" +
                "╚══════╝ ╚══╝╚══╝ ╚═╝  ╚═╝ ╚═════╝       ╚══════╝   ╚═╝   ╚═╝  ╚═══╝ ╚═════╝\n";
            Console.WriteLine(greetings);
            Console.WriteLine(string.Format("Watching root directory: {0}", options.RootDirectory));
            Console.WriteLine(string.Format("Maximum bucket uploads:  {0}", options.BucketMax));
            Console.WriteLine(string.Format("Bucket upload timeout:   {0} (s)", options.Timeout));
            Console.WriteLine(string.Format("Maximum failed limit:    {0}", options.FailLimit));

            if (options.SweepEnabled)
            {
                Console.WriteLine(string.Format("Sweep interval:          {0} (s)", options.SweepInterval));
                Console.WriteLine(string.Format("Sweep count:             {0}", options.SweepCount));
            }
            else
                Console.WriteLine("Sweeping is disabled by command line");
        }
    }
}
