# Database Worker（Channel + 單一背景 Worker）設計規格書（修訂版）

技術環境：SwiftlyS2、.NET 10、EF Core 10、SQL Server。
本文件為最終決策版本，取代先前草案。**此階段僅更新規格，不修改 Source Code。**

## 1. 背景與目標

### 1.1 問題
玩家進入伺服器時，`player_connect_full` 事件在 game thread 上同步執行 DB 查詢：

```
Main.PlayerConnectFullHandler → PlayerDataService.PreparePlayerCache
  → PlayerRepository.GetByKey / Add / Update（同步 EF Core + SQL Server）
```

查詢耗時（首次 EF model 建立、連線池冷啟動、網路延遲、例外）會卡住 game thread，
引擎判定 `ProcessMessages has exhausted its CPU time budget`，以 `NETWORK_DISCONNECT_OVERFLOW` 踢出玩家。

### 1.2 目標
- game thread 上不得執行任何 SQL I/O（`CreateDbContext`、查詢、`SaveChanges`）。
- 所有 DB 操作由單一背景 Worker 依 FIFO 順序執行。
- 事件與指令保持同步，只「提交工作」，不等待資料庫。
- 現有呼叫端（`SaysoundService`、`SaysoundVolume`、事件處理器）盡量維持原樣。

### 1.3 非目標
- 不修改 SQL Server Schema；保留 `Player.SteamId` 的 `HasConversion<long>()`。
- 不支援 Plugin Hot Reload（見 §9）。
- 不做 Retry、備份或資料恢復機制（Best-Effort Persistence，見 §7）。

## 2. 可行性評估

**結論：可行，改動範圍小。**

| 項目 | 現況 | 評估 |
|---|---|---|
| 專案數 | 單一專案 `VSBot\VSBot.csproj` | 無跨專案相容問題 |
| DB 存取點 | 僅 `PlayerRepository`（`IDbContextFactory<VSBotDbContext>`），只被 `PlayerDataService` 呼叫 | 唯一需要改的呼叫鏈 |
| `DbContext` | 每次操作由 factory 建立獨立 context | 適合背景執行緒 |
| 事件無法 `await` | GameEvent / Core Event 皆為同步回呼 | 以「只入列、不等待」解決 |
| 讀取結果用途 | 只用於填充記憶體 cache | 以 Scheduler 將結果交回 game thread |
| 現有可用機制 | `Core.Scheduler.NextWorldUpdate`（`Main.Events.cs`、`Buy.cs` 已使用） | 作為 Worker → game thread 的交接手段 |

## 3. 需要經過 Worker 的呼叫點

### 3.1 必須經過 Worker

| # | 位置 | 時機 | 現行 DB 操作 | 改後行為 |
|---|---|---|---|---|
| 1 | `Main.GameEvents.cs:37` `PlayerConnectFullHandler` → `PreparePlayerCache` | 玩家連線（含換圖後重新觸發） | `GetByKey` + `Add/Update` | 建立 `Loading` 的 cache entry，提交載入工作 |
| 2 | `Main.GameEvents.cs:70` `PlayerDisconnectHandler` → `SaveAndRemovePlayerCache` | 玩家離線 | `Update` | game thread 建立快照 → 提交存檔 → 立即移除 cache |
| 3 | `Main.Events.cs:47` `OnMapUnload` → `SaveAllPlayerCachesToDB` | 換圖 | `UpdateRange` | 建立所有 Dirty 快照 → 提交存檔 |
| 4 | `Main.cs` `Unload()` | Plugin 卸載 | **目前沒有任何存檔** | 提交最後存檔、停止 Worker、安全釋放（§8） |
| 5 | `Main.cs` `Load()` | 啟動 | 無 | 啟動 Worker、提交 Warm-up（§10） |

### 3.2 不需要 Worker（純記憶體）
`SaysoundService.PlaySaysound`、`SaysoundVolume` 指令、`GetPlayerCache`、`UpdatePlayerCache`、`ClearPlayerCaches`、`RemovePlayerCache`。

## 4. 執行緒模型與規則（全文件的一致性基準）

### 4.1 執行緒歸屬

