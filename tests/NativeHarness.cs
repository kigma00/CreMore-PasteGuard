using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PasteGuard;

// Interactive test only: compile this source twice as NativeHarness.exe and
// NativeOther.exe. Run NativeHarness.exe --run with an EMPTY clipboard.
// --run-preserve-text additionally permits exact in-memory backup/restore of the
// four standard text/locale formats. Any other format is refused, never converted.
// Existing contents are never printed, decoded, or saved to disk.
internal static class NativeHarness
{
    private static int passed;
    private static uint ownedSequence;
    private static bool wroteDummy;
    private static IntPtr clipboardOwner;
    private static Dictionary<uint, byte[]> original = new Dictionary<uint, byte[]>();
    private const string DummySecret = "DUMMY-KEY-123456";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        if (args.Length == 1 && args[0] == "--other")
        {
            using (Form form = Target("Paste Guard unrelated test process")) Application.Run(form);
            return 0;
        }
        if (args.Length == 1 && args[0] == "--lifecycle")
        {
            CheckSnapshot();
            // Empty policy means this test never acquires clipboard text or blocks input.
            using (WindowsGuard guard = new WindowsGuard())
            {
                guard.Start(); Check(guard.IsRunning, "native listener and keyboard hook install");
                guard.Stop(); Check(!guard.IsRunning, "native listener and keyboard hook stop");
                guard.Start(); Check(guard.IsRunning, "native listener and keyboard hook restart");
                guard.Dispose(); Check(!guard.IsRunning, "native listener and keyboard hook dispose");
            }
            return 0;
        }
        if (args.Length == 1 && args[0] == "--snapshot") { CheckSnapshot(); return 0; }
        bool preserveText = args.Length == 1 && args[0] == "--run-preserve-text";
        if (args.Length != 1 || (args[0] != "--run" && !preserveText))
        {
            Console.WriteLine("Interactive test: NativeHarness.exe --run. Requires an empty clipboard and an unlocked desktop.");
            return 77;
        }
        // This checks metadata only. Do not read, save, replace or print a user's clipboard.
        bool opened = false;
        for (int i = 0; i < 20 && !opened; i++) { opened = OpenClipboard(IntPtr.Zero); if (!opened) Thread.Sleep(25); }
        if (!opened)
        {
            int error = Marshal.GetLastWin32Error();
            uint ownerPid;
            GetWindowThreadProcessId(GetOpenClipboardWindow(), out ownerPid);
            Console.WriteLine("SKIP: clipboard unavailable (Win32 " + error + ", session " + Process.GetCurrentProcess().SessionId + ", lock owner PID " + ownerPid + ").");
            return 77;
        }
        bool empty;
        try
        {
            empty = CountClipboardFormats() == 0;
            if (!empty && preserveText)
            {
                List<uint> formats = new List<uint>();
                uint format = 0;
                while ((format = EnumClipboardFormats(format)) != 0)
                {
                    if (format != 1 && format != 7 && format != 13 && format != 16)
                    { Console.WriteLine("SKIP: clipboard includes a non-text format; existing contents were left untouched."); return 77; }
                    formats.Add(format);
                }
                if (formats.Count == 0)
                { Console.WriteLine("SKIP: clipboard formats could not be enumerated; existing contents were left untouched."); return 77; }
                foreach (uint item in formats)
                {
                    IntPtr data = GetClipboardData(item);
                    ulong bytes = data == IntPtr.Zero ? 0 : GlobalSize(data).ToUInt64();
                    if (bytes == 0 || bytes > 1048576)
                    { Console.WriteLine("SKIP: clipboard cannot be backed up within the test limit; existing contents were left untouched."); return 77; }
                    IntPtr pointer = GlobalLock(data);
                    if (pointer == IntPtr.Zero)
                    { Console.WriteLine("SKIP: clipboard backup is unavailable; existing contents were left untouched."); return 77; }
                    try { byte[] copy = new byte[(int)bytes]; Marshal.Copy(pointer, copy, 0, copy.Length); original.Add(item, copy); }
                    finally { GlobalUnlock(data); }
                }
            }
            ownedSequence = GetClipboardSequenceNumber();
        }
        finally { CloseClipboard(); }
        if (!empty && !preserveText) { Console.WriteLine("SKIP: clipboard is not empty; existing contents were left untouched."); return 77; }

