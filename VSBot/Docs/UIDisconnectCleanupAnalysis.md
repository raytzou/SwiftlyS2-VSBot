# 玩家離線 UI 殘留分析與修正計畫

- 分析日期：2026-11-26。
- 分支：`fix/ui-disconnect-cleanup`。
- 基準：已 pull 最新 `main`，commit `42b8f2c`。
- 狀態：已依核准規格完成程式修正與建置；尚待伺服器驗證。原分析與計畫保留，實作結果見文末。

## 結論與證據

目前 `PlayerDisconnectHandler` 沒有呼叫任何 UI 清理。三個 UI 都是 Singleton，每張地圖各建立一個共用的 `CCSCustomHudLayout`，只有換圖／插件卸載才銷毀。玩家離線不等於 HUD entity 卸載。

`SaysoundLanguage.Open(playerId)` 設定該玩家的 `is-open` class、input capture 與四個 dialog variables。`Close(playerId)` 能關閉及釋放輸入，但只有按鈕等操作會呼叫，直接離線不會呼叫。因此缺少離線清理是確定的程式缺口；使用者回報重連仍開啟，與共用 HUD 中舊 playerId 狀態未被重設一致。

SDK getter 讀取 `PlayerLayoutStates[playerId]`，這些狀態不是以 SteamID 或 SessionId 儲存。相同 slot 重用是必須測試的情境，包含其他玩家接手 slot。尚未用原生狀態快照確認此次重連是否取得相同 playerId，也不將此分析解讀為已證明引擎所有離線路徑都不清理狀態。

MOTD 每次 `PlayerConnectFullHandler` 都會重新 `Open()`，覆寫文字並開啟，因而無法單憑外觀正常證明它沒有舊狀態。Scoreboard 也有 per-player 數值，必須一併清除。

## 專案檢查範圍

| 位置 | 現況與處置 |
| --- | --- |
| `Main.GameEvents.cs` | Disconnect 為 Pre hook；先處理音樂與 Bot，再檢查 `IsValid`，沒有 UI 清理。ConnectFull 直接開啟 MOTD。 |
| `Main.cs` | Disconnect 註冊／解除使用 Pre，已配對。UIModule 與三個 UI 為 Singleton，沿用。 |
| `Modules/UIModule.cs` | 管理地圖初始化、卸載與點擊路由，缺少單一玩家的清理入口。 |
| `UI/SaysoundLanguage.cs` | 開啟狀態、輸入捕捉、標題與選項文字均依 playerId 寫入。 |
| `UI/Motd.cs` | 顯示狀態、輸入捕捉、玩家名稱等文字依 playerId 寫入。 |
| `UI/Scoreboard.cs` | `bot_respawns/remaining` 依 playerId 寫入；Open／Close 為空，不能只統一呼叫 Close 就完成清理。 |
| `Commands/Motd.cs`、`Commands/SaysoundLanguage.cs` | 同步呼叫 UI.Open，沒有延遲重新開啟選單的排程。 |
| `Services/PlayerDataService.cs` | 非同步 DB 回傳只套用玩家快取，不會重新開啟 UI；語言偏好是持久資料，不屬於應刪除的 HUD 狀態。 |
| `Modules/BotModule.cs` | 呼叫 Scoreboard.Update 更新剩餘重生次數；本次不改 Bot timer／回合流程。 |
| `Commands/Buy.cs` | 使用框架 MenusAPI，並非上述 CustomHud entity，由框架管理；納入離線回歸測試，不在缺乏異常證據時自行重作其生命週期。 |
| `Main.Events.cs` | 換圖已透過 UIModule.OnMapUnload 清理整個 HUD；保持現有時機與世代檢查。 |

本機 Panorama 資產為編譯後的 vxml_c／vcss_c，未把未讀取的 XML 預設顯示行為當成依據。關閉狀態沿用現有 Close 的明確設定。

## 建議介面與呼叫順序

在既有 internal `IUIComponentLifecycle` 增加：

```csharp
void OnPlayerDisconnect(int playerId);
```

三個 UI 都實作，沿用 explicit interface implementation。離線清理就是生命週期的一部分，現有 `_components` 已包含全部 UI，不需要新增另一個介面、清單、DI 註冊或 dispatcher。公開的 `IUIComponent.Open/Close` 及專屬介面維持原狀。

新增公開入口：

```csharp
public void OnPlayerDisconnect(int playerId)
```

此方法放在 UIModule 類別內。入口先確認 `0 <= playerId < _core.PlayerManager.MaxPlayers`，再遍歷 `_components` 呼叫元件的 OnPlayerDisconnect。每個元件檢查自己的 Layout 是否存在且有效；不依賴 `_initialized` 旗標阻止局部已建立資源的清理。

在 `PlayerDisconnectHandler` 取得非 null player 後、既有 `!player.IsValid` 提早返回之前，同步加入：

```csharp
GetRequiredService<UIModule>().OnPlayerDisconnect(player.PlayerID);
```

保留既有音樂與 Bot 清理，不更動後續資料存檔。UI 清理接收 playerId，不重新 GetPlayer、不要求有效 Pawn，也不以 SteamID／SessionId 當 HUD 索引。

