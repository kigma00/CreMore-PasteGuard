using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PasteGuard
{
    public sealed class BlockEvent
    {
        public DateTime TimeUtc { get; internal set; }
        public string ProcessName { get; internal set; }
        public string Reason { get; internal set; }
        public string[] RuleNames { get; internal set; }
    }

    // Opt-in keyboard protection on the current interactive desktop. Native menu
    // commands, WM_PASTE, automation and programs at higher integrity are outside
    // this hook's guarantees. Never writes, clears, persists or logs clipboard data.
    public sealed class WindowsGuard : IDisposable
    {
        public const int MaximumTextLength = 65536;
        private const int WmClipboardUpdate = 0x031D, WmStop = 0x8001;
        private readonly SynchronizationContext context = SynchronizationContext.Current;
        private readonly AutoResetEvent refresh = new AutoResetEvent(false);
        private readonly HashSet<int> suppressedKeys = new HashSet<int>();
        private readonly object lifecycle = new object();
        private volatile State state = new State(new Policy());
        private volatile bool running, disposed;
        private Thread hookThread, clipboardThread;
        private IntPtr windowHandle, hookHandle;
        private HookProc hookProc;
        private Exception startError;
        public event Action<BlockEvent> Blocked;

        private sealed class State
        {
            internal readonly Policy Policy;
            internal readonly HashSet<string> Targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal volatile Cache Cache;
            internal State(Policy policy)
            {
                Policy = policy;
                if (policy.enabled)
                    foreach (Rule rule in policy.rules)
                        if (rule.enabled)
                            foreach (string process in rule.processes)
                                Targets.Add(Path.GetFileName(process));
            }
        }

        private sealed class Cache
        {
            internal uint Sequence;
            internal string Failure;
            internal bool Retry;
            internal readonly Dictionary<string, string[]> Matches = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsRunning { get { return running; } }
        public bool IsClipboardReady
        {
            get
            {
                State current = state;
                Cache cache = current.Cache;
                return running && (current.Targets.Count == 0 || (cache != null && cache.Failure == null && cache.Sequence == GetClipboardSequenceNumber()));
            }
        }

        // Copy the caller's mutable policy before publication. Invalid updates
        // throw while leaving the previous valid snapshot active.
        public void SetPolicy(Policy policy)
        {
            if (disposed) throw new ObjectDisposedException("WindowsGuard");
            List<string> errors = PolicyEngine.Validate(policy);
            if (errors.Count != 0) throw new ArgumentException(String.Join("; ", errors.ToArray()), "policy");
            Policy copy = new Policy { version = policy.version, enabled = policy.enabled };
            foreach (Rule rule in policy.rules)
                copy.rules.Add(new Rule { id = rule.id, name = rule.name, enabled = rule.enabled, pattern = rule.pattern,
                    processes = new List<string>(rule.processes), sites = new List<string>(rule.sites) });
            state = new State(copy);
            refresh.Set();
        }

        public void Start()
        {
            lock (lifecycle)
            {
                if (disposed) throw new ObjectDisposedException("WindowsGuard");
                if (running) return;
                if (hookThread != null && hookThread.IsAlive) throw new InvalidOperationException("Previous hook is still stopping.");
                // One worker remains available between starts. A delayed clipboard
                // renderer can stall it; that never stalls the hook or allows stale data.
                if (clipboardThread == null)
                {
                    clipboardThread = new Thread(ClipboardLoop) { IsBackground = true, Name = "Paste Guard clipboard" };
                    clipboardThread.SetApartmentState(ApartmentState.STA);
                    clipboardThread.Start();
                }
                state.Cache = null;
                startError = null;
                running = true;
                ManualResetEvent ready = new ManualResetEvent(false);
                hookThread = new Thread(delegate() { HookLoop(ready); }) { IsBackground = true, Name = "Paste Guard keyboard" };
                hookThread.SetApartmentState(ApartmentState.STA);
                hookThread.Start();
                if (!ready.WaitOne(5000))
                {
                    running = false;
                    PostMessage(windowHandle, WmStop, IntPtr.Zero, IntPtr.Zero);
                    throw new InvalidOperationException("Keyboard protection did not start in time.");
                }
                ready.Dispose();
                if (startError != null) throw new InvalidOperationException("Keyboard protection could not start.", startError);
                refresh.Set();
            }
        }

        public void Stop()
        {
            lock (lifecycle)
            {
                running = false;
                state.Cache = null;
                refresh.Set();
                if (windowHandle != IntPtr.Zero) PostMessage(windowHandle, WmStop, IntPtr.Zero, IntPtr.Zero);
                if (hookThread != null && hookThread != Thread.CurrentThread) hookThread.Join(2000);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            Stop();
            disposed = true;
            refresh.Set();
            if (clipboardThread == null) refresh.Dispose();
            // Do not wait indefinitely on another application's delayed renderer.
            // The background worker owns/disposes its wait handle when it exits.
        }

        private void HookLoop(ManualResetEvent ready)
        {
            GuardWindow window = null;
            bool listening = false;
            bool signaled = false;
            try
            {
                window = new GuardWindow(this);
                windowHandle = window.Handle;
                if (!AddClipboardFormatListener(windowHandle)) throw new Win32Exception(Marshal.GetLastWin32Error());
                listening = true;
                hookProc = KeyboardHook;
                hookHandle = SetWindowsHookEx(13, hookProc, GetModuleHandle(null), 0);
                if (hookHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                signaled = true;
                ready.Set();
                if (running) Application.Run();
            }
            catch (Exception error) { startError = error; if (!signaled) ready.Set(); }
            finally
            {
                running = false;
                if (hookHandle != IntPtr.Zero) UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
                if (listening) RemoveClipboardFormatListener(windowHandle);
                if (window != null) window.DestroyHandle();
                windowHandle = IntPtr.Zero;
                suppressedKeys.Clear();
                state.Cache = null;
            }
        }

        private sealed class GuardWindow : NativeWindow
        {
            private readonly WindowsGuard owner;
            internal GuardWindow(WindowsGuard owner)
            {
                this.owner = owner;
                CreateHandle(new CreateParams { Caption = "Paste Guard clipboard listener", Parent = new IntPtr(-3) });
            }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == WmClipboardUpdate) owner.refresh.Set();
                if (message.Msg == WmStop) Application.ExitThread();
                base.WndProc(ref message);
            }
        }

        private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
        {
            if (code < 0) return CallNextHookEx(hookHandle, code, message, data);
            int key = Marshal.ReadInt32(data);
            int kind = message.ToInt32();
            bool up = kind == 0x0101 || kind == 0x0105;
            bool down = kind == 0x0100 || kind == 0x0104;
            if (up && suppressedKeys.Remove(key)) return new IntPtr(1);
            if (down && suppressedKeys.Contains(key)) return new IntPtr(1);
            if (!down || !running) return CallNextHookEx(hookHandle, code, message, data);
            bool paste = !IsDown(0x12) && !IsDown(0x5B) && !IsDown(0x5C)
                && ((key == 0x56 && IsDown(0x11)) || (key == 0x2D && IsDown(0x10)));
            if (!paste) return CallNextHookEx(hookHandle, code, message, data);

            State current = state;
            if (current.Targets.Count == 0) return CallNextHookEx(hookHandle, code, message, data);
            string process = ForegroundProcess();
            if (process == null || !current.Targets.Contains(process)) return CallNextHookEx(hookHandle, code, message, data);
            Cache cache = current.Cache;
            string reason = null;
            string[] names = new string[0];
            if (cache == null || cache.Sequence != GetClipboardSequenceNumber())
            {
                reason = "Clipboard inspection is not ready. Try again shortly.";
                refresh.Set();
            }
            else if (cache.Failure != null) reason = cache.Failure;
            else if (cache.Matches.TryGetValue(process, out names) && names.Length != 0) reason = "Clipboard text matched a rule or exceeded its inspection budget.";
            if (reason == null) return CallNextHookEx(hookHandle, code, message, data);
            suppressedKeys.Add(key);
            Notify(new BlockEvent { TimeUtc = DateTime.UtcNow, ProcessName = process, Reason = reason, RuleNames = names ?? new string[0] });
            return new IntPtr(1);
        }

        private void Notify(BlockEvent blocked)
        {
            // Event handlers and UI work must never run inside the hook callback.
            SendOrPostCallback deliver = delegate(object ignored)
            {
                Action<BlockEvent> handler = Blocked;
                if (handler != null && !disposed) { try { handler(blocked); } catch { /* A UI handler cannot break protection. */ } }
            };
            try
            {
                if (context != null) context.Post(deliver, null);
                else ThreadPool.QueueUserWorkItem(delegate(object ignored) { deliver(null); });
            }
            catch { /* The creating UI may already have shut down. */ }
        }

        private void ClipboardLoop()
        {
            try
            {
                while (!disposed)
                {
                    refresh.WaitOne();
                    if (disposed) break;
                    if (!running) continue;
                    State current = state;
                    if (current.Targets.Count == 0) continue;
                    Cache cache = new Cache();
                    string text = null;
                    try
                    {
                        text = ReadClipboard(cache);
                        if (cache.Failure == null && text != null)
                        {
                            var matches = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                            foreach (Rule rule in current.Policy.rules)
                            {
                                if (!running || current != state || cache.Sequence != GetClipboardSequenceNumber()) break;
                                if (!rule.enabled || rule.processes.Count == 0 || !PolicyEngine.MatchText(text, rule.pattern)) continue;
                                foreach (string process in rule.processes)
                                {
                                    List<string> names;
                                    if (!matches.TryGetValue(process, out names)) matches[process] = names = new List<string>();
                                    if (!names.Contains(rule.name)) names.Add(rule.name);
                                }
                            }
                            foreach (var match in matches) cache.Matches[match.Key] = match.Value.ToArray();
                        }
                    }
                    catch { cache.Failure = "Clipboard inspection failed; paste is blocked."; }
                    finally { text = null; } // No clipboard text is retained in published state.
                    if (running && current == state && cache.Sequence == GetClipboardSequenceNumber()) current.Cache = cache;
                    else if (running) refresh.Set();
                    if (cache.Retry && running)
                    {
                        // Busy clipboard owners can recover without changing sequence.
                        Thread.Sleep(250);
                        refresh.Set();
                    }
                }
            }
            finally { refresh.Dispose(); }
        }

        private static string ReadClipboard(Cache cache)
        {
            cache.Sequence = GetClipboardSequenceNumber();
            bool opened = false;
            for (int attempt = 0; attempt < 5 && !opened; attempt++)
            {
                opened = OpenClipboard(IntPtr.Zero);
                if (!opened) Thread.Sleep(20);
            }
            if (!opened) { cache.Retry = true; cache.Failure = "Clipboard is busy or unavailable; paste is blocked."; return null; }
            try
            {
                if (!IsClipboardFormatAvailable(13)) return null;
                IntPtr handle = GetClipboardData(13);
                if (handle == IntPtr.Zero) { cache.Retry = true; cache.Failure = "Clipboard text is unavailable; paste is blocked."; return null; }
                ulong bytes = GlobalSize(handle).ToUInt64();
                if (bytes > (MaximumTextLength + 1UL) * 2UL)
                { cache.Failure = "Clipboard text exceeds the inspection limit; paste is blocked."; return null; }
                if (bytes < 2 || (bytes & 1) != 0)
                { cache.Failure = "Clipboard text format is invalid; paste is blocked."; return null; }
                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero) { cache.Retry = true; cache.Failure = "Clipboard text is unavailable; paste is blocked."; return null; }
                try
                {
                    string value = Marshal.PtrToStringUni(pointer, (int)(bytes / 2));
                    int end = value.IndexOf('\0');
                    if (end < 0) { cache.Failure = "Clipboard text format is invalid; paste is blocked."; return null; }
                    return value.Substring(0, end);
                }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        }

        private static bool IsDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }

        private static string ForegroundProcess()
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            if (pid == 0) return null;
            IntPtr process = OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                StringBuilder path = new StringBuilder(32768);
                int length = path.Capacity;
                return QueryFullProcessImageName(process, 0, path, ref length) ? Path.GetFileName(path.ToString()) : null;
            }
            finally { CloseHandle(process); }
        }

        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc hook, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    }
}