        string otherPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NativeOther.exe");
        if (!File.Exists(otherPath)) { Console.WriteLine("SKIP: compile NativeOther.exe from this test source first."); return 77; }
        Process other = null;
        Form clipboardWindow = new Form();
        clipboardOwner = clipboardWindow.Handle;
        try
        {
            using (Form target = Target("Paste Guard protected test process"))
            using (WindowsGuard guard = new WindowsGuard())
            {
                TextBox box = (TextBox)target.Controls[0];
                target.Show();
                Application.DoEvents();
                Policy policy = new Policy();
                policy.rules.Add(new Rule { id = "native-test", name = "Dummy test rule", pattern = "NEVER-MATCH",
                    patterns = new List<string> { "NEVER-MATCH", "DUMMY-KEY-[0-9]+", "DUMMY-KEY-[0-9]+" },
                    processes = new List<string> { Path.GetFileName(Application.ExecutablePath).ToUpperInvariant() } });
                guard.SetPolicy(policy);
                PutDummy(DummySecret);
                guard.Start();
                int events = 0;
                int matchedRuleCount = 0;
                guard.Blocked += delegate(BlockEvent item)
                {
                    if (item.ProcessName.IndexOf("NativeHarness", StringComparison.OrdinalIgnoreCase) >= 0
                        && item.Reason.Length > 0)
                    { matchedRuleCount = item.RuleNames.Length; Interlocked.Increment(ref events); }
                };

                WaitFor(delegate { return guard.IsClipboardReady; }, "clipboard preparation");
                Paste(target.Handle, box.Handle, false);
                Check(ReadOwnBox(box.Handle).Length == 0, "Ctrl+V blocks the second pattern in a rule");
                Check(GetClipboardSequenceNumber() == ownedSequence, "blocked paste leaves clipboard unchanged");
                Paste(target.Handle, box.Handle, true);
                Check(ReadOwnBox(box.Handle).Length == 0, "Shift+Insert blocks a matching rule");
                WaitFor(delegate { return events >= 2; }, "metadata-only block notifications");
                Check(matchedRuleCount == 1, "multiple matching patterns report one rule");

                PutDummy("ordinary dummy note");
                WaitFor(delegate { return guard.IsClipboardReady; }, "benign clipboard preparation");
                Paste(target.Handle, box.Handle, false);
                Check(ReadOwnBox(box.Handle) == "ordinary dummy note", "non-matching text is allowed");
                SendMessage(box.Handle, 0x000C, IntPtr.Zero, "");
                Paste(target.Handle, box.Handle, true);
                Check(ReadOwnBox(box.Handle) == "ordinary dummy note", "Shift+Insert permits non-matching text");

                SendMessage(box.Handle, 0x000C, IntPtr.Zero, "");
                PutDummy(DummySecret);
                WaitFor(delegate { return guard.IsClipboardReady; }, "matching clipboard preparation");
                policy.rules[0].patterns.Clear(); // Snapshot is independent of caller mutation.
                policy.rules[0].patterns.Add("NEVER-MATCH");
                Paste(target.Handle, box.Handle, false);
                Check(ReadOwnBox(box.Handle).Length == 0, "caller mutation does not change the active policy");
                guard.SetPolicy(policy);
                WaitFor(delegate { return guard.IsClipboardReady; }, "updated policy preparation");
                Paste(target.Handle, box.Handle, false);
                Check(ReadOwnBox(box.Handle) == DummySecret, "policy update refreshes cached matches");

                policy.rules[0].patterns.Add("DUMMY-KEY-[0-9]+");
                guard.SetPolicy(policy);
                WaitFor(delegate { return guard.IsClipboardReady; }, "restored policy preparation");
                other = Process.Start(new ProcessStartInfo(otherPath, "--other") { UseShellExecute = false });
                WaitFor(delegate { other.Refresh(); return other.MainWindowHandle != IntPtr.Zero; }, "unrelated test process");
                IntPtr otherWindow = other.MainWindowHandle;
                IntPtr otherBox = FindOwnTextBox(otherWindow, other.Id);
                if (otherBox == IntPtr.Zero) throw new InvalidOperationException("Unrelated test textbox was not found.");
                Paste(otherWindow, otherBox, false);
                Check(ReadOwnBox(otherBox) == DummySecret, "another executable remains outside the policy scope");

                SendMessage(box.Handle, 0x000C, IntPtr.Zero, "");
                PutDummy(new string('x', WindowsGuard.MaximumTextLength + 1));
                // A failing inspection intentionally never reports ready; allow acquisition time.
                Pump(300);
                Paste(target.Handle, box.Handle, false);
                Check(ReadOwnBox(box.Handle).Length == 0, "oversized clipboard is blocked conservatively");
                Check(GetClipboardSequenceNumber() == ownedSequence, "oversized clipboard stays unchanged");

                PutDummy(DummySecret);
                guard.Stop();
                Paste(target.Handle, box.Handle, false);
                Check(ReadOwnBox(box.Handle) == DummySecret, "Stop removes keyboard protection");
                guard.Start();
                WaitFor(delegate { return guard.IsClipboardReady; }, "restart preparation");
                SendMessage(box.Handle, 0x000C, IntPtr.Zero, "");
                Paste(target.Handle, box.Handle, false);
                Check(ReadOwnBox(box.Handle).Length == 0, "Start reinstalls protection after Stop");
            }
            Console.WriteLine("PASS: " + passed + " native keyboard checks.");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL: " + error.Message);
            return 1;
        }
        finally
        {
            if (other != null)
            {
                try { if (!other.HasExited) { other.CloseMainWindow(); other.WaitForExit(1500); } }
                catch { }
                other.Dispose();
            }
            try { RestoreOriginal(); }
            finally
            {
                foreach (byte[] data in original.Values) Array.Clear(data, 0, data.Length);
                original.Clear();
                clipboardWindow.Dispose();
            }
        }
    }

    private static void CheckSnapshot()
    {
        using (var guard = new WindowsGuard())
        {
            var rule = new Rule { id = "snapshot", name = "Snapshot", pattern = "LEGACY",
                patterns = new List<string> { "FIRST", "SECOND" }, processes = new List<string> { "Snapshot.exe" } };
            var policy = new Policy { rules = new List<Rule> { rule } };
            guard.SetPolicy(policy);
            FieldInfo stateField = typeof(WindowsGuard).GetField("state", BindingFlags.NonPublic | BindingFlags.Instance);
            object state = stateField.GetValue(guard);
            var snapshot = (Policy)state.GetType().GetField("Policy", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(state);
            rule.patterns[1] = "MUTATED";
            Check(PolicyEngine.RuleMatches(snapshot.rules[0], "SECOND"), "native snapshot preserves additional patterns");
            Check(!PolicyEngine.RuleMatches(snapshot.rules[0], "MUTATED"), "native snapshot deep-copies the pattern list");
            rule.patterns[1] = "(invalid)";
            bool rejected = false;
            try { guard.SetPolicy(policy); } catch (ArgumentException) { rejected = true; }
            Check(rejected && Object.ReferenceEquals(state, stateField.GetValue(guard)), "invalid additional pattern preserves active snapshot");
            rule.patterns = null; policy.version = 1;
            guard.SetPolicy(policy);
            state = stateField.GetValue(guard);
            snapshot = (Policy)state.GetType().GetField("Policy", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(state);
            Check(snapshot.version == 1 && PolicyEngine.RuleMatches(snapshot.rules[0], "LEGACY"), "native snapshot accepts legacy single-pattern policies");
        }
    }

    private static Form Target(string title)
    {
        Form form = new Form { Text = title, Width = 560, Height = 220, StartPosition = FormStartPosition.CenterScreen };
        form.Controls.Add(new TextBox { Multiline = true, Dock = DockStyle.Fill, ShortcutsEnabled = true });
        return form;
    }

    private static void PutDummy(string value)
    {
        IntPtr allocation = GlobalAlloc(2, new UIntPtr((uint)((value.Length + 1) * 2)));
        if (allocation == IntPtr.Zero) throw new InvalidOperationException("Unable to allocate dummy clipboard text.");
        try
        {
            IntPtr pointer = GlobalLock(allocation);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Unable to prepare dummy clipboard text.");
            try { char[] characters = (value + "\0").ToCharArray(); Marshal.Copy(characters, 0, pointer, characters.Length); }
            finally { GlobalUnlock(allocation); }
            if (!OpenClipboard(clipboardOwner)) throw new InvalidOperationException("Clipboard became busy; no replacement made.");
            try
            {
                if (GetClipboardSequenceNumber() != ownedSequence)
                    throw new InvalidOperationException("Clipboard changed outside the harness; stopping without replacing it.");
                if (!EmptyClipboard()) throw new InvalidOperationException("Unable to prepare the test clipboard.");
                if (SetClipboardData(13, allocation) == IntPtr.Zero) throw new InvalidOperationException("Unable to install dummy clipboard text.");
                allocation = IntPtr.Zero; // Clipboard owns this allocation now.
                wroteDummy = true;
                ownedSequence = GetClipboardSequenceNumber();
            }
            finally { CloseClipboard(); }
        }
        finally { if (allocation != IntPtr.Zero) GlobalFree(allocation); }
    }

    private static void RestoreOriginal()
    {
        if (!wroteDummy) return;
        var allocations = new Dictionary<uint, IntPtr>();
        try
        {
            // Allocate all copies before touching the current clipboard.
            foreach (var item in original)
            {
                IntPtr handle = GlobalAlloc(2, new UIntPtr((uint)item.Value.Length));
                if (handle == IntPtr.Zero) throw new InvalidOperationException("Unable to allocate clipboard restoration data.");
                allocations.Add(item.Key, handle);
                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero) throw new InvalidOperationException("Unable to prepare clipboard restoration data.");
                try { Marshal.Copy(item.Value, 0, pointer, item.Value.Length); }
                finally { GlobalUnlock(handle); }
            }
            bool opened = false;
            for (int attempt = 0; attempt < 100 && !opened; attempt++) { opened = OpenClipboard(clipboardOwner); if (!opened) Thread.Sleep(20); }
            if (!opened) throw new InvalidOperationException("Unable to restore clipboard because it remains busy.");
            try
            {
                if (GetClipboardSequenceNumber() != ownedSequence)
                { Console.WriteLine("Clipboard changed outside the harness; the newer clipboard was preserved."); return; }
                if (!EmptyClipboard()) throw new InvalidOperationException("Unable to restore the original clipboard.");
                foreach (uint format in new List<uint>(allocations.Keys))
                {
                    if (SetClipboardData(format, allocations[format]) == IntPtr.Zero)
                        throw new InvalidOperationException("A text clipboard format could not be restored.");
                    allocations[format] = IntPtr.Zero;
                }
                Console.WriteLine(original.Count == 0 ? "Dummy clipboard removed." : "Original text clipboard formats restored.");
            }
            finally { CloseClipboard(); }
        }
        finally { foreach (IntPtr handle in allocations.Values) if (handle != IntPtr.Zero) GlobalFree(handle); }
    }

    private static void Paste(IntPtr window, IntPtr textbox, bool shiftInsert)
    {
        if (!SetForegroundWindow(window)) throw new InvalidOperationException("Unable to foreground the test window; no input sent.");
        SendMessage(window, 0x0028, textbox, new IntPtr(1)); // WM_NEXTDLGCTL focuses only our known textbox.
        Pump(80);
        if (GetForegroundWindow() != window) throw new InvalidOperationException("Test window lost focus; no input sent.");
        ushort modifier = (ushort)(shiftInsert ? 0x10 : 0x11), key = (ushort)(shiftInsert ? 0x2D : 0x56);
        INPUT[] inputs = { Key(modifier, false), Key(key, false), Key(key, true), Key(modifier, true) };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) != inputs.Length)
            throw new InvalidOperationException("Test input could not be delivered (desktop or integrity restriction).");
        Pump(120);
    }

    private static INPUT Key(ushort code, bool up)
    { return new INPUT { type = 1, data = new InputUnion { keyboard = new KEYBDINPUT { key = code, flags = (code == 0x2D ? 1U : 0U) | (up ? 2U : 0U) } } }; }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        passed++;
        Console.WriteLine("PASS: " + label);
    }

    private static void WaitFor(Func<bool> condition, string label)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 5000) Pump(20);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }

    private static void Pump(int milliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(5); } while (clock.ElapsedMilliseconds < milliseconds);
    }

    private static string ReadOwnBox(IntPtr box)
    {
        StringBuilder text = new StringBuilder(70000);
        SendMessage(box, 0x000D, new IntPtr(text.Capacity), text);
        return text.ToString();
    }

    private static IntPtr FindOwnTextBox(IntPtr parent, int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr child, IntPtr ignored)
        {
            uint pid;
            GetWindowThreadProcessId(child, out pid);
            StringBuilder name = new StringBuilder(256);
            GetClassName(child, name, name.Capacity);
            if (pid == processId && name.ToString().IndexOf("EDIT", StringComparison.OrdinalIgnoreCase) >= 0) { found = child; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT keyboard;
        [FieldOffset(0)] public MOUSEINPUT mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int x, y; public uint data, flags, time; public UIntPtr extra; }
    private delegate bool EnumWindowProc(IntPtr window, IntPtr argument);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] input, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr argument);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, string lparam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, StringBuilder lparam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern int CountClipboardFormats();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern IntPtr GetOpenClipboardWindow();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr handle);
}