此入口對所有離線 slot 呼叫，包含 Bot。原因是 HUD 狀態以 slot 為單位，Scoreboard 的 Update 也沒有直接檢查 IsFakeClient；清理無設定的 slot 是安全的重設，且避免依賴玩家類型推定 slot 歷史。這與 BotModule 只負責 Bot 資源的語意不同。若 player 為 null，維持原事件直接返回，不猜測 UserId 與 playerId 的轉換。

全部在現有同步 game event 中處理，不使用 NextWorldUpdate、DelayBySeconds、Async 版本或 OnTick。避免離線後排程延遲到下一個使用相同 slot 的玩家。也不新增多層 try-catch；已核對 setter 要求 main thread，遵守既有事件執行路徑即可，無需為假設框架失效另外建立重試機制。

## 各 UI 的具體實作

### SaysoundLanguage

新增 `void IUIComponentLifecycle.OnPlayerDisconnect(int playerId)`：

1. Layout 為 null 或無效就返回。
2. 呼叫現有 `Close(playerId)`：`SetInputCaptureEnabledForPlayer(playerId, false)`，並把 `ss_lang_root/is-open` 設為 `k_eHudPanelClassStatus_DoesNotHaveClass`。
3. 對以下 panel／variable 呼叫 `RemoveDialogVariableStringForPlayer(playerId, panelId, variableName)`：
   - `ss_lang_title`／`title`
   - `ss_lang_label_en`／`label`
   - `ss_lang_label_ja`／`label`
   - `ss_lang_label_zh_tw`／`label`

不刪除 PlayerCache.Language 或 DB 語言偏好；下一次手動開啟時既有 Open 會重新填入文字。

### Motd

新增相同生命週期方法，檢查 Layout 後：

1. 呼叫現有 `Close(playerId)`，設定 `motd_root/hidden = HasClass` 並釋放該玩家 input capture。
2. 移除 `motd_body/body`、`motd_link1/link1`、`motd_link2/link2` 三個 dialog variable overrides。

每次連線仍由原本 ConnectFull 呼叫 Open，重新填入玩家名稱、文字、版本並開啟 MOTD。

### Scoreboard

新增相同生命週期方法，檢查 Layout 後，移除 `bot_respawns/remaining` 的 per-player dialog variable override。它沒有本專案設定的 input capture 或開關 class，不加無意義的關閉操作。

下一次既有 Update 會寫入當前數值。重連後「立刻顯示當前數值」是另一個初始化需求：現況僅特定 Bot 流程呼叫 Update，本次先確保不繼承上一個 slot 的覆寫，不把預設值或暫時空白誤判成已完成即時更新。若驗收要求立即顯示，再另行加入最小的連線初始化，不能以保留舊數值達成。

### 清理的界線

RemoveDialogVariableStringForPlayer 移除的是玩家覆寫，回到全域設定，不是把文字寫成空字串。顯示 class 與 input capture 則明確設成關閉／false；這是重設狀態，不宣稱刪除了 HUD 全部底層儲存結構。

不直接 Clear PlayerLayoutStates／schema vector、不 Despawn 共用 Layout、不清空 UIModule 點擊字典、不改全域 input capture。上述操作會牽涉其他在線玩家或繞過引擎更新 API，沒有必要。

## 預計變更與提交範圍

| 檔案 | 預計變更 |
| --- | --- |
| `UI/Interfaces/IUIComponentLifecycle.cs` | 加入 OnPlayerDisconnect(int playerId) 契約。 |
| `Modules/UIModule.cs` | 加入公開離線入口、範圍檢查及元件分派。 |
| `Main.GameEvents.cs` | 在有效性提前返回前，呼叫統一 UI 清理入口。 |
| `UI/SaysoundLanguage.cs` | 關閉並移除四個文字覆寫。 |
| `UI/Motd.cs` | 關閉並移除三個文字覆寫。 |
| `UI/Scoreboard.cs` | 移除剩餘次數覆寫。 |
| `Docs/UIDisconnectCleanupAnalysis.md` | 實作後補上實際修改、commit 與驗證結果；目前只有計畫。 |

建議程式變更作為一個完整 Conventional Commit：`fix(ui): clear per-player HUD state on disconnect`，body 條列分派、選單關閉與變數移除；文件依實際內容另作 docs commit。此處不是已完成 commit 清單。

## 實作後驗證步驟

