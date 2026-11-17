# UIModule 重構規格書

更新日期為 2026-11-17。以下為本輪實作範圍；實作與驗證結果見文末。

## 目標

UIModule 成為所有 UI 初始化、清理及全域點擊事件的唯一管理入口。各 UI 獨立維護自己的 Layout、Open、Close 與按鈕處理。HUD 仍在 MapReady 建立。

目前三個 UI 在 Initialize 自行 += OnClicked，卻沒有配對解除。UIModule 為 Transient，UI 為 Singleton，因此統一管理後必須將 UIModule 改成 Singleton，讓註冊及清理操作使用同一個 instance。

## 介面與分派

保留現有專屬介面 IMotd、ISaysoundLanguage、IScoreboard 及明確 constructor injection。沿用現有 UI Singleton 註冊。

| 介面 | 成員與實作者 |
| --- | --- |
| IUIComponent | Open、Close，供既有 command 使用。 |
| internal IUIComponentLifecycle | 唯讀 Layout、Initialize、Unload，三個 UI 都實作。 |
| internal IClickableUIComponent | OnClicked，僅 Motd 與 SaysoundLanguage 實作。 |

管理成員採 explicit interface implementation，使具體 UI 的管理方法不直接出現在一般公開操作中。IScoreboard 保留 Update，既有 Open／Close 空操作沿用。

UIModule 將注入的三個 UI 組成固定清單，並使用 Layout.Address → IClickableUIComponent 字典。點擊事件只查找所屬 Layout 並呼叫該 UI 的 OnClicked。未知 Layout 直接忽略，ButtonId 由各 UI 自行判斷。Scoreboard 不加入點擊字典。

## 生命週期

| UIModule 方法 | 呼叫位置 | 行為 |
| --- | --- | --- |
| Register() | Main.Load，DI 完成後 | 訂閱唯一的 OnCustomHudClicked handler，以旗標避免重複訂閱。 |
| Initialize() | 現有 MapReady 回呼 | 逐一建立 UI，完成後建立點擊字典。同一地圖已初始化則直接返回。 |
| OnMapUnload() | Main.OnMapUnload | 先清空字典，再呼叫各 UI.Unload，重設初始化旗標。 |
| Unload() | Main.Unload | 解除全域事件訂閱，再執行地圖 UI 清理。重複呼叫安全。 |

各 UI.Initialize 只建立自己的 HUD。各 UI.Unload 清空 Layout reference，對仍有效且由自己建立的 Entity 執行必要移除及輸入釋放。實作時以 SwiftlyS2 1.5.1 確認實際 Entity 移除 API 與有效性檢查，避免操作已被地圖銷毀的 Entity。

採基本 try/finally 確保清理及旗標重設。初始化中途失敗則清理此次已建立的 UI 並記錄錯誤；清理個別 UI 失敗仍繼續清理其餘 UI。這部分以簡單迴圈處理。

UI 清理在 Plugin.Unload 的 game thread 上、DB worker shutdown 與 provider.Dispose 之前完成。MapUnload 的 UI 清理安排在玩家快取儲存之前。

Main 對 InitializeUIModule 的 MapReady 訂閱先 -= 再 +=，回呼執行後解除自身，MapUnload 與 Plugin.Unload 也移除尚未執行的該回呼。

## 具體修改檔案

| 檔案 | 修改內容 |
| --- | --- |
| Modules/UIModule.cs | 注入 Core，加入元件清單、Layout 字典、訂閱與初始化旗標，實作上表四個入口及私有點擊 handler。 |
| UI/Interfaces/IUIComponent.cs | 保留 Open／Close，將 Initialize 與 OnClicked 移至管理介面。 |
| UI/Interfaces/IUIComponentLifecycle.cs | 新增 Layout、Initialize、Unload 契約。 |
| UI/Interfaces/IClickableUIComponent.cs | 新增 OnClicked 契約。 |
| UI/Motd.cs、UI/SaysoundLanguage.cs | 移除全域事件 +=，提供管理介面與 Unload，保留各自按鈕邏輯。Layout 比對集中至 UIModule。 |
| UI/Scoreboard.cs | 移除全域事件 += 與空 OnClicked，加入生命週期契約及清理。 |
| Main.cs | UIModule 註冊改成 Singleton；Scrutor 排除兩個管理介面，避免額外註冊 instance；接上 Register／Unload。 |
| Main.Events.cs | 保留 MapReady 初始化入口，配對該回呼的訂閱與解除；MapUnload 呼叫統一清理。 |

