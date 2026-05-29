## Plan: Fix mp_randomspawn via PatchService

**單一目標**：在 `PatchService.cs` 新增對 `EntSelectSpawnPoint` 的 memory patch，讓 `mp_randomspawn` ConVar 在 CS2 中正常運作。只修改一個檔案，不新增任何事件。

---

**Steps**

**Phase 1 — 擴充 PatchDefinition**
1. `PatchService.cs` 頂部加入 `using System.Runtime.InteropServices;`
2. 將 `PatchDefinition` record 改為支援跨平台與 offset：
   - 以 `WindowsSignature` / `LinuxSignature` 取代單一 `Signature`
   - 新增 `WindowsOffset` / `LinuxOffset`（預設值 0，向下相容）
   - `ExpectedOriginalHex` 改為 `string?`（null = 跳過驗證）
   - record 內加入計算屬性 `Signature` 與 `Offset`（依 `RuntimeInformation` 選 OS）

**Phase 2 — 更新 _definitions**
3. 更新既有 `Fix_BotAdd_Quota_Logic` 使用新 record 語法（Windows/Linux 相同 signature，offset=0，驗證維持）
4. 新增 `Fix_RandomSpawn`（*depends on step 2*）：
   - `WindowsSignature`: `48 83 EC ? 48 89 5C 24 ? 33 DB 48 89 74 24`
   - `LinuxSignature`: `55 48 89 E5 41 57 41 56 41 55 41 54 49 89 FC 53 48 83 EC ? 4C 8D 2D`
   - `PatchHex`: `90 90`
   - `ExpectedOriginalHex`: `null`（patch 在 offset 處，原始值未知）
   - `WindowsOffset`: 145、`LinuxOffset`: 64

**Phase 3 — 修改 ApplyPatchInternal**（*depends on Phase 1*）
5. 計算實際 patch 位址：`nint patchAddress = baseAddress.Value + definition.Offset`
6. 後續所有 `address.Value` 替換為 `patchAddress`
7. `ExpectedOriginalHex` null check：null 時跳過 `ValidateBytes`，直接備份 + 寫入

---

**Relevant files**
- `VSBot/Services/PatchService.cs` — 唯一修改的檔案，影響 `PatchDefinition` record、`_definitions` 字典、`ApplyPatchInternal` 方法

---

**Verification**
1. `dotnet build` 編譯無錯誤
2. Plugin 載入後 server log 出現 `Applied patch 'Fix_RandomSpawn' (Type: AutoFix)`
3. Server console 執行 `mp_randomspawn 1`，確認玩家出生點隨機
4. 所有訊息，包含 Logger 或錯誤例外應使用英文撰寫，其格式需有一致性
5. 遵守 Clean Code 以及 SOLID 原則進行開發、實作
6. 原則上不寫註解，如果是邏輯過於抽象或是記憶體位址人類難以直覺理解才寫註解，並且註解僅可使用英文撰寫
7. Coding Style 須符合 Microsoft C# Coding Standard，不要有蛇型命名等其他不屬於 C# 常見規範，除了 Interface (Ex: ITheService.cs) 之外也禁止使用匈牙利命名
8. 如果有建立或修改 Interface，method 需補上 XML summary 註解表示用途以及用法，並使用英文撰寫

---

**Decisions**
- 不新增任何 Game Event handler — patch 套用後 ConVar 本身即可生效，無需額外邏輯
- `ExpectedOriginalHex: null` 跳過驗證是刻意設計：patch 位址在 signature 末端 offset 處，原始 bytes 無法從 gamedata 取得，強行驗證會需要硬編碼不穩定的值
- 兩個 patch 的 `PatchType` 皆為 `AutoFix`，在 `Load()` 的 `ApplyAllAutoPatches()` 中自動套用，不需額外呼叫