| 資料 / 操作 | 允許存取的執行緒 |
|---|---|
| `_playerCaches`（`Dictionary<ulong, PlayerCacheEntry>`）、`_modifiedCaches`（`HashSet<ulong>`）、`PlayerCacheEntry` 所有欄位、cache 內的 `Player` | **僅 game thread** |
| `IPlayer`、遊戲 Entity、`Core.*` 遊戲 API | **僅 game thread** |
| `IDbContextFactory`、`DbContext`、`Repository`、快照 `Player` | **僅 Worker**（快照在 game thread 建立後即「移交」，game thread 不再碰它） |
| `Channel` 的 `TryWrite` | 任意執行緒（實際由 game thread 呼叫） |
| `ILogger` | 任意執行緒 |

### 4.2 規則
1. **Worker 不得讀寫 cache 集合、`PlayerCacheEntry` 欄位，也不得持有 cache 內的 `Player` 物件。**
2. 需要 `IPlayer` 的資料（`SteamID`、`Name`、`IPAddress`）一律在 game thread 先擷取成值，再傳入工作。
3. Worker 完成讀取後，透過 `Core.Scheduler.NextWorldUpdate(...)` 把結果交回 game thread，由 callback 更新 cache。
4. **Worker 絕不 `await` / 等待任何 game thread callback**（fire-and-forget）。這是避免與 §8 的同步等待形成死結的前提。
5. 存檔時，在 game thread 建立 `Player` 的**獨立快照**（逐欄位複製為新物件）後才提交；Worker 只持有快照。
6. `PlayerCacheEntry` 物件可作為「不透明 token」傳給 Worker 並原樣帶回 callback，用於 `ReferenceEquals` 驗證（§6）；Worker 不得存取其內容。

> 已初步確認：官方 `SchedulerManager.NextWorldUpdate()` 以 Lock 保護排程集合，可由背景 Worker 提交同步 `Action`，再由 game thread 執行，因此不採用 `ConcurrentQueue` 備案。階段 2 開始前仍須確認專案實際使用的 SwiftlyS2 版本與官方原始碼行為相容。
>
> 7. **Shutdown 後不得再提交新的 cache 更新 callback**：shutdown 旗標（`volatile`）在 Worker 端「呼叫 `NextWorldUpdate` 之前」檢查一次，並在 callback 於 game thread 執行時再檢查一次。

## 5. Database Worker 規格

### 5.1 介面（`Services\Interfaces\IDatabaseWorker.cs`）

```csharp
public sealed record DbWorkItem(string Name, ulong? SteamId, Func<CancellationToken, Task> Work);

public interface IDatabaseWorker
{
	void Start();                                   // Plugin Load 時呼叫一次
	bool TryEnqueue(DbWorkItem item);               // TryWrite，絕不阻塞
	Task Completion { get; }                        // Worker 迴圈結束時完成
	bool Shutdown(TimeSpan drainTimeout, TimeSpan cancelGrace);  // 同步關閉，回傳 Worker 是否已確實結束
}
```

### 5.2 行為
- `Channel.CreateBounded<DbWorkItem>(new BoundedChannelOptions(1024) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait })`。
  - `FullMode.Wait` 搭配 `TryWrite`：滿載時 `TryWrite` 回傳 `false`，**不阻塞** game thread。
  - 單一 reader，依 FIFO 執行。
- `Start()`：只在 `Load()` 呼叫一次；以**單次** `Task.Run(ProcessAsync)` 啟動迴圈，確保 DB 程式碼不會在 game thread 上執行（這是啟動迴圈，不是每次 DB 操作都 `Task.Run`）。不得在建構式啟動。
- `ProcessAsync`：`await foreach (item in reader.ReadAllAsync())`；每個工作以 `try/catch` 包住，記錄 `Name`、`SteamId`、例外與耗時，**例外不得終止迴圈**。
- 每次工作前檢查關閉用的 `CancellationToken`（§8）；已取消則停止取出後續工作並記錄丟棄數量。
- 單一工作耗時超過 500 ms 記錄 warning。
- **Cancellation 不是一般 SQL 錯誤**：捕捉到 `OperationCanceledException` 且關閉 token 已取消時，只記錄單行 Information / Debug（或僅彙總丟棄數量），不以 Error 逐筆記錄。
- Worker 內任何工作都不得等待 game thread callback。
- 註冊：`services.AddSingleton<IDatabaseWorker, DatabaseWorker>();`（`Main.RegisterServices`）。

