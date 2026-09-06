using System;
using System.ComponentModel;
using _4RTools.Utils;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using Newtonsoft.Json;

namespace _4RTools.Model
{

    public class Autopot : Action
    {

        public static string ACTION_NAME_AUTOPOT = "Autopot";
        public static string ACTION_NAME_AUTOPOT_YGG = "AutopotYgg";

        public Key hpKey { get; set; }
        public int hpPercent { get; set; }
        public Key spKey { get; set; }
        public int spPercent { get; set; }
        public int delay { get; set; } = 15;
        public int delayYgg { get; set; } = 50;

        public string actionName { get; set; }
        private _4RThread thread;

        public Autopot() { }
        public Autopot(string actionName)
        {
            this.actionName = actionName;
        }

        public Autopot(Key hpKey, int hpPercent, int delay, Key spKey, int spPercent)
        {
            this.delay = delay;

            // HP
            this.hpKey = hpKey;
            this.hpPercent = hpPercent;

            // SP
            this.spKey = spKey;
            this.spPercent = spPercent;
        }

        public void Start()
        {
            Stop();
            Client roClient = ClientSingleton.GetClient();
            if(roClient != null)
            {
                int hpPotCount = 0;
                this.thread = new _4RThread(_ => AutopotThreadExecution(roClient, hpPotCount), roClient.HandleWorkerFailure);
                _4RThread.Start(this.thread);
            }
        }

        private int AutopotThreadExecution(Client roClient, int hpPotCount)
        {
            // check hp first
            if (hpKey != Key.None && hpPercent > 0 && roClient.IsHpBelow(hpPercent))
            {
                pot(roClient, this.hpKey);
                hpPotCount++;

                if (hpPotCount == 3)
                {
                    hpPotCount = 0;
                    if (spKey != Key.None && spPercent > 0 && roClient.IsSpBelow(spPercent))
                    {
                        pot(roClient, this.spKey);
                    }
                }
            }
            // check sp
            if (spKey != Key.None && spPercent > 0 && roClient.IsSpBelow(spPercent))
            {
                pot(roClient, this.spKey);
            }

            Thread.Sleep(this.delay);
            return 0;
        }

        private void pot(Client client, Key key)
        {
            Keys k = (Keys)Enum.Parse(typeof(Keys), key.ToString());
            if ((k != Keys.None) && !Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt))
            {
                Interop.PostMessage(client, client.process.MainWindowHandle, Constants.WM_KEYDOWN_MSG_ID, k, 0, true); // keydown
                Interop.PostMessage(client, client.process.MainWindowHandle, Constants.WM_KEYUP_MSG_ID, k, 0, true); // keyup
            }
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
            return this.actionName != null ? this.actionName : ACTION_NAME_AUTOPOT;
        }
    }
}
