namespace swag
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public class TaskUtils
    {
        public static async Task<bool> RunTimed(Action action, TimeSpan timeout)
        {
            bool timedout = true;

            try
            {
                if (action == null)
                    return timedout;

                using (var token_source = new CancellationTokenSource())
                using (var timeout_task = Task.Delay(timeout, token_source.Token))
                using (var action_task = Task.Run(action, token_source.Token))
                using (var completed_task = await Task.WhenAny(timeout_task, action_task))
                {
                    if (completed_task == action_task)
                        timedout = true;

                    token_source.Cancel();

                    CleanupTasks(timeout_task, action_task, completed_task);
                }
            }
            catch(Exception ex)
            {
                Log.Error("Running timed task failed: {0}", ex.Message);
                timedout = true;
            }

            return timedout;
        }

        public static void CleanupTasks(params Task[] tasks)
        {
            if (tasks == null || tasks.Length == 0)
                return;

            foreach (Task task in tasks)
            {
                try { if (task != null && !task.IsCompleted) task.Wait(50); }
                catch { Log.Error("Unable to put Task out of its misery."); }
            }
        }
    }
}
