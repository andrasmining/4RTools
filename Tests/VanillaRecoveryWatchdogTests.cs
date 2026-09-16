using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using _4RTools.Model.Vanilla;

namespace Vanilla.Diagnostics.Tests
{
    internal static class VanillaRecoveryWatchdogTests
    {
        private static int passed, failed;
        private static readonly DateTimeOffset Epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly Guid Session = Guid.NewGuid();
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        internal static int Run()
        {
            Test("Unchanged coordinates expire at exactly 120 seconds", Deadline);
            Test("X-only movement resets the deadline", () => Axis(true));
            Test("Y-only movement resets the deadline", () => Axis(false));
            Test("Zero is a valid coordinate, not an unavailable reading", Zero);
            Test("Missing coordinates expire without a popup", Missing);
            Test("Unreadable coordinates expire without inventing zero", Invalid);
            Test("Unverified coordinates cannot reset the watchdog", Unverified);
            Test("First late baseline is not movement", LateBaseline);
            Test("Stale and future readings cannot establish movement", Stale);
            Test("Repeated timestamps cannot authorize movement", Cached);
            Test("Intermediate movement survives a return to the same position", Intermediate);
            Test("First observed intermediate movement resets the deadline", FirstIntermediate);
            Test("Fleet tracks intermediate movement without a readiness field", FleetMovement);
            Test("Fleet does not infer motion across an invalid observation", FleetInvalidMovement);
            Test("Map and session replacement start new baselines", Context);
            Test("Unknown map flapping cannot hide stationary coordinates", UnknownMap);
            Test("Process replacement resets the deadline", ProcessChange);
            Test("Monotonic rollback resets safely", ClockRollback);
            Test("Explicit watchdog reset clears the old deadline", Reset);
            Test("Another client's movement cannot satisfy this client", Isolation);
            Test("Healthy sibling remains untouched by a position restart", OneClient);
            Test("Unavailable coordinates restart after 120 seconds without popup detection", UnreadableRestart);
            Test("Both positional failures restart sequentially through the full lease", BothPosition);
            Test("Mixed dialog and position failures use one shared recovery lease", Mixed);
            Test("Movement while queued cancels the stale restart decision", QueuedMovement);
            Test("STOP cancels queued close and prevents late completion", Stop);
            Test("Settings changes cancel queued close", Settings);
            Test("STOP at the owned close boundary sends no close", StopAtBoundary);
            Test("Replacement PID cannot be closed by an old worker", ReplacedPid);
            Test("Replacement operation cannot be closed by an old worker", ReplacedOperation);
            Test("Close failure retains the PID and exponential backoff", CloseFailure);
            Test("Recovery OFF disables position restarts", RecoveryOff);
            Test("Visual watchdog OFF does not disable the position watchdog", VisualOff);
            Test("Startup and recovery do not accrue movement timeouts", StartupGrace);
            Test("One terminal observation does not close a client", OneTerminal);
            Test("Unknown modal is never dismissed or closed as a disconnect", UnknownModal);
            Test("Changing terminal messages need new confirmation", ChangedTerminal);
            Test("Stale terminal observations need new confirmation", StaleTerminal);
            for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++)
            { int aa = a, bb = b; Test("Both terminal dialogs are serialized " + a + "/" + b, () => BothTerminal(aa, bb)); }
            Test("Terminal dialog present before START is closed under the startup gate", ColdStart);
            Test("Cold-start changed confirmation sends no close", ColdStartChanged);
            Test("Normal close confirms exit without termination", NormalClose);
            Test("Unresponsive close has one bounded termination attempt", ForcedClose);
            Test("Failed process exit is not successful recovery", CloseTimeout);
            Test("STOP during close wait prevents termination", StopDuringClose);
            Test("Unknown exit observation cannot become successful exit", UnknownExit);
            Test("Close permission denial does not try another access path", CloseDenied);
            Test("Process close requests no memory-write or injection rights", CloseRights);
            Test("Position cache clears only the confirmed exited client's reader", FleetCache);
            foreach (string name in new[] { "LoggingOut", "Disconnected" })
                foreach (float scale in new[] { .8f, 1f, 1.25f, 1.5f, 1.75f, 2f })
                { string n = name; float s = scale; Test("Terminal image " + n + " scale " + s, () => ImageCase(n, s)); }
            Test("Uniform captures are unknown rather than gameplay", BlankImage);
            Test("Wrong or partial popup text is not a terminal match", WrongImage);
            Console.WriteLine("Recovery watchdog: {0} passed; {1} failed. Fake time/processes and cropped dialog images only.", passed, failed);
            return failed;
        }
        private static VanillaPositionSample S(double sec, int x = 10, int y = 20, int pid = 101, Guid? session = null,
            string map = "map", bool verified = true, string error = null, double? moved = null)
        { return new VanillaPositionSample(pid, session ?? Session, Epoch.AddSeconds(sec), x, y, map, verified, error, moved.HasValue ? (DateTimeOffset?)Epoch.AddSeconds(moved.Value) : null); }
        private static string Check(VanillaMovementWatchdog w, double sec, VanillaPositionSample s, int pid = 101)
        { return w.Observe(pid, s, TimeSpan.FromSeconds(sec), Epoch.AddSeconds(sec)); }
        private static void Deadline()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); Assert(Check(w, 119.999, S(119.999)) == null); Assert(Check(w, 120, S(120)) != null); }
        private static void Axis(bool x)
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); Check(w, 119, S(119, x ? 11 : 10, x ? 20 : 21)); Assert(Check(w, 238, S(238, x ? 11 : 10, x ? 20 : 21)) == null); Assert(Check(w, 239, S(239, x ? 11 : 10, x ? 20 : 21)) != null); }
        private static void Zero()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0, 0, 0)); Assert(Check(w, 119, S(119, 0, 1)) == null); Assert(Check(w, 120, S(120, 0, 1)) == null); }
        private static void Missing()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, null); Assert(Check(w, 119, null) == null); Assert(Check(w, 120, null).Contains("unavailable")); }
        private static void Invalid()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); Check(w, 90, new VanillaPositionSample(101, Session, Epoch.AddSeconds(90), null, null, null, false, "read failed")); Assert(Check(w, 120, S(120)) != null); }
        private static void Unverified()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); Assert(Check(w, 120, S(120, 99, 99, verified: false)) != null); }
        private static void LateBaseline()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, null); Check(w, 119, S(119)); Assert(Check(w, 120, S(120)) != null); }
        private static void Stale()
        { foreach (double stamp in new[] { 1.0, 121.0 }) { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); Assert(Check(w, 120, S(stamp, 99, 99)) != null); } }
        private static void Cached()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); Check(w, 1, S(0, 99, 99)); Assert(Check(w, 120, S(120)) != null); }
        private static void Intermediate()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0, moved: 0)); Check(w, 90, S(90, moved: 89)); Assert(Check(w, 120, S(120, moved: 89)) == null); Assert(Check(w, 210, S(210, moved: 89)) != null); }
        private static void FirstIntermediate()
        {
            var w = new VanillaMovementWatchdog(); Check(w, 0, S(0));
            Check(w, 1, S(1, moved: .5));
            Assert(Check(w, 120, S(120, moved: .5)) == null, "First intermediate motion was lost.");
            Assert(Check(w, 121, S(121, moved: .5)) != null, "Motion kept extending without another move.");
        }
        private static void FleetMovement()
        {
            var reader = new Reader { Pid = 101 };
            using (var fleet = new VanillaFleetMonitor(Path.GetTempPath(),
                () => new VanillaFleetMonitor.IProcessMetadata[] { new Meta { ProcessId = 101 } }, pid => reader, () => null))
            {
                fleet.Poll(); var w = new VanillaMovementWatchdog(); Check(w, 0, fleet.LatestPosition(101));
                reader.Frame = S(.5, 11); fleet.Poll();
                reader.Frame = S(1, 10); fleet.Poll();
                var returned = fleet.LatestPosition(101);
                Assert(returned.MovementAt.HasValue && returned.MovementAt.Value == Epoch.AddSeconds(1),
                    "Verified X/Y motion depended on unavailable ClientReady or was lost between polls.");
                Check(w, 1, returned);
                Assert(Check(w, 120, S(120, moved: 1)) == null, "Healthy movement-and-return caused restart.");
            }
        }
        private static void FleetInvalidMovement()
        {
            var reader = new Reader { Pid = 101 };
            using (var fleet = new VanillaFleetMonitor(Path.GetTempPath(),
                () => new VanillaFleetMonitor.IProcessMetadata[] { new Meta { ProcessId = 101 } }, pid => reader, () => null))
            {
                fleet.Poll(); reader.Frame = S(.5, 11, verified: false); fleet.Poll();
                reader.Frame = S(1, 10); fleet.Poll();
                Assert(!fleet.LatestPosition(101).MovementAt.HasValue, "Unverified position fabricated motion.");
            }
        }
        private static void Context()
        { foreach (bool newSession in new[] { false, true }) { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); Guid g = newSession ? Guid.NewGuid() : Session; string m = newSession ? "map" : "map2"; Check(w, 119, S(119, session: g, map: m)); Assert(Check(w, 120, S(120, session: g, map: m)) == null); } }
        private static void UnknownMap()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); Check(w, 60, S(60, map: null)); Check(w, 119, S(119)); Assert(Check(w, 120, S(120)) != null); }
        private static void ProcessChange()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); Assert(Check(w, 120, S(120, pid: 102), 102) == null); }
        private static void ClockRollback()
        { var w = new VanillaMovementWatchdog(); Check(w, 50, S(50)); Assert(Check(w, 1, S(1)) == null); }
        private static void Reset()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); w.Reset(); Assert(Check(w, 120, S(120)) == null); }
        private static void Isolation()
        { var w = new VanillaMovementWatchdog(); Check(w, 0, S(0)); Assert(Check(w, 120, S(120, 99, 99, 102)) != null); }

        private sealed class Env : IVanillaRecoveryRestartEnvironment
        {
            internal double Seconds;
            internal bool FailClose;
            internal Action BeforeClose;
            internal readonly Queue<Action> Work = new Queue<Action>();
            internal readonly List<int> Closed = new List<int>();
            public DateTimeOffset UtcNow { get { return Epoch.AddSeconds(Seconds); } }
            public TimeSpan MonotonicNow { get { return TimeSpan.FromSeconds(Seconds); } }
            public void Queue(Action work) { Work.Enqueue(work); }
            public void CloseClient(int pid, DateTime expected, Func<bool> cancelled, Action<Action> owned)
            { BeforeClose?.Invoke(); if (cancelled()) throw new OperationCanceledException(); if (FailClose) throw new InvalidOperationException("denied"); owned(() => { if (cancelled()) throw new OperationCanceledException(); Closed.Add(pid); }); }
        }
        private sealed class H : IDisposable
        {
            internal readonly Env E = new Env();
            internal readonly VanillaReconnectSupervisor Supervisor;
            internal readonly object A, B;
            internal readonly Dictionary<int, VanillaPositionSample> Samples = new Dictionary<int, VanillaPositionSample>();
            internal readonly List<int> Forgotten = new List<int>();
            private readonly string root = Path.Combine(Path.GetTempPath(), "4R-watchdog-" + Guid.NewGuid().ToString("N"));
            internal H()
            {
                Supervisor = new VanillaReconnectSupervisor(root, E);
                var map = (IDictionary)Get(Supervisor, "runtimes");
                A = map[Supervisor.Settings.Accounts[0].Id]; B = map[Supervisor.Settings.Accounts[1].Id];
                foreach (var pair in new[] { Tuple.Create(A, 101), Tuple.Create(B, 102) })
                { Set(pair.Item1, "ProcessId", (int?)pair.Item2); Set(pair.Item1, "ResumeSent", true); Set(pair.Item1, "Stage", VanillaReconnectStage.Online); }
                Set(Supervisor, "running", true); // No live Start(), timer or process enumeration.
                Supervisor.SetPositionSource(pid => Samples.ContainsKey(pid) ? Samples[pid] : null, Forgotten.Add);
            }
            internal void Sample(int pid, int x = 10) { Samples[pid] = S(E.Seconds, x, pid: pid); }
            internal bool Motion(object runtime)
            { return (bool)Call(Supervisor, "CheckMovementWatchdog", runtime, E.UtcNow, (Func<DateTime>)(() => Epoch.UtcDateTime), (Func<VanillaVisualState>)(() => VanillaVisualState.Unknown)); }
            internal void Terminal(object runtime, int kind = 0)
            { Call(Supervisor, "HandleTerminalVisual", runtime, kind == 0 ? VanillaVisualState.LoggingOut : VanillaVisualState.Disconnected, E.UtcNow, (Func<DateTime>)(() => Epoch.UtcDateTime)); }
            internal void FinishFirst()
            {
                Call(Supervisor, "Bind", A, 201, true, "fake new client");
                Set(A, "ScriptRunning", true);
                Motion(B); Assert(E.Work.Count == 0, "Second close while first logs in.");
                Set(A, "Stage", VanillaReconnectStage.VerifyingAutobattle);
                Motion(B); Assert(E.Work.Count == 0, "Second close before verified movement/minimization.");
                Set(A, "ScriptRunning", false); Set(A, "ResumeSent", true); Set(A, "HasBeenOnline", true);
                Call(Supervisor, "ResetRecoverySuccessLocked", A);
            }
            public void Dispose() { Supervisor.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static void OneClient()
        { using (var h = new H()) { h.Sample(101); h.Motion(h.A); h.E.Seconds = 120; h.Sample(101); Assert(h.Motion(h.A)); Assert(h.E.Work.Count == 1); h.E.Work.Dequeue()(); Assert(h.E.Closed.SequenceEqual(new[] { 101 })); Assert((int?)Get(h.B,"ProcessId") == 102 && (bool)Get(h.B,"ResumeSent")); Assert(h.Forgotten.SequenceEqual(new[] {101})); } }
        private static void UnreadableRestart()
        { using (var h = new H()) { h.Motion(h.A); h.E.Seconds = 120; Assert(h.Motion(h.A)); Assert(h.E.Work.Count == 1); h.E.Work.Dequeue()(); Assert(h.E.Closed.Count == 1); } }
        private static void BothPosition()
        { using (var h = new H()) { h.Sample(101); h.Motion(h.A); h.Motion(h.B); h.E.Seconds = 120; h.Sample(101); h.Motion(h.A); h.Motion(h.B); Assert(h.E.Work.Count == 1); h.E.Work.Dequeue()(); Assert((bool)Get(h.A,"RecoveryOwned") && Get(h.A,"ProcessId") == null); h.Motion(h.B); Assert(h.E.Work.Count == 0); h.FinishFirst(); h.Motion(h.B); Assert(h.E.Work.Count == 1); h.E.Work.Dequeue()(); Assert(h.E.Closed.SequenceEqual(new[] {101,102})); } }
        private static void Mixed()
        { using (var h = new H()) { h.Motion(h.B); h.Terminal(h.A); h.E.Seconds=1; h.Terminal(h.A); h.E.Seconds=120; h.Motion(h.B); Assert(h.E.Work.Count==1); h.E.Work.Dequeue()(); h.FinishFirst(); h.Motion(h.B); Assert(h.E.Work.Count==1); } }
        private static void QueuedMovement()
        { using (var h = new H()) { h.Sample(102); h.Motion(h.B); h.Terminal(h.A); h.E.Seconds=1; h.Terminal(h.A); h.E.Seconds=120; h.Sample(102); h.Motion(h.B); Assert(h.E.Work.Count==1); h.E.Seconds=121; h.Sample(102,11); Assert(!h.Motion(h.B)); } }
        private static void Stop()
        { using (var h = new H()) { h.Terminal(h.A); h.E.Seconds=1; h.Terminal(h.A); h.Supervisor.Stop(); h.E.Work.Dequeue()(); Assert(h.E.Closed.Count==0 && (int?)Get(h.A,"ProcessId")==101); Assert(!h.Motion(h.A)); } }
        private static void StopAtBoundary()
        { using(var h=new H()){h.Terminal(h.A);h.E.Seconds=1;h.Terminal(h.A);h.E.BeforeClose=h.Supervisor.Stop;h.E.Work.Dequeue()();Assert(h.E.Closed.Count==0);} }
        private static void Settings()
        { using (var h = new H()) { h.Terminal(h.A); h.E.Seconds=1; h.Terminal(h.A); Set(h.Supervisor,"running",false); h.Supervisor.Apply(h.Supervisor.Settings,false); h.E.Work.Dequeue()(); Assert(h.E.Closed.Count==0); } }
        private static void ReplacedPid()
        { using (var h = new H()) { h.Terminal(h.A); h.E.Seconds=1; h.Terminal(h.A); Set(h.A,"ProcessId",(int?)202); h.E.Work.Dequeue()(); Assert(h.E.Closed.Count==0 && (int?)Get(h.A,"ProcessId")==202); } }
        private static void ReplacedOperation()
        { using (var h = new H()) { h.Terminal(h.A); h.E.Seconds=1; h.Terminal(h.A); Set(h.A,"ResumeOperationGeneration",99); h.E.Work.Dequeue()(); Assert(h.E.Closed.Count==0); } }
        private static void CloseFailure()
        { using (var h = new H()) { h.Terminal(h.A); h.E.Seconds=1; h.Terminal(h.A); h.E.FailClose=true; h.E.Work.Dequeue()(); Assert((int?)Get(h.A,"ProcessId")==101 && h.Forgotten.Count==0 && (int)Get(h.A,"RecoveryFailures")==1); h.E.Seconds=2; h.Terminal(h.A); h.E.Seconds=3; h.Terminal(h.A); Assert(h.E.Work.Count==0); } }
        private static void RecoveryOff()
        { using (var h = new H()) { var s=h.Supervisor.Settings;s.AutoRecover=false;Set(h.Supervisor,"settings",s);h.Motion(h.A);h.E.Seconds=121;h.Motion(h.A);Assert(h.E.Work.Count==0); } }
        private static void VisualOff()
        { using (var h = new H()) { var s=h.Supervisor.Settings;s.VisualWatchdog=false;Set(h.Supervisor,"settings",s);h.Motion(h.A);h.E.Seconds=120;h.Motion(h.A);Assert(h.E.Work.Count==1); } }
        private static void StartupGrace()
        { using (var h = new H()) { Set(h.A,"ScriptRunning",true);h.Motion(h.A);h.E.Seconds=1000;h.Motion(h.A);Set(h.A,"ScriptRunning",false);Assert(!h.Motion(h.A) && h.E.Work.Count==0); } }
        private static void OneTerminal()
        { using(var h=new H()){h.Terminal(h.A);Assert(h.E.Work.Count==0);} }
        private static void UnknownModal()
        { using(var h=new H()){Call(h.Supervisor,"HandleTerminalVisual",h.A,VanillaVisualState.ModalDialog,h.E.UtcNow,(Func<DateTime>)(()=>Epoch.UtcDateTime));Assert(h.E.Work.Count==0 && h.E.Closed.Count==0);} }
        private static void ChangedTerminal()
        { using(var h=new H()){h.Terminal(h.A);h.E.Seconds=1;h.Terminal(h.A,1);Assert(h.E.Work.Count==0);} }
        private static void StaleTerminal()
        { using(var h=new H()){h.Terminal(h.A);h.E.Seconds=10;h.Terminal(h.A);Assert(h.E.Work.Count==0);} }
        private static void BothTerminal(int a,int b)
        { using(var h=new H()){h.Terminal(h.A,a);h.Terminal(h.B,b);h.E.Seconds=1;h.Terminal(h.A,a);h.Terminal(h.B,b);Assert(h.E.Work.Count==1);h.E.Work.Dequeue()();h.E.Seconds=2;h.Terminal(h.B,b);Assert(h.E.Work.Count==0);h.FinishFirst();h.E.Seconds=3;h.Terminal(h.B,b);Assert(h.E.Work.Count==1);h.E.Work.Dequeue()();Assert(h.E.Closed.SequenceEqual(new[]{101,102}));} }
        private static void ColdStart()
        { using(var h=new H()){Set(h.Supervisor,"running",false);Set(h.Supervisor,"hardenedStartupRunning",true);Assert((bool)Call(h.Supervisor,"CloseTerminalBeforeStartup",h.A,101,0,h.Supervisor.Settings,(Func<VanillaVisualState>)(()=>VanillaVisualState.LoggingOut),(Func<DateTime>)(()=>Epoch.UtcDateTime),(Action<int>)(ms=>h.E.Seconds+=ms/1000.0)));Assert(h.E.Closed.SequenceEqual(new[]{101}) && (bool)Get(h.A,"RecoveryOwned"));} }
        private static void ColdStartChanged()
        { using(var h=new H()){Set(h.Supervisor,"running",false);Set(h.Supervisor,"hardenedStartupRunning",true);int reads=0;Expect<InvalidOperationException>(()=>Call(h.Supervisor,"CloseTerminalBeforeStartup",h.A,101,0,h.Supervisor.Settings,(Func<VanillaVisualState>)(()=>++reads==1?VanillaVisualState.LoggingOut:VanillaVisualState.Gameplay),(Func<DateTime>)(()=>Epoch.UtcDateTime),(Action<int>)(ms=>h.E.Seconds+=ms/1000.0)));Assert(h.E.Closed.Count==0);} }
        private sealed class Protocol
        {
            internal int Ms,Closes,Kills;internal bool Exited,Cancelled,NeverExit,BadRead,Denied;internal Action OnWait;
            internal void Run(){VanillaClientCloseProtocol.Run(()=>{if(BadRead)throw new InvalidOperationException();return Exited;},()=>{Closes++;if(Denied)throw new InvalidOperationException();},()=>{Kills++;if(!NeverExit)Exited=true;},()=>Cancelled,()=>TimeSpan.FromMilliseconds(Ms),ms=>{Ms+=ms;OnWait?.Invoke();});}
        }
        private static void NormalClose(){var p=new Protocol();p.OnWait=()=>p.Exited=true;p.Run();Assert(p.Closes==1&&p.Kills==0);}
        private static void ForcedClose(){var p=new Protocol();p.Run();Assert(p.Closes==1&&p.Kills==1&&p.Ms==3000);}
        private static void CloseTimeout(){var p=new Protocol{NeverExit=true};Expect<TimeoutException>(p.Run);Assert(p.Ms==6000&&p.Kills==1);}
        private static void StopDuringClose(){var p=new Protocol();p.OnWait=()=>p.Cancelled=true;Expect<OperationCanceledException>(p.Run);Assert(p.Kills==0&&p.Ms==50);}
        private static void UnknownExit(){var p=new Protocol{BadRead=true};Expect<InvalidOperationException>(p.Run);Assert(p.Closes+p.Kills==0);}
        private static void CloseDenied(){var p=new Protocol{Denied=true};Expect<InvalidOperationException>(p.Run);Assert(p.Kills==0);}
        private static void CloseRights(){Assert(VanillaClientCloseHandle.RequiredAccess==0x101001 && (VanillaClientCloseHandle.RequiredAccess&0x003A)==0);}
        private sealed class Meta:VanillaFleetMonitor.IProcessMetadata{public int ProcessId{get;set;}public bool HasExited{get{throw new Exception("Not needed");}}public IntPtr MainWindowHandle{get{throw new Exception("Not needed");}}public void Dispose(){}}
        private sealed class Reader:VanillaFleetMonitor.IClientReader{internal int Pid;internal VanillaPositionSample Frame;internal bool Disposed;public bool IsStopped{get{return Disposed;}}public VanillaFleetClientInfo Poll(TimeSpan now){return new VanillaFleetClientInfo{ProcessId=Pid,Position=Frame??S(0,pid:Pid)};}public void Dispose(){Disposed=true;}}
        private static void FleetCache()
        {
            var made=new List<Reader>();
            using(var m=new VanillaFleetMonitor(Path.GetTempPath(),()=>new VanillaFleetMonitor.IProcessMetadata[]{new Meta{ProcessId=101},new Meta{ProcessId=102}},pid=>{var r=new Reader{Pid=pid};made.Add(r);return r;},()=>null))
            {m.Poll();Assert(m.LatestPosition(101).X==10);m.ConfirmClientExited(101);Assert(m.LatestPosition(101)==null&&m.LatestPosition(102)!=null&&made[0].Disposed&&!made[1].Disposed);m.Poll();Assert(made.Count==3);}
        }
        private static Bitmap Scene(string name,float scale)
        {
            var image=new Bitmap(1280,900);
            using(var stream=VanillaDisconnectPattern.OpenReference(name))using(var reference=new Bitmap(stream))using(var g=Graphics.FromImage(image))
            {g.Clear(Color.FromArgb(55,90,40));g.FillRectangle(Brushes.DarkOliveGreen,0,0,320,140);g.InterpolationMode=InterpolationMode.HighQualityBicubic;int w=(int)(reference.Width*scale),h=(int)(reference.Height*scale);g.DrawImage(reference,(1280-w)/2,(900-h)/2,w,h);}
            return image;
        }
        private static void ImageCase(string name,float scale){using(var image=Scene(name,scale)){var expected=name=="LoggingOut"?VanillaVisualState.LoggingOut:VanillaVisualState.Disconnected;Assert(VanillaVisualProbe.Classify(image)==expected,"Message image was not recognized.");}}
        private static void BlankImage(){using(var image=new Bitmap(800,600))using(var g=Graphics.FromImage(image))foreach(var c in new[]{Color.Black,Color.White,Color.Gray}){g.Clear(c);Assert(VanillaVisualProbe.Classify(image)==VanillaVisualState.Unknown);}}
        private static void WrongImage(){using(var image=Scene("LoggingOut",1)){using(var g=Graphics.FromImage(image)){g.FillRectangle(Brushes.White,512,420,240,38);using(var font=new Font("Arial",9))g.DrawString("Please select a character.",font,Brushes.Black,519,425);}Assert(!VanillaReconnectSupervisor.IsTerminalDisconnect(VanillaVisualProbe.Classify(image)));}}
        private static object Get(object o,string n){return o.GetType().GetField(n,Flags).GetValue(o);}
        private static void Set(object o,string n,object v){o.GetType().GetField(n,Flags).SetValue(o,v);}
        private static object Call(object o,string n,params object[] args){try{return o.GetType().GetMethod(n,Flags).Invoke(o,args);}catch(TargetInvocationException e){throw e.InnerException;}}
        private static void Expect<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
        private static void Assert(bool value,string message="Assertion failed"){if(!value)throw new Exception(message);}
        private static void Test(string name,Action a){try{a();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
    }
}
