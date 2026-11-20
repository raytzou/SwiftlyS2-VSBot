# BotModule timer 修正紀錄

- 分支：`fix/bot-timer-lifecycle`。
- 基準：`main` 的 `4a1bc09`，未合入 `feature/music-service`。
- 分析：[BotTimerLifecycleAnalysis.md](BotTimerLifecycleAnalysis.md)。
- 狀態：已編譯與模擬驗證，待 CS2 server 實測。

## Commit 內容

| Commit | 內容 |
| --- | --- |
| `976a203` `docs(bot): record timer lifecycle analysis` | 提交兩種秒數 timer 與全部 next-world 回呼的生命週期盤點、證據限制及最小方案。 |
| `170f056` `fix(bot): cancel random spawn activation between rounds` | 保存單一 random spawn timer；重新排程、停用、回合開始／結束與地圖清理時取消。回合結束先停用，再進行其他工作。 |
| `f2aa76b` `fix(bot): scope pickup timers and respawns to active rounds` | 每個 session 至多一筆撿武器 timer，完成後移除；回合／地圖／重生／離線時取消。以 session 與 pawn handle 避免誤操作新玩家生命，並以回合 token 阻擋過期重生及允許撿武器回呼。修正 Disconnect 的 Pre 解除註冊。 |
| 本文件提交 `docs(bot): record timer fixes and server validation steps` | 更新分析狀態，記錄實作、驗證限制及測試步驟。 |

## 修正方式

### Random spawn

之前 `DisableRandomSpawn()` 只送出 `mp_randomspawn 0`，已排入的 timer 仍會到期並送出 `1`。現在此方法先取消 `_randomSpawnTimer`，再送出 `0`。`EnableRandomSpawn` 先取消前一筆，只保存最新 timer；執行時清掉欄位。

新回合先清理舊回合資源並停用 random spawn，再安排本回合啟用。回合結束先取消回合工作、停用 random spawn，再處理重生額度、難度及補 Bot。`ClearMapResources` 只取消工作，不向卸載中的世界額外送 ConVar 指令。

仍使用原生 `DelayBySeconds(mp_buytime)` 與 `StopOnMapChange`。延遲從 `OnRoundStart` 起算，並非改為 `BuytimeEnded`；本次維持現有時機。

### 撿武器與重生

`_pickupTimers` 以 `SessionId` 為 key。允許撿武器、排入新的禁止撿武器 timer，以及死亡進入 `RespawnBot` 時，都先取消該 session 先前的 timer。回呼用 session 重新取得有效 Bot，確認 pawn handle 相同後才禁止撿武器。

每次開局建立一個本模組持有的回合取消 token。結束或清理時取消，讓已排入 world update 的重生工作失效，包括同圖重開而回合數相同的情況。`RespawnBot` 在回呼內再次確認 session 與玩家有效性，避免離線後操作原玩家，也避免回合已結束後重新排入 timer。

玩家離線清理放在 `!IsFakeClient` 判斷之外，因此 Bot 被踢除也會取消；事件仍有玩家物件但已無效時，也先按 session 清理。此分支同時將 Disconnect 的 `UnhookPost` 改為 `UnhookPre`，與註冊模式配對。這與 music 分支的同一項修正一致。

`FillNormalBot` 仍可在回合結束後補下一回合 Bot。沒有增加 OnTick、通用 timer 管理器、固定額外延遲或固定 ConVar 強制設定。

## 驗證

- 最終編譯：0 errors、13 個既有 `Buy.cs` 舊選單 API 警告。
- 工作區外驗證使用實際專案類別、模擬 Scheduler／玩家邊界，共 14 項斷言通過：短回合取消、連續五回合、正常延遲、單次啟用、啟用後停用、重複排程替換、地圖取消、離線／回合／地圖清除追蹤中的撿武器 timer、同回合編號重用時舊重生失效、離線 session 不重生、有效 session 正常重生。
- 撿武器取消測試注入追蹤中的 timer，未模擬原生 pawn／WeaponServices 寫入；pawn handle 的實際生命週期及撿武器效果仍需 server 測試。
- 驗證程式印出全部通過後未自行退出，已手動停止；斷言通過不等同程序正常退出。未把該程式或原始 log 加入專案。

## Server 測試步驟

部署此分支編譯的插件，重新啟動 server。此分支來自 main，不含尚在 music 分支的音樂功能；本輪應先以 Bot 流程判斷結果。

1. **快速結束回合（優先）**：保留 `mp_buytime=20`，每回合開局 2～5 秒便用 `!slay @ct`（Bot 在 T 時改用 `@t`），連續至少五回合。每次開局先觀察玩家與 Bot 是否均在正常 spawn，再 slay。即使累積時間超過 20 秒，舊 timer 也不應影響新回合出生。
2. **正常長回合**：至少一回合持續超過 20 秒。可用 Console 的 `mp_randomspawn` 查值，確認本回合延遲到期後為 1；結束回合後立即查值應為 0。下一回合開場仍在正常 spawn；遊玩中一般 Bot 死亡後可按原規則隨機重生。
3. **撿武器 timer 跨回合**：freeze time 尚未結束便結束回合，重複數次。確認下一回合特殊 Bot 與一般 Bot 能正常取得預期武器，未被上一回合 timer 提前禁止撿武器。
4. **同回合反覆重生**：擊殺一般 Bot，待其重生後在 freezeTime 秒數內再次擊殺，重複數次。確認正常重生且武器行為正常，沒有無效 entity 例外；最後仍應進入禁止撿武器狀態。
5. **Bot 離線／替換**：在開局撿武器 timer 尚待執行時踢除一隻 Bot，再加入 Bot（盡量重用名稱／slot）。新 Bot 不應收到舊 session 的撿武器限制，Console 不應有無效玩家／pawn 例外。
6. **換圖／卸載**：在 freeze time 或 buyTime timer 尚待執行時換圖、重載同名地圖；另測插件卸載／重載後換圖。確認 warmup 特殊 Bot、開場出生與輸入控制正常，沒有舊回呼在新世界執行。

若異常，提供完整 log、實際操作時間、buyTime／freezeTime、在哪一回合及哪個出生階段發生；本次未新增高頻除錯輸出。
