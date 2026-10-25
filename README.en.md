# CreMore PasteGuard

[한국어](README.md) | **English**

**Block pasted text that matches a regular expression in selected Windows applications and websites. The clipboard contents remain unchanged.**

Use PasteGuard to help prevent accidental credential exposure, such as pasting a token into Slack. A rule specifies the target applications or domains and the regular expressions to check.

The application and browser extension currently have a Korean interface. This guide includes the exact Korean labels alongside their English meanings.

## Contents

- [Installation and launch](#installation-and-launch)
- [First test in Notepad](#first-test-in-notepad)
- [Protect Slack and Notion desktop apps](#protect-slack-and-notion-desktop-apps)
- [Connect the browser extension](#connect-the-browser-extension)
- [Add multiple regular expressions](#add-multiple-regular-expressions)
- [Write a custom regular expression](#write-a-custom-regular-expression)
- [Stop protection](#stop-protection)
- [Troubleshooting](#troubleshooting)
- [Move or update the application](#move-or-update-the-application)
- [How it works](#how-it-works)
- [Scope and limitations](#scope-and-limitations)

## Installation and launch

Requirements: Windows 10 or 11 and .NET Framework 4.8. No external server or service account is required. Website protection also requires the included extension in Chrome or Edge.

### If you have a compiled package

1. Extract all files from `CreMore-PasteGuard-windows.zip`.
2. Open the extracted folder and run **CreMore PasteGuard.exe**.
3. Check that the window has **차단 규칙** (Rules), **브라우저 연결** (Browser connection), and **차단 기록** (Block history) tabs.

Keep the executable, its `.exe.config` file, and the `extension` folder together. Do not run the executable directly from inside the ZIP.

### If you downloaded the source code

There is currently no compiled ZIP in GitHub Releases. **Code → Download ZIP** downloads source code, which must be built before you can run the application.

<details>
<summary>Build an executable from the source code</summary>

1. On the [repository page](https://github.com/kigma00/CreMore-PasteGuard), select **Code → Download ZIP** and extract the archive.
2. Open the folder containing `build.ps1` in File Explorer.
3. Type `powershell` in File Explorer's address bar and press Enter.
4. Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

5. After the command reports `Built:`, open `dist/PasteGuard` and run **CreMore PasteGuard.exe**.

The script uses the Windows .NET Framework compiler. It does not require NuGet packages. It also creates `dist/CreMore-PasteGuard-windows.zip`, excluding user settings and pairing keys. The executable is unsigned; if your organization's policy blocks it, contact your administrator.

</details>

## First test in Notepad

Use the synthetic test string below, not a real credential. This test does not require posting anything online.

### 1. Create a rule

Open a blank document in Windows Notepad. In PasteGuard, open **차단 규칙** (Rules) and select **+ 규칙 추가** (Add rule) if another rule is already selected.

| Field | Value |
|---|---|
| **규칙 이름** (Rule name) | `Notepad test` |
| **앱 실행 파일명** (Application executable name) | `notepad.exe` |
| **웹사이트 도메인** (Website domains) | Leave empty |
| Preset dropdown below **검사 패턴** (Regex patterns) | **테스트 문자열** (Test string) |
| **규칙 사용** (Enable rule) | Checked |

The test preset supplies `PGTEST_[A-Z0-9]{16}`. It matches `PGTEST_` followed by 16 uppercase ASCII letters or digits. Gray placeholder text in input fields is an example, not a saved value.

### 2. Check the regular expression

Copy this string into PasteGuard's **테스트** (Test) input and click **테스트**:

```text
PGTEST_ABCD1234EFGH5678
```

The result should begin with **차단 조건에 해당해요** (Matches a blocking condition). This button only checks the current rule; it does not save the rule or start protection.

### 3. Save and start protection

1. Click **규칙 저장** (Save rule).
2. Click **보호 시작** (Start protection).
3. Confirm that the status reads **Windows 보호 중** (Windows protection active).

### 4. Test an actual paste

1. Copy `PGTEST_ABCD1234EFGH5678` again.
2. Click inside the blank Notepad document.
3. Press **Ctrl+V**.

**Expected result:** the string is not inserted. The **차단 기록** (Block history) tab shows `notepad.exe` and the `Notepad test` rule.

Next, copy and paste this ordinary text:

```text
Hello, world!
```

**Expected result:** the text is inserted. If it is briefly blocked while the clipboard check is preparing, wait a moment and retry. Also check whether another rule matches the text.

This preset only matches the documented test format. It is not a detector for every password or token. For Windows applications, test with **Ctrl+V** or **Shift+Insert**; native context-menu paste is not intercepted.

## Protect Slack and Notion desktop apps

1. Select **+ 규칙 추가** (Add rule) and enter a rule name.
2. Under **차단 대상** (Targets), click **+ Slack** or **+ Notion**. This fills in the executable names and domains.
3. Under **검사 패턴** (Regex patterns), choose a preset or **직접 입력** (Custom).
4. Check **규칙 사용** (Enable rule), then click **규칙 저장** (Save rule).
5. Confirm that **Windows 보호 중** (Windows protection active) is displayed. Otherwise, click **보호 시작** (Start protection).

| Preset | Purpose |
|---|---|
| **테스트 문자열** (Test string) | Matches `PGTEST_` followed by 16 uppercase ASCII letters or digits. Use it for initial testing. |
| **GitHub PAT (classic)** | Matches `ghp_` followed by 36 ASCII letters or digits. It checks the format, not whether the token is valid. |
| **직접 입력** (Custom) | Enter a supported regular expression. See the [custom example](#write-a-custom-regular-expression). |

Desktop application protection and website protection use separate mechanisms. To protect Slack or Notion inside Chrome or Edge, also connect the browser extension.

## Connect the browser extension

The extension receives saved rules from the Windows application and checks pastes inside matching websites. It checks pasted text locally in the browser, without sending that text to the Windows application or an external server.

First, create and save a rule with a name, **+ Slack** or **+ Notion**, the **테스트 문자열** (Test string) preset, and **규칙 사용** (Enable rule) checked.

### 1. Install the extension

1. In PasteGuard, open **브라우저 연결** (Browser connection) and click **확장 폴더 열기** (Open extension folder). Note the folder location.
2. Open `chrome://extensions` in Chrome or `edge://extensions` in Edge.
3. Enable **Developer mode**.
4. Click **Load unpacked** and select the `extension` folder from step 1.
5. Confirm that **CreMore PasteGuard** appears in the extension list.

Select the folder itself, not an individual file or its parent folder.

### 2. Pair the extension with the Windows application

1. In PasteGuard, click **연결 키 복사** (Copy pairing key).
2. Open **CreMore PasteGuard** from the browser's extensions menu.
3. Paste the key into **Windows 앱 연결 키** (Windows app pairing key) and click **연결** (Connect).

The pairing key connects this browser extension to PasteGuard on this PC. Do not enter a GitHub token or another service's password in this field.

### 3. Synchronize and test

1. Confirm that the rule is saved and the Windows application shows **Windows 보호 중** (Windows protection active).
2. In the extension popup, click **정책 새로고침** (Refresh policy). A policy contains the saved rules and the current protection state.
3. Check for **웹사이트 보호 사용 중** (Website protection active) and the intended domain under **웹사이트 규칙** (Website rules).
4. Reload the target Slack or Notion webpage.
5. Paste `PGTEST_ABCD1234EFGH5678` into an input field. You do not need to send the message or share the page.

**Expected result:** the paste is blocked and an on-page notice appears.

The Windows application's **상태 새로고침** (Refresh status) only updates its connection display. Use the extension's **정책 새로고침** (Refresh policy) to fetch rules immediately. The extension also checks periodically, normally every 30 seconds, or every 3 seconds while its popup is visible. Browser suspension can delay these checks.

## Add multiple regular expressions

A rule can contain multiple regular expressions. **If any expression matches text within the paste, the entire paste is blocked.**

1. Select the rule to edit.
2. Click **+ 패턴 추가** (Add pattern).
3. Choose a preset or select **직접 입력** (Custom) and enter an expression.
4. Click **규칙 저장** (Save rule). For websites, also click **정책 새로고침** (Refresh policy) in the extension.

Changing a preset while an existing item is selected replaces that item's expression. To add another expression, click **+ 패턴 추가** first. To remove one, select it and click **선택 패턴 삭제** (Delete selected pattern).

Limits are **16 expressions per rule** and **100 expressions across all rules**. Complete or remove empty entries before saving. The **테스트** (Test) button evaluates all expressions in the current rule, not just the selected expression.

## Write a custom regular expression

For a synthetic string such as `ghg_DEMO123`:

1. Select **직접 입력** (Custom) under **검사 패턴** (Regex patterns).
2. Enter:

```text
ghg_[A-Za-z0-9]+
```

3. Enter `ghg_DEMO123` in **테스트** (Test) and click the test button.
4. Check the result, then click **규칙 저장** (Save rule).

`[A-Za-z0-9]` matches an ASCII letter or digit; `+` means one or more repetitions. A match anywhere in the pasted text can block the paste.

PasteGuard supports a subset of regex syntax: literals, character classes, escapes such as `\d`, `\w`, `\s`, and `\b`, repetition with `?`, `*`, `+`, `{n}`, and `{n,m}`, and anchors `^` and `$`. Matching is case-sensitive. Groups, alternation inside an expression (`|`), lookarounds, backreferences, flags, unescaped `.`, and open-ended `{n,}` repetition are not supported. Use separate expressions in the same rule for OR matching. See the [technical reference (Korean)](docs/technical-reference.md#직접-입력-패턴) for the complete limits.

## Stop protection

**보호 중지** (Stop protection) changes the state for both desktop and browser protection. Windows protection stops immediately; the extension stops after receiving the updated state.

| Action | Steps |
|---|---|
| Stop desktop and website protection | Click **보호 중지** (Stop protection) in the Windows application. |
| Confirm website protection has stopped | Then click **정책 새로고침** (Refresh policy) in the extension. Check for **전체 보호 일시 중지** (All protection paused). |
| Disconnect the browser | Click **연결 해제** (Disconnect) in the extension. This clears its pairing key and cached policy. Pair it again to resume. |
| Disable one rule | Uncheck **규칙 사용** (Enable rule), save, and refresh the extension policy if you use website protection. |

Closing the Windows application stops desktop protection, but the extension can continue using its last received policy. Closing the application alone does not reliably stop website protection.

Each time you reopen the Windows application, desktop protection starts **off**. Click **보호 시작** (Start protection) when ready.

## Troubleshooting

| Problem | What to check |
|---|---|
| I cannot save a rule even though the fields show examples. | Gray placeholder text is not a value. Enter a rule name and at least one target. |
| The in-app test matches, but an actual paste is allowed. | Save the rule, start protection, and test with Ctrl+V in a targeted Windows application. |
| Notepad paste is not blocked. | Check `notepad.exe`, the **규칙 사용** checkbox, and the exact test string. |
| Website paste is not blocked. | Check pairing, protection state, and the matching domain. Refresh the policy, then reload the webpage. |
| The extension says **현재 탭: 검사 모듈 준비됨**. | This means the checking module is ready in that tab. The page must still match an enabled website rule. |
| Website pastes are still blocked after I close the Windows application. | Disconnect the extension, or reopen the application and synchronize its stopped state. |
| Saving fails after adding an expression. | Complete or remove any empty pattern entries. |
| History reports **검사 한도 초과** (Inspection limit exceeded). | The input or inspection workload exceeded a limit. PasteGuard can block in this case without confirming a credential match. |
| Block history disappears after restarting. | History is kept only for the current application session. Saved rules are retained. |
| Text is clipped or display scaling looks wrong. | Check that **CreMore PasteGuard.exe.config** is next to the executable. |

### Enter targets manually

- Enter executable names such as `Slack.exe`, not full paths such as `C:\...`.
- Enter domains such as `notion.so`, without `https://` or a URL path.
- Separate multiple entries with commas: `Slack.exe, Notion.exe`.
- `*.example.com` matches subdomains such as `app.example.com`. To include the base domain, enter `example.com, *.example.com`.
- To target specific websites, use domain rules. Adding `chrome.exe` as an application target applies process-based protection across Chrome tabs.

## Move or update the application

### Move the application folder

The application does **not** have to stay inside `dist`. Close it and move the entire `PasteGuard` folder to a location where your Windows account can save files.

Keep **CreMore PasteGuard.exe**, **CreMore PasteGuard.exe.config**, `extension`, `README.md`, `README.en.md`, and `docs` together. Include the **data** folder to preserve existing rules and the pairing key.

If the extension folder moves, load it again from its new location in the browser. If it shows **Windows 앱 연결 필요** (Windows app connection required), copy the pairing key from the application, paste it into the extension, and click **연결** (Connect). Moving to a different PC or Windows account may also require pairing again.

### Update to a new version

1. Close the application and back up the existing **data** folder.
2. Replace the application files and `extension` folder with the new version, preserving `data`.
3. Reload the extension on the browser's extensions page. If its folder moved, load the new folder instead.
4. Open PasteGuard and click **보호 시작** (Start protection).
5. If the extension shows **Windows 앱 연결 필요** (Windows app connection required), pair it again. Then refresh the extension policy and reload the target webpage.
6. Repeat the synthetic-string test.

Version 1.3.0 introduced multiple expressions per rule. **Update the application and extension together**; older extensions may not understand the new policy. Do not include `data` or pairing keys in shared packages or GitHub commits.

## How it works

- **Windows applications:** PasteGuard checks clipboard text after a change and prepares a result. When you press Ctrl+V or Shift+Insert, a keyboard hook checks the foreground application's executable name and decides whether to suppress the shortcut. PasteGuard must be running with protection enabled; it does not install a permanent Windows policy or kernel driver.
- **Websites:** the extension receives rules through the local connection at `127.0.0.1:17843`. It checks the domain and pasted text in the browser, then cancels matching paste events. It does not wait for a server response for each paste.
- **Data:** rules are saved in `data/policy.json`. The pairing key is encrypted for the current Windows user in `data/pairing.key`. The extension stores its pairing key and last policy locally. Clipboard contents are not written to block history or sent to an external server. Pasted text does temporarily exist in process memory during inspection.

## Scope and limitations

| Supported | Not covered |
|---|---|
| Ctrl+V and Shift+Insert in targeted Windows applications | Native application context-menu paste or direct clipboard access |
| Ordinary paste events on supported webpages with the extension and policy loaded | Manual typing, drag and drop, file uploads, or all custom clipboard features |
| Matching registered regular expressions | Checking token validity, matching against Vault, or detecting every credential format |
| Up to 200 recent block events in the current app session | Permanent audit storage or guaranteed reporting of browser events while disconnected |

Protection is not guaranteed in elevated applications, browser internal pages, or unsupported frames such as `about:blank` and `srcdoc`. Website protection may not be ready before the extension loads its policy. Test the specific application or webpage you intend to protect.

## Demo video

[![Notepad paste-blocking demonstration](media/pasteguard-notepad-poc.png)](https://github.com/kigma00/CreMore-PasteGuard/blob/main/media/pasteguard-notepad-poc.mp4)

[Watch the Notepad demonstration](https://github.com/kigma00/CreMore-PasteGuard/blob/main/media/pasteguard-notepad-poc.mp4)

The video shows a synthetic `ghg_` string being blocked while ordinary text is allowed. It was recorded with version **1.2.1**, so its layout and labels differ from the current interface. The first test in this guide uses the built-in `PGTEST_` preset.

## Further documentation

The following detailed documents are currently in Korean:

- [Implementation and behavior](docs/how-it-works.md)
- [Regex syntax, storage, performance, build, and tests](docs/technical-reference.md)
- [Related research and existing products](docs/related-work.md)

The application's **사용 설명서** (User guide) button opens the Korean README. Select **English** at the top to open this guide.