### 5.3 入列失敗（`TryEnqueue == false`）
原因：Channel 已滿，或已停止接收（Shutdown 後）。呼叫端**不得視為已接受**，依各流程處理（§7.3），並記錄 warning（含 `Name`、`SteamId`）。

## 6. Player Cache 與 Session 驗證

### 6.1 Cache 結構
保留既有 `Dictionary<ulong, ...>` 與 `HashSet<ulong>`，僅 game thread 存取。元素改為輕量包裝：

```csharp
internal enum PlayerCacheState { Loading, Ready, LoadFailed }

internal sealed class PlayerCacheEntry
{
	public PlayerCacheState State;
	public Player? Data;       // Ready 時才有值
}
```

### 6.2 各狀態語意

| 狀態 | `GetPlayerCache` | 修改設定（`UpdatePlayerCache`） | 存檔 |
|---|---|---|---|
| Loading | `null` | 不允許（指令提示「資料尚未載入完成」） | 不存 |
| Ready | `Data` | 允許，加入 Dirty | Dirty 時存 |
| LoadFailed | `null` | 不允許 | **不存**（避免用預設值覆蓋 SQL 既有資料） |

- 新增 `PlayerCacheState? GetPlayerCacheState(ulong steamId)` 供指令判斷訊息。
- Saysound 等功能在 `GetPlayerCache == null`（Loading / LoadFailed / 無 entry）時使用**預設設定**（如 `SaysoundVolume = 50`）繼續運作，不等待資料庫。因此 `SaysoundService.PlaySaysound` 需把目前的 `continue`（略過玩家）改為使用預設音量；`SaysoundVolume` 指令僅調整訊息。
- `UpdatePlayerCache` 在非 Ready 時忽略並記錄 log（正常流程不會發生，指令已先檢查）。

### 6.3 Session 驗證：`ReferenceEquals`
以 `PlayerCacheEntry` 的**物件參考**作為 session 識別，不使用 SessionId / Generation。

流程：
1. game thread：`PreparePlayerCache` 建立新 `PlayerCacheEntry(Loading)` 放入 `_playerCaches[steamId]`，把該 entry 作為 token 放進載入工作的閉包。
2. Worker：查詢 / 寫入 DB，得到 `Player` 結果（或例外）。**不碰 entry**。
3. Worker 以 `Core.Scheduler.NextWorldUpdate` 把「(token, 結果)」交回 game thread。
4. game thread callback 套用條件（全部成立才套用，否則**丟棄結果**）：
   - Plugin 未進入 Shutdown 狀態；
   - `_playerCaches.TryGetValue(steamId, out var current)` 且 `ReferenceEquals(current, token)`；
   - `token.State == Loading`。
5. 套用：成功 → `Data = result; State = Ready`；失敗 → `State = LoadFailed`。

必須正確處理的情境：

| 情境 | 結果 |
|---|---|
| 載入未完成就斷線 | 斷線時 entry 被移除；callback 發現 `TryGetValue` 失敗 → 丟棄 |
| 快速斷線並重連 | 舊 entry 已被移除，新連線建立新 entry；舊 callback 的 `ReferenceEquals` 不成立 → 丟棄；新 entry 只會被新工作的 callback 套用 |
| 換圖 `ClearPlayerCaches` 後舊查詢才完成 | 集合已清空或被新 entry 取代 → 丟棄 |
| callback 抵達時 entry 已被替換 | `ReferenceEquals` 不成立 → 丟棄 |

> **FIFO 只保證 Worker 內 DB 工作的執行順序（例如「離線存檔」一定先於「重連載入」），不能取代 cache session 驗證**；兩者缺一不可。

### 6.4 載入工作（Worker 內）
輸入皆為值：`steamId`、`name`、`ipAddress`、`now`、token。

