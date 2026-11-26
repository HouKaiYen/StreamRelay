# StreamRelay

Windows WPF RTSP 轉發工具。多路來源共用一組本機 IP、TCP 連接埠及轉發帳密，播放器以不同路徑選擇串流。

## 使用方式

1. 將 Windows x64 可攜版解壓縮到可寫入的資料夾，再開啟 `StreamRelay.exe`。發行包已包含 MediaMTX 和 .NET 執行階段。
2. 輸入本機 IP、連接埠、轉發帳號及密碼。轉發帳密皆不可包含冒號。只在這台電腦播放可用 `127.0.0.1:8554`。區域網路播放時，請輸入這台電腦的區網 IP，並允許 Windows 防火牆的該 TCP 連接埠。
3. 在左欄填入完整的來源 RTSP 連結。來源需要帳密時，請將來源帳密包含在網址中。
4. 按「啟動轉發」，再複製右欄連結到播放器。播放器請使用 RTSP over TCP。有播放器連線時，才會拉取對應的來源。
5. 最小化或關閉視窗後，轉發會繼續在 Windows 系統匣執行。雙擊圖示可還原視窗，右鍵選「結束 StreamRelay」可離開。

成功啟動轉發時，程式會將設定與每路固定路徑加密儲存在程式資料夾內的 `StreamRelay.Data/settings.dat`。在同一台電腦以同一個 Windows 帳戶使用時，可連同設定一起移動資料夾。換電腦或 Windows 帳戶時須重新輸入設定。

「Windows 登入時自動啟動」由使用者自行開啟，並登記目前程式路徑。啟用後，下次登入會依已儲存的設定在系統匣啟動轉發。移動程式資料夾後，如要繼續使用自動啟動，請從新位置重新勾選。

## 從原始碼建置

- 在 Windows 安裝 .NET 10 SDK。
- 從 [MediaMTX v1.21.4 官方發行頁](https://github.com/bluenviron/mediamtx/releases/tag/v1.21.4) 下載 Windows amd64 ZIP，將其中的 `mediamtx.exe` 及 `LICENSE` 解壓縮到 `StreamRelay/MediaMTX/`。
- 執行 `dotnet build -c Release`，從 `StreamRelay/bin/Release/net10.0-windows/` 開啟程式，或用 Visual Studio 開啟 `StreamRelay.slnx`。
- 執行 `./Publish-Portable.ps1` 可製作可攜版 ZIP。腳本會下載並核對指定版本的官方 MediaMTX，預設輸出至 `artifacts/StreamRelay-Portable-win-x64.zip`。

轉發連結包含播放帳密，請只分享給需要播放的人。