1. 建置並檢查所有 IUIComponentLifecycle 實作者都實作離線方法。測試非法 playerId、無效 Layout、重複清理能安全返回／保持關閉，且不銷毀共用 Layout。
2. 關閉 MOTD，輸入 `!ss_lang` 保持開啟，直接 disconnect，再連入同一張地圖。預期只有 MOTD 自動開啟；關閉 MOTD 後語言選單沒有殘留，滑鼠／移動正常。重複數次。
3. 再輸入 `!ss_lang`，確認能開啟、選擇語言、關閉，且原先儲存的語言偏好仍在。
4. MOTD 開著直接離線、兩個互動 UI 同時開著離線，各自重連；MOTD 仍按既有規則自動出現且可正常關閉。
5. 使用兩個玩家：A 開 UI 後離線，B 在線的 UI 與輸入不受影響；讓另一個玩家接手 A 的 slot，確認不繼承 A 的語言選單、名稱文字或 Scoreboard 舊數值。必要時用暫時診斷確認 playerId 確實重用。
6. Scoreboard 清理後不保留該 slot 的 override；後續 Bot 更新時正常顯示。區分「沒有舊資料」與「連線即時刷新」，不把兩者混為一談。
7. UI 開啟時換圖、同名地圖重載，確認 MOTD、語言選單、Scoreboard 正常，沒有 crash。既有 map lifecycle 不應被改動。
8. 開啟 Buy 選單離線重連，確認框架選單沒有殘留；若可重現，另追查 MenusAPI，而非對所有 HUD 做全域清除。

目前不新增常態除錯輸出。若實機仍失敗，再以 `if (config.CurrentValue.IsDebug) logger.LogInformation(...)` 記錄 playerId 與清理前後 input／class 狀態；不使用 LogDebug，不提交原始 log。若發現 UserIdPlayer 為 null 導致漏清理，才評估框架更早的離線通知或連線時重設，不先增加兩套事件與補救排程。

## SDK 核對來源與限制

- 專案引用 `SwiftlyS2.CS2 1.5.1`，已核對本機套件 XML 文件的 RemoveDialogVariableStringForPlayer 語意與方法簽章。
- 已讀取 [對應 SDK HUD 實作](https://github.com/swiftly-solution/swiftlys2/blob/821ccf1acd11e81423238ee17e8873e08ebe68f0/managed/src/SwiftlyS2.Core/Modules/Schemas/Extensions/CCSCustomHudLayoutImpl.cs)：同步 setter 先確認 main thread，再呼叫原生函式；getter 依 playerId 查詢狀態。
- 此核對確認可用 API 與管理端資料存取方式，不能替代目前 CS2 runtime 的離線／slot 重用整合測試。尚未重現或量測原生清理結果。

## 實作結果（2026-11-26）

已參考「審核 UI 清理規格」對話的最終核准：接受 Scoreboard 在下一次 Update 前留空／回到 Layout 預設，不新增 ConnectFull 初始化，也不擴大處理框架選單。

### 已完成提交

| Commit | 內容 |
| --- | --- |
| `54163ac` — `docs(ui): record approved disconnect cleanup plan` | 先提交已核准的分析、介面規劃與測試步驟。 |
| `5d7c50d` — `fix(ui): clear per-player HUD state on disconnect` | 修改六個程式檔案，加入生命週期方法與分派、關閉互動 UI、移除三個 HUD 的玩家覆寫。 |

本節以後續 docs commit 記錄實作與驗證結果，commit body 條列變更。所有修改位於 `fix/ui-disconnect-cleanup`。

### 實際流程與範圍

- Disconnect Pre hook：player 非 null → Music.RemovePlayer → 僅 Bot 執行 ClearBotResources → UIModule.OnPlayerDisconnect → 既有 IsValid 判斷與玩家資料清理。
- UIModule 檢查 playerId 範圍，對全部元件同步分派，不以 `_initialized` 阻擋清理。元件自行檢查 Layout 是否有效。
- Motd 與 SaysoundLanguage 沿用 Close 關閉並釋放輸入，分別移除三個、四個文字覆寫；Scoreboard 移除剩餘次數覆寫。
- 沒有新增 timer、事件訂閱、try-catch、常態除錯輸出或連線初始化；未修改 BotModule、PlayerDataService、MenusAPI 與換圖生命週期。
- 檢查現有 ClearBotResources：只移除／取消該 Bot 的 pickup timer，不會寫回 Scoreboard。Scoreboard.Update 在 Bot 重生次數消耗及 SetMaxRespawnTimes 時同步寫入，遍歷當前 gameState.Players；正常離線流程會移除玩家，無效玩家也會被 Update 跳過。沒有捕捉舊 playerId 的延遲 HUD 寫入。

### 已完成驗證與限制

- `dotnet build VSBot.slnx --no-restore -v quiet` 通過：0 errors、14 個既有 `Commands/Buy.cs` 框架選單棄用警告。
- `git diff --check`（沿用 CRLF 設定）通過；逐一核對八個被移除的 panel／variable 與原 Open／Update 寫入項目一致。
- Repository 外的驗證程式引用本次編譯出的實際類別；無 Layout 時，UIModule 離線分派可完成且不需要重新查找玩家或 scheduler。
- 嘗試以 DispatchProxy 模擬 SDK 的 CCSCustomHudLayout，但 SDK interface 的 `From` 成員造成代理型別建立時 TypeLoadException。因此未完成有 Layout 的替身行為測試，不能宣稱 slot 隔離、重複清理與原生狀態移除已通過自動測試。此為驗證工具限制，並非本次插件建置錯誤；未因此更動產品架構，驗證程式也未加入 Repository。
- 上方八項伺服器驗收仍待執行，尤其是 Pre hook 中原生 setter 能否清理離線 slot、重連不再顯示舊選單，以及其他在線玩家不受影響。
