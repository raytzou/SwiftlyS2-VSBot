# Plan: 整合 PatchService 修正 bot_add Quota Bug

**TL;DR** — 直接 NOP 掉 `server.dll` orchestrator 內一條邏輯反向的 `cmovle r8d, ebx` 指令（這是引擎本身的 bug，讓 bot 計數器在第一次 +1 後就瞬間跳至配額上限）。借用 SwiftlyS2 的 signature scanner 找到地址，再用 OS 原生 API 寫入 NOP bytes。透過 `IDisposable` DI Singleton 確保 `Unload` 時自動還原。

---

## Phase 1 — Infrastructure（兩個檔案，可平行建立）

**Step 1** — 建立 `VSBot/Services/Interfaces/INativeMemoryService.cs`
直接採用 code snippet2 的 `INativeMemoryService` 介面，放入 `namespace VSBot.Services.Interfaces`。包含 `SetMemoryProtection`, `WriteBytes`, `ReadBytes`（`AllocateMemory`/`FreeMemory`/`WriteInt32`/`WriteIntPtr` 保留但本 patch 不需要）。

**Step 2**（*parallel with Step 1*）— 建立 `VSBot/Services/NativeMemoryService.cs`
直接採用 code snippet2 的 `NativeMemoryService` 實作，放入 `namespace VSBot.Services`。Windows `VirtualProtect`/Linux `mprotect` 兩路保留（跨平台）。

---

## Phase 2 — Patch Service（*depends on Phase 1*）

**Step 3** — 建立 `VSBot/Services/Interfaces/IPatchService.cs`
定義介面：`ApplyAllAutoPatches()`, `ApplyPatch(string)`, `RestorePatch(string)`, `RestoreAll()`，放入 `namespace VSBot.Services.Interfaces`。

**Step 4** — 建立 `VSBot/Services/PatchService.cs`
從 code snippet1 適配，關鍵修改：
- 移除靜態 `Kurome_Main.Core` 參考 → 改為建構子注入 `ISwiftlyCore _core`
- `Core.Memory.GetAddressBySignature(Library.Server, sig)` → `_core.Memory.GetAddressBySignature("server", sig)`
- 在同一檔案頂部定義 `enum PatchType { AutoFix, Feature }`
- Patch 定義庫只保留 `Fix_BotAdd_Quota_Logic`（其他 feature patches 不屬本專案範疇）：

  ```
  Signature:            "44 3B C3 44 0F 4E C3 E8"
  PatchHex:             "44 3B C3 90 90 90 90 E8"
  ExpectedOriginalHex:  "44 3B C3 44 0F 4E C3 E8"
  ```

- 整個 class 實作 `IDisposable`，`Dispose()` → `RestoreAll()` → DI 自動還原

---

## Phase 3 — DI 整合（*depends on Phase 2*）

**Step 5** — 修改 `VSBot/Main.cs`
- `RegisterServices()` 加入：

  ```csharp
  services.AddSingleton<INativeMemoryService, NativeMemoryService>();
  services.AddSingleton<IPatchService, PatchService>();
  ```

- `Load()` 在 `RegisterEvents()` 之後立即加入：

  ```csharp
  GetRequiredService<IPatchService>().ApplyAllAutoPatches();
  ```

- `Unload()` **不需要額外呼叫** — `_serviceProvider?.Dispose()` 已觸發所有 `IDisposable` Singleton 的 `Dispose()`，`PatchService.Dispose()` → `RestoreAll()` 自動執行

---

## Phase 4 — 清理（*parallel with Phase 3*）

**Step 6** — `BotHookModule` 保持現狀（Install/Uninstall 已 disabled），`AddSingleton<BotHookModule>()` 保留，對功能無影響但不急著移除

---

## Relevant Files

- `VSBot/Services/Interfaces/INativeMemoryService.cs` — 新建，來自 code snippet2 介面部分
- `VSBot/Services/NativeMemoryService.cs` — 新建，來自 code snippet2 完整實作
- `VSBot/Services/Interfaces/IPatchService.cs` — 新建
- `VSBot/Services/PatchService.cs` — 新建，來自 code snippet1，關鍵適配 `ISwiftlyCore` 注入
- `VSBot/Main.cs` — `RegisterServices()` + `Load()` 兩處修改

---

## Verification

1. `dotnet build` 無錯誤（特別確認 `namespace` 及 `using` 正確引用）
2. 伺服器啟動後，log 輸出 `[PatchService] 已自動套用 1 個修復 Patch`
3. `bot_add_ct expert BotName` — server console 出現 bot 加入，**無** `CreateBot() failed`，**無** crash
4. `bot_add expert BotName` — 同上
5. 重新 load plugin（hot reload）後確認 `RestoreAll` log 出現，patch 重新套用
6. 所有訊息，包含 Logger 或錯誤例外應使用英文撰寫，其格式需有一致性
7. 遵守 Clean Code 以及 SOLID 原則進行開發、實作
8. 原則上不寫註解，如果是邏輯過於抽象或是記憶體位址人類難以直覺理解才寫註解，並且註解僅可使用英文撰寫
9. Coding Style 須符合 Microsoft C# Coding Standard，不要有蛇型命名等其他不屬於 C# 常見規範，除了 Interface (Ex: ITheService.cs) 之外也禁止使用匈牙利命名
10. Interface 如果是 method，需補上 XML summary 註解表示用途以及用法，並使用英文撰寫

---

## Decisions

- PatchService 僅保留 `Fix_BotAdd_Quota_Logic` 一個 AutoFix patch；`FakeHeadshotOnly_Logic` 等 feature patches 屬友人專案功能，不引入
- 還原由 `IDisposable` + DI Dispose 自動處理，`Unload()` 不增加顯式呼叫，維持簡潔
- `NativeMemoryService` 保留 Linux 路徑（跨平台），若只跑 Windows dedicated server 也不影響
