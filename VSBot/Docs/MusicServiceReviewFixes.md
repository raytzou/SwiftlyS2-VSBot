# MusicService PR #3 修正紀錄

- 日期：2026-11-20。
- 分支：`feature/music-service`。
- PR：https://github.com/raytzou/SwiftlyS2-VSBot/pull/3 。
- 範圍：三個尚未解決的 Copilot review threads、玩家事件解除註冊，以及新玩家取得上一回合曲目與離線後延遲播放問題。已 resolved 的另外三個 threads 不在本輪修改範圍內。

## Commit 與處理方式

| Commit | 對應問題 | 修改 |
| --- | --- | --- |
| `6466ef8` `fix(events): pair player event hooks with matching cleanup` | ChatGPT 發現的三項事件配對問題 | `EventPlayerDisconnect` 以 `UnhookPre` 對應 Pre 註冊；補上 `EventPlayerTeam`、`EventPlayerSpawn` 的 `UnhookPost`。 |
| `e2cad31` `fix(music): retain the selected round track` | [同名曲目選錯](https://github.com/raytzou/SwiftlyS2-VSBot/pull/3#discussion_r4638288197) | 儲存選中的 `SoundEventInfo`，曲名只用於顯示。晚加入玩家直接使用選中曲目的 `EventName`。 |
| `30a1669` `fix(music): let pending round broadcast cover joining players` | [freeze time 提早／重複播放](https://github.com/raytzou/SwiftlyS2-VSBot/pull/3#discussion_r4638288229) | 追蹤全體回合播放的計時器。全體播放尚待執行時，加入隊伍不另排個別播放，由全體播放涵蓋。 |
| `f22c27b` `fix(music): cancel stale playback across round and player lifetimes` | [停止後舊回呼仍播放](https://github.com/raytzou/SwiftlyS2-VSBot/pull/3#discussion_r4638288268)，連同新玩家取得舊曲與離線延遲播放 | `StopCurrentMusic`／`Reset` 取消全體與各玩家計時器並清空選中曲目；回合結束期間不新增個別播放。離線取消該玩家的計時器及追蹤紀錄，回呼重新檢查玩家有效性與 SteamID，避免對重用欄位的其他玩家播放。 |
| 本文件提交 `docs(music): document PR3 fixes and server checks` | 修正說明 | 記錄分支、各 commit 的用途、驗證範圍及伺服器測試步驟。 |

## 生命週期

延遲工作使用 SwiftlyS2 的 `DelayBySeconds` 回傳計時器，並保留 `StopOnMapChange`。服務只追蹤一個全體回合計時器及每個玩家最多一個加入計時器；同玩家再次加入會取消前一個待執行計時器。

`PlayOnRound` 先停止舊音樂及待播工作，再選新曲並等待 freeze time。全體回呼執行後清除待播標記，之後加入的玩家才安排個別播放。`StopCurrentMusic` 同時清除曲目資料，因此回合結束後的新玩家無法以舊曲目重新排程。既有回合結束 handler 會先停止音樂，再播放勝負音效。

`PlayOnGameOver` 先停止舊音樂與排程；插件卸載時也呼叫 `StopCurrentMusic`。換圖仍使用既有 `Reset` 清除服務狀態。玩家離線則呼叫 `RemovePlayer`，即使玩家物件已無效，只要事件仍提供玩家物件，就先清掉對應計時器與追蹤資料。

本次不新增 OnTick、播放世代或框架外的排程管理器。固定 ConVar 繼續由伺服器設定管理。

## 開發端驗證

- `dotnet build VSBot.slnx --no-restore`：0 errors，13 個既有 `Buy.cs` 舊選單 API 警告。
- 工作區外的驗證程式編譯實際 `MusicService` 原始碼，替代 Scheduler、玩家查詢與聲音發送邊界，10 項檢查均輸出通過：freeze time 加入只保留全體排程、同名曲目一致、回合結束取消與曲目清空、停止取消全體排程、離線取消、玩家欄位重用、換圖 Reset、重複加入替換待播、新回合取消 warmup 待播、遊戲結束取消舊待播。
- 模擬測試不代表 CS2 原生音效整合已驗證；實際聽感、插件熱重載與事件是否重複觸發需以下實機測試。原始 log 與驗證程式不納入專案。
- 驗證程序輸出全部通過後未自行結束，已手動停止；上述結果指十項斷言通過，不代表程序正常退出。

## 伺服器測試

準備兩位真人玩家比較播放結果，使用有效且非空的各類音樂清單；測試用 round 清單可放兩首聽感明顯不同、但 `DisplayName` 相同的曲目。完成後還原測試設定。

1. **事件卸載／重載**：使用既有插件管理方式卸載與重新載入，再換圖。進行玩家加入隊伍、出生、離線操作，確認沒有殘留 handler 引起的重複播放、重複業務紀錄或 service provider 例外。
2. **相同顯示名稱**：先讓玩家 A 開始回合並聽到音樂，玩家 B 於 freeze time 結束後加入。等待兩秒，確認 B 播放的是 A 所聽到的同一首。重複數回合，涵蓋兩個不同曲目。
3. **freeze time 加入**：暫時將 freeze time 設為 10 秒，在剩餘超過兩秒時讓 B 加入隊伍。B 不應兩秒後先播一次，應在 freeze time 結束時隨 A 一起收到一次回合音樂。
4. **回合結束取消**：回合中讓 B 加入，兩秒內結束回合。勝負音效開始後，不應又出現上一回合音樂。再讓另一位玩家於回合結束後加入，也不應啟動上一回合曲目。
5. **離線與重連**：warmup 及正式回合各測一次，加入隊伍後兩秒內離線；等待超過兩秒再重連。確認離線期間沒有無效玩家例外，重連只收到本次加入應有的音樂。可另測其他玩家接替相同 player slot，不應收到前一玩家的待播音樂。
6. **階段切換**：warmup 加入後兩秒內進入正式回合，以及 freeze time 尚未結束前結束回合／換圖，確認沒有舊階段待播音樂。測試最後回合進入 game over，確認舊待播沒有在結束音樂後出現。
7. **正常回歸**：完整遊玩數回合，確認曲名顯示、回合音樂、勝負音效與 game over 音樂可正常播放。

若失敗，保留操作時間、玩家加入／離線順序、freeze time 設定及完整 Console log；聲音問題同時記錄「先後播放／疊播／錯曲」的實際聽感，以便對照事件。