1. `GetByKeyAsync(steamId)`。例外 → 回報 LoadFailed（不寫入任何資料）。
2. 存在 → 更新 `PlayerName / IpAddress / LastTimeConnect` 後 `UpdateAsync`。若這次 Update 失敗：記錄 Error，**仍以已讀到的資料回報 Ready**（資料來自 DB，不涉及覆蓋風險）。
3. 不存在 → 以預設值建立（`Volume = 50`、`SaysoundVolume = 50` 等）後 `AddAsync`。Add 失敗 → 回報 LoadFailed（避免之後對不存在的列做 Update）。
4. 以 `NextWorldUpdate` 交回結果（§6.3）。

## 7. 儲存策略：Best-Effort Persistence

Player 資料僅為玩家偏好設定，不含金錢、等級、道具等進度，故不做 Retry、備份、恢復。

### 7.1 Dirty 與快照
- Dirty 標記只在 game thread 讀寫。
- **Dirty 在「Channel 成功接受快照」的當下於 game thread 清除，絕不在 Worker 的 SQL 完成後清除。**
- 因此：存檔進行中若玩家又修改設定，新的修改會重新加入 Dirty，不會被先前存檔的結果抹除；多次修改同一玩家時，最後一次存檔的快照是最新資料（FIFO 保證後提交的快照後執行）。
- Worker 的 SQL 結果（成功 / 失敗）只記 log，**不回寫 cache 或 Dirty**。

### 7.2 事件流程

| 事件 | 流程 |
|---|---|
| `EventPlayerConnectFull` | 若已有 entry 則略過；否則建立 `Loading` entry → 擷取值 → `TryEnqueue(Load)`。入列失敗 → entry 設 `LoadFailed`，記錄 warning |
| `EventPlayerDisconnect` | 取出並移除 entry、移出 Dirty。若 `Ready` 且為 Dirty：建立快照 → `TryEnqueue(Save)`。`Loading` / `LoadFailed` 不存檔 |
| `OnMapUnload` | 將所有 `Ready` 且 Dirty 的 entry 建立快照，合併為一個批次工作 `TryEnqueue(SaveBatch, UpdateRangeAsync)`；入列成功才清除這些 Dirty；之後 `ClearPlayerCaches()` |
| `OnMapLoad` | `ClearPlayerCaches()` 清除過期 cache（同時清空 Dirty）。已確認換圖時在線玩家會重新觸發 `EventPlayerConnectFull`，不需額外的全員重新初始化 |
| `SavePlayerCacheToDB(Player)`（單筆，保留 cache） | `Ready` 才處理；建立快照 → `TryEnqueue`；成功才清除該 Dirty |

### 7.3 入列失敗處理
- Load 被拒：entry → `LoadFailed`（玩家以預設設定運作，不寫 DB）。
- Save 被拒：記錄 warning，**不視為已存**。
  - 若 cache entry 仍存在（單筆存檔 / 批次），Dirty 保留，之後的存檔流程仍會嘗試。
  - 若 entry 已依生命週期移除（離線 / 換圖），資料遺失，屬 Best-Effort 可接受範圍。
- Channel 成功接受後即可依生命週期清除 cache；後續 SQL 失敗僅記錄 Error。

## 8. Unload / Shutdown

### 8.1 前提
- `Main.Unload()` 為**同步**方法，在 game thread 執行。
- 不假設 Dedicated Server 的 `quit` 一定會觸發 `Unload()`；因此未存檔的 Dirty 資料在該情境可能遺失，屬可接受的 Best-Effort。
- 因 §4.2 規則 4（Worker 不等待 game thread），game thread 同步等待 Worker **不會形成死結**。

### 8.2 `Main.Unload()` 流程（順序固定）
1. `UnregisterEvents` / `UnhookGameEvents` / `UnregisterListeners`：停止產生新工作。
2. `IPlayerDataService.PrepareShutdown()`（game thread）：
   - 設定 shutdown 旗標（之後任何遲到的 `NextWorldUpdate` callback 一律丟棄，不再碰 cache）；
   - 將所有 `Ready` 且 Dirty 的 entry 建立快照，提交一個最後的批次存檔；
   - 清空 `_playerCaches` 與 `_modifiedCaches`。
