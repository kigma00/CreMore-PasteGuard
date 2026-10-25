# 관련 연구와 PasteGuard의 방향

확인일: 2026-10-25. [사용 설명서](../README.md) / [현재 구현의 동작 원리](how-it-works.md)

PasteGuard의 방향은 **단말기에서 내용과 목적지를 함께 판단하여 민감 문자열의 붙여넣기를 제한하는 Endpoint DLP**에 해당합니다. 이번 조사에서 관련 연구는 확인했지만, Windows 실행 파일명과 웹 도메인을 함께 지정하고 제한 정규식으로 붙여넣기만 취소하는 현재 구성을 모두 동일하게 구현한 논문은 확인하지 못했습니다. 이것이 새로운 개념이라는 증거는 아닙니다.

## 1. 기능 방향이 가까운 논문

**M. Asha, J. Sreerambabu, M. Mohammed Riyaz. “Clipboard Monitoring for Data Loss Prevention.” SSRG International Journal of Mobile Computing & Application, 4(2), 2017, pp. 13–18.**

[출판사 원문 PDF](https://www.internationaljournalssrg.org/IJMCA/2017/Volume4-Issue2/IJMCA-V4I4P101.pdf)

클립보드와 활성 창을 감시하고 사용자 정의 정규식과 키워드 정책으로 민감정보를 식별하는 구성을 설명합니다. §I, p.13이 관련 설계의 출발점입니다. 다만 §IV.C, p.15에서는 민감정보 발견 시 클립보드를 비우는 동작을 설명합니다.

PasteGuard와 비교할 부분은 **정규식 기반 내용 검사와 단말기 정책 적용**입니다. 차이는 PasteGuard가 복사 내용을 유지하고 지정 목적지의 지원 붙여넣기 경로를 취소한다는 점입니다. 이 논문은 서술과 구현 소개 중심이므로 PasteGuard의 속도, 정확도 또는 우회 방지를 입증하는 자료로 사용하지 않습니다.

## 2. 입력 시점과 수신 앱을 결합한 접근 제어 연구

**Franziska Roesner, Tadayoshi Kohno, Alexander Moshchuk, Bryan Parno, Helen J. Wang, Crispin Cowan. “User-Driven Access Control: Rethinking Permission Granting in Modern Operating Systems.” IEEE Symposium on Security and Privacy, 2012.**

[Microsoft Research 논문 정보](https://www.microsoft.com/en-us/research/publication/user-driven-access-control-rethinking-permission-granting-in-modern-operating-systems-2/) / [학회 논문 PDF](https://www.microsoft.com/en-us/research/wp-content/uploads/2016/02/paper-48.pdf)

사용자의 실제 UI 동작을 자원 접근 권한과 연결하는 연구입니다. §4.5와 §5.1.2는 입력 동작과 수신 앱을 연결하고, §5.2–5.3은 복사와 붙여넣기를 중재하는 구조를 설명합니다.

PasteGuard에는 **붙여넣는 순간과 수신 대상을 함께 판단한다**는 설계 참고점이 있습니다. 하지만 이 논문은 연구용 운영체제와 커널을 수정한 권한 체계를 다루며, 민감 문자열의 정규식 차단 제품이 아닙니다. 일반 Windows 사용자 모드 훅만 사용하는 PasteGuard가 같은 보안 보장을 제공한다고 볼 수 없습니다.

## 3. 붙여넣기 차단만으로 해결되지 않는 위험을 보여주는 연구

**Yongliang Chen, Ruoqin Tang, Chaoshun Zuo, Xiaokuan Zhang, Lei Xue, Xiapu Luo, Qingchuan Zhao. “Attention! Your Copied Data is Under Monitoring: A Systematic Study of Clipboard Usage in Android Apps.” ICSE 2024. DOI: 10.1145/3597503.3623317.**

[저자 제공 원문 PDF](https://bruceqczhao.github.io/assets/icse24/ICSE24a.pdf) / [DOI](https://doi.org/10.1145/3597503.3623317)

ClipboardScope라는 정적 분석 도구로 Android 앱의 클립보드 처리, 저장, 전송 경로를 조사합니다. §3–4는 분석 방법, §5는 관찰한 사용 행태를 다룹니다.

이 논문은 붙여넣기 차단 구현이 아닌 **위험 분석 연구**입니다. PasteGuard의 위협 범위를 설명할 때 “붙여넣기 입력 취소”와 “다른 앱의 클립보드 읽기 금지”가 다르다는 참고 자료가 됩니다. Android 관찰 결과를 Windows의 발생 빈도나 방어 효과로 그대로 옮기지는 않습니다.

## 논문과 별개로 이미 존재하는 제품 기능

Microsoft Purview Endpoint DLP는 붙여넣는 내용과 대상 웹사이트에 따라 감사, 경고, 차단하는 기능을 문서화하고 있습니다. 따라서 **SaaS에 저장되기 전 붙여넣기를 막는다는 특성 자체만으로 기존 DLP보다 앞선 새 범주라고 주장할 수는 없습니다.** [Microsoft 공식 안내](https://learn.microsoft.com/en-us/purview/endpoint-dlp-create-policy-restrict-paste-in-browsers)

이 제품 사례는 기능 방향의 선례이며, PasteGuard와 같은 구현, 라이선스, 배포 방식이라는 뜻은 아닙니다.

## PasteGuard에서 직접 검증할 항목

다음은 위 논문이 입증한 결과가 아니라 이 프로젝트의 평가 제안입니다.

| 평가 항목 | 확인할 내용 |
|---|---|
| 목적지별 선택 차단 | 같은 문자열이 지정한 앱과 도메인에서는 차단되고 비지정 대상에서는 허용되는가 |
| 차단 범위 | 단축키, 우클릭, iframe, 자동 입력 등 각 경로의 지원 여부를 분리했는가 |
| 정확도 | 합성 토큰과 정상 문장에서 미탐과 오차단을 각각 측정했는가 |
| 지연 | 복사 후 준비 시간과 붙여넣기 처리 지연 p50, p95, p99를 구분했는가 |
| 자원 사용 | 대기와 연속 붙여넣기의 CPU와 메모리, 규칙 수와 문자열 길이에 따른 변화를 측정했는가 |
| 실패 상태 | 정책 로딩 전, 연결 끊김, 클립보드 변경 경쟁, 권한 부족의 동작을 확인했는가 |
| 원문 취급 | 로그나 로컬 통신에 원문이 포함되지 않는지 확인했는가 |

현재 프로젝트가 다루는 목표는 **정상 복사는 유지하면서 지정한 목적지로의 민감 문자열 붙여넣기를 제한하고, 로컬 검사 비용과 원문 보관을 최소화하는 것**입니다. 형식 탐지만으로 실제 발급된 크레덴셜의 유효성까지 판정하지 않습니다.
