# PR #10 修正紀錄

- 日期：2026-11-28
- 分支：`feature/model-command`
- 修正基準：`2735bdb`
- 依據：[PR #10](https://github.com/raytzou/SwiftlyS2-VSBot/pull/10) 與「Review VSBot PR10」兩次檢討及最後共識。以使用者 Spawn 實測結果與最後決定為準。

## 修改內容與 Commit

| Commit | 修正與原因 |
| --- | --- |
| `910807d` `fix(models): capture native agent models during spawn` | 移除 Team 階段取得 Default Model；Spawn 同步取得並保存，再註冊 Cache Ready callback。記錄不依賴快取，且只保留本次連線首次有效原生路徑；拒絕模型清單中的自訂路徑，避免污染。離線移除 Default Model 提前至 IsValid 判斷前。 |
| `1b328ee` `fix(models): scope model operations to player sessions` | 換模排程以 SessionId 重新取得玩家，選單狀態也由 SteamID 改為 SessionId。新增 Unload，清除 defaults 並停止本次修改的換模 callback；不加入 Map Generation。 |
| `4c64df6` `fix(models): reset preferences before a player has spawned` | Reset 先清除 SelectedModelName 並 UpdatePlayerCache，不再要求 Default Model 存在。指令與選單以目前非 Bot 玩家作入口，不以有效 Pawn 阻擋未 Spawn 玩家。不能立即還原時提示下次 Spawn；Apply 訊息改為偏好已選定。 |
| `0026194` `fix(models): skip empty arm resources and reuse bot model names` | Precache 過濾 null／空字串／空白 ArmPath；Special Bot 模型選擇只取得一次 ModelNames List。 |

本文件另以 docs commit 提交。

## 行為細節

- `SetPlayerDefaultModelPath` 改為同步操作，透過 SessionId 取得當前玩家；要求存活與有效 Pawn，在任何本次 Spawn 的自訂模型套用之前完成記錄。已有原生路徑不覆寫；自訂路徑比較忽略大小寫並統一斜線，不以 `agents/` 前綴判斷原生模型。
- Spawn 的 Cache Ready callback 只捕捉 SessionId，執行時重新取得存活玩家；仍使用原本 PlayerDataService，未修改其快取機制。
- Reset 在 Cache Ready 時一定先清除偏好並標記 dirty。死亡、尚未 Spawn 或沒有 Default Model 時，提示 `Your model will be reset on your next spawn.`；Loading 時保留原設定並提示稍後重試。
- 立即 Reset 仍沿用 NextWorldUpdate。`SetPlayerModelByPath` 增加可選的 `Action<IPlayer, bool>` 完成通知：由查回的原 session 玩家回報本次是否執行 SetModel。若排程前死亡，回報下次 Spawn；session 已離線或插件卸載則不回呼，不向重用 slot 的玩家發訊息。
- `Your model has been reset to default.` 僅在 SetModel 返回後送出，代表伺服器端已呼叫換模，不宣稱客戶端渲染已完成。
- Apply 保存偏好；存活時安排換模，死亡時留待 Spawn，訊息為 `Your selected model is now ...`。
- Plugin Unload 先將 ModelService 標記卸載並清空 defaults。這是單一卸載旗標，不是地圖世代或狀態機；原 Map 初始化與卸載清理保持不變。

## 明確維持原狀

MeshGroupMask callback 已逐段比對，與 `2735bdb` 完全相同；本次 SessionId／卸載檢查不涵蓋這個保留的 callback，也不保證同一 session 仍是相同 Pawn。未新增 Timer、Retry、Map Generation 或 session Dictionary。`!models`、外部資源檔名、PlayerDataService、無關 typo 均未修改。

## 驗證

- `dotnet build VSBot.slnx --no-restore -v quiet`：0 errors、14 個既有 Buy 選單棄用警告。
- diff whitespace 檢查通過；確認修改僅涵蓋上述範圍。
- Repository 外的驗證程式連結實際 ModelService／ModelSelect 原始碼，以簡化的 SDK／Cache 替身執行 13 項檢查，全部顯示 PASS：同步記錄原生模型、防污染、舊 session 排程拒絕、無 Pawn／Default 的 Reset、Loading、選單 session 隔離、Reset 回報時機、排程前死亡、死亡時 Apply、空白 ArmPath、卸載清理。
- 驗證程式輸出 `13 checks passed` 後，執行程序未自行結束，已手動中止；因此記錄為 13 項斷言通過，不宣稱程序正常退出。
- 替身不模擬原生引擎 Spawn 時序、真實 HUD、SQL 或資源載入；以下仍需 Server 實測。

## 人工測試

1. **原生路徑與套用順序**：已有自訂模型偏好時，在 Warmup、Freeze Time 與下一回合 Spawn，確認先記錄正確 Agent，之後自訂模型正常套用；Reset 可回到原生 Agent。若需觀察路徑，使用暫時 IsDebug + LogInformation 診斷，不提交原始 log。
2. **本次主要重現情境**：回合中死亡狀態重連，尚未 Spawn 時開啟 `!models` 並 Reset。應提示下次 Spawn，下一回合保持原生 Agent；再次離線重連仍不恢復舊自訂偏好。
3. **立即 Reset**：存活時 Apply 自訂模型，再 Reset，確認模型還原且出現完成訊息。Reset 後立即死亡，確認提示與下一次 Spawn 正常。
4. **快取未就緒**：載入期間按 Apply／Reset，只提示 Loading；快取 Ready 後再按可正常保存。確認正常音量／語言保存不受影響。
5. **Session 隔離**：操作選單／Apply 後快速離線重連，同 SteamID 或另一玩家接手 slot，確認沒有舊選單操作、換模或訊息污染新 session。兩名玩家互不影響。
6. **流程回歸**：多次換圖與同名地圖重載；三隻特殊 Bot 的指定／隨機模型正常，模型選單分頁、圖片與關閉正常。測試環境可卸載插件，確認 defaults 清理且沒有本次換模 callback 繼續執行。

無 Default Model 的狀況本次不嘗試即時猜測原生路徑；偏好已清除，下次 Spawn 由引擎還原。若第三方套用的自訂模型不在本插件清單，路徑過濾無法辨識，仍依賴使用者已驗證的 Spawn 取得時機。