3. `IDatabaseWorker.Shutdown(drainTimeout: 3s, cancelGrace: 1s)`：
   - 停止接收新工作（`Writer.TryComplete()`，之後 `TryEnqueue` 一律回傳 `false`）；
   - 在 `drainTimeout` 內等待已接受的工作完成（Worker 迴圈自然結束）；
   - 逾時：觸發 `CancellationTokenSource.Cancel()`，通知 Worker 不再取出剩餘工作，並再等待至多 `cancelGrace`；
   - 回傳 Worker 是否「確實已結束」。
4. 依回傳值處理 ServiceProvider：

| 結果 | 處理 |
|---|---|
| Worker 已結束（`true`） | 正常 `_serviceProvider.Dispose()` |
| Worker 仍在執行（`false`） | **不得立即 Dispose**。記錄 warning，改為**延後釋放**：`worker.Completion` 完成後才 Dispose ServiceProvider；程式端以區域變數持有 provider，之後將靜態 `_serviceProvider` 設為 `null`。若 Worker 永遠不結束，provider 隨行程結束一併回收。**Completion continuation 必須自行處理例外**（`try/catch` 包住 `Dispose`，並觀察 `Completion` 的 Exception，避免 unobserved task exception） |

### 8.3 關於取消與逾時
- `Cancel()` 只是**請求**取消：EF / SQL 在連線卡住時，可能直到命令逾時（預設 30 秒）才返回，因此**不可假設 Cancel 後 Worker 立即停止**，也不可只呼叫 `Cancel()` 就視為完成。
- 逾時後的安全處理方式即上表的「延後釋放」，確保 Worker 仍使用 `DbContextFactory` 等相依服務時它們不被 Dispose。
- 遲到的 `NextWorldUpdate` callback 由 shutdown 旗標擋下，不會存取已清空的 cache。
- game thread 最長阻塞時間 = `drainTimeout + cancelGrace`（預設約 4 秒），僅發生在卸載時。
- 取消後被丟棄的佇列工作（含存檔）視為 Best-Effort 遺失，記錄丟棄數量。

## 9. Plugin Hot Reload
正式部署流程：停止 Dedicated Server → 更新 Plugin → 重新啟動 Server。

- 不支援也不特別處理 `Load(hotReload: true)`：不為在線玩家重建 cache，不設計跨 Plugin 實例的 session 恢復。
- 若仍發生 hot reload，在線玩家因無 cache 而使用預設設定，直到下次 `EventPlayerConnectFull`（如換圖）才會載入。
- `Unload()` 仍嘗試儲存 Dirty 資料（§8），但不保證 hot reload 後恢復記憶體狀態。

## 10. EF Core Warm-up

- `Load()` 於 `InitializeConfig()` **之後**呼叫 `IDatabaseWorker.Start()`，隨即透過 `IPlayerDataService.WarmUp()` 提交一次 Warm-up 工作（`Name = "WarmUp"`，`SteamId = null`）。需在 `RegisterEvents()` 之前，確保它排在任何玩家載入工作之前。
- Worker 內執行：`CreateDbContextAsync`（初始化 EF Model）→ 以 `_playerRepository.GetByKeyAsync(0)` 實際執行一次輕量 LINQ 查詢（`FirstOrDefaultAsync`，同時驗證 SQL Server 連線）。
- game thread 不等待 Warm-up。
- **Timeout 設定**：避免 Warm-up（或任何工作）長時間占住唯一的 Worker。
  - SQL Server 連線字串使用合理的 `Connect Timeout`（建議 5 秒，由使用者設定於 `config.jsonc`；實作時檢查現行設定並提醒）。
  - `Main.AddDbContext` 的 `UseSqlServer` 設定 `CommandTimeout`（建議 10 秒），不使用預設 30 秒。
  - Warm-up 工作使用「關閉 token 連結 10 秒逾時」的 `CancellationTokenSource`，逾時視為失敗並記錄，Worker 繼續處理後續工作。
- 失敗只記錄 Error；Plugin 不因資料庫暫時無法連線而失效，後續玩家載入仍會嘗試連線。
- 玩家載入工作**不等待** Warm-up 成功，只遵守 FIFO 順序（排在 Warm-up 之後）。
- 連線字串未設定的例外已延遲到 factory 首次建立 context 時，因此會在 Worker 內被記錄，不會使 `Load()` 失敗。

