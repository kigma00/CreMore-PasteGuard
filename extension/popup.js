'use strict';
const $ = id => document.getElementById(id);
const send = message => chrome.runtime.sendMessage(message);

function render(state) {
  const hasPolicy = !!state.policy;
  const fresh = state.lastSync && Date.now() - state.lastSync < 15000 && !state.error;
  $('dot').className = 'dot ' + (fresh && state.policy?.enabled ? 'good' : hasPolicy ? 'warn' : '');
  $('status').textContent = !state.paired ? 'Windows 앱 연결 필요'
    : !hasPolicy ? '정책 대기 중 · 보호 준비 전'
    : !state.policy.enabled ? '전체 보호 일시 중지'
    : fresh ? '웹사이트 보호 사용 중' : '저장된 정책으로 보호 중';
  $('detail').textContent = state.error || (state.lastSync
    ? '마지막 정책 확인 ' + new Date(state.lastSync).toLocaleTimeString('ko-KR')
    : '앱의 연결 키를 입력해 보호를 시작하세요.');
  const rules = state.policy?.rules.filter(rule => rule.enabled && rule.sites.length) || [];
  $('count').textContent = rules.length;
  $('rules').replaceChildren(...rules.map(rule => {
    const item = document.createElement('li');
    const title = document.createElement('strong'); title.textContent = rule.name;
    const scopes = document.createElement('small'); scopes.textContent = rule.sites.join(', ');
    item.append(title, scopes); return item;
  }));
  $('empty').hidden = rules.length > 0;
  $('disconnect').hidden = !state.paired;
  $('key').placeholder = state.paired ? '연결됨 · 키를 바꾸려면 입력' : '앱의 연결 키 붙여넣기';
}

async function act(message) {
  $('message').textContent = '';
  try {
    const state = await send(message);
    if (!('paired' in state)) { $('message').textContent = state.error || '처리할 수 없습니다.'; return; }
    render(state);
    if (message.type === 'pair') $('key').value = '';
  } catch (_) { $('message').textContent = '확장 프로그램을 다시 열어 주세요.'; }
}
$('pair-form').addEventListener('submit', event => { event.preventDefault(); act({ type: 'pair', key: $('key').value }); });
$('refresh').addEventListener('click', () => act({ type: 'sync' }));
$('disconnect').addEventListener('click', () => act({ type: 'disconnect' }));
act({ type: 'status' });
setInterval(() => act({ type: 'sync' }), 3000);
chrome.tabs.query({ active: true, currentWindow: true }).then(async tabs => {
  try {
    const frame = await chrome.tabs.sendMessage(tabs[0].id, { type: 'frameStatus' }, { frameId: 0 });
    $('page-status').textContent = frame.ready ? '현재 탭: 검사 모듈 준비됨' : '현재 탭: 정책이 준비되지 않았습니다.';
  } catch (_) { $('page-status').textContent = '현재 탭: 확장 프로그램이 없거나 지원하지 않는 페이지입니다. 웹페이지를 새로고침하세요.'; }
});
