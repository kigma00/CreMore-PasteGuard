using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PasteGuard;

// Diagnostic only: run from a fresh tests/uitest directory. The form is transparent;
// no protection hooks, clipboard operations, or production policy writes are used.
static class UiLayoutProbe
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checkedControls, clipping;
    static readonly List<string> details = new List<string>();

    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);

    [STAThread]
    static int Main()
    {
        GuardForm form = null;
        string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layout-report.txt");
        try
        {
            string baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            if (baseDir.IndexOf(Path.DirectorySeparatorChar + "uitest" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) < 0 || Directory.Exists(Path.Combine(baseDir, "data")))
                throw new InvalidOperationException("Run only from a fresh isolated tests/uitest directory.");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            form = new GuardForm();
            Field<Timer>(form, "timer").Stop();
            var bridge = Field<Bridge>(form, "bridge");
            if (bridge != null) bridge.Dispose();
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            details.Add("DPI native=" + GetDpiForWindow(form.Handle) + "; managed=" + form.DeviceDpi
                + "; awareness=" + GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext())
                + "; client=" + form.ClientSize + "; font=" + form.Font);
            Size normal = form.Size;
            Size minimum = form.MinimumSize;
            ComboBox preset = Field<ComboBox>(form, "preset");
            foreach (string size in new[] { "normal", "minimum" })
            {
                form.Size = size == "normal" ? normal : minimum;
                for (int view = 0; view < 3; view++)
                {
                    Call(form, "ShowView", view);
                    if (view == 0)
                    {
                        for (int pattern = 0; pattern < 3; pattern++)
                        {
                            preset.SelectedIndex = pattern;
                            Inspect(form, size + "/rules/preset" + pattern);
                        }
                    }
                    else Inspect(form, size + "/view" + view);
                }
            }
            details.Add("Checked=" + checkedControls + "; clipping findings=" + clipping);
            foreach (string line in details) Console.WriteLine(line);
            File.WriteAllLines(report, details.ToArray());
            FieldSet(form, "dirty", false);
            form.Close();
            return clipping == 0 ? 0 : 2;
        }
        catch (Exception error)
        {
            details.Add(error.ToString());
            File.WriteAllLines(report, details.ToArray());
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (form != null)
            {
                FieldSet(form, "dirty", false);
                Field<Timer>(form, "timer").Dispose();
                var bridge = Field<Bridge>(form, "bridge");
                if (bridge != null) bridge.Dispose();
                Field<WindowsGuard>(form, "guard").Dispose();
                if (!form.IsDisposed) form.Close();
                form.Dispose();
            }
        }
    }

    static void Inspect(Form form, string scenario)
    {
        form.PerformLayout();
        Application.DoEvents();
        foreach (Control control in Descendants(form))
        {
            if (!control.Visible || !control.IsHandleCreated) continue;
            var label = control as Label;
            if (label != null && label.Text.Length != 0)
            {
                checkedControls++;
                Size preferred = label.GetPreferredSize(new Size(Math.Max(1, label.Width), 0));
                if (preferred.Height > label.Height + 1)
                    Finding(scenario, label, "text needs height " + preferred.Height + ", has " + label.Height);
            }
            var input = control as TextBox;
            if (input != null && !input.Multiline)
            {
                checkedControls++;
                if (input.Height < input.PreferredHeight)
                    Finding(scenario, input, "single-line input needs height " + input.PreferredHeight + ", has " + input.Height);
            }
            var combo = control as ComboBox;
            if (combo != null)
            {
                checkedControls++;
                if (combo.Height < combo.PreferredHeight)
                    Finding(scenario, combo, "selection needs height " + combo.PreferredHeight + ", has " + combo.Height);
            }
        }
    }

    static void Finding(string scenario, Control control, string reason)
    {
        clipping++;
        string text = control.Text.Replace("\r", "").Replace("\n", " / ");
        if (text.Length > 100) text = text.Substring(0, 100);
        details.Add("CLIP " + scenario + " " + control.GetType().Name + " [" + text + "] " + reason
            + "; bounds=" + control.Bounds + "; font=" + control.Font.SizeInPoints + "pt");
    }

    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }
    static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, Private).GetValue(instance); }
    static void FieldSet(object instance, string name, object value) { instance.GetType().GetField(name, Private).SetValue(instance, value); }
    static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name, Private).Invoke(instance, args); }
}
