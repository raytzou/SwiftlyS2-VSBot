# WhenPlayerCacheReady 規格書

- 日期：2026-11-27。
- 分支：`feature/player-cache-ready`。
- 基準：最新 `main`，commit `f062e73`。
- 狀態：已依核准規格完成實作與本機驗證；原計畫保留，實作結果與後續審核回應見文末。尚待 Server 回歸測試。

## 目的與範圍

新增單次 callback API：快取已 Ready 就立即執行；仍 Loading 就保存，等現有載入流程完成後執行。這是等待既有載入結果，不是重新嘗試 SQL；不新增 timer、polling、Thread、TaskCompletionSource、lock 或 DatabaseWorker 架構。

保留 GetPlayerCache、GetPlayerCacheState、UpdatePlayerCache 的既有語意；不全面改寫 volume、saysound、music 等呼叫端。本次只提供快取就緒機制與必要生命週期清理，不實作 !models 或 Agent 擷取功能。

## 現況分析

| 位置 | 已確認行為與影響 |
| --- | --- |
| `PlayerDataService.PlayerCacheEntry` | private nested class，具有 State、Data；每次建立的新 instance 已被用作 session identity。 |
| `PreparePlayerCache` | ConnectFull 建立 Loading entry，DB 工作入列失敗則 LoadFailed。相同 SteamID 已有 entry 就返回，沒有自動重試。 |
| `LoadPlayerAsync` | Worker 只持有 entry 作為識別 token，不讀寫其內容；結果經既有 NextWorldUpdate 送到 game thread。 |
| `ApplyLoadResult` | 驗證未 shutdown、Dictionary 仍指向相同 entry、State 仍 Loading，然後設定 Data／Ready；失敗結果設定 LoadFailed。 |
| `RemovePlayerCache`／`ClearPlayerCaches` | 現在只移除集合。新增 callback 後必須先清空 List，因為尚未完成的 DB 工作可能繼續持有舊 entry。 |
| `SaveAndRemovePlayerCache` | 保存已修改的 Ready 資料後呼叫 RemovePlayerCache；可沿用同一清理入口。 |
| `PrepareShutdown` | 保存、清除所有快取後設定 shutdown，再由 Main 停止 Worker。Worker cancellation 發生在此流程之後。 |
| `Main.Events` | OnMapUnload 保存並清空快取；InitializeMap 也會清空。沿用這些邊界，不因 OnMapLoad 可能仍在舊世界就提前丟棄待存資料。 |
| `PlayerDisconnectHandler` | SaveAndRemovePlayerCache 在 IsValid 判斷之後；若提前返回，Loading entry 與 callback 可能殘留，必須一併處理。 |
| `PlayerTeamHandler` | 最新 main 只有音樂呼叫，沒有對話中 Agent ModelPath 擷取程式，不能把概念範例當成已存在的呼叫端。 |

## 公開 API

在 IPlayerDataService 與 PlayerDataService 都宣告：

```csharp
void WhenPlayerCacheReady(
    ulong steamId,
    Action<Player> callback,
    [CallerMemberName] string caller = "");
```

Player 是現有 `VSBot.Domain.Entities.Player`，不是 IPlayer，也不新增 PlayerCache 資料模型。Interface 與實作都加 `using System.Runtime.CompilerServices` 及 CallerMemberName，確保透過介面呼叫也能自動取得註冊來源。

契約如下：

- 呼叫端必須在 game thread 呼叫，和既有 cache API 一致；callback 也在該執行緒同步執行。不支援直接從 Worker 呼叫，背景工作需自行沿既有方式回到 game thread。
- callback 必須為同步 Action，不能傳 async lambda 形成 async void；執行時間應短，不做阻塞 I/O。
- 傳入非 null、目前 entry 持有的 Player instance，不是副本，不需要再 GetPlayerCache 或使用 null-forgiving operator。
- 每次註冊至多執行一次。重複註冊同一 delegate 視為兩次工作，不自行去重；失敗、離線、換圖或卸載可導致零次執行。
- 不保證一定執行，也不提供失敗 callback、取消 handle 或自動重試；失敗原因透過具來源資訊的 logger 記錄。
- callback 修改持久偏好時，仍須呼叫 UpdatePlayerCache 標記 dirty；API 本身不自動標記或寫 DB。
- `ArgumentNullException.ThrowIfNull(callback)` 處理無效參數。