## 11. Repository 非同步 API

### 11.1 `IRepository<TEntity, TKey>`（`Domain\Repositories\Interfaces\IRepository.cs`）
全面改為 Async，**移除同步方法，不保留兩套 API**：

```csharp
Task<TEntity?> GetByKeyAsync(TKey id, CancellationToken cancellationToken = default);
Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
Task UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
```

### 11.2 `PlayerRepository` 規則
- `await using var dbContext = await _factory.CreateDbContextAsync(cancellationToken);`
- 查詢：`AsNoTracking().FirstOrDefaultAsync(..., cancellationToken)`。
- 寫入：`await dbContext.SaveChangesAsync(cancellationToken)`。
- Repository 無狀態，不處理 GameEvent、不呼叫 SwiftlyS2 API、不吞例外（由 Worker 統一記錄）。
- `Main.RegisterByConvention` 以 `IRepository<,>` 掃描註冊，介面方法改名不影響註冊。

## 12. `IPlayerDataService` 對外 API（維持同步）

| 方法 | 變更 |
|---|---|
| `PreparePlayerCache(IPlayer)` | 內部建立 `Loading` entry 並提交載入工作，立即返回 |
| `GetPlayerCache(ulong)` | 簽名不變；僅 `Ready` 回傳資料 |
| `UpdatePlayerCache(Player)` | 簽名不變；僅 `Ready` 時生效並加入 Dirty |
| `SavePlayerCacheToDB(Player)` | 簽名不變；內部快照 + 入列 |
| `SaveAllPlayerCachesToDB()` | 簽名不變；快照批次 + 入列 |
| `SaveAndRemovePlayerCache(ulong)` | 簽名不變；快照 + 入列 + 移除 |
| `RemovePlayerCache` / `ClearPlayerCaches` | 簽名不變；`ClearPlayerCaches` 同時清空 Dirty |
| 新增 `PlayerCacheState? GetPlayerCacheState(ulong)` | 供指令判斷訊息 |
| 新增 `void WarmUp()` | 提交 Warm-up 工作 |
| 新增 `void PrepareShutdown()` | §8.2 步驟 2 |

`PlayerDataService` 需要注入 `ISwiftlyCore`（取得 Scheduler）與 `IDatabaseWorker`。

## 13. 檔案清單

| 動作 | 檔案 |
|---|---|
| 新增 | `Services\Interfaces\IDatabaseWorker.cs`、`Services\DatabaseWorker.cs` |
| 修改 | `Domain\Repositories\Interfaces\IRepository.cs`、`Domain\Repositories\PlayerRepository.cs` |
| 修改 | `Services\Interfaces\IPlayerDataService.cs`、`Services\PlayerDataService.cs` |
| 修改 | `Main.cs`（註冊 Worker、`Load` 啟動與 Warm-up、`Unload` 流程） |
| 小幅修改 | `Services\SaysoundService.cs`（cache 為 null 時用預設音量）、`Commands\SaysoundVolume.cs`（Loading / LoadFailed 訊息） |
| 不需修改 | `Main.Events.cs`、`Main.GameEvents.cs` 的呼叫方式（建議 `PreparePlayerCache` 呼叫處加 `try/catch` 以免任何非預期例外影響連線流程，屬選用） |

## 14. 分階段實作（每階段皆須可 Build）

