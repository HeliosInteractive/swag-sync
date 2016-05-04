namespace swag
{
    using System.Diagnostics;
    using System.Net.NetworkInformation;

    /// <summary>
    /// Serves as a periodic Internet connectivity checker.
    /// Use it like: Internet.IsUp
    /// </summary>
    public class InternetService : Service
    {
        private bool m_IsUp = false;

        /// <summary>
        /// Returns whether Google (aka the Internet) is reachable.
        /// This actually returns a cached version of the last periodic
        /// check result we made. The actual check happens every "CheckPeriod"
        /// </summary>
        public bool IsUp
        {
            get
            {
                if (Period == 0)
                    return true;

                if (!Started)
                    Start();

                return m_IsUp;
            }
        }

        protected override void Run()
        {
            Trace.TraceInformation("Checking for Internet connectivity...");

            try
            {
                using (var ping = new Ping())
                {
                    m_IsUp = (ping.Send("8.8.8.8").Status == IPStatus.Success);
                }
            }
            catch
            {
                m_IsUp = false;
            }

            if (m_IsUp)
                Trace.TraceInformation("Internet connectivity check passed.");
            else
                Trace.TraceInformation("Internet connectivity check failed.");
        }
    }
}