| 註冊時狀態 | 行為 |
| --- | --- |
| 已 shutdown | 拒絕，不保存，Warning 含 SteamID、Caller 與原因。 |
| entry 不存在 | 拒絕並 Warning；呼叫端應先讓 PreparePlayerCache 執行，不自動建立或等待未來 session。 |
| Loading | 加入該 entry 的 List，正常等待不輸出 Warning。 |
| Ready 且 Data 非 null | 同一個呼叫堆疊內立即執行，不排程到下一 tick。 |
| Ready 但 Data 為 null | 不執行、不保留，LogError 記錄狀態不一致；用 pattern matching 取得 data，不用 `!` 掩蓋問題。 |
| LoadFailed | 拒絕並 Warning；重新 Prepare 也不會自動重試既有失敗 entry，本次不改這個行為。 |

## Callback 保存方式

在既有 private sealed PlayerCacheEntry 裡保存：

```csharp
private readonly List<(Action<Player> Callback, string Caller)> _pendingCallbacks = [];
```

各 entry 有自己的 List，天然區分玩家與重新連線的 session。Caller 與 callback 一起保存，延遲執行出錯仍能辨識最初註冊來源。

為回應對 private field 的偏好，entry 提供三個小方法給外層 service 使用，List 不直接對外暴露：

- `AddPendingCallback(Action<Player> callback, string caller)`：加入一筆。
- `TakePendingCallbacks()`：回傳 tuple array 快照並 Clear 原 List。
- `ClearPendingCallbacks()`：直接 Clear，釋放閉包參考。

這些方法可以是 private nested class 上的 public methods；entry 型別本身不對服務外公開，不代表其他模組可以操作 List。不另建 callback manager 或 DTO。

## Ready 的執行流程

ApplyLoadResult 保留既有的 shutdown、ReferenceEquals、Loading 驗證。成功時：

1. `entry.Data = result`，接著 `entry.State = Ready`。
2. `TakePendingCallbacks()` 先取快照並清空 List，再開始執行。
3. 依註冊順序逐一處理快照。每次執行前再次檢查 shutdown、Dictionary 仍指向同一 entry、Ready 且 Data 仍為相同 result。
4. 若前一個 callback 移除快取、清空全部快取、shutdown，或移除後建立同 SteamID 新 entry，停止剩餘 callback；不能讓快照繞過清理。
5. 使用私有 `InvokeCacheReadyCallback(ulong steamId, Player data, Action<Player> callback, string caller)` 執行，逐筆 try-catch 記錄例外，讓其他 callback 在 entry 仍有效時繼續。Ready 立即執行也共用此方法，例外策略一致。

這裡的 try-catch 是 callback 隔離邊界，有明確的外部委派錯誤處理目的；不是替每層框架操作加入防禦性包裝。既有 ApplyLoadResult 外層 catch 不作為多個 callback 的唯一例外邊界。

callback 執行時重新呼叫 WhenPlayerCacheReady，因快取已 Ready，新的 callback 立即執行，不加入正在遍歷的快照。因此原本待處理的 A、B 順序不變，但 A 內註冊 C 時會是 A → C → B。呼叫端不得無條件自我註冊造成遞迴；不為此增加排程佇列或遞迴框架。

## 失敗與清理矩陣

| 時機 | 修改／保證 |
| --- | --- |
| ApplyLoadResult 收到 null | 設 LoadFailed，取出並清空 callback，逐筆 Warning 含 Caller，說明因載入失敗未執行；底層 SQL exception 沿用原 Error，不重複附整份 exception。 |
| TryEnqueue 載入被拒絕 | 設 LoadFailed 並清空 List。正常同步流程此時尚無外部註冊；之後註冊走 LoadFailed 拒絕分支。 |
| RemovePlayerCache | Remove 取得原 entry 後 ClearPendingCallbacks，同時維持 dirty 移除。即使 DB 還持有 entry，也不再保留 callback 閉包。 |
| SaveAndRemovePlayerCache | 沿用 RemovePlayerCache 清理，不重複實作。 |
| ClearPlayerCaches | 逐一清空所有 entry 的 List，再清空 Dictionary／dirty。 |
| 地圖卸載／初始化 | 沿用現有 ClearPlayerCaches；舊結果無法通過 entry identity 驗證。清理之前仍屬舊 cache 生命週期，不能宣稱 OnMapLoad 一觸發就停用 callback。 |
| 插件卸載 | PrepareShutdown 的 ClearPlayerCaches 清掉等待 callback；shutdown 後不接受新註冊，既有 worker cancellation 不另回傳成功。 |
| 快取替換 | 目前沒有直接替換入口；只能移除再建立，新 entry 不繼承舊 List。未來新增替換路徑也必須先清理舊 entry。 |
| 延遲 DB 回傳 | 既有 ReferenceEquals 拒絕過期結果；在 game thread 丟棄時可清空該舊 entry 的 List，不得碰新 entry。 |
| callback 拋例外 | 記錄 SteamID、Caller、exception，該 callback 不重試，剩餘 callback 經有效性檢查後繼續。 |

