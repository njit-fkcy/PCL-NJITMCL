''' <summary>
''' NJIT MCL 自建更新推送：从校内站点拉取版本清单并更新启动器。
''' </summary>
Public Module ModUpdate

    ''' <summary>
    ''' 更新清单地址（社团官网服务器）。
    ''' </summary>
    Public Const UpdateManifestUrl As String = "https://njitmc.club/update/njitmcl.json"

    ''' <summary>
    ''' 手动检查更新（关于页、设置页、侧边栏按钮）。
    ''' </summary>
    Friend Sub UpdateCheckByButton()
        RunInThread(Sub() UpdateCheck(True))
    End Sub

    ''' <summary>当前是否为强制更新：不询问是否更新，下载完成后自动重启替换。</summary>
    Private ForceUpdating As Boolean = False

    ''' <summary>
    ''' 启动时静默检查更新。
    ''' 强制更新不受 SystemUpdateCheck 影响：关闭了自动检查的用户也必须收到。
    ''' </summary>
    Public Sub UpdateCheckOnStartup()
        RunInThread(
        Sub()
            Thread.Sleep(4000)
            UpdateCheck(False, RespectSetting:=True)
        End Sub)
    End Sub

    Private Sub UpdateCheck(ShowResult As Boolean, Optional RespectSetting As Boolean = False)
        Try
            Dim Json As JObject = NetRequestByClientRetry(UpdateManifestUrl, RequireJson:=True).DeserializeJson()
            Dim NewCode As Integer = If(Json("versionCode") Is Nothing, 0, Json("versionCode").ToObject(Of Integer)())
            If NewCode <= VersionCode Then
                If ShowResult Then RunInUi(Sub() Hint("当前已是最新版本（" & VersionBaseName & "）"))
                Return
            End If
            Dim VersionName As String = If(Json("version")?.ToString(), "")
            Dim Changelog As String = If(Json("changelog")?.ToString(), "（无更新说明）")
            Dim IsForce As Boolean = Equals(Json("force")?.ToObject(Of Boolean)(), True)
            '关闭自动检查的用户只接收强制更新
            If RespectSetting AndAlso Not IsForce AndAlso Not Settings.Get(Of Boolean)("SystemUpdateCheck") Then Return
            Dim Files As JArray = TryCast(Json("files"), JArray)
            Dim UseFiles As Boolean = Files IsNot Nothing AndAlso Files.Count > 0
            '优先整包更新；清单保留 files 供旧版本客户端使用
            Dim Package As JObject = TryCast(Json("package"), JObject)
            Dim PackageUrls As JArray = If(Package Is Nothing, Nothing, TryCast(Package("urls"), JArray))
            Dim UsePackage As Boolean = Package IsNot Nothing AndAlso
                ((PackageUrls IsNot Nothing AndAlso PackageUrls.Count > 0) OrElse
                 Not String.IsNullOrWhiteSpace(Package("url")?.ToString()))
            Dim LegacyUrl As String = Json("url")?.ToString()
            If Not UsePackage AndAlso Not UseFiles AndAlso String.IsNullOrWhiteSpace(LegacyUrl) Then Throw New Exception("更新清单缺少下载地址")
            Logger.Info($"发现新版本：{VersionName}（{NewCode}），当前 {VersionBaseName}（{VersionCode}）")
            RunInUi(
            Sub()
                ForceUpdating = IsForce
                If IsForce Then
                    '强制更新：不提供跳过，下载完成后自动重启替换
                    MyMsgBox(
                        $"本次更新为强制更新，需要更新到 {VersionName}（当前 {VersionBaseName}）。{vbCrLf}{vbCrLf}{Changelog}{vbCrLf}{vbCrLf}点击「开始更新」后将自动下载，并在完成后重启启动器。",
                        "强制更新", "开始更新")
                Else
                    Dim Result As Integer = MyMsgBox(
                        $"发现新版本 {VersionName}（当前 {VersionBaseName}）{vbCrLf}{vbCrLf}{Changelog}{vbCrLf}{vbCrLf}是否立即更新？",
                        "发现更新", "立即更新", "稍后")
                    If Result <> 1 Then Return
                End If
                If UsePackage Then
                    RunInThread(Sub() UpdateDownloadAndInstallPackage(Package))
                ElseIf UseFiles Then
                    RunInThread(Sub() UpdateDownloadAndInstallFiles(Files))
                Else
                    '旧格式清单：整包只有一个 exe
                    RunInThread(Sub() UpdateDownloadAndInstall(LegacyUrl, If(Json("sha256")?.ToString(), "")))
                End If
            End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "检查更新失败")
            If ShowResult Then RunInUi(Sub() Hint("检查更新失败：" & ex.Message))
        End Try
    End Sub

    ''' <summary>
    ''' 下载更新包并启动新版本进程完成替换。
    ''' </summary>
    Private Sub UpdateDownloadAndInstall(Url As String, Optional ExpectedHash As String = "")
        Dim Target As String = PathTemp & "Cache\LatestNJITMCL.exe"
        Try
            RunInUi(Sub() Hint("正在下载更新……"))
            Directory.CreateDirectory(PathTemp & "Cache\")
            '旧格式清单只有单一地址，也走同一套超时与重试
            DownloadFromPool(New List(Of String) From {Url}, Target, ExpectedHash, Nothing, "主程序")
            If Not FileUtils.Exists(Target) Then Throw New Exception("下载的更新包不存在")
            Logger.Info($"更新包已下载：{Target}（{New FileInfo(Target).Length} 字节）")
            RunInUi(
            Sub()
                If ForceUpdating Then
                    Hint("更新已下载完成，正在重启……")
                    Process.Start(Target, $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{Target}"" True")
                    FormMain.EndProgramForce(ProcessReturnValues.TaskDone)
                    Return
                End If
                If MyMsgBox("更新已下载完成，启动器将重启并完成替换。", "准备更新", "立即重启", "取消") = 1 Then
                    ' 用新版本进程接管替换：--update <旧PID> <旧路径> <新路径> <是否重启>
                    Process.Start(Target, $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{Target}"" True")
                    FormMain.EndProgramForce(ProcessReturnValues.TaskDone)
                End If
            End Sub)
        Catch ex As Exception
            Logger.Error(ex, "下载更新失败")
            If ForceUpdating Then
                RunInUi(Sub() Hint("强制更新失败：" & ex.Message &
                    "。请检查网络后重启启动器重试，或到官网下载完整包覆盖", HintType.Red))
            Else
                RunInUi(Sub() Hint("下载更新失败：" & ex.Message))
            End If
        End Try
    End Sub

    ''' <summary>
    ''' 逐文件增量更新：只下载与本地不一致的文件，校验 SHA256 后统一下发替换。
    ''' </summary>
    Private Sub UpdateDownloadAndInstallFiles(Files As JArray)
        Dim TempFolder As String = PathTemp & "Cache\Update\"
        Dim BaseFolder As String = IO.Path.GetDirectoryName(PathExe)
        Dim SourceOrder As List(Of Integer) = Nothing
        Dim Probed As Boolean = False
        Try
            RunInUi(Sub() Hint("正在下载更新……"))
            If Directory.Exists(TempFolder) Then Directory.Delete(TempFolder, True)
            Directory.CreateDirectory(TempFolder)
            Dim Plan As New JArray()
            Dim UpdaterExe As String = ""
            '当前安装实际用的文件名：用户可以把启动器改名，替换时必须写回他自己的名字
            Dim ExeName As String = ProgramFileName
            Dim ExeBase As String = ProgramBaseName
            '先认主程序：不能只按文件名比，改名过的安装一个都对不上
            Dim MainEntry As String = ""
            Dim MainBase As String = ""
            For Each Item As JToken In Files
                Dim Candidate As String = Item("path")?.ToString()
                If String.IsNullOrWhiteSpace(Candidate) Then Continue For
                Dim CandidateName As String = IO.Path.GetFileName(Candidate)
                If CandidateName.Equals(ExeName, StringComparison.OrdinalIgnoreCase) OrElse
                   CandidateName.Equals("PCL-NJITMCL.exe", StringComparison.OrdinalIgnoreCase) OrElse
                   CandidateName.Equals("Plain Craft Launcher 2.exe", StringComparison.OrdinalIgnoreCase) Then
                    MainEntry = Candidate
                    MainBase = IO.Path.GetFileNameWithoutExtension(CandidateName)
                    Exit For
                End If
            Next
            If MainEntry = "" Then
                '兜底：清单里只有一个非组件的 exe，就是它
                Dim Candidates As New List(Of String)
                For Each Item As JToken In Files
                    Dim Candidate As String = Item("path")?.ToString()
                    If String.IsNullOrWhiteSpace(Candidate) Then Continue For
                    Dim CandidateName As String = IO.Path.GetFileName(Candidate)
                    If Not CandidateName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If CandidateName.Equals("wg.exe", StringComparison.OrdinalIgnoreCase) OrElse
                       CandidateName.Equals("wireguard.exe", StringComparison.OrdinalIgnoreCase) Then Continue For
                    Candidates.Add(Candidate)
                Next
                If Candidates.Count = 1 Then
                    MainEntry = Candidates(0)
                    MainBase = IO.Path.GetFileNameWithoutExtension(MainEntry)
                End If
            End If
            If MainEntry <> "" AndAlso
               Not IO.Path.GetFileName(MainEntry).Equals(ExeName, StringComparison.OrdinalIgnoreCase) Then
                Logger.Info($"启动器已改名：清单 {IO.Path.GetFileName(MainEntry)} → 安装 {ExeName}，按当前文件名落盘")
            End If
            For Each Item As JToken In Files
                Dim RelativePath As String = Item("path")?.ToString()
                Dim ExpectedHash As String = If(Item("sha256")?.ToString(), "").ToLowerInvariant()
                If String.IsNullOrWhiteSpace(RelativePath) Then Continue For
                '候选下载源：urls 数组（多个中转 + 直连 + 兜底），旧格式回退到单个 url
                Dim Urls As New List(Of String)
                Dim UrlsJson As JArray = TryCast(Item("urls"), JArray)
                If UrlsJson IsNot Nothing Then
                    For Each OneUrl As JToken In UrlsJson
                        If Not String.IsNullOrWhiteSpace(OneUrl?.ToString()) Then Urls.Add(OneUrl.ToString())
                    Next
                End If
                If Urls.Count = 0 Then
                    Dim SingleUrl As String = Item("url")?.ToString()
                    If String.IsNullOrWhiteSpace(SingleUrl) Then Throw New Exception($"更新清单缺少 {RelativePath} 的下载地址")
                    Urls.Add(SingleUrl)
                End If
                '首次下载前对全部源实测一次，之后所有文件沿用同一顺序
                If Not Probed AndAlso Urls.Count > 1 Then
                    Probed = True
                    RunInUi(Sub() Hint("正在测速选择更新源……"))
                    Dim Ranked As List(Of String) = RankPackSources(Urls)
                    SourceOrder = New List(Of Integer)
                    For Each OneUrl As String In Ranked
                        Dim RankIndex As Integer = Urls.IndexOf(OneUrl)
                        If RankIndex >= 0 Then SourceOrder.Add(RankIndex)
                    Next
                    For i = 0 To Urls.Count - 1
                        If Not SourceOrder.Contains(i) Then SourceOrder.Add(i)
                    Next
                End If
                Dim Attempts As New List(Of String)
                If SourceOrder IsNot Nothing Then
                    For Each RankIndex As Integer In SourceOrder
                        If RankIndex < Urls.Count Then Attempts.Add(Urls(RankIndex))
                    Next
                Else
                    Attempts.AddRange(Urls)
                End If
                Dim LocalPath As String = IO.Path.Combine(BaseFolder, RelativePath)
                Dim TempPath As String = IO.Path.Combine(TempFolder, RelativePath)
                If FileUtils.Exists(LocalPath) AndAlso ExpectedHash <> "" AndAlso GetFileSha256(LocalPath) = ExpectedHash Then
                    Logger.Info($"跳过未变动的文件：{RelativePath}")
                Else
                    Directory.CreateDirectory(IO.Path.GetDirectoryName(TempPath))
                    '多源下载：逐源重试，5xx / 超时都换下一个源，全部失败才抛错
                    DownloadFromPool(Attempts, TempPath, ExpectedHash, Nothing, RelativePath)
                    Dim Entry As New JObject()
                    '主程序与它的伴生文件按当前安装的名字落盘，其余原样
                    Entry.Add("path", MapProgramFileName(RelativePath, MainBase, ExeName, ExeBase))
                    Entry.Add("src", TempPath)
                    Plan.Add(Entry)
                End If
                '更新进程必须从临时目录启动，否则会占用安装目录里的 dll 导致替换失败
                If RelativePath.Equals(MainEntry, StringComparison.OrdinalIgnoreCase) Then
                    If Not FileUtils.Exists(TempPath) AndAlso FileUtils.Exists(LocalPath) Then
                        IO.File.Copy(LocalPath, TempPath, True)
                    End If
                    If FileUtils.Exists(TempPath) Then UpdaterExe = TempPath
                End If
            Next
            If Plan.Count = 0 Then
                RunInUi(Sub() Hint("更新文件已是最新，无需下载"))
                Return
            End If
            If UpdaterExe = "" Then
                Throw New Exception($"更新清单中没有可用的启动器主程序（未找到 .exe 条目，或本地缺少 {ExeName}）")
            End If
            Dim PlanFile As String = IO.Path.Combine(TempFolder, "plan.json")
            Dim PlanJson As New JObject()
            PlanJson.Add("files", Plan)
            IO.File.WriteAllText(PlanFile, PlanJson.ToString(), Text.Encoding.UTF8)
            Logger.Info($"更新文件已准备：{Plan.Count} 个，更新进程 {UpdaterExe}")
            RunInUi(
            Sub()
                If ForceUpdating Then
                    Hint("更新已下载完成，正在重启……")
                    Process.Start(UpdaterExe, $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{PlanFile}"" True")
                    FormMain.EndProgramForce(ProcessReturnValues.TaskDone)
                    Return
                End If
                If MyMsgBox("更新已下载完成，启动器将重启并完成替换。", "准备更新", "立即重启", "取消") = 1 Then
                    ' 用新版本进程接管替换：--update <旧PID> <旧路径> <替换清单> <是否重启>
                    Process.Start(UpdaterExe, $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{PlanFile}"" True")
                    FormMain.EndProgramForce(ProcessReturnValues.TaskDone)
                End If
            End Sub)
        Catch ex As Exception
            Logger.Error(ex, "下载更新失败")
            If ForceUpdating Then
                RunInUi(Sub() Hint("强制更新失败：" & ex.Message &
                    "。请检查网络后重启启动器重试，或到官网下载完整包覆盖", HintType.Red))
            Else
                RunInUi(Sub() Hint("下载更新失败：" & ex.Message))
            End If
        End Try
    End Sub

    ''' <summary>
    ''' 整包更新：下载完整包，只覆盖启动器程序文件，不触碰游戏版本与存档目录。
    ''' </summary>
    Private Sub UpdateDownloadAndInstallPackage(Package As JObject)
        Dim TempFolder As String = PathTemp & "Cache\Update\"
        Dim ZipPath As String = IO.Path.Combine(TempFolder, "package.zip")
        Dim ExtractFolder As String = IO.Path.Combine(TempFolder, "package")
        Dim UpdaterExe As String = ""
        Try
            RunInUi(Sub() Hint("正在下载更新……"))
            If Directory.Exists(TempFolder) Then Directory.Delete(TempFolder, True)
            Directory.CreateDirectory(ExtractFolder)
            Dim ExpectedHash As String = If(Package("sha256")?.ToString(), "").ToLowerInvariant()
            Dim Urls As New List(Of String)
            Dim UrlsJson As JArray = TryCast(Package("urls"), JArray)
            If UrlsJson IsNot Nothing Then
                For Each OneUrl As JToken In UrlsJson
                    If Not String.IsNullOrWhiteSpace(OneUrl?.ToString()) Then Urls.Add(OneUrl.ToString())
                Next
            End If
            If Urls.Count = 0 Then
                Dim SingleUrl As String = Package("url")?.ToString()
                If String.IsNullOrWhiteSpace(SingleUrl) Then Throw New Exception("更新清单缺少完整包下载地址")
                Urls.Add(SingleUrl)
            End If
            '多源测速，按可用顺序尝试
            If Urls.Count > 1 Then
                RunInUi(Sub() Hint("正在测速选择更新源……"))
                Dim Ranked As List(Of String) = RankPackSources(Urls)
                Dim Ordered As New List(Of String)
                For Each OneUrl As String In Ranked
                    If Urls.Contains(OneUrl) Then Ordered.Add(OneUrl)
                Next
                For Each OneUrl As String In Urls
                    If Not Ordered.Contains(OneUrl) Then Ordered.Add(OneUrl)
                Next
                Urls = Ordered
            End If
            '多源下载：逐源重试，5xx / 超时都换下一个源，全部失败才抛错
            DownloadFromPool(Urls, ZipPath, ExpectedHash, Nothing, "完整包")
            RunInUi(Sub() Hint("正在解压更新包……"))
            IO.Compression.ZipFile.ExtractToDirectory(ZipPath, ExtractFolder)
            UpdaterExe = IO.Path.Combine(ExtractFolder, "PCL-NJITMCL.exe")
            If Not FileUtils.Exists(UpdaterExe) Then Throw New Exception("完整包中缺少启动器主程序")
            '只替换程序文件；目标名跟随当前安装的文件名，兼容重命名后的安装目录
            Dim ExeName As String = ProgramFileName
            Dim ExeBase As String = ProgramBaseName
            Dim ProgramFiles As New List(Of KeyValuePair(Of String, String)) From {
                New KeyValuePair(Of String, String)("PCL-NJITMCL.exe", ExeName),
                New KeyValuePair(Of String, String)("PCL-NJITMCL.exe.config", ExeName & ".config"),
                New KeyValuePair(Of String, String)("PCL-NJITMCL.xml", ExeBase & ".xml"),
                New KeyValuePair(Of String, String)("PCLCS.dll", "PCLCS.dll"),
                New KeyValuePair(Of String, String)("MeloongCore.dll", "MeloongCore.dll"),
                New KeyValuePair(Of String, String)("MeloongCore.Wpf.dll", "MeloongCore.Wpf.dll"),
                New KeyValuePair(Of String, String)("Microsoft.Win32.Registry.dll", "Microsoft.Win32.Registry.dll")}
            Dim Plan As New JArray()
            For Each Pair As KeyValuePair(Of String, String) In ProgramFiles
                Dim SourcePath As String = IO.Path.Combine(ExtractFolder, Pair.Key)
                If Not FileUtils.Exists(SourcePath) Then
                    '兼容旧包：wg 组件可能平铺在包根目录，回退取一次
                    Dim FallbackPath As String = IO.Path.Combine(ExtractFolder, IO.Path.GetFileName(Pair.Key))
                    If FileUtils.Exists(FallbackPath) Then
                        SourcePath = FallbackPath
                    Else
                        Logger.Warn($"完整包中缺少 {Pair.Key}，跳过")
                        Continue For
                    End If
                End If
                Dim Entry As New JObject()
                Entry.Add("path", Pair.Value)
                Entry.Add("src", SourcePath)
                Plan.Add(Entry)
            Next
            If Plan.Count = 0 Then Throw New Exception("完整包中没有可用的启动器文件")
            Dim PlanFile As String = IO.Path.Combine(TempFolder, "plan.json")
            Dim PlanJson As New JObject()
            PlanJson.Add("files", Plan)
            IO.File.WriteAllText(PlanFile, PlanJson.ToString(), Text.Encoding.UTF8)
            Logger.Info($"整包更新已准备：{Plan.Count} 个文件")
            RunInUi(
            Sub()
                If ForceUpdating Then
                    Hint("更新已下载完成，正在重启……")
                    Process.Start(UpdaterExe, $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{PlanFile}"" True")
                    FormMain.EndProgramForce(ProcessReturnValues.TaskDone)
                    Return
                End If
                If MyMsgBox("更新已下载完成，启动器将重启并完成替换。", "准备更新", "立即重启", "取消") = 1 Then
                    Process.Start(UpdaterExe, $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{PlanFile}"" True")
                    FormMain.EndProgramForce(ProcessReturnValues.TaskDone)
                End If
            End Sub)
        Catch ex As Exception
            Logger.Error(ex, "下载更新失败")
            If ForceUpdating Then
                RunInUi(Sub() Hint("强制更新失败：" & ex.Message &
                    "。请检查网络后重启启动器重试，或到官网下载完整包覆盖", HintType.Red))
            Else
                RunInUi(Sub() Hint("下载更新失败：" & ex.Message))
            End If
        End Try
    End Sub

    ''' <summary>
    ''' 计算文件的 SHA256（小写十六进制）。
    ''' </summary>
    Private Function GetFileSha256(FilePath As String) As String
        Using Stream As New IO.FileStream(FilePath, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.Read)
            Using Hasher As System.Security.Cryptography.SHA256 = System.Security.Cryptography.SHA256.Create()
                Return BitConverter.ToString(Hasher.ComputeHash(Stream)).Replace("-", "").ToLowerInvariant()
            End Using
        End Using
    End Function

    ''' <summary>
    ''' 由新版本进程执行：等待旧进程退出后替换文件，并按需重启。
    ''' </summary>
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        If NewFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) Then
            UpdateReplaceByPlan(ProcessId, OldFileName, NewFileName, TriggerRestart)
            Return
        End If
        Dim Backup As String = OldFileName & ".old"
        Try
            Logger.Info($"开始替换更新文件：旧 {OldFileName}，新 {NewFileName}")
            Try
                Dim OldProcess As Process = Process.GetProcessById(ProcessId)
                If Not OldProcess.WaitForExit(20000) Then
                    Logger.Warn("等待旧进程退出超时（20 秒），继续尝试替换")
                End If
            Catch
                ' 旧进程已退出
            End Try
            Thread.Sleep(300)
            Try
                If FileUtils.Exists(Backup) Then FileUtils.Delete(Backup)
                If FileUtils.Exists(OldFileName) Then File.Move(OldFileName, Backup)
            Catch ex As Exception
                Logger.Warn(ex, "备份旧版本失败，将直接覆盖")
            End Try
            File.Copy(NewFileName, OldFileName, True)
            Logger.Info("更新文件替换完成")
            If TriggerRestart Then StartProcess(OldFileName, "--wait")
        Catch ex As Exception
            Logger.Error(ex, "更新替换失败", LogBehavior.Alert)
            '覆盖失败时把备份还原，避免 exe 缺位导致启动器损坏
            Try
                If FileUtils.Exists(Backup) AndAlso Not FileUtils.Exists(OldFileName) Then
                    File.Move(Backup, OldFileName)
                    Logger.Info("已从备份还原旧版本文件")
                End If
            Catch restoreEx As Exception
                Logger.Warn(restoreEx, "还原旧版本文件失败")
            End Try
        End Try
    End Sub

    ''' <summary>
    ''' 按替换清单逐个覆盖文件（跨多版本增量更新）。
    ''' 失败时逆序还原已替换的文件，避免半更新状态。
    ''' </summary>
    Private Sub UpdateReplaceByPlan(ProcessId As Integer, OldFileName As String, PlanFile As String, TriggerRestart As Boolean)
        Dim Replaced As New List(Of Tuple(Of String, String)) '（目标路径，备份路径）
        Try
            Logger.Info($"开始按清单替换更新文件：{PlanFile}")
            Try
                Dim OldProcess As Process = Process.GetProcessById(ProcessId)
                If Not OldProcess.WaitForExit(20000) Then
                    Logger.Warn("等待旧进程退出超时（20 秒），继续尝试替换")
                End If
            Catch
                ' 旧进程已退出
            End Try
            Thread.Sleep(300)
            Dim BaseFolder As String = IO.Path.GetDirectoryName(OldFileName)
            Dim Plan As JObject = JObject.Parse(IO.File.ReadAllText(PlanFile))
            '整包更新时先清理旧布局残留（例如根目录平铺的 wg 组件）
            Dim DeleteList As JArray = TryCast(Plan("delete"), JArray)
            If DeleteList IsNot Nothing Then
                For Each Item As JToken In DeleteList
                    Dim RelativePath As String = Item?.ToString()
                    If String.IsNullOrWhiteSpace(RelativePath) Then Continue For
                    Dim FullPath As String = IO.Path.Combine(BaseFolder, RelativePath)
                    Try
                        If FileUtils.Exists(FullPath) Then FileUtils.Delete(FullPath)
                        Logger.Info($"已清理旧布局文件：{RelativePath}")
                    Catch ex As Exception
                        Logger.Warn(ex, $"清理旧布局文件失败：{RelativePath}")
                    End Try
                Next
            End If
            Dim Count As Integer = 0
            For Each Item As JToken In CType(Plan("files"), JArray)
                Dim RelativePath As String = Item("path")?.ToString()
                Dim Source As String = Item("src")?.ToString()
                If String.IsNullOrWhiteSpace(RelativePath) OrElse String.IsNullOrWhiteSpace(Source) Then Continue For
                Dim Dest As String = IO.Path.Combine(BaseFolder, RelativePath)
                If Not FileUtils.Exists(Source) Then
                    Logger.Warn($"更新源文件不存在，跳过：{RelativePath}")
                    Continue For
                End If
                Directory.CreateDirectory(IO.Path.GetDirectoryName(Dest))
                Dim Backup As String = Dest & ".old"
                Dim Moved As Boolean = False
                Try
                    If FileUtils.Exists(Backup) Then FileUtils.Delete(Backup)
                    If FileUtils.Exists(Dest) Then
                        File.Move(Dest, Backup)
                        Moved = True
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, $"备份 {RelativePath} 失败，将直接覆盖")
                End Try
                Try
                    For Retry As Integer = 1 To 3
                        Try
                            IO.File.Copy(Source, Dest, True)
                            Count += 1
                            Exit For
                        Catch ex As Exception
                            If Retry = 3 Then Throw
                            Thread.Sleep(400)
                        End Try
                    Next
                    If Moved Then Replaced.Add(Tuple.Create(Dest, Backup))
                    Logger.Info($"已替换：{RelativePath}")
                Catch ex As Exception
                    '当前文件替换失败：先把备份还原，避免该文件缺位
                    Try
                        If Moved AndAlso FileUtils.Exists(Backup) Then
                            If FileUtils.Exists(Dest) Then FileUtils.Delete(Dest)
                            File.Move(Backup, Dest)
                        End If
                    Catch restoreEx As Exception
                        Logger.Warn(restoreEx, $"还原 {RelativePath} 失败")
                    End Try
                    Throw
                End Try
            Next
            Logger.Info($"更新文件替换完成，共 {Count} 个")
            If TriggerRestart Then StartProcess(OldFileName, "--wait")
        Catch ex As Exception
            Logger.Error(ex, "更新替换失败", LogBehavior.Alert)
            '失败时逆序还原已替换的文件，避免半更新状态
            For Index As Integer = Replaced.Count - 1 To 0 Step -1
                Dim Dest As String = Replaced(Index).Item1
                Dim Backup As String = Replaced(Index).Item2
                Try
                    If FileUtils.Exists(Backup) Then
                        If FileUtils.Exists(Dest) Then FileUtils.Delete(Dest)
                        File.Move(Backup, Dest)
                        Logger.Info($"已还原：{Dest}")
                    End If
                Catch restoreEx As Exception
                    Logger.Warn(restoreEx, $"还原 {Dest} 失败")
                End Try
            Next
        End Try
    End Sub

#Region "多源下载"

    ''' <summary>
    ''' 把清单里的规范文件名映射到当前安装实际使用的文件名。
    ''' 只改主程序及其伴生文件（&lt;MainBase&gt;.exe / .exe.config / .xml），
    ''' 其余（dll、wg.exe、wireguard.exe）原样返回——组件的名字不能跟着启动器一起改。
    ''' </summary>
    Private Function MapProgramFileName(RelativePath As String, MainBase As String,
                                        ExeName As String, ExeBase As String) As String
        If String.IsNullOrWhiteSpace(MainBase) Then Return RelativePath
        Dim FileName As String = IO.Path.GetFileName(RelativePath)
        Dim DirPart As String = RelativePath.Substring(0, RelativePath.Length - FileName.Length)
        If FileName.Equals(MainBase & ".exe", StringComparison.OrdinalIgnoreCase) Then Return DirPart & ExeName
        If FileName.Equals(MainBase & ".exe.config", StringComparison.OrdinalIgnoreCase) Then Return DirPart & ExeName & ".config"
        If FileName.Equals(MainBase & ".xml", StringComparison.OrdinalIgnoreCase) Then Return DirPart & ExeBase & ".xml"
        Return RelativePath
    End Function

    ''' <summary>
    ''' 从多个源依次下载，返回成功的源地址。
    ''' 单源失败后立刻换下一个源；5xx（含中转站的 520/522/504）与超时按可重试处理，
    ''' 同一源最多试 RetryPerSource 次，全部失败才抛错。
    ''' </summary>
    Private Function DownloadFromPool(Urls As List(Of String), Target As String, ExpectedHash As String,
                                     Optional Progress As Action(Of Long, Long) = Nothing,
                                     Optional DisplayName As String = "",
                                     Optional RetryPerSource As Integer = 2) As String
        Dim Label As String = If(String.IsNullOrWhiteSpace(DisplayName), IO.Path.GetFileName(Target), DisplayName)
        Dim LastError As String = ""
        For Each OneUrl As String In Urls
            For Attempt As Integer = 1 To RetryPerSource
                Try
                    If FileUtils.Exists(Target) Then FileUtils.Delete(Target)
                    DownloadSingle(OneUrl, Target, Progress)
                    If Not FileUtils.Exists(Target) Then Throw New Exception("下载后文件不存在")
                    If ExpectedHash <> "" AndAlso GetFileSha256(Target) <> ExpectedHash.ToLowerInvariant() Then
                        Throw New Exception("SHA256 校验不一致")
                    End If
                    Logger.Info($"已下载 {Label}（来源 {OneUrl}）")
                    Return OneUrl
                Catch ex As Exception
                    LastError = $"{OneUrl} → {ex.Message}"
                    Logger.Warn($"下载源失败（第 {Attempt}/{RetryPerSource} 次）：{LastError}")
                    If Not IsRetryableDownloadError(ex) Then Exit For
                    Thread.Sleep(300 * Attempt)
                End Try
            Next
        Next
        Throw New Exception($"下载失败：{Label}（全部 {Urls.Count} 个源均不可用，最后一次：{LastError}）")
    End Function

    ''' <summary>
    ''' 超时、连接中断、以及任何 5xx 都视为可重试；4xx（404 之类）说明该源本身没有这个文件，不再重试。
    ''' </summary>
    Private Function IsRetryableDownloadError(ex As Exception) As Boolean
        Dim WebEx As Net.WebException = TryCast(ex, Net.WebException)
        If WebEx Is Nothing Then Return False
        Select Case WebEx.Status
            Case Net.WebExceptionStatus.Timeout, Net.WebExceptionStatus.ConnectFailure,
                 Net.WebExceptionStatus.ConnectionClosed, Net.WebExceptionStatus.ReceiveFailure,
                 Net.WebExceptionStatus.SendFailure, Net.WebExceptionStatus.KeepAliveFailure
                Return True
        End Select
        If WebEx.Response IsNot Nothing Then
            Try
                Return CInt(DirectCast(WebEx.Response, Net.HttpWebResponse).StatusCode) >= 500
            Catch
                Return False
            End Try
        End If
        Return False
    End Function

    ''' <summary>
    ''' 单源下载：连接 8 秒、读写 20 秒无响应即失败。
    ''' 内网镜像与各中转站强制不走系统代理，避免代理二次转发把 5xx 搅得更难定位。
    ''' </summary>
    Private Sub DownloadSingle(Url As String, Target As String, Progress As Action(Of Long, Long))
        Dim Req As Net.HttpWebRequest = DirectCast(Net.WebRequest.Create(Url), Net.HttpWebRequest)
        Req.Method = "GET"
        Req.Timeout = 8000
        Req.ReadWriteTimeout = 20000
        Req.AllowAutoRedirect = True
        Req.UserAgent = $"PCL2/{VersionBaseName}.{CInt(BuildType)}"
        If Url.Contains("njit-fkcy.top") OrElse Url.Contains("gh-proxy") OrElse
           Url.Contains("ghproxy") OrElse Url.Contains("ghfast") OrElse Url.Contains("llkk") Then
            Req.Proxy = Nothing
        End If
        Using Resp As Net.HttpWebResponse = DirectCast(Req.GetResponse(), Net.HttpWebResponse)
            Dim Total As Long = Resp.ContentLength
            Using Source As IO.Stream = Resp.GetResponseStream()
                Using Sink As New IO.FileStream(Target, IO.FileMode.Create, IO.FileAccess.Write, IO.FileShare.None)
                    Dim Buffer(65535) As Byte
                    Dim Done As Long = 0
                    Do
                        Dim Count As Integer = Source.Read(Buffer, 0, Buffer.Length)
                        If Count <= 0 Then Exit Do
                        Sink.Write(Buffer, 0, Count)
                        Done += Count
                        If Progress IsNot Nothing Then Progress(Done, Total)
                    Loop
                End Using
            End Using
        End Using
    End Sub

#End Region

End Module
