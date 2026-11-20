# BotModule 計時器生命週期分析與修正方案

- 日期：2026-11-20。
- 分支：`fix/bot-timer-lifecycle`。
- 基準：已 pull 的 `main`，`4a1bc09`。
- 狀態：分析與方案，尚未修改執行邏輯、部署或實機驗證。
- 範圍：完整盤點 BotModule 內的秒數計時器、下一次 world update 回呼，以及呼叫端的回合／地圖／玩家清理入口。不是全專案的 timer 稽核。

## 結論與證據界線

BotModule 有兩處 `DelayBySeconds`，沒有 Repeat 計時器：`EnableRandomSpawn` 與 `PreventBotPickupWeapon`。兩者都只註冊 `StopOnMapChange`，未保留回傳的取消控制物件供回合結束時使用。

這是可直接從程式確認的生命週期缺口。`StopOnMapChange` 只處理換圖，不能代替回合結束取消。已核對先前下載的 SwiftlyS2 1.5.3 SchedulerService 原始碼：秒數計時器使用插件生命週期 token，`StopOnMapChange` 額外註冊換圖取消；並沒有自動處理遊戲回合。

`2026-11-20_22-52-25.log` 顯示多次快速 `!slay @ct` 結束回合。以當前 cfg 的 `mp_buytime=20` 推算：22:53:55 排定的 random spawn 啟用，會在約 22:54:15 到期；中間 22:53:59、22:54:10 都結束過回合，下一回合在 22:54:17 開始。舊計時器因未取消，可能在新回合出生前將 `mp_randomspawn` 改回 1。

log 沒有記錄每次 timer 到期、ConVar 寫入或出生座標，因此以上是程式與事件時間所支持的因果推論，不是已觀測到所有原生執行細節。撿武器 timer 的跨回合問題尚無實測故障證據，應記為程式風險。

## 全部排程盤點

| 排程／呼叫點 | 現況 | 判斷與處理 |
| --- | --- | --- |
| `EnableRandomSpawn` | 每回合排一個 buyTime 秒後的 timer，只在換圖取消 | 確認有缺口：回合結束關閉 ConVar 後，舊 timer 仍可重新開啟；重複呼叫也不替換前一筆。優先修正。 |
| `PreventBotPickupWeapon` | 每隻 Bot 在開局及重生後排 freezeTime timer，以名稱重新查找 Bot；只在換圖取消 | 確認缺乏回合取消及同 Bot 去重。舊工作可能作用於新生命／新回合的同名 Bot，提早禁止撿武器。修正追蹤與取消。 |
| `RespawnBot` 三個回呼 | 排程前檢查回合結束，回呼只有地圖世代保護；第三個回呼會新增撿武器 timer | 回合可能在排程後結束，玩家物件也可能失效。即使清空 timers，這些回呼仍可能重新加回 timer。需在執行時檢查所屬回合與玩家。 |
| `DisableBotMovement`、`KickBot`、`AddSpecialBot`、`LogMapStartupState` | 使用 `NextMapWorldUpdate`，地圖清理使舊回呼失效 | 開場工作有地圖保護，沒有秒數 timer；保留。 |
| `FillNormalBot` | 使用 `NextMapWorldUpdate`，也會在 RoundEnd 排程補下一回合的 Bot | 這是刻意在回合結束後執行的工作，不能一律以 `IsRoundEnd` 阻擋。保留，不納入回合 timer 清空。 |
| `DisableCheats`、`EnableBotMovement` | 原生 `NextWorldUpdate`，warmup 結束觸發 | 無秒數 timer；沒有自動跨圖取消保證，但目前沒有相關殘留證據。本輪不全面改寫。 |
| `SetBotMoney`、`RemoveBotWeapon`、`GiveBotWeapon`、`SetBotScore`、`SetBotHealth`、`AllowBotPickupWeapon` | 原生 `NextWorldUpdate`，回呼按名稱取得有效 Bot | 名稱與有效性不等於同一玩家生命，短回呼也不是絕不跨回合；記錄風險，不把所有工作轉成自訂 Scheduler。 |
| `SetBotArmor` | 外層原生 `NextWorldUpdate` 查 Bot，兩個內層使用 `NextMapWorldUpdate` 並持有 Bot | 地圖世代不保證同回合／同 pawn，也不取代執行時有效性檢查。屬短回呼實體風險，與本次秒數 timer 問題分開；若修改此處應有針對性測試。 |

`NextWorldUpdate` 不回傳 timer 取消物件。以上短回呼是排隊中的一次性工作，不應與未取消的秒數 timer 混稱為記憶體洩漏。

## 建議的最小修正

### 1. Random spawn：單一 timer 欄位