正常離線／換圖的取消不逐筆寫 Warning，以免正常流程洗版。除上述拒絕、載入失敗、callback 例外外，不新增常態 log；如實作驗證需要額外診斷，使用 IsDebug + LogInformation，不使用 LogDebug。

## 離線入口的必要小幅調整

將 PlayerDisconnectHandler 既有的非 Bot `SaveAndRemovePlayerCache(player.SteamID)` 移至 UI 清理之後、`if (!player.IsValid) return` 之前，移除原位置的重複呼叫與不再使用的區域變數。只搬動 cache 清理，不改其他模組流程。

目的是離線時即使 player 已無效，也能以其 session 身分資料清理等待工作。實作時需核對 SDK IPlayer.SteamID 在 Pre disconnect／無效 player 狀態的可讀性；不可透過 Pawn 取資料，也不可用 slot 猜 SteamID。player 為 null 的事件目前仍無法取得 session 身分，本規格不承諾補償這個未驗證路徑；若 SDK 核對或實測顯示缺失，先提出有證據的最小調整。

## 執行緒與資料有效性

所有 callback List 操作與執行限定在 game thread。LoadPlayerAsync 不加入、清空或執行 callback，僅沿原本 NextWorldUpdate 交回 ApplyLoadResult；這是既有跨執行緒交接，不是新建重試 timer。

entry identity 能隔離同 SteamID 重連與 ClearPlayerCaches 後的舊結果，但不能證明同一 session 內 Pawn、隊伍或回合沒有改變。需要操作遊戲物件的 callback，由呼叫端捕捉 SessionId／當時任務識別並在執行時重新查詢有效物件；不要長時間捕捉 IPlayer、Pawn、SceneNode。單純資料更新則優先捕捉字串等值。

本 API 不替呼叫端自動處理「最新隊伍才有效」或模型來源正確性，也不新增通用世代計數。

## DefaultSkinModelPath 分析

目前 Player.DefaultSkinModelPath 有 `[Column("default_skin_model_path")]`，CreateSnapshot 也會複製，Repository.UpdateAsync 使用整個 entity 更新。因此即使 callback 只改這個欄位、沒有呼叫 UpdatePlayerCache，之後其他偏好變動觸發存檔仍可能把它一併寫進 DB。不能把省略 dirty 標記當成不持久化保證。

目前裝備的原生 Agent model 較適合作為 session runtime state，因為玩家換隊伍或離線更換 Agent 後舊值可能失效。但本次不更動 EF mapping、DB schema 或 Player 模型，不擅自加入 NotMapped。後續模型功能再決定獨立 runtime 欄位或既有 PlayerService 狀態的歸屬，以及正確擷取時機。

示範 API 時可使用已存在的偏好欄位，不在正式程式加入示範呼叫：

```csharp
playerDataService.WhenPlayerCacheReady(steamId, cache =>
{
    cache.Volume = requestedVolume;
    playerDataService.UpdatePlayerCache(cache);
});
```

此範例不表示要把既有 !volume 改成延遲操作。新 API 的第一個正式呼叫端可由後續模型需求加入。

## 預計檔案與工作階段

| 檔案 | 內容 |
| --- | --- |
| `Services/Interfaces/IPlayerDataService.cs` | 新增 API、CallerMemberName 與 game-thread／至多一次契約註解。 |
| `Services/PlayerDataService.cs` | entry 私有 List、註冊分支、Ready 執行、例外隔離及所有 cache 移除清理。 |
| `Main.GameEvents.cs` | 將既有非 Bot cache 清理移到有效性提早返回之前。 |
| `Docs/PlayerCacheReadySpec.md` | 實作後補上實際 commit、驗證結果與限制。 |

