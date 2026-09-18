using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace _4RTools.Model.Vanilla
{
    internal sealed class VanillaWeightCartResult
    {
        internal bool RequiresManualIntervention;
        internal bool Deferred;
        internal int ItemsMoved;
        internal string Message;
    }

    internal sealed class VanillaWeightMaintenanceToken
    {
        internal string AccountId;
        internal int ProcessId;
        internal int Generation;
        internal VanillaReconnectAccount Account;
    }

    public sealed partial class VanillaReconnectSupervisor
    {
        private readonly HashSet<string> weightManualHolds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int weightMaintenanceGeneration;

        internal bool IsWeightEnabledForProcess(int pid)
        {
            lock (gate)
            {
                Runtime runtime = runtimes.Values.FirstOrDefault(item => item.ProcessId == pid && item.Account.Enabled);
                return runtime != null && runtime.Account.WeightEnabled;
            }
        }

        internal bool TryBeginWeightMaintenance(int pid, out VanillaWeightMaintenanceToken token, out string reason)
        {
            token = null; reason = null;
            lock (gate)
            {
                if (disposed || !running) { reason = "reconnect supervision is not running"; return false; }
                Runtime runtime = runtimes.Values.FirstOrDefault(item => item.ProcessId == pid && item.Account.Enabled);
                if (runtime == null) { reason = "the client is not assigned to an enabled character row"; return false; }
                if (!runtime.Account.WeightEnabled) { reason = "Weight/Cart is disabled for this character"; return false; }
                if (weightManualHolds.Contains(runtime.Account.Id)) { reason = "the character is waiting for manual cart emptying"; return false; }
                if (runtime.ScriptRunning || runtime.RecoveryOwned || runtime.ClosingForRecovery
                    || runtimes.Values.Any(item => !ReferenceEquals(item, runtime) && (item.ScriptRunning || item.RecoveryOwned || item.ClosingForRecovery)))
                { reason = "another serialized startup/recovery/UI operation owns the input lease"; return false; }
                runtime.ScriptRunning = true;
                int generation = ++weightMaintenanceGeneration;
                token = new VanillaWeightMaintenanceToken
                {
                    AccountId = runtime.Account.Id, ProcessId = pid, Generation = generation, Account = runtime.Account.Clone()
                };
                SetStage(runtime, VanillaReconnectStage.Online, "Weight/cart maintenance owns the serialized client input lease");
                return true;
            }
        }

        internal bool WeightMaintenanceCancelled(VanillaWeightMaintenanceToken token)
        {
            if (token == null) return true;
            lock (gate)
            {
                Runtime runtime;
                return disposed || !running || token.Generation != weightMaintenanceGeneration
                    || !runtimes.TryGetValue(token.AccountId, out runtime) || runtime.ProcessId != token.ProcessId
                    || !runtime.Account.Enabled || !runtime.Account.WeightEnabled || CharacterOwnershipChanged(runtime, token.ProcessId);
            }
        }

        internal void CompleteWeightMaintenance(VanillaWeightMaintenanceToken token, bool manualHold, string detail)
        {
            if (token == null) return;
            lock (gate)
            {
                if (token.Generation != weightMaintenanceGeneration) return;
                Runtime runtime;
                if (!runtimes.TryGetValue(token.AccountId, out runtime) || runtime.ProcessId != token.ProcessId) return;
                runtime.ScriptRunning = false;
                runtime.MovementWatchdog.Reset();
                runtime.MovementRecoveryPending = false;
                runtime.NonMinimizedSince = null;
                if (manualHold)
                {
                    weightManualHolds.Add(token.AccountId);
                    SetStage(runtime, VanillaReconnectStage.Error, detail ?? "Weight/cart maintenance needs manual attention");
                }
                else
                {
                    weightManualHolds.Remove(token.AccountId);
                    SetStage(runtime, VanillaReconnectStage.Online, detail ?? "Weight/cart maintenance completed");
                }
            }
            RaiseUpdated();
        }

        internal bool IsWeightManualHold(string accountId)
        { lock (gate) return !string.IsNullOrWhiteSpace(accountId) && weightManualHolds.Contains(accountId); }

        internal void MarkWeightMaintenanceCancelled(VanillaWeightMaintenanceToken token, bool autobattleMayBePaused, string detail)
        {
            if (token == null) return;
            lock (gate)
            {
                // Once Autobattle may have been toggled OFF, retain the hold by stable
                // character-row ID even if a settings edit removed/rebuilt its runtime.
                if (autobattleMayBePaused) weightManualHolds.Add(token.AccountId);

                Runtime runtime;
                if (!runtimes.TryGetValue(token.AccountId, out runtime) || runtime.ProcessId != token.ProcessId) return;
                runtime.ScriptRunning = false;
                runtime.MovementWatchdog.Reset();
                runtime.MovementRecoveryPending = false;
                runtime.NonMinimizedSince = null;
                if (autobattleMayBePaused)
                {
                    if (running)
                        SetStage(runtime, VanillaReconnectStage.Error, detail ?? "Weight/cart maintenance was cancelled after Autobattle may have been paused");
                    else
                        SetStage(runtime, VanillaReconnectStage.Stopped, "Supervisor stopped; manual Weight/Cart hold retained");
                }
                else if (running && token.Generation == weightMaintenanceGeneration)
                    SetStage(runtime, VanillaReconnectStage.Online, detail ?? "Weight/cart maintenance cancelled before pausing Autobattle");
            }
            RaiseUpdated();
        }

        public void ClearWeightManualHolds()
        {
            lock (gate)
            {
                var held = new HashSet<string>(weightManualHolds, StringComparer.OrdinalIgnoreCase);
                weightManualHolds.Clear();
                foreach (Runtime runtime in runtimes.Values)
                    if (held.Contains(runtime.Account.Id) && runtime.ProcessId.HasValue && runtime.Account.Enabled
                        && runtime.Stage == VanillaReconnectStage.Error)
                        SetStage(runtime, VanillaReconnectStage.Online, "Manual Weight/Cart hold explicitly cleared");
            }
            VanillaDebugLog.Write("WEIGHT", "event=cart-holds-cleared source=explicit-user-action.");
            RaiseUpdated();
        }

        internal bool MinimizeWeightMaintenanceClient(VanillaWeightMaintenanceToken token)
        {
            if (WeightMaintenanceCancelled(token)) return false;
            return MinimizeAssignedClientCore(token.AccountId, false, true);
        }
    }

    internal sealed class VanillaWeightCartAutomation
    {
        private const int ToggleSettleMs = 500;
        private const int CategorySettleMs = 350;
        private const int TransferSettleMs = 300;
        private const int MaxTransfers = 120;
        private readonly VanillaFleetMonitor fleet;
        private readonly VanillaReconnectSupervisor supervisor;

        internal VanillaWeightCartAutomation(VanillaFleetMonitor fleet, VanillaReconnectSupervisor supervisor)
        {
            this.fleet = fleet ?? throw new ArgumentNullException(nameof(fleet));
            this.supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        }

        internal VanillaWeightCartResult Run(int pid, VanillaWeightAlertSettings settings, System.Action<string> report,
            string trigger = "automatic-threshold")
        {
            report = report ?? (_ => { });
            System.Action<string> activity = message =>
            {
                report(message);
                VanillaDebugLog.Write("WEIGHT", message);
            };
            VanillaWeightMaintenanceToken token;
            string reason;
            if (!supervisor.TryBeginWeightMaintenance(pid, out token, out reason))
            {
                VanillaDebugLog.Write("WEIGHT", "event=cart-deferred trigger=" + trigger + " pid=" + pid + " reason='" + reason + "'.");
                return new VanillaWeightCartResult { Deferred = true, Message = "Cart maintenance deferred: " + reason + "." };
            }

            VanillaDebugLog.Write("WEIGHT", "event=cart-start trigger=" + trigger + " account='" + token.Account.Label
                + "' accountId=" + token.AccountId + " pid=" + pid + ".");
            bool paused = false, manualHold = false, completed = false;
            Rectangle inventory = Rectangle.Empty, cart = Rectangle.Empty;
            int moved = 0;
            Func<bool> cancelled = () => supervisor.WeightMaintenanceCancelled(token);
            VanillaForegroundInput openedInput;
            try { openedInput = new VanillaForegroundInput(pid); }
            catch (Exception ex)
            {
                VanillaDebugLog.Write("WEIGHT", "event=cart-deferred trigger=" + trigger + " account='" + token.Account.Label
                    + "' pid=" + pid + " stage=window reason='" + ex.Message + "'.");
                supervisor.CompleteWeightMaintenance(token, false, "Weight/cart maintenance could not acquire the verified client window: " + ex.Message);
                return new VanillaWeightCartResult { Deferred = true, Message = token.Account.Label + ": cart maintenance deferred: " + ex.Message };
            }
            using (var input = openedInput)
            {
                input.CancellationRequested = cancelled;
                try
                {
                    activity(token.Account.Label + ": weight maintenance: pausing Autobattle with " + token.Account.HotkeyText + ".");
                    input.Chord(token.Account.ResumeCtrl, token.Account.ResumeAlt, token.Account.ResumeShift, (Keys)token.Account.ResumeKey);
                    paused = true;
                    Thread.Sleep(700);
                    ThrowIfCancelled(cancelled);

                    inventory = EnsureToggledPanel(input, settings.InventoryCtrl, settings.InventoryAlt, settings.InventoryShift,
                        (Keys)settings.InventoryKey, "Inventory", cancelled, activity);
                    cart = EnsureToggledPanel(input, settings.CartCtrl, settings.CartAlt, settings.CartShift,
                        (Keys)settings.CartKey, "Cart", cancelled, activity);
                    if (inventory.IntersectsWith(cart) && IntersectionRatio(inventory, cart) > 0.60)
                        throw new InvalidOperationException("Inventory and Cart overlap too heavily for safe drag-and-drop. Move them apart once and retry.");

                    var categories = new List<int>();
                    if (settings.TransferUseItems) categories.Add(0);
                    if (settings.TransferEquipItems) categories.Add(1);
                    if (settings.TransferEtcItems) categories.Add(2);
                    foreach (int category in categories)
                    {
                        ThrowIfCancelled(cancelled);
                        SelectCategory(input, inventory, category);
                        Thread.Sleep(CategorySettleMs);
                        string categoryName = category == 0 ? "Use" : category == 1 ? "Equip" : "Etc";
                        activity(token.Account.Label + ": weight maintenance: processing " + categoryName + " inventory items.");
                        int noProgress = 0;
                        while (moved < MaxTransfers)
                        {
                            ThrowIfCancelled(cancelled);
                            uint? pendingWeightBefore = null;
                            Point sourcePoint;
                            using (Bitmap frame = input.CaptureClientBitmap())
                            {
                                VanillaUiSlotGrid grid = VanillaInventoryVision.DetectSlotGrid(frame, inventory);
                                Point? source = VanillaInventoryVision.FirstOccupiedSlot(frame, grid);
                                if (!source.HasValue) break;
                                sourcePoint = source.Value;
                                VanillaUiSlotGrid cartGrid = VanillaInventoryVision.DetectSlotGrid(frame, cart);
                                Point destination = VanillaInventoryVision.FirstEmptySlot(frame, cartGrid)
                                    ?? VanillaInventoryVision.SafeDropPoint(cart, frame.Size);
                                Point from = sourcePoint;
                                uint? weightBefore = CurrentWeight(token.ProcessId);
                                input.DragNormalized(NormalizeX(from.X, frame.Width), NormalizeY(from.Y, frame.Height),
                                    NormalizeX(destination.X, frame.Width), NormalizeY(destination.Y, frame.Height));
                                pendingWeightBefore = weightBefore;
                            }

                            Thread.Sleep(TransferSettleMs);
                            bool quantity = WaitForQuantityPrompt(input, cancelled, 1200);
                            if (quantity)
                            {
                                activity(token.Account.Label + ": weight maintenance: quantity dialog positively detected; pressing Enter for the full stack.");
                                input.Press(Keys.Enter);
                                Thread.Sleep(TransferSettleMs);
                            }
                            else
                            {
                                VanillaDebugLog.Write("WEIGHT", token.Account.Label + ": no quantity dialog detected during the bounded post-drag window; Enter was NOT sent (single-item safe path).");
                            }

                            bool cleared, weightReduced = WaitForWeightReduction(token.ProcessId, pendingWeightBefore, cancelled);
                            using (Bitmap verify = input.CaptureClientBitmap())
                            {
                                VanillaUiSlotGrid grid = VanillaInventoryVision.DetectSlotGrid(verify, inventory);
                                Point? stillOccupied = VanillaInventoryVision.FirstOccupiedSlotNear(verify, grid, sourcePoint);
                                cleared = !stillOccupied.HasValue;
                                if (VanillaInventoryVision.HasQuantityPrompt(verify))
                                {
                                    manualHold = true;
                                    throw new VanillaCartManualException("A quantity dialog remained or appeared late. No further key was sent; Autobattle stays OFF for manual inspection.");
                                }
                            }
                            if (!weightReduced && !cleared)
                            {
                                noProgress++;
                                if (noProgress >= 1)
                                {
                                    manualHold = true;
                                    throw new VanillaCartManualException("Cart did not accept the dragged item. The cart may be full; autobattle is left OFF and this character is held for manual emptying.");
                                }
                            }
                            else
                            {
                                noProgress = 0;
                                moved++;
                                activity(token.Account.Label + ": weight maintenance: moved inventory item " + moved + " to Cart.");
                            }
                        }
                        if (moved >= MaxTransfers)
                        {
                            manualHold = true;
                            throw new VanillaCartManualException("Transfer safety limit reached. Autobattle is left OFF for manual inspection.");
                        }
                    }

                    ClosePanelIfOpen(input, settings.CartCtrl, settings.CartAlt, settings.CartShift, (Keys)settings.CartKey, cart, "Cart", cancelled);
                    ClosePanelIfOpen(input, settings.InventoryCtrl, settings.InventoryAlt, settings.InventoryShift, (Keys)settings.InventoryKey, inventory, "Inventory", cancelled);
                    activity(token.Account.Label + ": weight maintenance: transfer complete; resuming Autobattle with the shared verified ResumeHotkey routine.");
                    VerifyResume(token, input, cancelled, activity);
                    paused = false;
                    if (!supervisor.MinimizeWeightMaintenanceClient(token))
                        throw new InvalidOperationException("Autobattle movement was verified but the client could not be minimized.");
                    completed = true;
                    string message = token.Account.Label + ": cart maintenance completed; moved " + moved
                        + " item(s), autobattle movement verified, client minimized.";
                    VanillaDebugLog.Write("WEIGHT", "event=cart-complete trigger=" + trigger + " account='" + token.Account.Label
                        + "' accountId=" + token.AccountId + " pid=" + pid + " items=" + moved + ".");
                    supervisor.CompleteWeightMaintenance(token, false, message);
                    return new VanillaWeightCartResult { ItemsMoved = moved, Message = message };
                }
                catch (OperationCanceledException ex)
                {
                    string detail = "Weight/cart maintenance cancelled by supervisor/settings/client ownership change: " + ex.Message;
                    supervisor.MarkWeightMaintenanceCancelled(token, paused, detail);
                    VanillaDebugLog.Write("WEIGHT", "event=cart-cancelled trigger=" + trigger + " account='" + token.Account.Label
                        + "' accountId=" + token.AccountId + " pid=" + pid + " items=" + moved
                        + " autobattleMayBePaused=" + paused + " manualHold=" + paused + ".");
                    return new VanillaWeightCartResult
                    {
                        ItemsMoved = moved,
                        Deferred = !paused,
                        RequiresManualIntervention = paused,
                        Message = paused
                            ? token.Account.Label + ": cart maintenance was cancelled after Autobattle may have been paused; manual hold retained."
                            : token.Account.Label + ": cart maintenance cancelled before Autobattle was paused; it may retry when supervision is stable."
                    };
                }
                catch (VanillaCartManualException ex)
                {
                    manualHold = true;
                    bool cancelledNow = cancelled();
                    VanillaDebugLog.Write("WEIGHT", "event=cart-manual-hold trigger=" + trigger + " account='" + token.Account.Label
                        + "' accountId=" + token.AccountId + " pid=" + pid + " items=" + moved + " reason='" + ex.Message
                        + "' cancellationRace=" + cancelledNow + ".");
                    if (cancelledNow)
                        supervisor.MarkWeightMaintenanceCancelled(token, true, ex.Message);
                    else
                        supervisor.CompleteWeightMaintenance(token, true, ex.Message);
                    return new VanillaWeightCartResult { ItemsMoved = moved, RequiresManualIntervention = true, Message = token.Account.Label + ": " + ex.Message };
                }
                catch (Exception ex)
                {
                    // After Autobattle has been toggled OFF, any unexpected UI state is deliberately
                    // fail-closed, including a cancellation racing with this exception. Never let a
                    // stale generation erase the manual hold merely because another failure won first.
                    bool cancelledNow = cancelled();
                    if (paused) manualHold = true;
                    VanillaDebugLog.Write("WEIGHT", "event=cart-failed trigger=" + trigger + " account='" + token.Account.Label
                        + "' accountId=" + token.AccountId + " pid=" + pid + " items=" + moved
                        + " manualHold=" + manualHold + " cancellationRace=" + cancelledNow + " reason='" + ex.Message + "'.");
                    string detail = manualHold
                        ? "Weight/cart maintenance stopped in an uncertain UI state; Autobattle remains OFF for manual inspection: " + ex.Message
                        : "Weight/cart maintenance aborted safely: " + ex.Message;
                    if (cancelledNow)
                        supervisor.MarkWeightMaintenanceCancelled(token, manualHold, detail);
                    else
                        supervisor.CompleteWeightMaintenance(token, manualHold, detail);
                    return new VanillaWeightCartResult
                    {
                        ItemsMoved = moved, RequiresManualIntervention = manualHold,
                        Message = token.Account.Label + ": automatic cart maintenance " + (manualHold ? "needs manual attention: " : "aborted safely: ") + ex.Message
                    };
                }
                finally
                {
                    if (!completed && cancelled()) VanillaDebugLog.Write("WEIGHT", token.Account.Label + ": weight maintenance cancelled by ownership/supervisor change.");
                }
            }
        }

        private uint? CurrentWeight(int pid)
        {
            VanillaFleetClientInfo client = fleet.Poll().FirstOrDefault(item => item.ProcessId == pid);
            return client != null && client.WeightVerified ? client.CurrentWeight : null;
        }

        private static bool WaitForQuantityPrompt(VanillaForegroundInput input, Func<bool> cancelled, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                ThrowIfCancelled(cancelled);
                using (Bitmap frame = input.CaptureClientBitmap())
                    if (VanillaInventoryVision.HasQuantityPrompt(frame)) return true;
                Thread.Sleep(100);
            }
            return false;
        }

        private bool WaitForWeightReduction(int pid, uint? before, Func<bool> cancelled)
        {
            if (!before.HasValue) return false;
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 1800)
            {
                ThrowIfCancelled(cancelled);
                uint? current = CurrentWeight(pid);
                if (current.HasValue && current.Value < before.Value) return true;
                Thread.Sleep(150);
            }
            return false;
        }

        private void VerifyResume(VanillaWeightMaintenanceToken token, VanillaForegroundInput input, Func<bool> cancelled, System.Action<string> report)
        {
            var clock = Stopwatch.StartNew();
            var verifier = new VanillaAutobattleResumeVerifier();
            Func<VanillaClientState> read = () =>
            {
                VanillaFleetClientInfo client = fleet.Poll().FirstOrDefault(item => item.ProcessId == token.ProcessId);
                if (client == null || client.Snapshot == null) throw new InvalidOperationException("Fresh fleet state is unavailable for autobattle verification.");
                return client.Snapshot;
            };
            verifier.VerifyAsync(token.ProcessId, read, input.Activate,
                () => input.ChordInVerifiedForeground(token.Account.ResumeCtrl, token.Account.ResumeAlt, token.Account.ResumeShift, (Keys)token.Account.ResumeKey),
                cancelled, () => clock.Elapsed, () => DateTimeOffset.UtcNow,
                milliseconds => Task.Delay(milliseconds), text => report(token.Account.Label + ": weight maintenance: " + text))
                .GetAwaiter().GetResult();
        }

        private static Rectangle EnsureToggledPanel(VanillaForegroundInput input, bool ctrl, bool alt, bool shift, Keys key,
            string caption, Func<bool> cancelled, System.Action<string> report)
        {
            ThrowIfCancelled(cancelled);
            using (Bitmap before = input.CaptureClientBitmap())
            {
                input.Chord(ctrl, alt, shift, key);
                Thread.Sleep(ToggleSettleMs);
                using (Bitmap after = input.CaptureClientBitmap())
                {
                    bool opened;
                    Rectangle panel;
                    if (!VanillaInventoryVision.TryFindToggledPanel(before, after, out panel, out opened))
                        throw new InvalidOperationException(caption + " hotkey changed no confidently detectable slot panel; no drag input sent.");
                    if (opened)
                    {
                        report(caption + " detected at " + panel + ".");
                        return panel;
                    }
                    input.Chord(ctrl, alt, shift, key);
                    Thread.Sleep(ToggleSettleMs);
                    using (Bitmap reopened = input.CaptureClientBitmap())
                    {
                        Rectangle reopenedPanel; bool reopenedOpened;
                        if (!VanillaInventoryVision.TryFindToggledPanel(after, reopened, out reopenedPanel, out reopenedOpened) || !reopenedOpened)
                            throw new InvalidOperationException(caption + " was initially open but could not be reopened and verified; no drag input sent.");
                        report(caption + " was already open; toggled closed/reopened and detected at " + reopenedPanel + ".");
                        return reopenedPanel;
                    }
                }
            }
        }

        private static void ClosePanelIfOpen(VanillaForegroundInput input, bool ctrl, bool alt, bool shift, Keys key,
            Rectangle panel, string caption, Func<bool> cancelled)
        {
            ThrowIfCancelled(cancelled);
            using (Bitmap before = input.CaptureClientBitmap())
            {
                if (!VanillaInventoryVision.PanelStillPresent(before, panel)) return;
                input.Chord(ctrl, alt, shift, key);
                Thread.Sleep(250);
                VanillaDebugLog.Write("WEIGHT", caption + " close hotkey sent after verified panel presence.");
            }
        }

        private static void SelectCategory(VanillaForegroundInput input, Rectangle inventory, int category)
        {
            double x = inventory.Left + Math.Max(8, inventory.Width * 0.025);
            double bodyTop = inventory.Top + Math.Max(18, inventory.Height * 0.07);
            double bodyBottom = inventory.Bottom - Math.Max(24, inventory.Height * 0.09);
            double segment = Math.Max(20, (bodyBottom - bodyTop) / 4.0);
            double y = bodyTop + segment * (category + 0.5);
            using (Bitmap frame = input.CaptureClientBitmap())
                input.ClickNormalized(NormalizeX(x, frame.Width), NormalizeY(y, frame.Height));
        }

        private static double IntersectionRatio(Rectangle a, Rectangle b)
        {
            Rectangle intersection = Rectangle.Intersect(a, b);
            if (intersection.IsEmpty) return 0;
            return intersection.Width * intersection.Height / (double)Math.Min(a.Width * a.Height, b.Width * b.Height);
        }
        private static double NormalizeX(double x, int width) { return Math.Max(0, Math.Min(1, x / Math.Max(1.0, width - 1.0))); }
        private static double NormalizeY(double y, int height) { return Math.Max(0, Math.Min(1, y / Math.Max(1.0, height - 1.0))); }
        private static void ThrowIfCancelled(Func<bool> cancelled) { if (cancelled()) throw new OperationCanceledException("Weight/cart maintenance cancelled."); }

        private sealed class VanillaCartManualException : Exception { internal VanillaCartManualException(string message) : base(message) { } }
    }

    internal sealed class VanillaUiSlotGrid
    {
        internal Rectangle Panel;
        internal int[] Columns;
        internal int[] Rows;
        internal int EmptyPaleThreshold;
    }

    internal static class VanillaInventoryVision
    {
        private sealed class Component
        {
            internal Rectangle Bounds;
            internal int Area;
            internal Point Center;
        }

        internal static bool TryFindToggledPanel(Bitmap before, Bitmap after, out Rectangle panel, out bool opened)
        {
            panel = Rectangle.Empty; opened = false;
            Rectangle added = FindChangedLightPanel(after, before);
            Rectangle removed = FindChangedLightPanel(before, after);
            int addedScore = ScorePanel(after, added), removedScore = ScorePanel(before, removed);
            if (addedScore <= 0 && removedScore <= 0) return false;
            opened = addedScore >= removedScore;
            panel = opened ? added : removed;
            return !panel.IsEmpty;
        }

        internal static bool PanelStillPresent(Bitmap frame, Rectangle panel)
        {
            if (frame == null || panel.IsEmpty) return false;
            Rectangle clipped = Rectangle.Intersect(new Rectangle(Point.Empty, frame.Size), panel);
            if (clipped.Width < 100 || clipped.Height < 50) return false;
            PixelBuffer pixels = PixelBuffer.Read(frame);
            int light = 0, total = 0;
            int step = Math.Max(1, Math.Min(clipped.Width, clipped.Height) / 80);
            for (int y = clipped.Top; y < clipped.Bottom; y += step)
                for (int x = clipped.Left; x < clipped.Right; x += step) { total++; if (pixels.IsLight(x, y)) light++; }
            return total > 0 && light / (double)total > 0.35;
        }

        internal static VanillaUiSlotGrid DetectSlotGrid(Bitmap frame, Rectangle panel)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            Rectangle clipped = Rectangle.Intersect(new Rectangle(Point.Empty, frame.Size), panel);
            if (clipped.Width < 120 || clipped.Height < 60) throw new InvalidOperationException("Detected item panel is too small for slot recognition.");
            PixelBuffer pixels = PixelBuffer.Read(frame);
            List<Component> components = ConnectedComponents(pixels, clipped, p => p.Pale, 18, 55, 8, 30, 120, 1000);
            var rows = new List<List<Component>>();
            foreach (Component item in components.OrderBy(c => c.Center.Y))
            {
                List<Component> row = rows.FirstOrDefault(r => Math.Abs(r.Average(c => c.Center.Y) - item.Center.Y) <= 5);
                if (row == null) { row = new List<Component>(); rows.Add(row); }
                row.Add(item);
            }
            var useful = rows.Select(row => row.OrderBy(c => c.Center.X).ToList())
                .Where(row => LongestRegularRun(row.Select(c => c.Center.X).ToArray()).Length >= 4).ToList();
            if (useful.Count == 0) throw new InvalidOperationException("No regular Vanilla item-slot grid was detected inside the toggled panel.");

            int[] columns = useful.Select(row => LongestRegularRun(row.Select(c => c.Center.X).ToArray()))
                .OrderByDescending(run => run.Length).First();
            if (columns.Length < 4) throw new InvalidOperationException("Item-slot column lattice is incomplete.");
            int columnSpacing = MedianSpacing(columns);
            var rowCenters = useful.Select(row => (int)Math.Round(row.Average(c => c.Center.Y))).OrderBy(v => v).ToList();
            int rowSpacing = rowCenters.Count > 1 ? MedianSpacing(rowCenters.ToArray()) : columnSpacing;
            rowSpacing = Math.Max(24, Math.Min(60, rowSpacing));
            int first = rowCenters.First();
            while (first - rowSpacing - clipped.Top > rowSpacing * 2) first -= rowSpacing;
            int last = rowCenters.Last();
            while (clipped.Bottom - (last + rowSpacing) > rowSpacing) last += rowSpacing;
            var allRows = new List<int>();
            for (int y = first; y <= last; y += rowSpacing) allRows.Add(y);

            int emptyMedian = components.OrderBy(c => Math.Abs(c.Center.Y - rowCenters[0])).Take(Math.Max(1, columns.Length))
                .Select(c => pixels.PaleCount(c.Center.X, c.Center.Y, 18, 10)).OrderBy(v => v).ElementAt(Math.Max(0, Math.Min(columns.Length - 1, columns.Length / 2)));
            int threshold = Math.Max(240, (int)Math.Round(emptyMedian * 0.66));
            return new VanillaUiSlotGrid { Panel = clipped, Columns = columns, Rows = allRows.ToArray(), EmptyPaleThreshold = threshold };
        }

        internal static Point? FirstOccupiedSlot(Bitmap frame, VanillaUiSlotGrid grid)
        {
            PixelBuffer pixels = PixelBuffer.Read(frame);
            foreach (int y in grid.Rows)
                foreach (int x in grid.Columns)
                    if (grid.Panel.Contains(x, y) && pixels.PaleCount(x, y, 18, 10) < grid.EmptyPaleThreshold) return new Point(x, y);
            return null;
        }

        internal static Point? FirstOccupiedSlotNear(Bitmap frame, VanillaUiSlotGrid grid, Point previous)
        {
            PixelBuffer pixels = PixelBuffer.Read(frame);
            Point? nearest = null; double best = double.MaxValue;
            foreach (int y in grid.Rows)
                foreach (int x in grid.Columns)
                {
                    if (!grid.Panel.Contains(x, y) || pixels.PaleCount(x, y, 18, 10) >= grid.EmptyPaleThreshold) continue;
                    double distance = Math.Abs(x - previous.X) + Math.Abs(y - previous.Y);
                    if (distance < best) { best = distance; nearest = new Point(x, y); }
                }
            return best <= 14 ? nearest : null;
        }

        internal static Point? FirstEmptySlot(Bitmap frame, VanillaUiSlotGrid grid)
        {
            PixelBuffer pixels = PixelBuffer.Read(frame);
            foreach (int y in grid.Rows)
                foreach (int x in grid.Columns)
                    if (grid.Panel.Contains(x, y) && pixels.PaleCount(x, y, 18, 10) >= grid.EmptyPaleThreshold) return new Point(x, y);
            return null;
        }

        internal static Point SafeDropPoint(Rectangle cart, Size size)
        {
            int x = Math.Max(cart.Left + 10, Math.Min(cart.Right - 10, cart.Left + (int)Math.Round(cart.Width * 0.72)));
            int y = Math.Max(cart.Top + 20, Math.Min(cart.Bottom - 20, cart.Top + (int)Math.Round(cart.Height * 0.48)));
            return new Point(Math.Max(0, Math.Min(size.Width - 1, x)), Math.Max(0, Math.Min(size.Height - 1, y)));
        }

        internal static bool HasQuantityPrompt(Bitmap frame)
        {
            if (frame == null) return false;
            PixelBuffer pixels = PixelBuffer.Read(frame);
            Rectangle whole = new Rectangle(Point.Empty, frame.Size);
            // The Vanilla quantity prompt is a short, wide white modal containing a focused
            // blue-selected numeric edit field. Basic Info, inventory/cart panes and chat also
            // contain white/blue pixels, so dimensions/aspect/fill are mandatory before Enter
            // can ever be authorized. Missing/ambiguous evidence always means NO Enter.
            List<Component> white = ConnectedComponents(pixels, whole, p => p.Light, 100, 440, 30, 95, 900, 60000);
            foreach (Component component in white)
            {
                Rectangle box = component.Bounds;
                double aspect = box.Width / (double)Math.Max(1, box.Height);
                double fill = component.Area / (double)Math.Max(1, box.Width * box.Height);
                if (aspect < 2.4 || aspect > 7.0 || fill < 0.45) continue;
                Rectangle leftBody = new Rectangle(box.Left, box.Top + box.Height / 4,
                    Math.Max(1, (int)Math.Round(box.Width * 0.68)), Math.Max(1, box.Height * 3 / 4));
                if (pixels.BlueSelectionCount(leftBody) >= 18) return true;
            }
            return false;
        }

        private static Rectangle FindChangedLightPanel(Bitmap primary, Bitmap secondary)
        {
            if (primary == null || secondary == null || primary.Size != secondary.Size) return Rectangle.Empty;
            PixelBuffer a = PixelBuffer.Read(primary), b = PixelBuffer.Read(secondary);
            int width = a.Width, height = a.Height;
            bool[] mask = new bool[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    mask[y * width + x] = a.IsLight(x, y) && a.ColorDistance(b, x, y) >= 32;
            List<Component> components = ConnectedComponents(a, new Rectangle(0, 0, width, height), mask, 80, 700, 40, 760, 1200, width * height);
            if (components.Count == 0) return Rectangle.Empty;
            Component best = components.OrderByDescending(c => c.Area).First();
            Rectangle r = best.Bounds;
            r.Inflate(8, 8);
            r.Intersect(new Rectangle(0, 0, width, height));
            return r;
        }

        private static int ScorePanel(Bitmap frame, Rectangle panel)
        {
            if (panel.IsEmpty || panel.Width < 120 || panel.Height < 50) return 0;
            try
            {
                VanillaUiSlotGrid grid = DetectSlotGrid(frame, panel);
                return panel.Width * panel.Height + grid.Columns.Length * grid.Rows.Length * 100;
            }
            catch { return 0; }
        }

        private static int[] LongestRegularRun(int[] values)
        {
            values = values.Distinct().OrderBy(v => v).ToArray();
            int[] best = new int[0];
            for (int i = 0; i < values.Length; i++)
            {
                var run = new List<int> { values[i] };
                int spacing = 0;
                for (int j = i + 1; j < values.Length; j++)
                {
                    int delta = values[j] - run[run.Count - 1];
                    if (run.Count == 1)
                    {
                        if (delta < 28 || delta > 58) continue;
                        spacing = delta; run.Add(values[j]);
                    }
                    else if (Math.Abs(delta - spacing) <= 5) run.Add(values[j]);
                    else if (delta > spacing + 6) break;
                }
                if (run.Count > best.Length) best = run.ToArray();
            }
            return best;
        }

        private static int MedianSpacing(int[] values)
        {
            if (values == null || values.Length < 2) return 41;
            int[] d = values.Skip(1).Select((value, index) => value - values[index]).Where(v => v > 0).OrderBy(v => v).ToArray();
            return d.Length == 0 ? 41 : d[d.Length / 2];
        }

        private static List<Component> ConnectedComponents(PixelBuffer pixels, Rectangle area, Func<PixelInfo, bool> predicate,
            int minWidth, int maxWidth, int minHeight, int maxHeight, int minArea, int maxArea)
        {
            bool[] mask = new bool[pixels.Width * pixels.Height];
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++) mask[y * pixels.Width + x] = predicate(pixels.At(x, y));
            return ConnectedComponents(pixels, area, mask, minWidth, maxWidth, minHeight, maxHeight, minArea, maxArea);
        }

        private static List<Component> ConnectedComponents(PixelBuffer pixels, Rectangle area, bool[] mask,
            int minWidth, int maxWidth, int minHeight, int maxHeight, int minArea, int maxArea)
        {
            var result = new List<Component>();
            int width = pixels.Width;
            int[] queue = new int[Math.Max(1, area.Width * area.Height)];
            for (int y0 = area.Top; y0 < area.Bottom; y0++)
            for (int x0 = area.Left; x0 < area.Right; x0++)
            {
                int start = y0 * width + x0;
                if (!mask[start]) continue;
                int head = 0, tail = 0; queue[tail++] = start; mask[start] = false;
                int minX = x0, maxX = x0, minY = y0, maxY = y0, count = 0;
                while (head < tail)
                {
                    int index = queue[head++], y = index / width, x = index - y * width; count++;
                    if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < area.Left || nx >= area.Right || ny < area.Top || ny >= area.Bottom) continue;
                        int ni = ny * width + nx;
                        if (!mask[ni]) continue;
                        mask[ni] = false; queue[tail++] = ni;
                    }
                }
                int w = maxX - minX + 1, h = maxY - minY + 1;
                if (w >= minWidth && w <= maxWidth && h >= minHeight && h <= maxHeight && count >= minArea && count <= maxArea)
                    result.Add(new Component { Bounds = new Rectangle(minX, minY, w, h), Area = count, Center = new Point((minX + maxX) / 2, (minY + maxY) / 2) });
            }
            return result;
        }

        internal struct PixelInfo
        {
            internal byte R, G, B;
            internal bool Light { get { return R >= 235 && G >= 235 && B >= 235; } }
            internal bool Pale
            {
                get
                {
                    int max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
                    return R > 180 && G > 185 && B > 190 && B - R >= 5 && max - min <= 48;
                }
            }
        }

        internal sealed class PixelBuffer
        {
            private readonly byte[] data;
            internal int Width { get; private set; }
            internal int Height { get; private set; }
            private PixelBuffer(int width, int height, byte[] data) { Width = width; Height = height; this.data = data; }
            internal static PixelBuffer Read(Bitmap source)
            {
                using (var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(copy)) g.DrawImageUnscaled(source, 0, 0);
                    Rectangle rect = new Rectangle(0, 0, copy.Width, copy.Height);
                    BitmapData bits = copy.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        int stride = bits.Stride, row = copy.Width * 3;
                        byte[] raw = new byte[row * copy.Height];
                        byte[] scan = new byte[Math.Abs(stride) * copy.Height];
                        Marshal.Copy(bits.Scan0, scan, 0, scan.Length);
                        for (int y = 0; y < copy.Height; y++) Buffer.BlockCopy(scan, y * Math.Abs(stride), raw, y * row, row);
                        return new PixelBuffer(copy.Width, copy.Height, raw);
                    }
                    finally { copy.UnlockBits(bits); }
                }
            }
            internal PixelInfo At(int x, int y)
            {
                int index = (y * Width + x) * 3;
                return new PixelInfo { B = data[index], G = data[index + 1], R = data[index + 2] };
            }
            internal bool IsLight(int x, int y) { return At(x, y).Light; }
            internal int ColorDistance(PixelBuffer other, int x, int y)
            {
                PixelInfo a = At(x, y), b = other.At(x, y);
                return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            }
            internal int PaleCount(int cx, int cy, int rx, int ry)
            {
                int count = 0;
                for (int y = Math.Max(0, cy - ry); y <= Math.Min(Height - 1, cy + ry); y++)
                    for (int x = Math.Max(0, cx - rx); x <= Math.Min(Width - 1, cx + rx); x++) if (At(x, y).Pale) count++;
                return count;
            }
            internal int BlueSelectionCount(Rectangle box)
            {
                int count = 0;
                Rectangle clipped = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), box);
                for (int y = clipped.Top; y < clipped.Bottom; y++)
                    for (int x = clipped.Left; x < clipped.Right; x++)
                    {
                        PixelInfo p = At(x, y);
                        if (p.B >= 180 && p.B - p.R >= 60 && p.B - p.G >= 30) count++;
                    }
                return count;
            }
        }
    }
}