新增 UI 時，實作所需介面、沿用專屬介面 DI 註冊，並加入 UIModule 的 constructor 與固定清單即可。點擊分派器不需要新增 ButtonId 分支。

## 範圍

本輪聚焦以上檔案與事件配對。原版規格中的地圖世代機制、GameStateService／BotModule 生命週期重構、hot reload 恢復、多視窗輸入協調、初始化重試框架及額外 Dispatcher／抽象基底均移出本輪。語言選單未知按鈕等獨立業務問題另行處理。

## 驗收

1. 全域 OnCustomHudClicked 的 += 與 -= 僅存在於 UIModule，重複 Register 或 Initialize 不增加訂閱或 HUD。
2. MOTD 與語言選單各自開關，點擊只觸發正確 UI 一次；兩名玩家操作互不影響。
3. 以 +map 接 Workshop collection 啟動，再切圖數次，每次語言選擇仍只有一次處理，Scoreboard 正常更新。
4. MapUnload 後字典及 Layout reference 清空，新 MapReady 可重新建立；Plugin.Unload 後全域訂閱解除。
5. 重複清理安全，UI 開啟中卸載可釋放所屬 HUD 與輸入。建置通過後，在 CS2 server 驗證這些原生行為。

## 實作與驗證結果（2026-11-17）

- 已完成上述九個程式檔案的修改，未擴大至其他模組或 UI 業務邏輯。
- 以 SwiftlyS2.CS2 1.5.1 套件對應原始碼 commit `821ccf1acd11e81423238ee17e8873e08ebe68f0` 核對 API：清理時先清空自身 Layout reference，僅對 `IsValid` 的 HUD 執行 `Despawn()`。互動 UI 先逐一釋放 per-player input capture，單一玩家釋放失敗會記錄並繼續，finally 仍嘗試移除 HUD。
- SDK 依據：[CEntityInstanceImpl](https://github.com/swiftly-solution/swiftlys2/blob/821ccf1acd11e81423238ee17e8873e08ebe68f0/managed/src/SwiftlyS2.Core/Modules/Schemas/Extensions/CEntityInstanceImpl.cs)、[CCSCustomHudLayoutImpl](https://github.com/swiftly-solution/swiftlys2/blob/821ccf1acd11e81423238ee17e8873e08ebe68f0/managed/src/SwiftlyS2.Core/Modules/Schemas/Extensions/CCSCustomHudLayoutImpl.cs)。全域 input capture 與 per-player 狀態分開，因此不只設定全域為 false。
- `dotnet build VSBot.slnx --no-restore` 通過：0 errors，13 個既有 `Commands/Buy.cs` 舊選單 API 警告，與修改前相同。
- Repository 外的獨立測試程式直接編譯本次 UIModule 與介面原始碼，以 SDK 邊界替身及假 UI 驗證以下九項：重複 Register、同圖重複 Initialize、依 Layout 分派且保留 PlayerId、清空路由及 Layout、換圖後重建且維持單一訂閱、個別清理失敗仍繼續、初始化失敗回復、失敗後可重新初始化、重複 Unload 並解除訂閱。全部通過；這不等同原生 HUD 整合測試。
- 已檢查全域點擊訂閱／解除只存在 UIModule、Scrutor 排除兩個管理介面、UI 清理早於 DB shutdown，以及 MapReady 回呼的訂閱／解除位置。
- 尚待 CS2 server 實測：兩名玩家各自操作 MOTD／語言選單、啟動 `+map` 後進 Workshop collection、多次換圖、Scoreboard 更新，以及 UI 開啟中卸載時的輸入與 Entity 釋放。建置與替身測試不能證明這些原生行為已通過。
