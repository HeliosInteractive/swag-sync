namespace swag
{
    using System;

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

            Greet(opts);
            return 0;
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

            if (options.SweepEnabled)
            {
                Console.WriteLine(string.Format("Sweep interval: {0}", options.SweepInterval));
                Console.WriteLine(string.Format("Sweep count: {0}", options.SweepCount));
            }
            else
                Console.WriteLine("Sweeping is disabled by command line");
        }
    }
}