建議先提交核准規格；callback 核心與生命週期清理作一個可建置的功能 commit；必要的離線入口調整可獨立 fix commit；最後更新驗證文件。全部使用 Conventional Commits 與條列 body。本輪僅提供規格，尚未進行這些提交。

## 驗證計畫

使用可控制完成時間的 Repository／Worker／Scheduler 替身測試實際 PlayerDataService，不需真 SQL 或靠等待秒數猜時機：

1. Ready 立即同步執行，收到非 null 且與 GetPlayerCache 相同的 instance；Loading 不提早執行，成功後多筆依註冊順序各執行一次並清空 List。
2. 兩名玩家交錯載入，callback 不混用資料；Interface 與具體 service 呼叫均能記錄 CallerMemberName。
3. DB load 失敗、queue 拒絕、entry 不存在、shutdown：不執行、不保留 callback；沒有額外載入／重試工作。
4. Loading 時離線、Remove、Clear、換圖、shutdown；舊結果晚到或同 SteamID 已建立新 entry，都不能執行舊 callback。直接檢查原 entry List 已清空，避免只驗證 Dictionary 移除。
5. 第一個 callback 拋例外，下一個仍執行；第一個 callback 移除／重建 entry 或 shutdown，剩餘舊 callback 不執行。
6. callback 內註冊新 callback，驗證立即執行的順序與無集合修改錯誤；重複 ApplyLoadResult 不會重播 callback。
7. Ready 但 Data 為 null 的異常測試：不將 null 傳給 caller，記錄 Error。
8. Worker 完成但 game-thread queue 尚未執行時，callback 仍未執行；推進 game-thread queue 後才執行。持久變更需 UpdatePlayerCache 才維持既有 dirty 行為。
9. 建置與原有讀取／存檔流程回歸；離線有效性提前返回路徑需核對 SDK 或實機驗證。

Server 測試需有一個明確的暫時驗證呼叫端或之後的正式消費者：現有 !volume 等尚未使用此 API，正常遊玩不能證明 callback 已測到。驗證慢載入後執行一次、載入中斷線重連、換圖、兩玩家隔離與插件卸載；診斷程式與原始 log 不加入正式專案。實作完成後再提供可執行的具體步驟，並區分替身測試與 Server 實測結果。

## 實作與審核回應（2026-11-27）

### 提交紀錄

| Commit | 內容 |
| --- | --- |
| `40d5650` | `docs(cache): record approved cache-ready callback specification`：先提交核准規格。 |
| `2450b39` | `feat(cache): execute callbacks when player data becomes ready`：per-entry 私有 List、CallerMemberName、立即／延遲執行、例外隔離及清理。 |
| `94511cc` | `fix(events): clean player caches before disconnect validity checks`：early return 分流、真人快取提前清理，保留音樂與 UI 清理。 |

本節另以 docs commit 記錄結果。分支仍為 `feature/player-cache-ready`，未新增正式呼叫端、未改 Model／DB schema，也未加入測試程式或原始 log。

### Disconnect 流程

實際順序為：null 返回 → Music.RemovePlayer → UIModule.OnPlayerDisconnect → Bot 清理後返回 → 真人取得 SteamID 並 SaveAndRemovePlayerCache → 無效玩家返回 → 其餘既有真人清理與通知。

IsFakeClient 只檢查一次，不新增 helper。Bot 的 ClearBotResources 只取消 pickup timer，不寫 UI，故 UI 放在 Bot 分流前沒有已知順序依賴。保留原本對所有 slot 的音樂清理，沒有照抄對話中省略音樂的示例。其餘真人資源仍沿用 IsValid 邊界，沒有順便擴大重構其他 Service。