### 階段 1：Repository Async API 與 Database Worker（含生命週期初始化）
- `IRepository` / `PlayerRepository` 改為 Async 並移除同步方法。
- 新增 `IDatabaseWorker` / `DatabaseWorker`（含 Start、TryEnqueue、Completion、Shutdown 的 Worker 層邏輯），完成 DI 註冊。
- `Main.Load()` 在 `InitializeConfig()` 之後呼叫 `Start()`（階段 2 開始透過 Channel 載入玩家資料，因此 Worker 必須已啟動）。
- `Main.Unload()` 基本生命週期：呼叫 `Shutdown`；僅在 Worker 確實結束時才 Dispose ServiceProvider，否則暫不 Dispose 並記錄 warning（延後釋放留待階段 4）。
- 為維持可 Build，`PlayerDataService` 暫以 `.GetAwaiter().GetResult()` 橋接 Async Repository，並以 `// TEMP-BRIDGE` 標示。**含 Bridge 的版本（階段 1、2）不得啟動 Server 驗證**，只做 Build 與程式碼 Review；Bridge 於階段 2、3 移除。
- 最終程式碼不得留下任何對資料庫 Task 的 `.Result`、`.Wait()`、`.GetAwaiter().GetResult()`（`DatabaseWorker.Shutdown` 內等待 Worker 本身的有上限 `Wait(timeout)` 除外，且僅用於 Unload）。

### 階段 2：載入交接、Cache 狀態與 Session 驗證
- 引入 `PlayerCacheEntry` / `PlayerCacheState`，保留既有集合型別。
- `PreparePlayerCache` 改為提交載入工作，經 `NextWorldUpdate` 交回，並以 `ReferenceEquals` 驗證。
- 新增 `GetPlayerCacheState`；調整 `SaysoundService`（預設音量）與 `SaysoundVolume`（載入中訊息）。
- 開始前先確認 `NextWorldUpdate` 的執行緒安全性（§4.2 註）。

### 階段 3：存檔快照與離線 / 換圖整合
- 實作快照、`SaveAndRemovePlayerCache`、`SaveAllPlayerCachesToDB`、`SavePlayerCacheToDB` 入列流程與 Dirty 清除時機（§7）。
- 移除階段 1 所有 `// TEMP` 橋接，確認專案內沒有對 Repository 的 `GetAwaiter().GetResult()` / `.Result` / `.Wait()`。

### 階段 4：Warm-up、Shutdown 與完整驗證
- 提交 Warm-up 與 Timeout 設定（§10）。
- `Unload()` 依 §8 完成（`PrepareShutdown`、延後釋放與 continuation 例外處理、shutdown 旗標）。
- 完整檢視呼叫鏈：確認任何 SQL I/O 都不在 game thread 執行、無同步等待資料庫 Task。
- 執行 §15 全部驗證；Build 成功不等於遊戲內實測完成，CS2 Server 實測項目由使用者手動執行。

### 階段驗收方式
每階段完成後執行 `dotnet build`，列出異動檔案、設計決策、臨時程式碼 / 已知問題，並停止等待驗收。階段 3 完成且 Bridge 全數移除後，才開始完整 CS2 Server 實測。

每階段不得夾帶不必要的架構重構。

## 15. 驗證項目

| # | 案例 | 預期 |
|---|---|---|
| 1 | 首次啟動 Server 後立即連線 | 不因 EF 初始化或 SQL I/O 出現 Overflow |
| 2 | SQL Server 故意延遲或停止 | game thread 仍正常處理事件；玩家為預設設定；log 有 Worker 錯誤 |
| 3 | 玩家快速斷線重連 | 舊查詢結果不覆蓋新 cache（`ReferenceEquals` 不成立而丟棄） |
| 4 | 換圖 | 舊地圖的查詢結果不污染新地圖 cache |
| 5 | 修改設定後立即離線 | 存檔快照內容正確 |
| 6 | 多次修改同一玩家設定 | 最終儲存的是最新資料 |
| 7 | 存檔進行中再次修改 | 新修改的 Dirty 不被舊存檔結果清除 |
| 8 | Channel 滿載或已停止接收 | `TryEnqueue` 回傳 `false`，呼叫端正確處理且不卡頓 |
| 9 | 載入失敗（DB 不通） | entry 為 `LoadFailed`，不以預設值覆蓋 SQL 既有資料 |
| 10 | Plugin Unload | Worker 不因 ServiceProvider 提前釋放而報錯；逾時情境走延後釋放 |
| 11 | Unload 期間仍有遲到的 callback | 被 shutdown 旗標丟棄，不存取 cache |
| 12 | 資料庫工作發生例外 | Worker 繼續處理後續工作 |
| 13 | Warm-up 失敗 | 僅記錄 Error，Plugin 仍正常載入，之後的玩家載入仍可嘗試 |
