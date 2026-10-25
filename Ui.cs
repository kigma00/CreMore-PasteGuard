using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PasteGuard
{
    // Native cue text is visual guidance only: it never becomes Text or policy data.
    sealed class HintTextBox : TextBox
    {
        string hint = "";
        public string Hint { get { return hint; } set { hint = value ?? ""; ApplyHint(); } }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr parameter, string text);
        void ApplyHint() { if (IsHandleCreated) SendMessage(Handle, 0x1501, new IntPtr(1), hint); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyHint(); }
    }

    sealed partial class GuardForm
    {
        const string ManualUrl = "https://github.com/kigma00/CreMore-PasteGuard#readme";
        static readonly Color Canvas = Color.FromArgb(245, 247, 250), Ink = Color.FromArgb(28, 46, 55),
            Muted = Color.FromArgb(72, 89, 103), Accent = Color.FromArgb(32, 86, 94),
            Tint = Color.FromArgb(232, 243, 242), Line = Color.FromArgb(222, 229, 234), Danger = Color.FromArgb(166, 59, 59);
        readonly Label stateDetail = new Label(), browserStatus = new Label(), saveStatus = new Label(), ruleCount = new Label(), patternDescription = new Label(), logEmpty = new Label();
        readonly Button deleteButton = new Button(), saveButton = new Button();
        readonly Button addPatternButton = new Button(), removePatternButton = new Button();
        readonly Label patternCount = new Label();
        readonly ComboBox preset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly Panel pages = new Panel { Dock = DockStyle.Fill };
        readonly Panel[] views = { new Panel(), new Panel(), new Panel() };
        readonly Button[] navigation = new Button[3];
        TableLayoutPanel patternRows;
        bool dirty;

        static Font UiFont(float size, bool bold = false)
        {
            return new Font("Malgun Gothic", Math.Max(11, size), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        }
        static Label LabelText(string text, float size = 11, bool bold = false, Color? color = null)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color ?? Ink,
                Font = UiFont(size, bold), Margin = new Padding(0), UseCompatibleTextRendering = false };
        }
        static Button ButtonStyle(Button button, string text, bool primary = false)
        {
            button.Text = text; button.FlatStyle = FlatStyle.Flat; button.AutoSize = false;
            button.UseCompatibleTextRendering = false;
            button.Size = new Size(126, 38); button.Cursor = Cursors.Hand; button.Margin = new Padding(0, 0, 8, 0);
            button.BackColor = primary ? Accent : Color.White; button.ForeColor = primary ? Color.White : Ink;
            button.FlatAppearance.BorderColor = primary ? Accent : Line;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(44, 104, 111) : Tint;
            return button;
        }
        static Panel Card() { return new Panel { BackColor = Color.White, Dock = DockStyle.Fill, Padding = new Padding(18), Margin = Padding.Empty }; }
        static TableLayoutPanel Grid(params float[] rows)
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length, Margin = Padding.Empty };
            foreach (float row in rows) grid.RowStyles.Add(new RowStyle(row < 0 ? SizeType.Percent : SizeType.Absolute, row < 0 ? 100 : row));
            return grid;
        }
        static void InputStyle(TextBox input, string accessible)
        {
            input.AccessibleName = accessible; input.Dock = DockStyle.Fill; input.Margin = new Padding(0, 4, 0, 4);
            input.BorderStyle = BorderStyle.FixedSingle; input.BackColor = Color.White; input.ForeColor = Ink;
        }
        static Panel Field(string title, Control input, int height)
        {
            var field = new Panel { Height = height, Dock = DockStyle.Fill, Margin = Padding.Empty };
            var label = LabelText(title, 10, true); label.Dock = DockStyle.Top; label.Height = 24; label.AutoSize = false;
            input.Dock = DockStyle.Fill;
            var box = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4) }; box.Controls.Add(input);
            field.Controls.Add(box); field.Controls.Add(label); return field;
        }

        void BuildUi()
        {
            BackColor = Canvas; ForeColor = Ink;
            var root = Grid(64, 88, 48, -1, 30); root.Padding = new Padding(24, 18, 24, 12); Controls.Add(root);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
            Bitmap logo;
            using (var stream = GetType().Assembly.GetManifestResourceStream("CreMore.Logo"))
            using (var source = Image.FromStream(stream)) logo = new Bitmap(source);
            header.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(36, 42), BackColor = Color.Transparent, Margin = Padding.Empty }, 0, 0);
            var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            brand.Controls.Add(LabelText("CreMore PasteGuard", 18, true)); brand.Controls.Add(LabelText("복사는 자유롭게, 민감한 붙여넣기는 안전하게.", 9, false, Muted));
            header.Controls.Add(brand, 1, 0);
            var local = LabelText("이 기기에서만 검사", 9, false, Muted); local.Dock = DockStyle.Fill; local.TextAlign = ContentAlignment.MiddleRight; header.Controls.Add(local, 2, 0);
            root.Controls.Add(header, 0, 0); FormClosed += (s, e) => logo.Dispose();

            var protection = Card(); protection.BackColor = Tint; protection.Padding = new Padding(18, 12, 18, 12);
            var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); summary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            var stateLabels = Grid(29, -1);
            state.Font = new Font(Font.FontFamily, 14, FontStyle.Bold); state.Dock = DockStyle.Fill; state.Margin = Padding.Empty;
            stateDetail.ForeColor = Muted; stateDetail.Dock = DockStyle.Fill; stateDetail.Margin = Padding.Empty;
            stateLabels.Controls.Add(state); stateLabels.Controls.Add(stateDetail, 0, 1); summary.Controls.Add(stateLabels, 0, 0);
            ButtonStyle(toggle, "보호 시작", true); toggle.Anchor = AnchorStyles.Right; toggle.Margin = Padding.Empty;
            toggle.Click += (s, e) => SetRunning(!running); summary.Controls.Add(toggle, 1, 0); protection.Controls.Add(summary); root.Controls.Add(protection, 0, 1);

            var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0), Margin = Padding.Empty };
            string[] titles = { "차단 규칙", "브라우저 연결", "차단 기록" };
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i; var button = ButtonStyle(new Button(), titles[i]); button.FlatAppearance.BorderSize = 0;
                button.Click += (s, e) => ShowView(index); navigation[i] = button; tabs.Controls.Add(button);
                views[i].Dock = DockStyle.Fill; pages.Controls.Add(views[i]);
            }
            root.Controls.Add(tabs, 0, 2); root.Controls.Add(pages, 0, 3);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            var manual = new LinkLabel { Text = "사용 설명서", AutoSize = true, LinkColor = Accent, Margin = new Padding(0, 8, 24, 0) };
            manual.LinkClicked += (s, e) => OpenManual(); footer.Controls.Add(manual);
            var coverage = new LinkLabel { Text = "보호 범위와 제한 보기", AutoSize = true, LinkColor = Muted, Margin = new Padding(0, 8, 0, 0) };
            coverage.LinkClicked += (s, e) => ShowCoverage(); footer.Controls.Add(coverage); root.Controls.Add(footer, 0, 4);
            BuildRules(); BuildBrowser(); BuildHistory(); ShowView(0);
        }

        void BuildRules()
        {
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); views[0].Controls.Add(columns);
            var sidebar = Card(); sidebar.Margin = new Padding(0, 0, 14, 0); sidebar.Padding = new Padding(12);
            var ruleLayout = Grid(32, -1, 48); ruleCount.Font = new Font(Font, FontStyle.Bold); ruleCount.Dock = DockStyle.Fill; ruleLayout.Controls.Add(ruleCount);
            list.Dock = DockStyle.Fill; list.BorderStyle = BorderStyle.None; list.BackColor = Color.White; list.DrawMode = DrawMode.OwnerDrawFixed; list.ItemHeight = 58; list.IntegralHeight = false;
            list.DrawItem += DrawRule;
            list.SelectedIndexChanged += (s, e) =>
            {
                if (loading || list.SelectedIndex < 0) return;
                int next = list.SelectedIndex;
                if (ConfirmLeave()) EditRule(next);
                else { loading = true; list.SelectedIndex = editing; loading = false; }
            };
            ruleLayout.Controls.Add(list, 0, 1);
            var add = ButtonStyle(new Button(), "+ 규칙 추가"); add.Dock = DockStyle.Fill; add.Margin = new Padding(0, 10, 0, 0);
            add.Click += (s, e) => { if (ConfirmLeave()) EditRule(-1); };
            ruleLayout.Controls.Add(add, 0, 2); sidebar.Controls.Add(ruleLayout); columns.Controls.Add(sidebar, 0, 0);

            var card = Card(); card.Padding = Padding.Empty; columns.Controls.Add(card, 1, 0);
            var frame = Grid(-1, 64); card.Controls.Add(frame);
            var scroll = new Panel { AutoScroll = true, Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 4), Margin = Padding.Empty };
            var form = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Action<Control, int> row = (control, height) => { int index = form.RowCount++; form.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); control.Dock = DockStyle.Fill; form.Controls.Add(control, 0, index); };
            Action<Control> flowingRow = control => { int index = form.RowCount++; form.RowStyles.Add(new RowStyle(SizeType.AutoSize)); control.Dock = DockStyle.Fill; form.Controls.Add(control, 0, index); };
            form.RowCount = 0;
            row(LabelText("규칙 설정", 13, true), 32);
            foreach (var input in new[] { name, processes, sites, pattern, sample }) InputStyle(input, "");
            name.Hint = "예: GitHub 토큰 보호"; processes.Hint = "예: Slack.exe, Notion.exe";
            sites.Hint = "예: slack.com, *.slack.com"; pattern.Hint = "예: ghp_[A-Za-z0-9]{36}";
            name.AccessibleName = "규칙 이름"; processes.AccessibleName = "차단할 앱"; sites.AccessibleName = "차단할 웹사이트";
            pattern.AccessibleName = "직접 입력한 검사 패턴"; sample.AccessibleName = "테스트 문자열";
            var identity = new TableLayoutPanel { ColumnCount = 2, Margin = Padding.Empty };
            identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); identity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            identity.Controls.Add(Field("규칙 이름", name, 60)); enabled.Text = "규칙 사용"; enabled.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; enabled.Margin = new Padding(8, 0, 0, 10); identity.Controls.Add(enabled, 1, 0); row(identity, 60);
            row(LabelText("1. 어디에서 막을까요?", 10, true), 28);
            var quick = new FlowLayoutPanel { Margin = Padding.Empty, WrapContents = false };
            foreach (var target in RulePresets.Targets)
            {
                var current = target; var button = ButtonStyle(new Button(), "+ " + target.Name); button.Size = new Size(target.Name.Length > 8 ? 158 : 108, 34);
                button.Click += (s, e) => { processes.Text = RulePresets.MergeTargets(processes.Text, current.Processes); sites.Text = RulePresets.MergeTargets(sites.Text, current.Sites); };
                quick.Controls.Add(button);
            }
            row(quick, 38);
            var targets = new TableLayoutPanel { ColumnCount = 2, Margin = Padding.Empty };
            targets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42)); targets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            var appField = Field("앱 실행 파일명", processes, 62); appField.Margin = new Padding(0, 0, 12, 0);
            targets.Controls.Add(appField); targets.Controls.Add(Field("웹사이트 도메인", sites, 62), 1, 0); row(targets, 64);
            var targetHint = LabelText("예: Slack.exe / notion.so. 여러 대상은 쉼표로 구분해요.", 9, false, Muted);
            targetHint.Margin = new Padding(0, 0, 0, 8); flowingRow(targetHint);
            row(LabelText("2. 어떤 내용을 막을까요?", 10, true), 28);
            patternCount.Font = UiFont(10); patternCount.ForeColor = Muted; row(patternCount, 28);
            patternList.AccessibleName = "등록된 검사 패턴"; patternList.IntegralHeight = false;
            patternList.HorizontalScrollbar = true; patternList.BorderStyle = BorderStyle.FixedSingle;
            patternList.SelectedIndexChanged += (s, e) => SelectPattern(); patternList.Dock = DockStyle.Fill;
            var patternPicker = new TableLayoutPanel { ColumnCount = 2, Margin = new Padding(0, 0, 0, 6) };
            patternPicker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); patternPicker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
            patternPicker.Controls.Add(patternList);
            var patternActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10, 0, 0, 0) };
            ButtonStyle(addPatternButton, "+ 패턴 추가"); addPatternButton.Click += (s, e) => AddPattern();
            ButtonStyle(removePatternButton, "선택 패턴 삭제"); removePatternButton.Click += (s, e) => RemovePattern();
            addPatternButton.Margin = new Padding(0, 0, 0, 4); removePatternButton.Margin = Padding.Empty;
            patternActions.Controls.Add(addPatternButton); patternActions.Controls.Add(removePatternButton);
            patternPicker.Controls.Add(patternActions, 1, 0); row(patternPicker, 88);
            preset.Items.AddRange(RulePresets.PatternNames); preset.AccessibleName = "검사 유형"; preset.Margin = Padding.Empty; preset.FlatStyle = FlatStyle.Standard; preset.Dock = DockStyle.Fill;
            preset.SelectedIndexChanged += (s, e) =>
            {
                if (!loading && !syncingPattern) { var value = RulePresets.Pattern(preset.SelectedIndex); if (value != null) pattern.Text = value; MarkDirty(); }
                UpdateSampleHint();
                ConfigurePattern();
            };
            patternRows = Grid(36, 0, 0); patternRows.AutoSize = true; patternRows.AutoSizeMode = AutoSizeMode.GrowAndShrink; patternRows.Dock = DockStyle.Top;
            patternRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); patternRows.RowStyles[1].SizeType = SizeType.AutoSize;
            patternRows.Controls.Add(preset); patternDescription.Dock = DockStyle.Fill; patternDescription.AutoSize = true; patternDescription.Margin = new Padding(0, 4, 0, 8); patternDescription.Font = UiFont(10); patternDescription.ForeColor = Muted;
            patternRows.Controls.Add(patternDescription, 0, 1); patternRows.Controls.Add(pattern, 0, 2);
            int patternIndex = form.RowCount++; form.RowStyles.Add(new RowStyle(SizeType.AutoSize)); form.Controls.Add(patternRows, 0, patternIndex);
            var testHeading = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            testHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); testHeading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var testTitle = LabelText("3. 저장하기 전에 확인해 보세요", 10, true); testTitle.Dock = DockStyle.Fill;
            testHeading.Controls.Add(testTitle);
            var help = new LinkLabel { Text = "패턴 도움말", AutoSize = true, LinkColor = Accent, Margin = new Padding(16, 2, 0, 0), Font = UiFont(10) };
            help.LinkClicked += (s, e) => MessageBox.Show(this, "여러 형식은 ‘+ 패턴 추가’로 등록하세요. 하나라도 일치하면 차단합니다.\n규칙당 최대 16개, 전체 규칙을 합쳐 100개까지 등록할 수 있습니다.\n문자열의 형식을 검사하며, 실제 토큰의 유효성을 확인하지 않습니다.\n\n직접 입력: 문자, [A-Z0-9], \\d, \\w, \\s, 반복(* + ? {n,m}), ^ $ \\b를 사용할 수 있습니다.\n패턴 내부의 그룹, OR(|), lookaround, 역참조는 지원하지 않습니다.\n\n예: ghp_[A-Za-z0-9]{36}\n아래 테스트는 등록된 모든 패턴을 검사합니다. 가짜 값으로 테스트하세요.", "검사 패턴 도움말");
            testHeading.Controls.Add(help, 1, 0); flowingRow(testHeading);
            var testRow = new TableLayoutPanel { ColumnCount = 2, Margin = Padding.Empty };
            testRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); testRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            testRow.Controls.Add(sample); var test = ButtonStyle(new Button(), "테스트"); test.Dock = DockStyle.Fill; test.Margin = new Padding(10, 0, 0, 0); test.Click += (s, e) => TestRule(); testRow.Controls.Add(test, 1, 0); row(testRow, 36);
            testResult.AutoSize = true; testResult.Font = UiFont(10); testResult.ForeColor = Muted; testResult.Padding = new Padding(0, 5, 0, 8); flowingRow(testResult);
            scroll.Controls.Add(form); frame.Controls.Add(scroll);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(20, 12, 20, 12), BackColor = Color.FromArgb(248, 250, 251), Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            ButtonStyle(deleteButton, "규칙 삭제"); deleteButton.ForeColor = Danger; deleteButton.FlatAppearance.BorderSize = 0; deleteButton.Dock = DockStyle.Fill; deleteButton.Click += (s, e) => DeleteRule(); actions.Controls.Add(deleteButton);
            deleteButton.AutoSize = true; deleteButton.AutoSizeMode = AutoSizeMode.GrowAndShrink; deleteButton.Padding = new Padding(12, 0, 12, 0);
            saveStatus.Dock = DockStyle.Fill; saveStatus.ForeColor = Muted; saveStatus.Font = UiFont(10); saveStatus.TextAlign = ContentAlignment.MiddleLeft; saveStatus.Margin = new Padding(10, 0, 0, 0); actions.Controls.Add(saveStatus, 1, 0);
            ButtonStyle(saveButton, "규칙 저장", true); saveButton.Dock = DockStyle.Fill; saveButton.Margin = Padding.Empty; saveButton.Click += (s, e) => SaveRule(); actions.Controls.Add(saveButton, 2, 0); frame.Controls.Add(actions, 0, 1);
        }

        void BuildBrowser()
        {
            var card = Card(); card.AutoScroll = true; views[1].Controls.Add(card);
            var content = Grid(34, 34, 44, 108, 108, 108, 116); content.Dock = DockStyle.Top; content.Height = 552; card.Controls.Add(content);
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.AutoSize = true; content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            content.RowStyles[6].SizeType = SizeType.AutoSize;
            content.Controls.Add(LabelText("브라우저에도 보호를 연결하세요", 15, true));
            browserStatus.Font = new Font(Font, FontStyle.Bold); browserStatus.ForeColor = Accent; browserStatus.Dock = DockStyle.Fill; content.Controls.Add(browserStatus, 0, 1);
            connection.Dock = DockStyle.Fill; connection.ForeColor = Muted; content.Controls.Add(connection, 0, 2);
            content.Controls.Add(BrowserStep("1", "확장 프로그램 설치", "Chrome 또는 Edge 확장 관리에서 개발자 모드를 켠 뒤,\n‘압축해제된 확장 프로그램 로드’로 이 폴더를 선택하세요.", "확장 폴더 열기", () => System.Diagnostics.Process.Start("explorer.exe", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extension"))), 0, 3);
            content.Controls.Add(BrowserStep("2", "앱과 브라우저 연결", "연결 키를 복사해 확장 프로그램의 입력칸에 붙여넣고\n‘연결’을 눌러 주세요. 이 키는 서비스 토큰이 아닙니다.", "연결 키 복사", () => { try { Clipboard.SetText(key); connection.Text = "복사했어요. 확장 프로그램에 붙여넣어 주세요."; } catch (Exception) { connection.Text = "복사하지 못했습니다. 잠시 후 다시 시도하세요."; } }), 0, 4);
            content.Controls.Add(BrowserStep("3", "대상 웹페이지 새로고침", "설치 전에 열었던 탭은 새로고침하세요. 페이지의 보호 상태는\n확장 프로그램에서 확인할 수 있어요.", "연결 상태 확인", UpdateStatus), 0, 5);
            content.Controls.Add(LabelText("알아두세요\n규칙 저장과 보호 변경은 다음 동기화 때 반영됩니다. 확장의 ‘정책 새로고침’으로 바로 확인할 수 있어요.\n앱이 닫히거나 연결이 끊겨도 확장은 마지막 정책을 유지합니다. 중지하려면 확장에서도 동기화하거나 연결을 해제하세요.", 9, false, Muted), 0, 6);
        }
        Control BrowserStep(string number, string title, string detail, string action, Action click)
        {
            var step = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 10, 0, 10), Margin = Padding.Empty };
            step.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38)); step.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); step.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
            var index = LabelText(number, 16, true, Accent); step.Controls.Add(index);
            var copy = Grid(28, -1); var heading = LabelText(title, 11, true); heading.Dock = DockStyle.Fill;
            var description = LabelText(detail, 10, false, Muted); description.AutoSize = false; description.Dock = DockStyle.Fill;
            copy.Controls.Add(heading); copy.Controls.Add(description, 0, 1); step.Controls.Add(copy, 1, 0);
            var button = ButtonStyle(new Button(), action); button.Size = new Size(148, 38); button.Anchor = AnchorStyles.Right; button.Click += (s, e) => click(); step.Controls.Add(button, 2, 0); return step;
        }
        void BuildHistory()
        {
            var card = Card(); views[2].Controls.Add(card); var layout = Grid(34, 38, -1); card.Controls.Add(layout);
            layout.Controls.Add(LabelText("차단 기록", 15, true)); layout.Controls.Add(LabelText("이번 실행 중의 최근 200건입니다. 붙여넣은 원문은 남기지 않으며, 앱을 닫으면 기록이 지워집니다.", 9, false, Muted), 0, 1);
            var area = new Panel { Dock = DockStyle.Fill }; layout.Controls.Add(area, 0, 2);
            events.BorderStyle = BorderStyle.None; events.Columns.Add("시각", 90); events.Columns.Add("대상 앱 / 웹사이트", 230); events.Columns.Add("규칙", 250); events.Columns.Add("차단 사유", 360); area.Controls.Add(events);
            logEmpty.Text = "아직 차단 기록이 없어요\n보호 중인 대상에서 차단이 발생하면 여기에 표시됩니다.";
            logEmpty.TextAlign = ContentAlignment.MiddleCenter; logEmpty.ForeColor = Muted; logEmpty.BackColor = Color.White; logEmpty.Dock = DockStyle.Fill; area.Controls.Add(logEmpty); logEmpty.BringToFront();
            events.SizeChanged += (s, e) => { if (events.Columns.Count == 4) { int width = Math.Max(420, events.ClientSize.Width - 24); events.Columns[0].Width = width / 10; events.Columns[1].Width = width * 27 / 100; events.Columns[2].Width = width * 25 / 100; events.Columns[3].Width = width * 38 / 100; } };
        }
        void ShowView(int index)
        {
            for (int i = 0; i < views.Length; i++) { views[i].Visible = i == index; navigation[i].BackColor = i == index ? Color.White : Canvas; navigation[i].ForeColor = i == index ? Accent : Muted; }
            views[index].BringToFront();
        }
        void DrawRule(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= policy.rules.Count) return;
            var rule = policy.rules[e.Index]; bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Tint : Color.White)) e.Graphics.FillRectangle(brush, e.Bounds);
            int gap = LogicalToDeviceUnits(10); var title = new Rectangle(e.Bounds.X + gap, e.Bounds.Y + gap, e.Bounds.Width - gap * 2, e.Bounds.Height / 2);
            TextRenderer.DrawText(e.Graphics, rule.name, Font, title, Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            var detail = new Rectangle(title.X, e.Bounds.Y + e.Bounds.Height / 2, title.Width, e.Bounds.Height / 2 - 2);
            string text = (rule.enabled ? "사용 중" : "꺼짐") + "   앱 " + rule.processes.Count + " / 웹 " + rule.sites.Count;
            TextRenderer.DrawText(e.Graphics, text, Font, detail, rule.enabled ? Accent : Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }
        void ConfigurePattern()
        {
            if (patternRows == null) return;
            bool custom = preset.SelectedIndex == 2; pattern.Visible = custom;
            patternRows.RowStyles[2].Height = custom ? (IsHandleCreated ? LogicalToDeviceUnits(36) : 36) : 0;
            patternDescription.Text = RulePresets.Description(preset.SelectedIndex);
        }
        void UpdateSampleHint()
        {
            string example = RulePresets.Example(preset.SelectedIndex);
            sample.Hint = example.Length > 0 ? "예: " + example : "확인할 테스트 문자열을 입력하세요";
        }
        void OpenManual()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ManualUrl) { UseShellExecute = true }); }
            catch (Exception) { MessageBox.Show(this, "브라우저를 열지 못했습니다. 아래 주소에서 설명서를 확인하세요.\n\n" + ManualUrl, "사용 설명서"); }
        }
        void MarkDirty() { if (!loading) { dirty = true; saveStatus.Text = "저장하지 않은 변경사항"; saveStatus.ForeColor = Muted; } }
        bool ConfirmLeave()
        {
            if (!dirty) return true;
            var choice = MessageBox.Show(this, "변경한 규칙을 저장할까요?\n‘아니요’를 선택하면 변경한 내용이 취소됩니다.", "규칙 변경사항", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.No) { EditRule(editing); return true; }
            return choice == DialogResult.Yes && SaveRule();
        }
        string ValidationMessage(Rule rule)
        {
            if (String.IsNullOrWhiteSpace(rule.name) || rule.name.Length > 80) return "규칙 이름을 1~80자로 입력하세요.";
            if (rule.processes.Count + rule.sites.Count == 0) return "차단할 앱이나 웹사이트를 하나 이상 추가하세요.";
            var values = PolicyEngine.GetPatterns(rule);
            if (values.Count == 0 || values.Count > PolicyEngine.MaxPatternsPerRule) return "검사 패턴은 규칙당 1~16개를 등록하세요.";
            for (int i = 0; i < values.Count; i++)
                try { PolicyEngine.MatchText("", values[i]); } catch (ArgumentException) { return (i + 1) + "번째 검사 패턴을 확인하세요. 빈 패턴을 채우거나 삭제하고 ‘패턴 도움말’을 참고하세요."; }
            if (policy.rules.Where((r, i) => i != editing).Sum(r => PolicyEngine.GetPatterns(r).Count) + values.Count > PolicyEngine.MaxPatternsPerPolicy)
                return "전체 규칙을 합쳐 검사 패턴을 최대 100개까지 등록할 수 있습니다.";
            return "앱은 실행 파일명(Slack.exe), 웹사이트는 도메인(notion.so)으로 입력하세요.\n규칙은 최대 100개, 앱과 웹사이트는 각각 128개까지 등록할 수 있습니다.";
        }
        void UpdateStatus()
        {
            if (running && !guard.IsRunning) running = false;
            int active = policy.rules.Count(r => r.enabled);
            state.Text = running ? "Windows 보호 중" : "Windows 보호가 꺼져 있어요"; state.ForeColor = running ? Accent : Ink;
            stateDetail.Text = running ? (active == 0 ? "사용 중인 규칙이 없어요. 규칙을 추가하거나 켜 주세요." : "사용 중인 " + active + "개 규칙으로 지정한 앱의 붙여넣기를 검사합니다.") : "규칙을 저장한 뒤 보호를 시작하세요.";
            toggle.Text = running ? "보호 중지" : "보호 시작"; ruleCount.Text = "내 규칙  " + policy.rules.Count;
            if (bridge == null || !bridge.IsRunning) { browserStatus.Text = "브라우저 연결 오류"; return; }
            var seconds = (DateTime.UtcNow - bridge.LastSeen).TotalSeconds; bool recent = seconds < 65;
            browserStatus.Text = recent ? "브라우저 최근 연결 확인됨" : "브라우저 연결 필요";
            connection.Text = recent ? "최근 동기화 " + Math.Max(0, (int)seconds) + "초 전. 페이지별 상태는 확장에서 확인하세요." : "아래 순서대로 확장 프로그램을 설치하고 이 앱과 연결하세요.";
        }
        void ShowCoverage()
        {
            MessageBox.Show(this, "Windows 앱: Ctrl+V / Shift+Insert를 검사합니다.\n웹: 확장 프로그램이 준비된 페이지의 일반 붙여넣기를 검사합니다.\n\n앱 우클릭이나 메뉴 붙여넣기, 드래그, 직접 입력은 이 버전에서 보호하지 않습니다.\n관리자 권한 앱, 자동화, 다른 사용자 세션의 모든 경로를 보장하지 않습니다.\n\n브라우저를 앱 대상으로 등록하면 모든 탭이 해당됩니다. 도메인별 보호에는 웹사이트 규칙을 사용하세요.\n*.example.com은 하위 도메인만 포함하므로 example.com도 따로 추가하세요.\n\n검사 준비 실패나 한도 초과 시에는 지정된 대상의 붙여넣기를 보수적으로 차단합니다.", "보호 범위와 제한", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