- 使用一個 `CancellationTokenSource?` 欄位保存待啟用 timer。
- 每次排程前先取消前一個；回呼正常開始時清掉追蹤欄位，再執行啟用。
- `DisableRandomSpawn` 先取消待播 timer 並清空欄位，再下達 `mp_randomspawn 0`。
- `OnRoundEnd` 一進入就取消回合 timers／關閉 random spawn，放在 scoreboard 等其他工作之前，確保後續工作失敗不會略過取消。
- 新回合開始時先清理上一輪尚未完成的工作，再建立本回合 timer；warmup 結束及新地圖初始化沿用關閉方法。
- `ClearMapResources` 取消 timer，但不用對正在卸載的世界再送 ConVar 指令。保留原生 `StopOnMapChange`。

### 2. 撿武器：每個 Bot 最多一個待執行 timer

- 用一個小型字典追蹤 Bot 的 timer，排新工作前取消舊工作；完成、回合結束、換圖／插件清理時移除。
- 使用玩家欄位定位清理，回呼再確認是原 Bot／原 pawn；不要以名稱或 Bot SteamID 作為唯一身分。實作時使用 SDK 可取得的連線或 entity handle／serial 比對，避免單憑 slot 判斷。
- Bot 重生、開始允許撿武器時，先取消上一個生命的撿武器 timer，避免等待中的舊工作提前禁止撿武器。
- 回合結束先取消全部撿武器 timers，再踢除／補 Bot。這不影響正常下一回合的 freezeTime 延遲。
- Bot 離線／被踢除時取消其 timer。現有 `PlayerDisconnectHandler` 的業務清理在 `!IsFakeClient` 內，Bot 清理入口必須放在此條件之外；即使沒有有效 pawn 也應能按 slot 清理。
- 保留 `StopOnMapChange`，不增加輪詢、不用每次 tick 清字典。

### 3. 防止清完後又新增舊工作

- 在 `RespawnBot` 的各個延遲回呼執行前，檢查地圖仍有效、玩家有效、回合尚未結束且仍是排程時的回合。
- 對同圖重啟／回合數重用，單純比較 `CurrentRound` 不夠。若要完整涵蓋此情境，可只加一個 BotModule 回合取消 token：開局建立、結束取消，回呼捕捉 token 並檢查；不建立通用排程管理器，也不重寫所有 `NextWorldUpdate` 呼叫。
- `PreventBotPickupWeapon` 排程入口也檢查當前回合可執行，避免舊重生回呼在回合結束後重新加入 timer。
- 不把 `FillNormalBot` 加入這項回合已結束禁止執行的規則，它本來就負責回合結束後補 Bot。

回合取消 token 僅針對不回傳取消物件、又會新增 timer 的重生回呼；秒數 timer 本身仍用 SwiftlyS2 回傳的物件直接 `Cancel()`。不要對仍被 Scheduler 或 `StopOnMapChange` 註冊引用的取消物件立即 `Dispose()`，以免後續框架取消時操作已釋放物件。

## 啟用時機是另一個決定

目前程式是「RoundStart 後等待 mp_buytime 秒」，並非真正等到 `EventBuytimeEnded`。log 22:53:05 開局、22:53:30 才購買結束，與 freeze time 5 秒加 buy time 20 秒相符。

本輪最小方案保留現有延遲時機，只修取消與殘留。若需求是嚴格在購買結束後啟用，較清楚的另案是使用現有 `BuytimeEndedHandler` 並檢查非 warmup、回合未結束；這能移除 random spawn 的秒數 timer，但會改變目前時機，不與殘留修正混在一起。

## 建議實作拆分與驗證

1. Random spawn 單 timer 追蹤與回合／地圖取消。
2. 撿武器 timer 追蹤、Bot 重生／離線清理，以及重生回呼的回合檢查。
3. 更新文件，列出實際驗證結果與仍待實機確認部分。

必要模擬情境：20 秒 timer 排入後第 4 秒結束回合，推進至第 20 秒不得再啟用；連續快速回合；重複排程替換；撿武器前 Bot 死亡重生／被踢除並重用 slot；回合結束與重生回呼交錯；換圖、重載同名地圖及插件卸載；正常長回合的啟用與撿武器限制仍會執行。

伺服器回歸：使用原本 cfg，連續於開局 2～5 秒執行 `!slay @ct` 至少五回合，確認玩家及 Bot 均在正常 spawn；另正常遊玩超過 buyTime，確認一般 Bot 死亡後仍能隨機重生。freeze time 換圖、Bot 替換與插件重載分別測試。

若需要追查實際時序，只增加受 `IsDebug` 控制的 `LogInformation`，在 random spawn 計時器排定／取消／啟用時記錄回合與延遲；不在每個 tick 或每個 Bot 一律輸出。原始 log、dump 與工作區外驗證程式不納入 repository。
