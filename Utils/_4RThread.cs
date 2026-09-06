using System;
using System.Threading;

namespace _4RTools.Utils
{
    public class _4RThread
    {
        private Thread thread;
        private volatile bool stopRequested;
        [ThreadStatic] private static _4RThread currentWorker;

        // A stopped callback stays cancelled even if its feature starts a replacement worker.
        public static bool IsCancellationRequested { get { return currentWorker != null && currentWorker.stopRequested; } }
        public static void ThrowIfCancellationRequested()
        {
            if (IsCancellationRequested) throw new OperationCanceledException("The stock feature worker was stopped.");
        }


        public _4RThread(Func<int, int> toRun, Func<Exception, bool> stopAfterFailure = null)
        {
            if (toRun == null) throw new ArgumentNullException(nameof(toRun));
            this.thread = new Thread(() =>
            {
                currentWorker = this;
                try
                {
                    while (!stopRequested)
                    {
                        try
                        {
                            toRun(0);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (ThreadInterruptedException) when (stopRequested) { break; }
                        catch (Exception) when (stopRequested) { break; }
                        catch (Exception ex)
                        {
                            if (stopAfterFailure != null && stopAfterFailure(ex)) break;
                            Console.WriteLine("[4RThread Exception] Error while Executing Thread Method ==== " + ex.Message);
                        }
                        if (stopRequested) break;
                        try { Thread.Sleep(5); }
                        catch (ThreadInterruptedException) when (stopRequested) { break; }
                    }
                }
                finally { currentWorker = null; }
            });
            this.thread.IsBackground = true;
            this.thread.SetApartmentState(ApartmentState.STA);
        }

        public static void Start(_4RThread _4RThread)
        {
            if (_4RThread == null) throw new ArgumentNullException(nameof(_4RThread));
            _4RThread.thread.Start();
        }

        public static void Stop(_4RThread _4RThread)
        {
            if (_4RThread == null) return;
            _4RThread.stopRequested = true;
            if (!_4RThread.thread.IsAlive) return;
            try { _4RThread.thread.Interrupt(); }
            catch (ThreadStateException) { /* The worker completed between IsAlive and Interrupt. */ }
        }
    }
}