已讀取 [SwiftlyS2 v1.5.3 Player 實作](https://github.com/swiftly-solution/swiftlys2/blob/v1.5.3/managed/src/SwiftlyS2.Core/Modules/Players/Player.cs)：SteamID 與 IsFakeClient 讀取 managed `_steamID`，會 ThrowIfDisposed，但不要求有效 Pawn／Controller。SteamID 仍受授權模式影響；Strict 模式未授權可回傳 0。這支持在 IsValid 前清理，但不代表已 Dispose 的 player 可以存取，也不代表本機已驗證 CS2 Pre disconnect 的完整時序；本次維持既有 Pre hook，不新增第二套離線事件。

### Loading 是否可能永遠殘留

已檢查既有失敗路徑：

- Queue 拒絕：同步設 LoadFailed 並清空 List。
- 查詢／新增資料丟出一般例外，或非 Worker 停止所造成的取消：LoadPlayerAsync 記錄錯誤，交回 null，ApplyLoadResult 設 LoadFailed 並清除 callback。
- 已取得舊資料但更新連線資訊失敗：沿原邏輯記錄錯誤後仍返回有效資料，進入 Ready；這不是 Loading 殘留。
- Worker token 取消：現有 Main.Unload 先 PrepareShutdown 清空快取，再 Shutdown Worker；停止時略過的工作也不再持有 callback 閉包。
- ApplyLoadResult 套用過程發生例外：新增清空 List，且仍 Loading 時改成 LoadFailed，避免外層 catch 只記錄錯誤卻保留等待工作。
- PreparePlayerCache 在 shutdown 後直接返回，不再建立新的 Loading entry。

不宣稱任意框架／程序故障都能自動復原：Scheduler 若無法排入或執行 Game Thread 工作、Worker loop 非預期終止、底層 I/O 永不完成，仍可能等待到 Remove／Clear／Shutdown 才清理。本次不從 Worker 跨執行緒清空 List，也不加入 timer、timeout、polling 或 Worker 重啟機制。

### Reference 與 callback 行為

UpdatePlayerCache 原本已要求傳入 Player 與 entry.Data 為相同 reference，因此正常更新只修改原物件及 dirty 標記，不會讓下一筆 callback 被誤判過期。此情境已測試。

每筆 callback 之前重驗 entry；callback 自行移除、清空、重建或 shutdown 時停止剩餘舊工作。List 在執行前已取出清空，因此單筆例外不會留下重播工作。Ready 即時 callback 與延遲 callback 共用例外隔離方法。

CallerMemberName 是註冊呼叫所在的成員名稱，並非 delegate 的方法名稱；若註冊發生於 local function／lambda，名稱可能是其外層成員，不保證顯示 local function 名稱。介面與實作都有標註，也可在需要時明確傳 caller。

依使用者後續決定，DefaultSkinModelPath 維持原持久化行為，由 Model 分支處理；前文分析不代表本次提出修改需求。

### 已完成驗證

- `dotnet build VSBot.slnx --no-restore -v quiet` 通過：0 errors、14 個既有 Buy 選單棄用警告。
- `git diff --check` 通過，保留原檔案 BOM／換行格式。
- Repository 外 `F:\Codex\SwiftlyS2\cache-ready-verification` 使用本次實際編譯的 PlayerDataService，搭配 SDK interface、Repository／Worker／Scheduler 替身；25 項檢查通過，程序 exit code 0。
- 覆蓋：Worker 完成後必須等排程執行才通知、順序與玩家隔離、Ready 同步取得相同資料、dirty 更新不換 reference、List 清空、Remove／Clear／Shutdown／SaveAndRemove 後舊 entry 不執行、callback 例外及移除／重建／清空／shutdown／巢狀註冊、load failure 與 queue rejection、介面 CallerMemberName、Ready 無資料的拒絕行為。
- 測試使用可控制的執行佇列，不是 CS2 native scheduler、真 SQL 或真離線事件。Disconnect 流程以程式審閱與 SDK 查證確認，尚未宣稱 Server 實測通過。

### 可直接進行的 Server 回歸測試

1. 部署本次建置並啟動，連線後等既有 `Player cache added` 訊息，測試 !volume、語言設定，離線重連確認保存正常。
2. 剛連線就立即離線、再重連數次，確認新連線可正常取得快取，沒有舊工作造成重複操作或例外。
3. 開啟 !ss_lang 後離線重連，確認原 UI 清理仍正常；在音樂延遲播放期間離線，確認音樂清理未受 early return 重構影響。
4. 正常遊玩與快速換圖，確認 Bot 清理及真人回合流程正常；換圖後快取可重新準備，插件卸載不出現新增例外。

以上是既有行為回歸，不會觸發新 API。callback 的確定性情境已由上述 25 項替身檢查驗證；要進行原生端到端測試，需在驗證用建置或 Model 正式呼叫端，在 PreparePlayerCache 後註冊 callback（記錄 SteamID／Caller），確認 Ready 只執行一次，再測載入中離線／換圖不執行舊工作。正式分支不為此加入示範功能、延遲或診斷指令。
