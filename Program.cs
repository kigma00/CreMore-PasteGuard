using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("CreMore PasteGuard")]
[assembly: System.Reflection.AssemblyProduct("CreMore PasteGuard")]
[assembly: System.Reflection.AssemblyDescription("앱과 웹사이트의 민감정보 붙여넣기 보호")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace PasteGuard
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                using (var single = new System.Threading.Mutex(true, "Local\\PasteGuard.Desktop", out created))
                {
                    if (!created) { MessageBox.Show("CreMore PasteGuard가 이미 실행 중입니다."); return; }
                    Application.Run(new GuardForm());
                }
            }
            catch (Exception error)
            {
                MessageBox.Show("CreMore PasteGuard를 시작하지 못했습니다. 폴더 쓰기 권한과 Windows 계정을 확인하세요.\n오류 유형: "
                    + error.GetType().Name, "CreMore PasteGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        static bool created;
    }

    sealed partial class GuardForm : Form
    {
        readonly string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly WindowsGuard guard = new WindowsGuard();
        readonly ListBox list = new ListBox();
        readonly HintTextBox name = new HintTextBox(), processes = new HintTextBox(), sites = new HintTextBox(), pattern = new HintTextBox(), sample = new HintTextBox();
        readonly CheckBox enabled = new CheckBox { Text = "이 규칙 사용", Checked = true, AutoSize = true };
        readonly Label state = new Label(), connection = new Label(), testResult = new Label();
        readonly Button toggle = new Button();
        readonly ListView events = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
        readonly Timer timer = new Timer { Interval = 2000 };
        Policy policy;
        Bridge bridge;
        string key;
        bool running, loading;
        int editing = -1;

        public GuardForm()
        {
            SuspendLayout();
            Text = "CreMore PasteGuard — 붙여넣기 보호";
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("CreMore.Icon"))
            using (var source = new Icon(stream)) Icon = (Icon)source.Clone();
            ClientSize = new Size(1120, 790); MinimumSize = new Size(920, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiFont(11); BackColor = Color.FromArgb(245, 247, 251);
            Directory.CreateDirectory(folder);
            policy = LoadPolicy();
            policy.enabled = true; // The Start/Stop control owns global runtime protection.
            key = LoadKey();
            BuildUi(); RefreshRules();
            foreach (var input in new[] { name, processes, sites, pattern }) input.TextChanged += (s, e) => MarkDirty();
            enabled.CheckedChanged += (s, e) => MarkDirty();
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);
            guard.Blocked += e => OnUi(() => AddEvent(e.ProcessName, String.Join(", ", e.RuleNames),
                e.RuleNames.Length > 0 ? "패턴 일치 또는 검사 한도 초과 — 차단" : "검사 준비 중 또는 읽기 실패 — 차단"));
            try
            {
                bridge = new Bridge(key, GetPolicyJson, OnBrowserEvent);
                bridge.Start();
            }
            catch (Exception) { connection.Text = "브라우저 연결 실패: 17843 포트 사용 여부를 확인하세요."; }
            timer.Tick += (s, e) => UpdateStatus(); timer.Start(); UpdateStatus();
            Shown += (s, e) =>
            {
                var area = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 24), Math.Min(MinimumSize.Height, area.Height - 24));
                Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                list.ItemHeight = LogicalToDeviceUnits(58);
            };
            DpiChanged += (s, e) => list.ItemHeight = LogicalToDeviceUnits(58);
            FormClosing += (s, e) => { if (!ConfirmLeave()) e.Cancel = true; };
            FormClosed += (s, e) => { timer.Dispose(); guard.Dispose(); if (bridge != null) bridge.Dispose(); };
        }

        void RefreshRules()
        {
            loading = true; list.BeginUpdate(); list.Items.Clear();
            foreach (var rule in policy.rules) list.Items.Add(rule.name);
            if (policy.rules.Count > 0) list.SelectedIndex = Math.Max(0, Math.Min(editing, policy.rules.Count - 1));
            list.EndUpdate(); loading = false; EditRule(list.SelectedIndex); UpdateStatus();
        }
        void EditRule(int index)
        {
            loading = true;
            editing = index; var r = index < 0 ? new Rule { name = "", enabled = true, pattern = RulePresets.Pattern(0) } : policy.rules[index];
            name.Text = r.name; processes.Text = String.Join(", ", r.processes ?? new List<string>());
            sites.Text = String.Join(", ", r.sites ?? new List<string>()); pattern.Text = r.pattern ?? ""; enabled.Checked = r.enabled; testResult.Text = "";
            preset.SelectedIndex = RulePresets.PatternIndex(pattern.Text); ConfigurePattern(); sample.Clear(); UpdateSampleHint();
            testResult.Text = "실제 자격증명 대신 테스트 문자열을 사용하세요."; testResult.ForeColor = Muted;
            if (index < 0) list.ClearSelected(); else list.SelectedIndex = index;
            deleteButton.Enabled = index >= 0; loading = false; dirty = false;
            saveStatus.Text = index < 0 ? "새 규칙" : "저장된 규칙";
        }
        Rule EditedRule()
        {
            return new Rule { id = editing >= 0 ? policy.rules[editing].id : Guid.NewGuid().ToString("N"), name = name.Text.Trim(), enabled = enabled.Checked,
                processes = Split(processes.Text), sites = Split(sites.Text), pattern = pattern.Text };
        }
        static List<string> Split(string value) { return value.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        bool SaveRule()
        {
            var r = EditedRule(); var next = json.Deserialize<Policy>(json.Serialize(policy));
            if (editing < 0) next.rules.Add(r); else next.rules[editing] = r;
            var errors = PolicyEngine.Validate(next);
            if (errors.Count > 0) { MessageBox.Show(ValidationMessage(r), "규칙을 확인하세요"); return false; }
            try {
                Persist(next); if (editing < 0) editing = next.rules.Count - 1;
                policy = next; dirty = false; guard.SetPolicy(policy); RefreshRules();
                saveStatus.Text = "저장 완료"; saveStatus.ForeColor = Accent; return true;
            }
            catch (Exception) { MessageBox.Show("규칙을 저장하지 못했습니다. 폴더 쓰기 권한을 확인하세요."); return false; }
        }
        void DeleteRule()
        {
            if (editing < 0) return;
            if (MessageBox.Show(this, "‘" + policy.rules[editing].name + "’ 규칙을 삭제할까요?", "규칙 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var next = json.Deserialize<Policy>(json.Serialize(policy)); next.rules.RemoveAt(editing);
            try { Persist(next); policy = next; guard.SetPolicy(policy); dirty = false; editing = Math.Min(editing, next.rules.Count - 1); RefreshRules(); }
            catch (Exception) { MessageBox.Show("규칙을 삭제하지 못했습니다."); }
        }
        void TestRule()
        {
            if (sample.Text.Length == 0) { testResult.Text = "먼저 테스트할 문자열을 입력하세요."; return; }
            try {
                bool match = PolicyEngine.MatchText(sample.Text, pattern.Text);
                testResult.Text = match ? "차단 조건에 해당해요. 패턴 일치 또는 검사 한도 초과입니다." : "이 검사 패턴에 해당하지 않아요.";
                testResult.ForeColor = match ? Accent : Muted;
            }
            catch (ArgumentException) { testResult.Text = "검사 패턴을 확인하세요. ‘패턴 도움말’을 참고하세요."; testResult.ForeColor = Danger; }
        }
        void SetRunning(bool value)
        {
            if (value && dirty && !ConfirmLeave()) return;
            try
            {
                if (value) { guard.SetPolicy(policy); guard.Start(); } else guard.Stop();
                running = value; toggle.Text = value ? "보호 중지" : "보호 시작";
                state.Text = value ? "Windows 보호 켜짐" : "보호 꺼짐";
                state.ForeColor = value ? Color.ForestGreen : Color.DimGray;
            }
            catch (Exception)
            {
                running = false; toggle.Text = "보호 시작"; state.Text = "보호 시작 실패"; state.ForeColor = Color.Firebrick;
                MessageBox.Show("보호를 시작하지 못했습니다. 앱을 다시 실행하세요.");
            }
            UpdateStatus();
        }
        string GetPolicyJson()
        {
            string result = null;
            OnUi(() => { var snapshot = json.Deserialize<Policy>(json.Serialize(policy)); snapshot.enabled = running && guard.IsRunning; result = json.Serialize(snapshot); }, true);
            return result ?? "{\"version\":1,\"enabled\":false,\"rules\":[]}";
        }
        void OnBrowserEvent(string host, string rule, string reason)
        {
            OnUi(() =>
            {
                var known = policy.rules.FirstOrDefault(r => r.name == rule || r.id == rule);
                if (known != null && Uri.CheckHostName(host) != UriHostNameType.Unknown)
                    AddEvent(host, known.name, reason == "limit" ? "검사 한도 초과" : "웹 붙여넣기 차단");
            });
        }
        void AddEvent(string target, string rule, string reason)
        {
            var item = new ListViewItem(new[] { DateTime.Now.ToString("HH:mm:ss"), target, rule, reason });
            events.Items.Insert(0, item); if (events.Items.Count > 200) events.Items.RemoveAt(200);
            logEmpty.Visible = false;
        }
        void OnUi(Action action, bool wait = false)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (!InvokeRequired) action(); else if (wait) Invoke(action); else BeginInvoke(action);
        }
        Policy LoadPolicy()
        {
            var path = Path.Combine(folder, "policy.json");
            if (File.Exists(path))
            {
                try { var p = json.Deserialize<Policy>(File.ReadAllText(path)); if (PolicyEngine.Validate(p).Count == 0) return p; }
                catch (Exception) { }
                MessageBox.Show("저장된 정책이 올바르지 않습니다. 원본 파일은 유지하고 빈 규칙 화면을 엽니다.");
            }
            return new Policy();
        }
        void Persist(Policy p)
        {
            var path = Path.Combine(folder, "policy.json"); var temp = path + ".tmp";
            File.WriteAllText(temp, json.Serialize(p), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        string LoadKey()
        {
            var path = Path.Combine(folder, "pairing.key");
            if (File.Exists(path))
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)); } catch (CryptographicException) { }
            var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var value = Convert.ToBase64String(bytes);
            File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
            return value;
        }
    }
}
