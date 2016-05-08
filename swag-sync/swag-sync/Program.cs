namespace swag
{
    using System;
    using System.IO;
    using System.Security;
    using System.Threading;
    using System.Diagnostics;
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

            Log.Setup(opts);

            if (!CheckEnvironmentVariables(ref opts))
                return 1;

            return Run(opts);
        }

        static bool CheckEnvironmentVariables(ref Options opts)
        {
            string access_key = string.Empty;
            string secret_key = string.Empty;

            try
            {
                access_key = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
                secret_key = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
            }
            catch (SecurityException ex)
            {
                Log.Error("Unable to access environment variables: {0}", ex.Message);
                return false;
            }

            if (string.IsNullOrWhiteSpace(access_key) ||
                string.IsNullOrWhiteSpace(secret_key))
            {
                Log.Error("You need to define {0} and {1} environment variables!",
                    "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY");
                return false;
            }

            opts.AwsAccessKey = access_key;
            opts.AwsSecretKey = secret_key;

            return true;
        }

        static int Run(Options opts)
        {
            Greet(opts);

            List<Bucket> buckets = new List<Bucket>();

            using (InternetService internet = new InternetService())
            {
                try
                {
                    foreach (string bucket_path in Directory.GetDirectories(opts.RootDirectory))
                        buckets.Add(new Bucket(bucket_path, opts, internet));
                }
                catch (Exception ex)
                {
                    Log.Error("Unable to read bucket directories: {0}", ex.Message);
                    return 1;
                }

                if (opts.SweepOnce)
                {
                    Log.Info("About to sweep...");
                    buckets.ForEach(b => { b.Sweep(); });
                    buckets.ForEach(b => { b.FinishPendingTasks(); });
                }
                else
                {
                    Log.Info("About to watch...");

                    using (Database db = new Database(opts.FailLimit))
                    using (ManualResetEvent quit_event = new ManualResetEvent(false))
                    using (DatabaseService db_service = new DatabaseService(db, opts))
                    using (SynchronizeService sync_service = new SynchronizeService(internet, buckets, db, opts))
                    {
                        internet.Period = opts.PingInterval;

                        buckets.ForEach(b =>
                        {
                            b.OnFileUploaded += f => db.PushSucceed(f);
                            b.OnFileFailed += f => db.PushFailed(f);
                            b.SetupWatcher();
                            b.Sweep(db);
                        });

                        if (opts.CleanEnabled)
                        {
                            db_service.Period = opts.CleanInterval;
                            db_service.Start();
                        }

                        if (opts.SweepEnabled)
                        {
                            sync_service.Period = opts.SweepInterval;
                            sync_service.Start();
                        }

                        Console.CancelKeyPress += (sender, eArgs) =>
                        {
                            try
                            {
                                quit_event.Set();
                                eArgs.Cancel = true;
                            }
                            catch
                            {
                                Process.GetCurrentProcess().Kill();
                            }
                        };

                        quit_event.WaitOne();
                    }
                }
            }

            foreach (Bucket bucket in buckets)
                bucket.Dispose();

            return 0;
        }
        
        static void Greet(Options options)
        {
            string greetings =
                "\n" +
                @" _____      ____ _  __ _   ___ _   _ _ __   ___ " + Environment.NewLine +
                @"/ __\ \ /\ / / _` |/ _` | / __| | | | '_ \ / __|" + Environment.NewLine +
                @"\__ \\ V  V / (_| | (_| | \__ \ |_| | | | | (__ " + Environment.NewLine +
                @"|___/ \_/\_/ \__,_|\__, | |___/\__, |_| |_|\___|" + Environment.NewLine +
                @"                   |___/       |___/            " + Environment.NewLine ;

            Console.WriteLine(greetings);
            Console.WriteLine("Press Ctrl+C to exit.");
            Console.WriteLine("---------------------");
            Console.WriteLine(string.Format("Watching root directory: {0}", options.RootDirectory));
            Console.WriteLine(string.Format("Sweep interval:          {0} (s)", options.SweepInterval));
            Console.WriteLine(string.Format("Sweep count:             {0}", options.SweepCount));
            Console.WriteLine(string.Format("Bucket upload timeout:   {0} (s)", options.Timeout));
            Console.WriteLine(string.Format("Maximum failed limit:    {0}", options.FailLimit));
            Console.WriteLine(string.Format("Ping time interval:      {0} (s)", options.PingInterval));
            Console.WriteLine(string.Format("Maximum bucket uploads:  {0}", options.BucketMax));
            Console.WriteLine("---------------------");
        }
    }
}
