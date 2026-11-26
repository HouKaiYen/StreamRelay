# StreamRelay

Windows WPF RTSP 轉發工具。多路來源共用一組本機 IP、TCP 連接埠及轉發帳密，播放器以不同路徑選擇串流。

## 使用方式

1. 解壓縮 Windows 發行包後開啟 `StreamRelay.exe`。
2. 輸入本機 IP、連接埠、轉發帳號及密碼。轉發帳密皆不可包含冒號。只在這台電腦播放可用 `127.0.0.1:8554`。區域網路播放時，請輸入這台電腦的區網 IP，並允許 Windows 防火牆的該 TCP 連接埠。
3. 在左欄填入完整的來源 RTSP 連結。來源需要帳密時，請將來源帳密包含在網址中。
4. 按「啟動轉發」，再複製右欄連結到播放器。播放器請使用 RTSP over TCP。有播放器連線時，才會拉取對應的來源。
5. 最小化或關閉視窗後，轉發會繼續在 Windows 系統匣執行。雙擊圖示可還原視窗，右鍵選「結束 StreamRelay」可離開。

「Windows 登入時自動啟動」適用於目前的 Windows 使用者。成功啟動轉發時會儲存設定，包含每路固定的轉發路徑。設定使用 Windows DPAPI 加密，存放於 `%LOCALAPPDATA%\StreamRelay\settings.dat`。啟用開關後，下次登入會依已儲存的設定在系統匣啟動轉發。若移動程式資料夾，請從新位置重新勾選。

## 從原始碼建置

- 在 Windows 安裝 .NET 10 SDK。
- 從 [MediaMTX v1.21.4 官方發行頁](https://github.com/bluenviron/mediamtx/releases/tag/v1.21.4) 下載 Windows amd64 ZIP，將其中的 `mediamtx.exe` 及 `LICENSE` 解壓縮到 `StreamRelay/MediaMTX/`。
- 執行 `dotnet build -c Release`，從 `StreamRelay/bin/Release/net10.0-windows/` 開啟程式，或用 Visual Studio 開啟 `StreamRelay.slnx`。

轉發連結包含播放帳密，請只分享給需要播放的人。
