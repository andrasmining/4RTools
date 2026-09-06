using System;
using System.Threading;
using System.Windows.Input;
using System.Windows.Forms;
using Newtonsoft.Json;
using _4RTools.Utils;

namespace _4RTools.Model
{
    public class AutoRefreshSpammer : Action
    {
        private _4RThread thread;
        public string ActionName { get; set; }
        public int RefreshDelay { get; set; } = 5;
        public Key RefreshKey { get; set; }

        public AutoRefreshSpammer(string actionName)
        {
            this.ActionName = actionName;
        }

        public void Start()
        {
            Stop();
            Client roClient = ClientSingleton.GetClient();
            if (roClient != null)
            {
                const int defaultDelayInSeconds = 1000;
                int delayInSeconds = this.RefreshDelay * 1000;
                int delay = delayInSeconds == 0 ? defaultDelayInSeconds : delayInSeconds;
                this.thread = new _4RThread(_ => AutorefreshThreadExecution(roClient, delay), roClient.HandleWorkerFailure);
                _4RThread.Start(this.thread);
            }
        }

        private int AutorefreshThreadExecution(Client roClient, int delay)
        {
            if (this.RefreshKey != Key.None)
            {
                roClient.EnsureAutomaticActionsReady();
                Interop.PostMessage(roClient, roClient.process.MainWindowHandle, Constants.WM_KEYDOWN_MSG_ID, (Keys)Enum.Parse(typeof(Keys), this.RefreshKey.ToString()), 0, true);
            }
            Thread.Sleep(delay);
            return 0;
        }

        public void Stop()
        {
            _4RThread.Stop(this.thread);
        }

        public string GetConfiguration()
        {
            return JsonConvert.SerializeObject(this);
        }

        public string GetActionName()
        {
            return this.ActionName;
        }
    }
}
