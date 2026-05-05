# Unity 클라이언트 Freeze 원인 분석

날짜: 2026-05-05

## 요약

`Time.timeScale = 0f`로 진입하는 경로가 2개 존재하며, 각 경로에서 복구 UI(패널/버튼)가 Inspector에 연결되지 않았을 때 영구 정지 상태가 된다. 현재 freeze는 **레벨업 시 UpgradeSystem이 timeScale을 0으로 설정한 뒤 UpgradeUI 패널이 표시되지 않는 것**이 가장 유력하다.

---

## Time.timeScale = 0 진입 경로

### 경로 1: 레벨업 (UpgradeSystem)

```
PlayerStats.AddXp()
  └─ while (Xp >= XpToNextLevel) → Level++
       └─ OnLevelUp?.Invoke(Level)
            └─ UpgradeSystem.HandleLevelUp()        ← OnEnable에서 구독
                 ├─ Time.timeScale = 0f              ★ 정지 발생
                 └─ OnUpgradeOffered?.Invoke()       ← UpgradeUI가 구독해야 패널 표시
```

**복구 경로:**
```
UpgradeUI 패널의 버튼 클릭
  └─ UpgradeUI.OnChoiceClicked(index)
       └─ upgradeSystem.Choose(index)
            └─ Time.timeScale = 1f               ← 복구
```

**freeze 조건 (OR):**
- A. `UpgradeUI.upgradeSystem`이 null → `OnUpgradeOffered` 구독 없음 → 패널 미표시 → 버튼 클릭 불가
- B. `UpgradeUI.panel`이 null → `Show()`는 호출되지만 `panel.SetActive(true)` 무시 → 패널 미표시
- C. `UpgradeUI.choiceButtons[i]`가 null → 버튼 렌더링 안 됨 → 클릭 불가

---

### 경로 2: 게임 오버 (GameManager)

```
PlayerStats.TakeDamage()
  └─ currentHp == 0
       └─ GameManager.Instance.GameOver()
            └─ Time.timeScale = 0f              ★ 정지 발생
            └─ OnGameOver?.Invoke()             ← GameOverUI가 구독해야 패널 표시
```

**복구 경로:**
```
GameOverUI 패널의 재시작 버튼 클릭
  └─ GameOverUI.OnRestartClicked()
       └─ Time.timeScale = 1f
       └─ SceneManager.LoadScene("LoginScene")
```

**freeze 조건:** GameOverUI.panel 또는 restartButton이 null

---

## 현재 적용된 수정의 한계

`UpgradeSystem.HandleLevelUp()`에 아래 수정이 적용됨:

```csharp
if (OnUpgradeOffered == null)
{
    Apply(_currentChoices[0].Type);
    _currentChoices = null;
    return;  // timeScale 건드리지 않음
}
```

**이 수정으로 막히는 경우:** 조건 A (upgradeSystem null → 구독 없음)
**이 수정으로 막히지 않는 경우:**
- 조건 B: `upgradeSystem`은 연결됐고 `OnUpgradeOffered`에 구독자 있음 → `Time.timeScale = 0f` 진입 → 하지만 `panel == null`이어서 UI가 안 보임 → 버튼 클릭 불가 → **여전히 freeze**
- 조건 C: 패널은 보이지만 버튼이 null → 클릭 불가 → **여전히 freeze**

---

## 기타 freeze 후보 검토

| 항목 | 코드 위치 | 실제 freeze 가능성 |
|------|-----------|-------------------|
| `ReceiveParser` 루프 `maxParsingCount=10000` | ReceiveParser.cs:41 | 낮음 — background thread, main thread 무관 |
| `NetworkManager.Update()` while 루프 | NetworkManager.cs:141 | 낮음 — ConcurrentQueue.TryDequeue는 비면 즉시 false |
| `async void OnLoginClicked()` | LoginUI.cs:42 | 낮음 — LoginScene에서만 사용, 게임 중 아님 |
| `EnemyController.TryFindPlayer()` in FixedUpdate | EnemyController.cs:57 | 낮음 — 1초 쿨다운 적용됨 |
| `PlayerStats.AddXp()` while 루프 | PlayerStats.cs:51 | 낮음 — XpToNextLevel이 매 레벨 1.4배 증가하므로 무한 아님 |

---

## 핵심 결론

**현재 freeze의 가장 유력한 원인:**

1. **레벨업 발생** → `UpgradeSystem`이 `Time.timeScale = 0f` 설정
2. `UpgradeUI`가 구독은 됐지만 **`panel` 또는 `choiceButtons`가 Inspector에서 연결되지 않음**
3. 패널이 화면에 나타나지 않아 버튼 클릭 불가
4. `Time.timeScale`이 복구되지 않음 → **영구 정지**

**확인 방법 (Unity Editor에서):**
- Hierarchy에서 UpgradeUI 오브젝트 선택
- Inspector에서 Panel, Choice Buttons[0~2] 필드가 연결됐는지 확인
- Console에 `[UpgradeSystem] OnUpgradeOffered 구독자 없음` 로그가 있는지 확인

---

## 관련 파일

| 파일 | 역할 | freeze 관여 |
|------|------|------------|
| `Game/System/UpgradeSystem.cs` | 레벨업 시 timeScale=0 설정 | ★ 직접 원인 |
| `UI/UpgradeUI.cs` | 업그레이드 패널 표시/버튼 처리 | ★ 복구 담당 |
| `Manager/GameManager.cs` | 게임오버 시 timeScale=0 설정 | 2차 원인 |
| `UI/GameOverUI.cs` | 게임오버 패널 표시/재시작 | 2차 복구 |
| `Game/Player/PlayerStats.cs` | XP/레벨업/HP 관리 | 트리거 |
