# 換圖初始化與 Bot 排程修正紀錄

## 分支與範圍

- 日期：2026-11-18。
- 修正分支：`fix/map-startup-lifecycle`。
- 起點：`main`，commit `980a09a`。
- 伺服器測試環境：SwiftlyS2 runtime 1.5.3；專案的 `SwiftlyS2.CS2` 套件維持 1.5.1。
- 本分支處理換圖後的遊戲流程初始化與 Bot 排程，不包含另一分支的 UIModule 重構。

| Commit | 修改目的 |
| --- | --- |
| `b2fc070` — `fix(lifecycle): initialize gameplay after the new server starts` | 將初始化移至新伺服器啟動後，取消過期的地圖工作與事件訂閱。 |
| `05991f2` — `refactor(bot): narrow map-scoped scheduler guards` | 縮小跨地圖排程保護範圍，一般回合工作恢復原生 Scheduler 呼叫，延遲工作改用原生換圖取消機制。 |

## 原始問題與修正原因

換圖後曾出現玩家可加入非任務方、三隻特殊 Bot 未建立，以及 warmup 第 0 回合 Bot 未停止移動等現象。切回 `main` 後仍能重現，因此不能將此問題歸因於 UI 重構。

原先 `Main.OnMapLoad` 直接初始化遊戲流程，`GameStateService.OnMapStart` 再以 `NextWorldUpdate` 發出 `MapReady`。但 `OnMapLoad` 不代表新地圖的遊戲世界已經可用；僅等待下一次 world update，也不能保證回呼執行時已經切換至新世界。此時可能讀到舊地圖名稱、舊 Bot，或在新伺服器設定完成前下達指令。

因此，本分支改以新伺服器啟動事件作為初始化的前提，並以地圖世代編號使過期回呼失效。即使連續載入同名地圖，也不會只因地圖名稱相同而接受舊工作。

## 最終實作

### 新地圖啟動流程

1. `Main.OnMapLoad` 取消前一輪初始化、更新世代並標記正在載入。
2. `Main.OnStartupServer` 排入下一次 `NextWorldUpdate`；執行前確認世代與載入狀態，且同一輪只初始化一次。
3. `InitializeMap` 使用當下的 `GlobalVars.MapName`，執行原有遊戲設定、玩家狀態重設、GameState 與 Bot 初始化。
4. `GameStateService.OnMapStart` 先重設 warmup／回合狀態，再排程帶有世代檢查的 `MapReady`。
5. `OnMapUnload`、下一次 `OnMapLoad` 與插件卸載時取消過期工作，解除尚未執行的一次性 `MapReady` 訂閱。

相關檔案：`Main.cs`、`Main.Events.cs`、`Services/GameStateService.cs`、`Services/Interfaces/IGameStateService.cs`、`Modules/BotModule.cs`。

### Bot Scheduler 保護範圍

第一階段將 BotModule 的 `NextWorldUpdate` 廣泛改為 `NextMapWorldUpdate`。開場流程修復後，實測出現部分玩家／Bot 在回合開局隨機出生的情況，因此第二階段重新檢視各呼叫點，將一般回合操作恢復原本的呼叫方式。

`NextMapWorldUpdate` 仍呼叫原生 `NextWorldUpdate`，只檢查地圖是否有效及世代是否相符；它沒有額外排入第二次 world update，也沒有新增固定延遲。

| 最終處理方式 | 呼叫點／用途 | 原因 |
| --- | --- | --- |
| 保留 `NextMapWorldUpdate` | 開場 `DisableBotMovement`、`KickBot`、`AddSpecialBot` 與開場狀態紀錄 | 不讓前一次初始化的工作作用於新地圖。 |
| 保留 `NextMapWorldUpdate` | `FillNormalBot` | 回呼捕捉了當次地圖的 Bot 隊伍。 |
| 保留 `NextMapWorldUpdate` | `RespawnBot` 的回呼、`SetBotArmor` 內層回呼 | 回呼持有已取得的玩家物件，不能跨圖使用。 |
| 恢復直接 `NextWorldUpdate` | `EnableRandomSpawn`、`DisableCheats`、`EnableBotMovement` | 恢復一般回合流程原有排程方式。 |
| 恢復直接 `NextWorldUpdate` | 金錢、移除／給予武器、分數、血量、允許撿武器、護甲外層回呼 | 回呼執行時重新查找 Bot，保留原有有效性檢查，不再一律套用地圖包裝。 |

此範圍調整不表示原生 `NextWorldUpdate` 會自動取消跨圖工作，也不保證所有普通回合回呼絕不跨圖；原生 API 本身仍沒有這項保證。

### 撿武器延遲工作

移除自訂 `DelayForMap`，`PreventBotPickupWeapon` 改為：

- 使用 `_core.Scheduler.DelayBySeconds(freezeTime, ...)`。
- 將回傳的計時器交給 `_core.Scheduler.StopOnMapChange(timer)`。

`DelayBySeconds` 預設不因換圖取消，因此直接使用 SwiftlyS2 提供的 `StopOnMapChange` 管理計時器生命週期，取代自訂世代包裝，讓處理方式簡單清楚。目前沒有直接證據證明本次異常由舊世代 timer 殘留造成；這項調整是使用框架既有的換圖取消機制，不應描述為已證實的 timer 殘留修復。

### Random spawn 行為

`EnableRandomSpawn` 恢復直接使用 `_core.Scheduler.NextWorldUpdate` 下達 `mp_randomspawn 1`。保留原本由回合開始流程呼叫，以及 warmup 結束、回合結束時停用隨機出生的設計。

本次沒有新增 OnTick 輪詢、固定等待時間，或重寫出生點選擇邏輯。

### 除錯資訊

