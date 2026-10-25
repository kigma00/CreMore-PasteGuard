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
    static int checkedControls, checkedButtons, clipping;
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
            TextBox sample = Field<TextBox>(form, "sample");
            TextBox customPattern = Field<TextBox>(form, "pattern");
            foreach (string size in new[] { "normal", "minimum" })
            {
                form.Size = size == "normal" ? normal : minimum;
                details.Add("Scenario " + size + "; window=" + form.Size + "; client=" + form.ClientSize);
                for (int view = 0; view < 3; view++)
                {
                    Call(form, "ShowView", view);
                    if (view == 0)
                    {
                        for (int pattern = 0; pattern < 3; pattern++)
                        {
                            preset.SelectedIndex = pattern;
                            string scenario = size + "/rules/preset" + pattern;
                            Inspect(form, scenario);
                            // Exercise the longest real dirty-state and result captions
                            // using synthetic text only; do not save or enable protection.
                            Call(form, "MarkDirty");
                            if (pattern == 2) customPattern.Text = "LAYOUT_[A-Z]{4}";
                            sample.Text = pattern == 2 ? "LAYOUT_PASS" : RulePresets.Example(pattern);
                            Call(form, "TestRule");
                            Inspect(form, scenario + "/match");
                            sample.Text = "Harmless layout probe sample";
                            Call(form, "TestRule");
                            Inspect(form, scenario + "/no-match");
                            if (pattern == 2)
                            {
                                customPattern.Text = "(";
                                Call(form, "TestRule");
                                Inspect(form, scenario + "/invalid-pattern");
                            }
                        }
                    }
                    else Inspect(form, size + "/view" + view);
                }
            }
            if (Field<bool>(form, "running") || Field<WindowsGuard>(form, "guard").IsRunning)
                throw new InvalidOperationException("The layout probe must never enable protection hooks.");
            details.Add("Checked=" + checkedControls + "; buttons=" + checkedButtons + "; clipping findings=" + clipping);
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
                if (preferred.Width > label.Width + 1)
                    Finding(scenario, label, "text needs width " + preferred.Width + ", has " + label.Width);
                if (preferred.Height > label.Height + 1)
                    Finding(scenario, label, "text needs height " + preferred.Height + ", has " + label.Height);
                CheckParentTextBounds(scenario, label, label.TextAlign, preferred - label.Padding.Size);
            }
            var button = control as ButtonBase;
            if (button != null && button.Text.Length != 0)
            {
                checkedControls++; checkedButtons++;
                // All current actions are single-line captions. Unconstrained preferred
                // width includes the native button's text padding (or checkbox glyph),
                // which plain glyph measurement and label-only checks would miss.
                Size preferred = button.GetPreferredSize(Size.Empty);
                if (preferred.Width > button.Width + 1)
                    Finding(scenario, button, "single-line caption needs width " + preferred.Width + ", has " + button.Width);
                if (preferred.Height > button.Height + 1)
                    Finding(scenario, button, "caption needs height " + preferred.Height + ", has " + button.Height);
                if (button is Button)
                {
                    Size caption = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    CheckParentTextBounds(scenario, button, button.TextAlign, caption);
                }
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

    static void CheckParentTextBounds(string scenario, Control control, ContentAlignment alignment, Size textSize)
    {
        Control parent = control.Parent;
        var scrolling = parent as ScrollableControl;
        // Content may legitimately sit outside a scrollable viewport. Its own
        // controls still have to fit their immediate row or non-scrolling panel.
        if (parent == null || (scrolling != null && scrolling.AutoScroll)) return;
        Rectangle viewport = parent.ClientRectangle;
        Rectangle content = new Rectangle(control.Left + control.Padding.Left, control.Top + control.Padding.Top,
            control.ClientSize.Width - control.Padding.Horizontal, control.ClientSize.Height - control.Padding.Vertical);
        int x = content.Left, y = content.Top;
        if (alignment == ContentAlignment.TopCenter || alignment == ContentAlignment.MiddleCenter || alignment == ContentAlignment.BottomCenter)
            x += (content.Width - textSize.Width) / 2;
        else if (alignment == ContentAlignment.TopRight || alignment == ContentAlignment.MiddleRight || alignment == ContentAlignment.BottomRight)
            x += content.Width - textSize.Width;
        if (alignment == ContentAlignment.MiddleLeft || alignment == ContentAlignment.MiddleCenter || alignment == ContentAlignment.MiddleRight)
            y += (content.Height - textSize.Height) / 2;
        else if (alignment == ContentAlignment.BottomLeft || alignment == ContentAlignment.BottomCenter || alignment == ContentAlignment.BottomRight)
            y += content.Height - textSize.Height;
        Rectangle text = new Rectangle(x, y, textSize.Width, textSize.Height);
        if (text.Left < viewport.Left - 1 || text.Right > viewport.Right + 1
            || text.Top < viewport.Top - 1 || text.Bottom > viewport.Bottom + 1)
            Finding(scenario, control, "text " + text + " extends outside parent " + parent.GetType().Name + " " + viewport);
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
