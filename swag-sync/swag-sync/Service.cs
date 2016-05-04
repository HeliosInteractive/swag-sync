namespace swag
{
    using System;
    using System.Threading;
    using System.Reactive.Linq;

    public abstract class Service : IDisposable
    {
        CancellationTokenSource m_CancelSource;
        IObservable<long>       m_Interval;
        TimeSpan                m_Period;

        public Service()
        {
            m_Period = TimeSpan.FromSeconds(DefaultPeriod);
        }

        /// <summary>
        /// Default period time, passed if you never
        /// change it via "Period"
        /// </summary>
        public static uint DefaultPeriod
        {
            get { return 10; }
        }

        /// <summary>
        /// Time in-between executions of Run()
        /// passing 0 calls Stop()
        /// </summary>
        public uint Period
        {
            get { return (uint)m_Period.Seconds; }
            set
            {
                if (value == 0)
                {
                    Stop();
                    return;
                }

                m_Period = TimeSpan.FromSeconds(value);
            }
        }

        /// <summary>
        /// Checks to see if this instance is disposed
        /// </summary>
        public bool Disposed
        {
            get { return m_Disposed; }
        }

        /// <summary>
        /// Checks to see if underlying task has started
        /// </summary>
        public bool Started
        {
            get { return m_Interval != null; }
        }

        /// <summary>
        /// Implement this method, this is where you put your
        /// periodic logic to be executed.
        /// NOTE: this runs in a separate thread
        /// </summary>
        protected abstract void Run();

        /// <summary>
        /// Call this to start a recursivly calling Run()
        /// every "Period". You can use Stop() to stop,
        /// </summary>
        public void Start()
        {
            if (Disposed)
                throw new ObjectDisposedException("Service");

            if (Started)
                Stop();

            m_Interval = Observable.Interval(m_Period);
            m_CancelSource = new CancellationTokenSource();
            m_Interval.Subscribe(status => { Run(); }, m_CancelSource.Token);
        }

        /// <summary>
        /// Stops what has been started by Start();
        /// </summary>
        public void Stop()
        {
            if (Disposed)
                throw new ObjectDisposedException("Service");

            try
            {
                if (m_CancelSource != null)
                    m_CancelSource.Cancel();
            }
            catch (ObjectDisposedException) { /* no-op */ };

            if (m_Interval != null)
                m_Interval.Wait();

            m_CancelSource = null;
            m_Interval = null;
        }

        #region IDisposable Support
        private bool m_Disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!m_Disposed)
            {
                if (disposing)
                    Stop();

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
