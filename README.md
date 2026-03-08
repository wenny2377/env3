# 🎮 Unity 端設定指南

靜態多相機感知系統 — Unity 整合文件

---

## 📁 Scripts 結構

```
Assets/Scripts/
├── Camera/
│   ├── CameraNode.cs           # 掛在每個相機 Pivot 上的資料容器
│   ├── StaticCameraManager.cs  # 評分演算法 + 動態張數決策
│   ├── VirtualCameraBrain.cs   # 離屏渲染 + Payload 封裝傳送
│   └── RoomArea.cs             # 房間觸發區，用戶進入時啟動感知
├── Network/
│   ├── NetworkClient.cs        # HTTP POST 傳送至 Flask 後端
│   └── SharedDataStructures.cs # 統一資料結構（不需掛在物件上）
├── Entity/
│   └── UserEntity.cs           # 用戶行為切換 + 瞄準點計算
└── Debug/
    ├── CameraDebugViewer.cs    # Game 視窗即時影像預覽
    └── GodModeController.cs    # 鍵盤控制用戶行為（開發用）
```

---

## 🗂️ Hierarchy 結構

```
Scene
├── Rooms_Root
│   ├── Kitchen              ← RoomArea.cs + BoxCollider (IsTrigger ✅)
│   ├── LivingRoom           ← RoomArea.cs + BoxCollider (IsTrigger ✅)
│   ├── BedRoom(dad)         ← RoomArea.cs + BoxCollider (IsTrigger ✅)
│   └── BedRoom(mom)         ← RoomArea.cs + BoxCollider (IsTrigger ✅)
│
├── Camera_Pivots
│   ├── Kitchen
│   │   ├── Kitchen_Cam1     ← CameraNode.cs (roomName = "Kitchen")
│   │   ├── Kitchen_Cam2     ← CameraNode.cs (roomName = "Kitchen")
│   │   └── Kitchen_Cam3     ← CameraNode.cs (roomName = "Kitchen")
│   ├── LivingRoom
│   │   ├── LivingRoom_Cam1  ← CameraNode.cs (roomName = "LivingRoom")
│   │   ├── LivingRoom_Cam2
│   │   ├── LivingRoom_Cam3
│   │   └── LivingRoom_Cam4
│   ├── Dad's Room
│   │   ├── DadRoom_Cam1     ← CameraNode.cs (roomName = "BedRoom(dad)")
│   │   ├── DadRoom_Cam2
│   │   ├── DadRoom_Cam3
│   │   └── DadRoom_Cam4
│   └── Mom's Room
│       ├── Mom'sRoom_Cam1   ← CameraNode.cs (roomName = "Mom'sRoom")
│       ├── Mom'sRoom_Cam2
│       ├── Mom'sRoom_Cam3
│       └── Mom'sRoom_Cam4
│
├── [空物件] SystemManagers
│   ├── StaticCameraManager  ← StaticCameraManager.cs
│   ├── VirtualCameraBrain   ← VirtualCameraBrain.cs + CameraDebugViewer.cs
│   └── NetworkClient        ← NetworkClient.cs
│
├── VirtualCamera            ← 專用隱形相機（見下方說明）
│
└── Users
    ├── User_Mom             ← UserEntity.cs, Tag: User, Layer: User
    │   ├── mom_typing       ← CapsuleCollider, Layer: User
    │   ├── mom_drinking     ← CapsuleCollider, Layer: User
    │   ├── mom_cooking      ← CapsuleCollider, Layer: User
    │   └── mom_idle         ← CapsuleCollider, Layer: User
    └── User_Dad             ← UserEntity.cs, Tag: User, Layer: User
        ├── dad_sleeping     ← CapsuleCollider, Layer: User
        └── ...
```

> ⚠️ `Rooms_Root` 和 `Camera_Pivots` 是完全獨立的分支，`RoomArea` 透過 `CameraNode.roomName` 自動配對，不需要是父子關係。

---

## ⚙️ Inspector 設定

### CameraNode.cs（每個 Pivot 都要掛）

| 欄位 | 說明 | 範例 |
|------|------|------|
| `nodeName` | 節點識別名稱 | `Kitchen_Cam1` |
| `roomName` | **必須與對應 RoomArea.roomName 完全一致（含大小寫）** | `Kitchen` |
| `scoreMultiplier` | 評分倍率（0.5–2.0）。俯角廣視野 → `1.2`，偏斜角度 → `0.8` | `1.0` |

