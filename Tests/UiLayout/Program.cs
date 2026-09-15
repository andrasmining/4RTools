using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Loads the production assembly in its inert smoke mode. Only fictional account/fleet
// observations are supplied. No game process, credentials, email or input is used.
internal static class UiLayoutHarness
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Assembly app;
    private static string output;
    private static int caseNumber;
    private static readonly List<string> failures = new List<string>();
    private static readonly StringBuilder report = new StringBuilder();
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) return 2;
        output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("FOURRTOOLS_DATA_ROOT", Path.Combine(output, "isolated-data"));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            report.AppendLine("Assembly: " + app.GetName().Version + "; platform=" + Environment.OSVersion + "; pointerBytes=" + IntPtr.Size);
            CallStatic("_4RTools.Model.Vanilla.VanillaAppData", "InitializeAndMigrateLegacy", Path.GetDirectoryName(Path.GetFullPath(args[0])));
            CallStatic("_4RTools.Model.ProfileSingleton", "Create", "Default");
            CallStatic("_4RTools.Program", "LoadStockClients");
            using (Form main = (Form)Activator.CreateInstance(app.GetType("_4RTools.Forms.Container", true), new object[] { true }))
            {
                main.MaximumSize = new Size(4096, 4096);
                main.StartPosition = FormStartPosition.Manual;
                main.Location = Point.Empty;
                main.Show();
                Pump();
                ((Control)Field(main, "vanillaWorkspace")).Enabled = true;
                object recovery = Field(main, "integratedReconnectView");
                Check(recovery != null, "Production Recovery form was not embedded by Container startup.");
                SeedFleet(main);
                RunCase(main, recovery, 1920, 1020, 2, 1F, false);
                RunCase(main, recovery, 1920, 1020, 4, 1F, false);
                // Realistic Full-HD work area, allowing space for borders/title/taskbar.
                RunCase(main, recovery, 1904, 981, 12, 1F, false);
                RunCase(main, recovery, 1980, 1020, 4, 1F, false);
                RunCase(main, recovery, 1600, 900, 12, 1F, false);
                RunCase(main, recovery, 1366, 768, 4, 1F, false);
                RunCase(main, recovery, 1050, 700, 4, 1F, false);
                RunCase(main, recovery, 1920, 1020, 40, 1F, false);
                RunCase(main, recovery, 1366, 768, 40, 1F, false);
                RunCase(main, recovery, 1920, 1020, 4, 1.25F, false);
                RunCase(main, recovery, 1920, 1020, 12, 1.50F, false);
                RunCase(main, recovery, 1366, 768, 4, 1.50F, false);
                RunCase(main, recovery, 1050, 700, 4, 1F, true);
                RunCase(main, recovery, 1920, 1020, 4, 1F, true);
                // Return from enlarged text/small windows to the initial size on the same instance.
                RunCase(main, recovery, 1920, 1020, 4, 1F, false);
                // Vertical scrolling must not consume the last column or create horizontal overflow,
                // including enlarged text, a stacked narrow workspace and a return to a short list.
                RunCase(main, recovery, 1920, 1020, 40, 1.50F, false);
                RunCase(main, recovery, 1050, 700, 40, 1F, false);
                RunCase(main, recovery, 1920, 1020, 4, 1F, false);
                Call(main, "AssertSmokeBackgroundServicesInactive");
                report.AppendLine("Background services: inactive; fleet polls=0; no game input or email enabled.");
            }
        }
        catch (Exception ex) { failures.Add("Harness exception: " + ex); }
        report.AppendLine("Cases: " + caseNumber);
        report.AppendLine("Failures: " + failures.Count);
        foreach (string failure in failures) report.AppendLine("FAIL " + failure);
        File.WriteAllText(Path.Combine(output, "layout-report.txt"), report.ToString());
        Console.WriteLine(report.ToString());
        return failures.Count == 0 ? 0 : 1;
    }

    private static void ResizeNativeViewport(Form main, int width, int height)
    {
        // Standard hosted desktops may be 1024x768. Size only this test application's
        // top-level viewport natively; all child layout is still the production code.
        RECT client, outer;
        if (!GetClientRect(main.Handle, out client) || !GetWindowRect(main.Handle, out outer))
            throw new InvalidOperationException("Could not measure the native test window.");
        int borderX = outer.Right - outer.Left - (client.Right - client.Left);
        int borderY = outer.Bottom - outer.Top - (client.Bottom - client.Top);
        if (!SetWindowPos(main.Handle, IntPtr.Zero, 0, 0, width + borderX, height + borderY, 0x0014))
            throw new InvalidOperationException("Could not size native test viewport: " + Marshal.GetLastWin32Error());
        Pump();
        GetClientRect(main.Handle, out client);
        if (client.Right - client.Left != width || client.Bottom - client.Top != height)
            throw new InvalidOperationException("Native viewport was not resized to " + width + "x" + height + ".");
    }

    private static void RunCase(Form main, object recovery, int width, int height, int rows, float textScale, bool notifications)
    {
        caseNumber++;
        string name = caseNumber.ToString("00") + "-" + width + "x" + height + "-" + rows + "accounts-text" + (int)(textScale * 100)
            + (notifications ? "-notifications" : "");
        try
        {
            ResizeNativeViewport(main, width, height);
            Control view = (Control)recovery;
            view.Font = new Font("Segoe UI", 9F * textScale);
            SeedAccounts(recovery, rows);
            ((Label)Field(recovery, "testState")).Text = notifications ? "Save failed: sample error; details in debug log" : string.Empty;
            ((Label)Field(recovery, "runState")).Text = notifications ? "STARTING" : "STOPPED";
            string version = app.GetName().Version.ToString(3);
            ((Label)Field(main, "integratedUpdateStatus")).Text = "Version " + version + (notifications
                ? " - update check unavailable. Sample long status." : " - up to date.");
            Pump();
            Call(main, "AssertSmokeBackgroundServicesInactive");
            DataGridView grid = (DataGridView)Field(recovery, "accounts");
            Control launcher = (Control)Field(recovery, "launchPath");
            Control box = grid.Parent;
            while (box != null && !(box is GroupBox)) box = box.Parent;
            Control log = (Control)Field(recovery, "log");
            Control left = (Control)Field(recovery, "responsiveLeft");
            Control logBox = (Control)Field(recovery, "responsiveLogBox");
            report.AppendLine("CASE " + name + " actualClient=" + main.ClientSize + " outer=" + main.Size
                + " recovery=" + view.Bounds + " parentClient=" + view.Parent.ClientSize
                + " grid=" + BoundsIn(grid, main) + " launcher=" + BoundsIn(launcher, main) + " log=" + BoundsIn(log, main));
            Check(main.ClientSize == new Size(width, height), name + ": managed viewport differs from native size.");
            Check(main.Width >= width && main.Height >= height, name + ": screenshot is smaller than requested viewport.");
            Check(Math.Abs(view.Width - (view.Parent.ClientSize.Width - view.Parent.Padding.Horizontal)) <= 2,
                name + ": embedded recovery form is not filling the parent width.");
            Check(grid.Rows.Count == rows, name + ": not all mock account profiles are rendered.");
            Check(FullyVisible(grid, main), name + ": account grid is clipped by an ancestor viewport.");
            Check(FullyVisible(log, main), name + ": log is clipped by an ancestor viewport.");
            Check(grid.Columns.Contains("RuntimePid") && grid.Columns.Contains("RuntimeStatus"), name + ": runtime columns missing.");
            if (width >= 1600 && textScale <= 1.25F)
            {
                Check(BoundsIn(logBox, main).Left >= BoundsIn(left, main).Right,
                    name + ": Full-HD recovery/log panes are stacked instead of side-by-side.");
                double ratio = left.Width / (double)(left.Width + logBox.Width);
                Check(ratio >= 0.64 && ratio <= 0.69, name + ": accounts/log split is not approximately 2:1: " + ratio);
            }
            int totalWidth = CheckAccountViewport(grid, name, true);
            Check(grid.Height >= grid.ColumnHeadersHeight + grid.RowTemplate.Height * 5,
                name + ": fewer than four account rows plus one spare row can fit.");
            if (rows <= 4) Check(grid.DisplayedRowCount(false) == rows, name + ": an account row is not fully visible.");

            Control strip = (Control)Field(recovery, "responsiveHeader");
            int lastBottom = Descendants(strip).Where(c => c.Visible && (c is Button || c is CheckBox || c is TextBox || c is Label))
                .Select(c => BoundsIn(c, main).Bottom).DefaultIfEmpty(BoundsIn(launcher, main).Bottom).Max();
            int gap = BoundsIn(box, main).Top - lastBottom;
            report.AppendLine("  HEADER gap=" + gap + " totalColumnWidth=" + totalWidth + " displayedRows=" + grid.DisplayedRowCount(false));
            Check(gap >= 0 && gap <= 16, name + ": dead space below launcher/actions: " + gap + "px.");
            foreach (Control control in Descendants(strip).Where(c => c.Visible && (c is Button || c is CheckBox || c is TextBox)))
                Check(FullyVisible(control, main), name + ": action clipped: " + control.Text);
            foreach (string caption in new[] { "Add", "Edit", "Remove" })
            {
                Button button = Descendants(view).OfType<Button>().FirstOrDefault(b => b.Visible && b.Text == caption);
                Check(button != null && FullyVisible(button, main), name + ": account action clipped/missing: " + caption);
            }
            foreach (string field in new[] { "globalDebugEnabled", "globalCopyDebug", "integratedUpdateStatus" })
            {
                Control control = (Control)Field(main, field);
                Check(control != null && control.Visible && FullyVisible(control, main), name + ": global header control clipped/missing: " + field);
            }
            foreach (Button button in Descendants(main).OfType<Button>().Where(b => b.Visible && b.Text == "CHECK FOR UPDATES"))
                Check(FullyVisible(button, main), name + ": update button is clipped.");
            Control fleet = (Control)Field(main, "integratedFleetDashboard");
            foreach (Label label in Descendants(fleet).OfType<Label>().Where(c => c.Visible))
            {
                Check(label.Height >= label.Font.Height && FullyVisible(label, main),
                    name + ": live-card text is clipped: " + label.Text + " bounds=" + BoundsIn(label, main));
            }
            Check(Descendants(fleet).OfType<Label>().Count(c => c.Visible && c.Text.StartsWith("Activity:")) == 2,
                name + ": both live-card activity fields must remain visible.");
            if (rows > grid.DisplayedRowCount(false))
            {
                grid.FirstDisplayedScrollingRowIndex = rows - 1;
                Pump();
                Check(grid.GetRowDisplayRectangle(rows - 1, true).Height >= grid.Rows[rows - 1].Height,
                    name + ": final account cannot be scrolled fully into view.");
                CheckAccountViewport(grid, name + " at final account", false);
                grid.FirstDisplayedScrollingRowIndex = 0;
            }
            grid.CurrentCell = grid.Rows[Math.Min(1, rows - 1)].Cells[1];
            object selected = grid.CurrentRow.Tag;
            TabControl tabs = (TabControl)Field(main, "primaryWorkspace");
            tabs.SelectedIndex = 1; Pump(); tabs.SelectedIndex = 0; Pump();
            Check(object.Equals(grid.CurrentRow.Tag, selected), name + ": account selection lost when returning to Vanilla.");
            CheckAccountViewport(grid, name + " after tab return", false);
            SaveScreenshot(main, Path.Combine(output, name + ".png"));
            Rectangle stable = BoundsIn(grid, main);
            Pump();
            Check(stable == BoundsIn(grid, main), name + ": layout moves after settling.");
            Call(main, "AssertSmokeBackgroundServicesInactive");
        }
        catch (Exception ex)
        {
            failures.Add(name + ": " + ex);
            try { SaveScreenshot(main, Path.Combine(output, name + "-error.png")); } catch { }
        }
    }

    private static int CheckAccountViewport(DataGridView grid, string context, bool writeReport)
    {
        // ClientSize includes DataGridView's managed scrollbar children. Comparing column bounds
        // only to ClientSize would pass even when the rightmost column sits under the scrollbar.
        VScrollBar vertical = grid.Controls.OfType<VScrollBar>().FirstOrDefault(b => b.Visible);
        HScrollBar horizontal = grid.Controls.OfType<HScrollBar>().FirstOrDefault(b => b.Visible);
        int dataRight = vertical == null ? grid.ClientSize.Width - 2 : vertical.Left;
        int totalWidth = 0;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (!column.Visible) continue;
            Rectangle cell = grid.GetColumnDisplayRectangle(column.Index, false);
            totalWidth += column.Width;
            if (writeReport) report.AppendLine("  COLUMN " + column.Name + " width=" + column.Width + " rect=" + cell);
            Check(cell.Width == column.Width && cell.Left >= 0 && cell.Right <= dataRight,
                context + ": column " + column.Name + " is clipped or obscured by a scrollbar (right=" + cell.Right + ", dataRight=" + dataRight + ").");
        }
        Check(totalWidth <= grid.ClientSize.Width, context + ": column widths exceed grid viewport.");
        Check(horizontal == null, context + ": unnecessary horizontal scrollbar despite a fitting account-pane width.");
        if (writeReport) report.AppendLine("  SCROLLBARS vertical=" + (vertical != null) + ", horizontal=" + (horizontal != null) + ", dataRight=" + dataRight);
        return totalWidth;
    }

    private static void SeedAccounts(object recovery, int count)
    {
        Type type = app.GetType("_4RTools.Model.Vanilla.VanillaReconnectAccount", true);
        IList catalog = (IList)Field(recovery, "accountCatalog");
        catalog.Clear();
        for (int i = 0; i < count; i++)
        {
            object account = Activator.CreateInstance(type);
            Property(account, "Id", "layout-account-" + i);
            Property(account, "Enabled", i < 2);
            Property(account, "Label", i == 0 ? "Priest - long account label to test fitting" : "Account " + (i + 1));
            Property(account, "UserName", i == 1 ? "long_username_for_layout_test" : "mock-user-" + (i + 1));
            Property(account, "CharacterSlot", i % 15 + 1);
            Property(account, "ResumeCtrl", false); Property(account, "ResumeAlt", true);
            catalog.Add(account);
        }
        Call(recovery, "SynchronizeSupervisorAccountsFromCatalog");
        object supervisor = Field(recovery, "supervisor");
        Call(supervisor, "Apply", Field(recovery, "settings"), false);
        IDictionary runtimes = (IDictionary)Field(supervisor, "runtimes");
        int n = 0;
        foreach (DictionaryEntry entry in runtimes)
        {
            SetField(entry.Value, "ProcessId", (int?)(12064 + n));
            FieldInfo stage = entry.Value.GetType().GetField("Stage", All);
            stage.SetValue(entry.Value, Enum.Parse(stage.FieldType, n++ == 0 ? "Online" : "Backoff"));
            SetField(entry.Value, "Detail", "Mock recovery detail: waiting for the other client. No real process is controlled.");
        }
        Call(recovery, "RefreshAccountGridFromCatalog");
        TextBox log = (TextBox)Field(recovery, "log");
        log.Text = string.Join(Environment.NewLine, Enumerable.Range(0, 60).Select(i =>
            "18:00:" + (i % 60).ToString("00") + " [MOCK] Account " + (i % 2 + 1) + ": observed gameplay; client remains minimized. Detailed diagnostic entry " + i));
        log.SelectionStart = 0; log.ScrollToCaret();
    }

    private static void SeedFleet(object main)
    {
        Array cards = (Array)Field(Field(main, "integratedFleetDashboard"), "cards");
        Type type = app.GetType("_4RTools.Model.Vanilla.VanillaFleetClientInfo", true);
        for (int i = 0; i < cards.Length; i++)
        {
            object info = Activator.CreateInstance(type);
            Property(info, "ProcessId", 12064 + i);
            Property(info, "CharacterName", i == 0 ? "Mock Novicer" : "Mock Nordina");
            Property(info, "NameVerified", true); Property(info, "HpVerified", true); Property(info, "SpVerified", true);
            Property(info, "CurrentHP", (uint?)3465); Property(info, "MaxHP", (uint?)4187);
            Property(info, "CurrentSP", (uint?)303); Property(info, "MaxSP", (uint?)367);
            Property(info, "Location", "yuno_fild08 (283, 233)"); Property(info, "Activity", "Stationary");
            Call(cards.GetValue(i), "ShowClient", info);
        }
    }

    private static Rectangle BoundsIn(Control control, Control ancestor) { return ancestor.RectangleToClient(control.RectangleToScreen(control.ClientRectangle)); }
    private static bool FullyVisible(Control control, Control root)
    {
        Rectangle bounds = control.RectangleToScreen(control.ClientRectangle);
        for (Control parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (!parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds)) return false;
            if (object.ReferenceEquals(parent, root)) return true;
        }
        return false;
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }
    private static void SaveScreenshot(Form form, string path)
    {
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            Point origin = form.PointToScreen(Point.Empty);
            foreach (Control control in form.Controls.Cast<Control>().Reverse())
            {
                if (!control.Visible || control is MdiClient) continue;
                Rectangle bounds = new Rectangle(origin.X - form.Left + control.Left, origin.Y - form.Top + control.Top, control.Width, control.Height);
                bounds.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                if (bounds.Width > 0 && bounds.Height > 0) control.DrawToBitmap(bitmap, bounds);
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }
    private static void Pump() { for (int i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(10); } }
    private static void Check(bool condition, string message) { if (!condition) failures.Add(message); }
    private static object Field(object target, string name) { return target.GetType().GetField(name, All).GetValue(target); }
    private static void SetField(object target, string name, object value) { target.GetType().GetField(name, All).SetValue(target, value); }
    private static void Property(object target, string name, object value) { target.GetType().GetProperty(name, All).SetValue(target, value, null); }
    private static object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, All).Invoke(target, args); }
    private static object CallStatic(string type, string name, params object[] args) { return app.GetType(type, true).GetMethod(name, All).Invoke(null, args); }
}
