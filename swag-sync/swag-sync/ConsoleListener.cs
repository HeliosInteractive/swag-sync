using System;
using System.Diagnostics;

namespace swag
{
    class ConsoleListener : TraceListener
    {
        private static string now()
        {
            return DateTime.UtcNow.ToString();
        }
        public override void Write(string message)
        {
            lock(this)
            {
                Console.WriteLine("{0} | {1}", now(), message);
            }
        }

        public override void WriteLine(string message)
        {
            lock (this)
            {
                Console.WriteLine("{0} | {1}", now(), message);
            }
        }
    }
}
