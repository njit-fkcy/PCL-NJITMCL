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

    ''' <summary>
    ''' 启动时静默检查更新（可在设置项 SystemUpdateCheck 中关闭）。
    ''' </summary>
    Public Sub UpdateCheckOnStartup()
        If Not Settings.Get(Of Boolean)("SystemUpdateCheck") Then Return
        RunInThread(
        Sub()
            Thread.Sleep(4000)
            UpdateCheck(False)
        End Sub)
    End Sub

    Private Sub UpdateCheck(ShowResult As Boolean)
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
            Dim Files As JArray = TryCast(Json("files"), JArray)
            Dim UseFiles As Boolean = Files IsNot Nothing AndAlso Files.Count > 0
            Dim LegacyUrl As String = Json("url")?.ToString()
            If Not UseFiles AndAlso String.IsNullOrWhiteSpace(LegacyUrl) Then Throw New Exception("更新清单缺少下载地址")
            Logger.Info($"发现新版本：{VersionName}（{NewCode}），当前 {VersionBaseName}（{VersionCode}）")
            RunInUi(
            Sub()
                Dim Result As Integer = MyMsgBox(
                    $"发现新版本 {VersionName}（当前 {VersionBaseName}）{vbCrLf}{vbCrLf}{Changelog}{vbCrLf}{vbCrLf}是否立即更新？",
                    "发现更新", "立即更新", "稍后")
                If Result = 1 OrElse IsForce Then
                    If UseFiles Then
                        RunInThread(Sub() UpdateDownloadAndInstallFiles(Files))
                    Else
                        '旧格式清单：整包只有一个 exe
                        RunInThread(Sub() UpdateDownloadAndInstall(LegacyUrl, If(Json("sha256")?.ToString(), "")))
                    End If
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
            Using Client As New Net.WebClient()
                Client.DownloadFile(Url, Target)
            End Using
            If Not FileUtils.Exists(Target) Then Throw New Exception("下载的更新包不存在")
            If ExpectedHash <> "" AndAlso GetFileSha256(Target) <> ExpectedHash.ToLowerInvariant() Then
                FileUtils.Delete(Target)
                Throw New Exception("更新包校验失败，请重试")
            End If
            Logger.Info($"更新包已下载：{Target}（{New FileInfo(Target).Length} 字节）")
            RunInUi(
            Sub()
                If MyMsgBox("更新已下载完成，启动器将重启并完成替换。", "准备更新", "立即重启", "取消") = 1 Then
                    ' 用新版本进程接管替换：--update <旧PID> <旧路径> <新路径> <是否重启>
                    Process.Start(Target, $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{Target}"" True")
                    FormMain.EndProgramForce(ProcessReturnValues.TaskDone)
                End If
            End Sub)
        Catch ex As Exception
            Logger.Error(ex, "下载更新失败")
            RunInUi(Sub() Hint("下载更新失败：" & ex.Message))
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
                    Dim Downloaded As Boolean = False
                    Dim LastError As String = ""
                    For Each OneUrl As String In Attempts
                        Try
                            If FileUtils.Exists(TempPath) Then FileUtils.Delete(TempPath)
                            Using Client As New Net.WebClient()
                                Client.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
                                Client.DownloadFile(OneUrl, TempPath)
                            End Using
                            If Not FileUtils.Exists(TempPath) Then Throw New Exception("下载后文件不存在")
                            If ExpectedHash <> "" AndAlso GetFileSha256(TempPath) <> ExpectedHash Then Throw New Exception("SHA256 校验不一致")
                            Downloaded = True
                            Logger.Info($"已下载：{RelativePath}（来源 {OneUrl}）")
                            Exit For
                        Catch ex As Exception
                            LastError = ex.Message
                            Logger.Warn($"下载源失败（{OneUrl}）：{ex.Message}")
                        End Try
                    Next
                    If Not Downloaded Then Throw New Exception($"下载失败：{RelativePath}（{LastError}）")
                    Dim Entry As New JObject()
                    Entry.Add("path", RelativePath)
                    Entry.Add("src", TempPath)
                    Plan.Add(Entry)
                End If
                '更新进程必须从临时目录启动，否则会占用安装目录里的 dll 导致替换失败
                If RelativePath.Equals(IO.Path.GetFileName(PathExe), StringComparison.OrdinalIgnoreCase) Then
                    If Not FileUtils.Exists(TempPath) Then
                        IO.File.Copy(LocalPath, TempPath, True)
                    End If
                    UpdaterExe = TempPath
                End If
            Next
            If Plan.Count = 0 Then
                RunInUi(Sub() Hint("更新文件已是最新，无需下载"))
                Return
            End If
            If UpdaterExe = "" OrElse Not FileUtils.Exists(UpdaterExe) Then Throw New Exception("更新清单中没有启动器主程序")
            Dim PlanFile As String = IO.Path.Combine(TempFolder, "plan.json")
            Dim PlanJson As New JObject()
            PlanJson.Add("files", Plan)
            IO.File.WriteAllText(PlanFile, PlanJson.ToString(), Text.Encoding.UTF8)
            Logger.Info($"更新文件已准备：{Plan.Count} 个，更新进程 {UpdaterExe}")
            RunInUi(
            Sub()
                If MyMsgBox("更新已下载完成，启动器将重启并完成替换。", "准备更新", "立即重启", "取消") = 1 Then
                    ' 用新版本进程接管替换：--update <旧PID> <旧路径> <替换清单> <是否重启>
                    Process.Start(UpdaterExe, $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{PlanFile}"" True")
                    FormMain.EndProgramForce(ProcessReturnValues.TaskDone)
                End If
            End Sub)
        Catch ex As Exception
            Logger.Error(ex, "下载更新失败")
            RunInUi(Sub() Hint("下载更新失败：" & ex.Message))
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
        Try
            Logger.Info($"开始替换更新文件：旧 {OldFileName}，新 {NewFileName}")
            Try
                Dim OldProcess As Process = Process.GetProcessById(ProcessId)
                OldProcess.WaitForExit(20000)
            Catch
                ' 旧进程已退出
            End Try
            Thread.Sleep(300)
            Dim Backup As String = OldFileName & ".old"
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
        End Try
    End Sub

    ''' <summary>
    ''' 按替换清单逐个覆盖文件（跨多版本增量更新）。
    ''' </summary>
    Private Sub UpdateReplaceByPlan(ProcessId As Integer, OldFileName As String, PlanFile As String, TriggerRestart As Boolean)
        Try
            Logger.Info($"开始按清单替换更新文件：{PlanFile}")
            Try
                Process.GetProcessById(ProcessId).WaitForExit(20000)
            Catch
                ' 旧进程已退出
            End Try
            Thread.Sleep(300)
            Dim BaseFolder As String = IO.Path.GetDirectoryName(OldFileName)
            Dim Plan As JObject = JObject.Parse(IO.File.ReadAllText(PlanFile))
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
                Try
                    If FileUtils.Exists(Backup) Then FileUtils.Delete(Backup)
                    If FileUtils.Exists(Dest) Then File.Move(Dest, Backup)
                Catch ex As Exception
                    Logger.Warn(ex, $"备份 {RelativePath} 失败，将直接覆盖")
                End Try
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
                Logger.Info($"已替换：{RelativePath}")
            Next
            Logger.Info($"更新文件替换完成，共 {Count} 个")
            If TriggerRestart Then StartProcess(OldFileName, "--wait")
        Catch ex As Exception
            Logger.Error(ex, "更新替换失败", LogBehavior.Alert)
        End Try
    End Sub

End Module