新增地圖載入世代與開場狀態資訊，受 `IsDebug` 控制並使用 `LogInformation`，不使用 `LogDebug`。開場狀態包含地圖、玩家任務方、`mp_humanteam`、`bot_stop`、warmup 與特殊 Bot 數量。

特殊 Bot 數量只是紀錄當下的取樣值，Bot 指令實際處理可能晚於此回呼；必須搭配後續加入紀錄判讀，不能把單次 `0/3` 直接當成建立失敗。

## 驗證與實測結果

### 開發端驗證

- 程式碼修改後 `dotnet build VSBot.slnx --no-restore` 成功，0 errors；13 個既有警告來自 `Buy.cs` 舊選單 API。
- 工作區外的驗證程式使用實際專案類別與模擬 SDK，16 項檢查輸出通過，涵蓋任務方與特殊 Bot 隊伍、取消舊開場工作、同名地圖重載、`MapReady` 世代、原生 random spawn 排程及計時器的 `StopOnMapChange` 註冊。
- 該驗證程序完成檢查後未自行退出，已手動停止。模擬排程驗證不等同於 CS2 引擎實測，且驗證程式未納入本 repository。

### 伺服器驗證

參考工作區 `crash reports/2026-11-18_04-34-03.log`（未將原始 log 納入本文件提交）：

| 項目 | Log 可確認的結果 |
| --- | --- |
| 換圖初始化 | 8 筆開場狀態，順序為 `de_dust2`、`de_manor_d_prefab`、`cs_office`、`de_dust2`、`de_inferno`、`cs_office`、`de_overpass`、`de_mirage`。 |
| 任務方與 warmup | 8 筆均為正確的 `humanTeam`／`mp_humanteam` 對應，且 `bot_stop=1`、`warmup=True`。 |
| 特殊 Bot | 7 筆開場取樣為 `3/3`。04:46:07 的 Mirage 取樣為 `0/3`，但後續同秒記錄三隻特殊 Bot 加入 CT 並進入回合重設流程。 |
| 正式回合 | `BotModule OnRoundStart` 共 13 次：Inferno 4 次、Office 2 次、Overpass 4 次、Mirage 3 次；`OnWarmupEnd` 共 4 次。 |
| 延遲撿武器設定 | 可見 `PreventWeaponPickup=True` 的執行紀錄，例如 04:48:00 開局後，04:48:05 多隻 Bot 套用設定。 |

使用者於多場實測中，每回合開局直接前往 Bot 隊伍出生區擊殺，回報擊殺數符合當時 Bot 數量，且玩家自己也未再遇到開局隨機出生。依此實測回報，恢復一般回合原生 Scheduler 呼叫後，本次測試中的 random spawn 異常已消失，使用者確認結果符合預期。

這份 log 沒有逐一記錄出生座標，也沒有 Scheduler 回呼與 `mp_randomspawn` 變更的高精度時間，因此無法單憑 log 重建每一隻 Bot 的實際出生位置，或證明問題是包裝回呼造成幾毫秒差異。實測改善是已觀察到的結果；精確的底層時序原因尚未證實。

Log 中仍有 `Giving up attempts to make more random spawn points` 訊息（04:36:21、04:40:00）；這是引擎嘗試產生隨機出生點的紀錄，不能單獨視為開局出生錯誤的證據。

## 後續回歸測試方式

1. 在 `de_` 與 `cs_` 地圖間連續切換，也測試重載同名地圖，確認玩家只能加入任務方。
2. 確認 warmup 第 0 回合三隻特殊 Bot 均建立，且不移動。
3. 結束 warmup，連續遊玩多回合；確認玩家與 Bot 開局位於各自出生區，並依本次實測方式核對 Bot 數量。
4. 確認遊玩中的一般 Bot 重生與撿武器限制仍正常，避免只驗證回合開局。
5. 在 freeze time 的延遲工作尚未結束前換圖，確認舊計時器不影響新地圖 Bot。

使用者後續已多次實測 freeze time 期間換圖，回報流程正常；這確認測試情境未出現異常，但不能反推舊版曾有 timer 殘留。


## PR review 後的整理與後續實測

- `BotModule.OnMapUnload()` 改名為 `ClearMapResources()`，同時更新 `OnMapStart` 及 `Main.CancelMapInitialization` 的呼叫點。方法負責使舊地圖排程失效、清除地圖有效狀態並解除一次性 `MapReady` 訂閱；名稱描述清理工作，且適用於換圖、重新初始化與插件卸載等呼叫時機。
- 保留原生 `DelayBySeconds` 搭配 `StopOnMapChange`；目前沒有直接證據指出舊世代 timer 殘留曾造成問題。
- 後續 `2026-11-18_17-45-10.log` 的 Bot 建立異常有 `Team is stacked` 訊息；本機檢查發現官方 `gamemode_casual.cfg` 被更新為預設內容。還原正確 cfg 後，使用者以未包含 `feb5ef5` 的版本再次測試，運作正常。`feb5ef5` 曾加入插件強制設定四項固定 ConVar，已依使用者決定從分支歷史移除；固定玩法設定交由 cfg 統一管理。
- `2026-11-18_19-36-19.log` 記錄四次地圖初始化均為 `specialBots=3/3`，任務方、`bot_stop=1` 與 warmup 正確，共八次正式回合開始，未發現 `Team is stacked`。
- 使用者多次測試 freeze time 換圖均無異常。該 log 中 19:38:08 開局後於 19:38:11 換圖、19:40:09 開局後於 19:40:11 換圖，後續初始化皆正常。
- 原始 log、dump 與工作區外的除錯／驗證程式不納入專案提交。以上是使用者實測回報與 log 核對結果，不是對未記錄之 timer 狀態的直接觀測。