> ⚠️ `CameraNode.roomName` 會直接作為 `room_name` 傳送給 Python 後端，Python 用它從 MongoDB 查詢同房間家具清單並注入 VLM prompt。**大小寫必須與 MongoDB `scene_snapshots.room` 欄位一致。**

---

### RoomArea.cs

| 欄位 | 說明 |
|------|------|
| `roomName` | 房間名稱，與 CameraNode.roomName 完全一致 |
| `autoFetchByRoomName` | ✅ 勾選後自動從全場景收集相符的 CameraNode |
| `ignoreTagCheck` | 開發測試時可勾選，跳過 Tag 檢查 |
| `cameraPivots` | 自動填入後可在 Inspector 確認收集結果 |

**BoxCollider 必要設定：**
- `Is Trigger` ✅

---

### StaticCameraManager.cs

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `userLayer` | 用戶所在的 Physics Layer，選 `User` | — |
| `scanInterval` | Idle 狀態的輪詢間隔（秒）| `0.5` |
| `singleViewThreshold` | 最高分 ≥ 此值 → 傳 1 張 | `0.85` |
| `dualViewThreshold` | 最高分 ≥ 此值 → 傳 2 張，否則傳 3 張 | `0.60` |
| `maxOutputImages` | 最多傳出張數上限 | `3` |

---

### VirtualCameraBrain.cs

| 欄位 | 說明 |
|------|------|
| `virtualCam` | 拖入場景中的 `VirtualCamera` 物件 |
| `resolution` | 渲染解析度，`512` 對 VLM 辨識已足夠 |
| `jpegQuality` | JPEG 壓縮品質（建議 `75`） |

---

### NetworkClient.cs

| 欄位 | 說明 |
|------|------|
| `flaskUrl` | Python 後端位址，預設 `http://127.0.0.1:5000/predict` |

---

## 📷 VirtualCamera 建立步驟

1. Hierarchy 右鍵 → **Camera** → 命名為 `VirtualCamera`
2. Inspector 調整：
   - `Target Display` → `Display 8`（不輸出到任何螢幕）
   - Camera 元件 → **取消勾選 enabled**（不自動渲染）
   - 刪除 `Audio Listener`（避免場景有兩個 Listener 警告）
3. 將 `VirtualCamera` 拖入 `VirtualCameraBrain` Inspector 的 `virtualCam` 欄位

> VirtualCamera 是一台「按快門才工作」的隱形相機。平常完全不渲染，只有 StaticCameraManager 觸發時才瞬移到 CameraNode 位置、拍一張、立即回到休眠狀態。

---

## 🏷️ Layer 與 Tag 設定

### 新增 User Layer
`Edit` → `Project Settings` → `Tags and Layers` → User Layer 欄位填入 `User`

### 新增 User Tag
`Edit` → `Project Settings` → `Tags and Layers` → Tags 區塊新增 `User`

### 套用到物件

| 物件 | Tag | Layer |
|------|-----|-------|
| `User_Mom`、`User_Dad` | `User` | `User` |
| 所有子動畫物件 | — | `User` |

> 選 User_Mom 改 Layer 時，彈窗選 **Yes, change children** 一次套用所有子物件。

### 子動畫物件加 CapsuleCollider

每個子動畫物件（`mom_typing` 等）需要掛 `CapsuleCollider`：

```
Direction : Y-Axis
Height    : 1.8
Radius    : 0.3
Center    : X=0, Y=0.9, Z=0
```

Raycast 需要打中實體 Collider 才能計算可視性分數。

---

## 🎮 GodModeController 按鍵對照

| 按鍵 | 動作 |
|------|------|
| `1` | Mom → sleeping |
| `2` | Mom → typing |
| `3` | Mom → drinking |
| `4` | Mom → sitting |
| `7` | Dad → sleeping |
| `8` | Dad → typing |
| `9` | Dad → drinking |
| `0` | Dad → swinging |
| `P` | 機器人巡邏 Toggle |
| `Space` | 清除所有動作 |

---

## 🔍 評分演算法

```
totalScore = visibility × 0.5 + angle × 0.3 + distance × 0.2
```

