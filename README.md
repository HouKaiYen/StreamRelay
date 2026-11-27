# StreamRelay

Windows WPF RTSP 轉發工具。多路來源共用一組本機 IP、TCP 連接埠及轉發帳密，播放器以不同路徑選擇串流。

## 使用方式

1. 從 [GitHub Releases](https://github.com/HouKaiYen/StreamRelay/releases/latest) 下載 Windows x64 可攜版，解壓縮到可寫入的資料夾後開啟 `StreamRelay.exe`。發行包已包含 MediaMTX 和 .NET 執行階段。
2. 輸入本機 IP、連接埠、轉發帳號及密碼。全新設定的帳號與密碼皆為 `admin`，可自行修改。轉發帳密皆不可包含冒號。只在這台電腦播放可用 `127.0.0.1:8554`。區域網路播放時，請輸入這台電腦的區網 IP，並允許 Windows 防火牆的該 TCP 連接埠。
3. 在左欄填入完整的來源 RTSP 連結。來源需要帳密時，請將來源帳密包含在網址中。
4. 按「啟動轉發」，再複製右欄連結到播放器。播放器請使用 RTSP over TCP。有播放器連線時，才會拉取對應的來源。
5. 最小化或關閉視窗後，轉發會繼續在 Windows 系統匣執行。將滑鼠移到圖示上，或按右鍵，可查看轉發 IP、連接埠和狀態。按一下圖示可開啟視窗，右鍵選「結束 StreamRelay」可離開。

成功啟動轉發時，程式會將設定與每路固定路徑加密儲存在程式資料夾內的 `StreamRelay.Data/settings.dat`。在同一台電腦以同一個 Windows 帳戶使用時，可連同設定一起移動資料夾。換電腦或 Windows 帳戶時須重新輸入設定。

主視窗下方會顯示可攜版與開機轉發設定檔的完整位置，可選取路徑並複製。

## Windows 開機自啟動

填好來源並停止手動轉發後，勾選「Windows 開機自啟動」，再以目前的 Windows 管理員帳戶通過 UAC 授權。程式會將 MediaMTX 安裝至 `%ProgramFiles%\StreamRelay Boot`，並註冊 `StreamRelay` 排程。

在「工作排程器 → 工作排程器程式庫 → StreamRelay」可查看「啟動時」觸發程序。Windows 開機後，排程會以內建的 Local Service 帳戶在背景啟動 MediaMTX，約延後 30 秒，無須登入 Windows。

啟用開機自啟動後，設定時使用的 Windows 帳戶下次登入會在右下角顯示 StreamRelay 圖示。按一下圖示即可開啟 WPF 視窗。可在工作管理員的「啟動應用程式」管理 `StreamRelayTray` 登入項目。從舊版升級或移動可攜版資料夾後，手動開啟新版一次即可登記目前的路徑。

停用開關會移除排程及背景元件。若要更新開機轉發設定，請先停用，修改完成後再啟用。背景設定包含來源連結，安裝目錄只允許系統、管理員及 Local Service 存取，轉發帳密以雜湊儲存。

若排程由舊版建立，可停用後重新啟用一次，讓系統匣能讀取排程的實際轉發 IP 和連接埠。

## 從原始碼建置

- 在 Windows 安裝 .NET 10 SDK。
- 從 [MediaMTX v1.21.4 官方發行頁](https://github.com/bluenviron/mediamtx/releases/tag/v1.21.4) 下載 Windows amd64 ZIP，將其中的 `mediamtx.exe` 及 `LICENSE` 解壓縮到 `StreamRelay/MediaMTX/`。
- 執行 `dotnet build -c Release`，從 `StreamRelay/bin/Release/net10.0-windows/` 開啟程式，或用 Visual Studio 開啟 `StreamRelay.slnx`。
- 執行 `./Publish-Portable.ps1` 可製作可攜版 ZIP。腳本會下載並核對指定版本的官方 MediaMTX，預設輸出至 `artifacts/StreamRelay-Portable-win-x64.zip`。

轉發連結包含播放帳密，請只分享給需要播放的人。

## 第三方授權

可攜版 ZIP 的 `MediaMTX/LICENSE` 保留官方 MIT 授權。MediaMTX Windows x64 執行檔所含的 Go 模組、Go 執行階段及 hls.js 授權與聲明收錄於 [MediaMTX v1.21.4 第三方授權](Licenses/MediaMTX-v1.21.4-windows-amd64.txt)，發行包也附有 .NET 的授權與聲明。
