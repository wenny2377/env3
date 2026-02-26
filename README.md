## 📁 Project Structure (Assets)

本專案於 Unity 2022.3 開發，旨在構建一個整合多模態大型模型 (VLM) 的居家服務機器人模擬環境。

### 🤖 Core Logic & Scripts (核心邏輯)

| 檔案名稱 | 功能描述 |
| --- | --- |
| **`StaticCameraManager.cs`** | **居家監控系統中心**。負責管理房間內的固定攝影機，偵測 User 行為，並透過協程 (Coroutine) 進行目標鎖定與派遣指令發送。 |
| **`RobotPatro.cs`** | **機器人巡邏與派遣控制**。控制機器人在 NavMesh 上的移動邏輯，支援「自動巡邏」與「緊急中斷（前往驗證點）」的狀態切換。 |
| **`UserEntity.cs`** | **用戶實體管理**。定義 User（如 Mom, Dad）的狀態、ID 及行為模型切換邏輯。 |
| **`RobotCameraManager.cs`** | **機器人視覺模組**。負責從機器人視角擷取快照 (Snapshot)，並將視覺資訊傳輸至後端 AI 管線。 |
| **`GodModeController.cs`** | **系統總控台**。協調監控系統、機器人與環境物件之間的全局互動。 |
| **`NetworkClient.cs`** | **通訊介面**。負責與後端 AI Server（Gemma 3, MongoDB, FAISS）進行資料交換。 |
| **`RoomArea.cs`** | **區域感測邏輯**。定義房間邊界，當 User 進入特定區域時觸發掃描與監控網路。 |
| **`ObservationLogic.cs`** | **觀察點計算**。優化機器人拍攝目標時的最佳位姿與角度判定。 |

### 🏠 Environment & Assets (場景與資源)

* **`/Apartment`**: 居家環境模型與場景佈置。
* **`/mom` & `/dad**`: User 角色模型及其行為動畫（如：typing, drinking, sleeping）。
* **`/eve`**: 機器人實體模型（Eve 模型與 URDF 配置）。
* **`/Scenes`**: 存放主要的實驗場景（如：SampleScene）。
* **`/urdf`**: 存放機器人的描述文件，用於精確的物理模擬。
* **`scene_snapshots.json`**: 存儲環境狀態與行為特徵的快照紀錄。

---

### 🚀 技術亮點 (Technical Highlights)

* **多人併行監控**：`StaticCameraManager` 採用 `Dictionary` 管理多對象監控行程，確保多個 User 同時存在時，系統能獨立追蹤而不互相干擾。
* **狀態中斷機制**：機器人具備緊急任務介入功能，可立即中斷例行巡邏，前往由 VLM 指定的行為異常點進行驗證。
* **動態模型檢索**：`UserEntity` 結合 `Renderer` 檢查機制，確保即便在動畫切換瞬間，監控系統仍能精確鎖定用戶的視覺中心。

---

## 🛠️ 系統運作流程 (System Workflow)

本模擬環境的核心邏輯圍繞著「偵測、派遣、驗證」三個階段：

1. **偵測 (Detection)**:
* 當 `UserEntity`（用戶）進入 `RoomArea`（房間區域）時，`StaticCameraManager` 會被啟動並鎖定目標。
* 監控系統會持續掃描用戶的 `currentActivity`。


2. **派遣 (Dispatch)**:
* 當偵測到特定行為（如 `drinking`, `typing`），系統會計算目標位置並呼叫 `RobotPatro.cs`。
* 機器人會中斷目前的 `TogglePatrol`（自動巡邏），改為執行 `InterruptAndMoveTo` 前往驗證點。


3. **驗證與回傳 (Verification & Feedback)**:
* 機器人抵達後，透過 `RobotCameraManager` 拍攝特寫照片。
* 影像資料經由 `NetworkClient.cs` 傳送至後端 AI Server 進行語義分析。

---

## 🔧 技術修正日誌 (Technical Fixes)

在開發過程中，針對多人環境下的監控穩定性進行了以下優化：

* **併行監控優化**: 將原本全域的 `StopAllCoroutines()` 修正為使用 `Dictionary<string, Coroutine>` 獨立管理不同用戶的監控行程，解決了多人同時在場時行程互相干擾的問題。
* **動態模型中心鎖定**: 修正了僅掃描第一層子物件的限制，改用 `GetComponentInChildren<Renderer>()` 配合 `activeInHierarchy` 判定，確保在動畫切換瞬間仍能精確定位。
* **任務優先級管理**: 透過 `_isProcessing` 旗標與 `RobotPatro` 的回呼機制 (Callback)，確保機器人在執行緊急驗證任務時，邏輯狀態鎖定完整，避免指令衝突。


---

## 🎯 設計目的 (Design Objectives)

本系統的開發核心基於「主動式居家安全監測」的概念，解決傳統固定式監控系統（CCTV）的視覺盲點與反應遲鈍問題。其具體設計目標如下：

### 1. 實現行為觸發的自動化反應 (Context-Aware Triggering)

傳統監控僅提供被動紀錄，本系統設計目的是讓環境能「理解」用戶行為。當監控系統偵測到特定高風險或需驗證的行為（如：老人在非進餐時間頻繁翻找、長時間維持同一姿勢）時，能自動將語義需求轉化為機器人的物理派遣指令。

### 2. 解決居家環境中的視覺遮擋問題 (Handling Occlusion)

固定攝影機（Static Camera）常受限於家具遮擋。本系統設計了「中斷與前往（Interrupt & Move To）」機制，當固定攝影機鎖定行為但細節不明確時，派遣移動式機器人（Eve）前往現場進行近距離特寫（Snapshot），為後端 VLM 提供更高品質的視覺輸入。

### 3. 多用戶環境下的強健監控 (Multi-user Robustness)

在真實居家環境中，家中可能同時有多位成員（如 Mom 與 Dad）。本系統的設計目的之一是透過 **Concurrent Monitoring Dictionary** 機構，確保系統在多人併行活動時，不會因為單一對象的移動而漏掉其他人的安全監控，實現全天候、無死角的居家照護支援。

### 4. 降低雲端運算負載 (Optimizing Computation)

系統僅在「偵測到有效行為」時才啟動機器人相機與 VLM 通訊。這種設計能有效減少長時間傳輸高解析度影像造成的頻寬浪費與隱私疑慮，僅在必要時刻（Emergency/Validation）進行資料擷取。


