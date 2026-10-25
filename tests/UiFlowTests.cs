using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using PasteGuard;

// Runs with an isolated executable directory under tests/uitest. No windows are
// shown, no keyboard hooks are started, and the clipboard is never read/written.
static class UiFlowTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int assertions;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr handle, int message, StringBuilder text, IntPtr capacity);

    [STAThread]
    static int Main()
    {
        GuardForm form = null;
        try
        {
            string baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            if (baseDir.IndexOf(Path.DirectorySeparatorChar + "uitest" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Run only from an isolated tests/uitest directory.");
            if (Directory.Exists(Path.Combine(baseDir, "data")))
                throw new InvalidOperationException("Use a fresh isolated directory for each run.");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            form = new GuardForm();
            HintTextBox name = Field<HintTextBox>(form, "name"), processes = Field<HintTextBox>(form, "processes"),
                sites = Field<HintTextBox>(form, "sites"), pattern = Field<HintTextBox>(form, "pattern"), sample = Field<HintTextBox>(form, "sample");
            ListBox list = Field<ListBox>(form, "list");
            ComboBox preset = Field<ComboBox>(form, "preset");
            CheckBox enabled = Field<CheckBox>(form, "enabled");
            string keyBefore = Field<string>(form, "key");
            byte[] storedKeyBefore = File.ReadAllBytes(Path.Combine(baseDir, "data", "pairing.key"));

            Check(!Field<bool>(form, "dirty"), "Loading the empty first-run editor is clean.");
            Check(!Field<bool>(form, "running"), "No protection hooks were requested.");
            Check(Field<Policy>(form, "policy").rules.Count == 0 && list.Items.Count == 0,
                "First launch does not create a demo rule.");
            Check(Field<int>(form, "editing") == -1 && list.SelectedIndex == -1,
                "First launch opens an unsaved new rule.");
            Check(name.TextLength == 0 && processes.TextLength == 0 && sites.TextLength == 0 && sample.TextLength == 0,
                "Example names, targets, and sample strings are not prefilled data.");
            Check(!File.Exists(Path.Combine(baseDir, "data", "policy.json")),
                "First launch does not persist example rules.");
            Check(new[] { name, processes, sites, pattern, sample }.All(input => input.Hint.Length > 0),
                "Empty inputs provide visual guidance.");
            Check(sample.Hint.Contains(RulePresets.Example(0)), "The sample hint illustrates the selected test pattern.");
            Rule emptyDraft = (Rule)Call(form, "EditedRule");
            Check(emptyDraft.name == "" && emptyDraft.processes.Count == 0 && emptyDraft.sites.Count == 0,
                "Reading an empty editor does not turn hints into rule data.");
            Check(emptyDraft.pattern == RulePresets.Pattern(0) && preset.SelectedIndex == 0,
                "The selected detector remains an actual pattern.");
            foreach (HintTextBox input in new[] { name, processes, sites, pattern, sample }) CheckNativeHint(input);
            Check(!Field<bool>(form, "dirty"), "Creating native hint controls does not dirty the policy.");
            sample.Text = "Synthetic sample only";
            Check(!Field<bool>(form, "dirty"), "Test text does not dirty the policy.");

            // Create an explicit saved fixture now that first launch has no demo rule.
            name.Text = "Fixture rule";
            processes.Text = "Editor.exe";
            Check((bool)Call(form, "SaveRule"), "The first explicit rule saves successfully.");
            Check(Field<Policy>(form, "policy").rules.Count == 1 && list.SelectedIndex == 0,
                "Saving the first rule selects it.");

            // Save an existing rule with a custom pattern and hand-written targets.
            string initialId = Field<Policy>(form, "policy").rules[0].id;
            name.Text = "Custom existing rule";
            processes.Text = "Editor.exe";
            sites.Text = "private.example.com";
            preset.SelectedIndex = 2;
            pattern.Text = "INTERNAL_[A-Z]{8}";
            enabled.Checked = false;
            Check(Field<bool>(form, "dirty"), "Editing a rule marks changes unsaved.");
            Check((bool)Call(form, "SaveRule"), "Existing rule saves successfully.");
            Policy policy = Field<Policy>(form, "policy");
            Check(policy.rules[0].id == initialId, "Saving preserves the existing rule ID.");
            Check(policy.rules[0].pattern == "INTERNAL_[A-Z]{8}", "Saving preserves the custom pattern.");
            Check(!policy.rules[0].enabled, "Saving preserves the disabled state.");
            Check(!Field<bool>(form, "dirty"), "Successful save clears unsaved state.");

            // A fresh save must keep the newly added rule selected.
            Call(form, "EditRule", -1);
            Check(name.TextLength == 0 && processes.TextLength == 0 && sites.TextLength == 0 && sample.TextLength == 0,
                "Adding another rule opens empty fields instead of example data.");
            name.Text = "New test rule";
            processes.Text = "Other.exe";
            sites.Text = "other.example.com";
            Check((bool)Call(form, "SaveRule"), "A new rule saves successfully.");
            policy = Field<Policy>(form, "policy");
            Check(policy.rules.Count == 2, "A new save adds one rule.");
            Check(Field<int>(form, "editing") == 1 && list.SelectedIndex == 1,
                "The newly saved rule remains selected and is being edited.");
            Check(name.Text == "New test rule", "The new rule stays visible after save.");

            // Ordinary selection events must preserve saved custom content.
            list.SelectedIndex = 0;
            Check(Field<int>(form, "editing") == 0 && name.Text == "Custom existing rule",
                "List selection loads the matching rule.");
            Check(preset.SelectedIndex == 2 && pattern.Text == "INTERNAL_[A-Z]{8}",
                "Selecting a saved custom rule does not replace its pattern.");
            Check(processes.Text == "Editor.exe" && sites.Text == "private.example.com",
                "Selecting a saved rule retains manual targets.");
            Check(!Field<bool>(form, "dirty"), "Rule selection itself is clean.");

            // Invoke a real quick-add Click handler without showing the form.
            processes.Text = "Editor.exe, slack.exe";
            sites.Text = "private.example.com, slack.com";
            Button slack = Descendants(form).OfType<Button>().Single(b => b.Text == "+ Slack");
            typeof(Button).GetMethod("OnClick", Private).Invoke(slack, new object[] { EventArgs.Empty });
            Check(RulePresets.ParseTargets(processes.Text).SequenceEqual(new[] { "Editor.exe", "slack.exe" }),
                "Quick-add preserves custom executables and deduplicates case-insensitively.");
            Check(RulePresets.ParseTargets(sites.Text).SequenceEqual(new[] { "private.example.com", "slack.com", "*.slack.com" }),
                "Quick-add preserves custom domains while adding missing preset targets.");
            Check(pattern.Text == "INTERNAL_[A-Z]{8}", "Quick-add preserves the custom pattern.");

            sample.Text = "Manually typed synthetic sample";
            string customSampleHint = sample.Hint;
            preset.SelectedIndex = 1;
            Check(pattern.Text == RulePresets.Pattern(1), "Choosing a preset updates the effective pattern.");
            Check(name.Text == "Custom existing rule", "Choosing a preset keeps the rule name.");
            Check(sample.Text == "Manually typed synthetic sample", "Choosing a preset preserves typed test text.");
            Check(sample.Hint != customSampleHint && sample.Hint.Contains(RulePresets.Example(1)),
                "Choosing a preset updates its sample hint.");
            CheckNativeHint(sample);
            preset.SelectedIndex = 2;
            Check(pattern.Text == RulePresets.Pattern(1), "Switching to direct entry retains the current pattern.");
            Check(sample.Text == "Manually typed synthetic sample", "Switching to direct entry preserves typed test text.");
            sample.Clear();
            Check(sample.TextLength == 0, "Clearing typed test text restores a hint without inserting sample data.");
            CheckNativeHint(sample);
            pattern.Text = "CUSTOM_[0-9]{6}";
            Check((bool)Call(form, "SaveRule"), "Updated custom rule saves successfully.");
            policy = Field<Policy>(form, "policy");
            Check(policy.rules[0].processes.SequenceEqual(new[] { "Editor.exe", "slack.exe" }),
                "Saving retains custom and quick-added app targets.");
            Check(policy.rules[0].sites.SequenceEqual(new[] { "private.example.com", "slack.com", "*.slack.com" }),
                "Saving retains custom and quick-added site targets.");
            Check(policy.rules[0].id == initialId && policy.rules[1].name == "New test rule",
                "Editing one rule preserves its ID and the other saved rule.");

            var persisted = new JavaScriptSerializer().Deserialize<Policy>(File.ReadAllText(Path.Combine(baseDir, "data", "policy.json")));
            Check(persisted.rules.Count == 2 && persisted.rules[0].pattern == "CUSTOM_[0-9]{6}",
                "The expected rules and custom pattern are persisted.");
            Check(Field<string>(form, "key") == keyBefore && File.ReadAllBytes(Path.Combine(baseDir, "data", "pairing.key")).SequenceEqual(storedKeyBefore),
                "Rule editing preserves the pairing key and its stored bytes.");
            Check(!Field<bool>(form, "running"), "No test started Windows protection.");
            Console.WriteLine("PASS: " + assertions + " UI-flow assertions; no dialogs, hooks, clipboard, or visible UI used.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.ToString());
            return 1;
        }
        finally
        {
            // Dispose background resources explicitly; disposing an unshown Form
            // does not raise FormClosed and therefore does not run its handlers.
            if (form != null)
            {
                Field<Timer>(form, "timer").Dispose();
                var bridge = Field<Bridge>(form, "bridge");
                if (bridge != null) bridge.Dispose();
                Field<WindowsGuard>(form, "guard").Dispose();
                form.Dispose();
            }
        }
    }

    static T Field<T>(object instance, string field) { return (T)instance.GetType().GetField(field, Private).GetValue(instance); }
    static object Call(object instance, string method, params object[] args) { return instance.GetType().GetMethod(method, Private).Invoke(instance, args); }
    static void CheckNativeHint(HintTextBox input)
    {
        string actualText = input.Text;
        var cue = new StringBuilder(512);
        // EM_GETCUEBANNER reads the native guidance, including after handle creation.
        IntPtr result = SendMessage(input.Handle, 0x1502, cue, new IntPtr(cue.Capacity));
        Check(result != IntPtr.Zero && cue.ToString() == input.Hint,
            "The native edit control displays its hint: " + input.AccessibleName);
        Check(input.Text == actualText, "Native cue text leaves the real value unchanged: " + input.AccessibleName);
    }
    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control control in root.Controls)
        {
            yield return control;
            foreach (Control child in Descendants(control)) yield return child;
        }
    }
    static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException("FAIL: " + message);
        assertions++;
    }
}