| 元素 | 權重 | 計算方式 |
|------|------|---------|
| **Visibility** | 50% | 三點 Raycast（頭 / 胸 / 腰），命中 1/2/3 條對應 0.33 / 0.67 / 1.0，0 條直接淘汰 |
| **Angle** | 30% | `Dot(camera.forward, toTarget)`，正對為 +1，背對為 -1 |
| **Distance** | 20% | 3m 最佳分，10m 衰減半徑，≥13m 得 0 分 |

最終分數再乘以 `CameraNode.scoreMultiplier`。

**動態張數決策：**

| 最高分 | 傳出張數 |
|--------|---------|
| ≥ 0.85 | 1 張 |
| 0.60 – 0.84 | 2 張 |
| < 0.60 | 3 張 |
| 全部遮擋 | 跳過，等待下次掃描 |

---

## 📡 傳送 Payload 結構（Unity → Python）

```json
{
  "image_list":        ["<base64_jpeg>", "<base64_jpeg>"],
  "image_count":       2,
  "source_nodes":      ["Kitchen_Cam1", "Kitchen_Cam2"],
  "node_scores":       [0.91, 0.74],
  "userID":            "User_Mom",
  "activity":          "drinking",
  "room_name":         "Kitchen",
  "user_pos":          { "x": 3.2, "y": 0.0, "z": 1.5 },
  "timestamp":         "2025-01-01 12:00:00",
  "robot_rotation_y":  0,
  "camera_fov":        0
}
```

> `activity` 是 Unity 模擬環境的 ground truth，**只用於事後評估，不傳給 VLM**。  
> `room_name` 是 Python 端的關鍵輸入，VLM prompt 和 MongoDB 家具查詢都依賴它。

---

## 🗄️ room_name 對齊規則

`CameraNode.roomName` → `MultiImagePayload.room_name` → Python `scene_snapshots.room`

三者必須**完全一致（含大小寫與特殊字元）**，否則 Python 無法從 MongoDB 查到同房間家具清單，VLM 會幻覺出不存在的家具。

| Unity RoomArea | CameraNode.roomName | MongoDB scene_snapshots.room |
|----------------|--------------------|-----------------------------|
| Kitchen | `Kitchen` | `Kitchen` |
| LivingRoom | `LivingRoom` | `LivingRoom` |
| BedRoom(dad) | `BedRoom(dad)` | `BedRoom(dad)` |
| BedRoom(mom) | `Mom'sRoom` | `Mom'sRoom` |

> 如果不確定 MongoDB 裡的 room 值，用 `db.scene_snapshots.distinct("room")` 查詢確認。

---

## 🐛 常見問題排查

### `room=` 是空的（Python log）
1. 確認 `CameraNode.roomName` 有填值（不是空字串）
2. 確認 `StaticCameraManager` → `VirtualCameraBrain` 的 `roomName` 參數有沿鏈傳遞
3. Python 端有 fallback：從 `source_nodes[0]` 解析（`"Mom'sRoom_Cam1"` → `"Mom'sRoom"`），但最好從 Unity 正確傳入

### VLM 辨識房間錯誤（把臥室看成客廳）
1. 確認 `room_name` 有正確傳入（見上）
2. 確認 `/scene` 同步已執行，MongoDB 裡有該房間的家具資料
3. 用 `db.scene_snapshots.find({"room": "Mom'sRoom"})` 確認家具存在

### 所有節點遮擋 → 重新等待
1. 確認子動畫物件（`mom_typing` 等）有掛 `CapsuleCollider`
2. 確認子動畫物件的 Layer 是 `User`
3. 確認 `StaticCameraManager.userLayer` 選的是 `User`
4. Console 查看 `[AimDebug]` 的 aimPos 座標是否合理（不應該是 0,0,0）

### RoomArea 觸發但沒有進入 StaticCameraManager
1. 確認 `CameraNode.roomName` 與 `RoomArea.roomName` **大小寫完全一致**
2. Play 後 Console 確認有 `[RoomArea] 已綁定 N 個相機` 的訊息
3. 若沒有，檢查 Camera_Pivots 下的物件是否有掛 `CameraNode.cs`

### RoomArea 完全沒有觸發
1. 確認 `BoxCollider` 的 `Is Trigger` 已勾選
2. 確認 `User_Mom` 的 Tag 是 `User`（或勾選 `ignoreTagCheck`）
3. 確認 `User_Mom` 有 `Rigidbody` 元件
