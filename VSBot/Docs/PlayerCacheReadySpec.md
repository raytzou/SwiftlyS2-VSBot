# WhenPlayerCacheReady 規格書

- 日期：2026-11-27。
- 分支：`feature/player-cache-ready`。
- 基準：最新 `main`，commit `f062e73`。
- 狀態：分析與規劃，尚未實作。參考使用者指定的快取重試機制對話，並核對目前 Repository。

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
