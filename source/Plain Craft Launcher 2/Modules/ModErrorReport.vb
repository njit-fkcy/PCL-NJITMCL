Imports System.IO
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>
''' 把启动器与游戏错误上报到社团官网（njitmc.club），由官网通过 SMTP 发邮件提醒管理员。
''' 服务端按指纹去重，同一个错误只会发一次邮件。
''' </summary>
Public Module ModErrorReport

    ''' <summary>官网错误报告接口。</summary>
    Public ReadOnly ReportUrl As String = ModNJIT.ClubHomepage & "/api/launcher/error-report"

    ''' <summary>以共享方式读取文件末尾若干字节（避免与写入方冲突），超过时丢弃第一个不完整行。</summary>
    Private Function ReadTextTailShared(FilePath As String, MaxBytes As Long) As String
        Try
            If String.IsNullOrWhiteSpace(FilePath) OrElse Not File.Exists(FilePath) Then Return ""
            Using Stream As New FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Dim Length As Long = Stream.Length
                If Length <= 0 Then Return ""
                Dim Start As Long = Math.Max(0, Length - MaxBytes)
                Stream.Seek(Start, SeekOrigin.Begin)
                Using Reader As New StreamReader(Stream, Encoding.UTF8, True)
                    Dim Text As String = Reader.ReadToEnd()
                    If Start > 0 Then
                        Dim FirstBreak As Integer = Text.IndexOf(vbLf)
                        If FirstBreak >= 0 Then Text = Text.Substring(FirstBreak + 1)
                    End If
                    Return Text
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "读取文件失败：" & FilePath)
            Return ""
        End Try
    End Function

    ''' <summary>实例 crash-reports 目录下最新一份崩溃报告（全文，最多 1 MB）。</summary>
    Private Function FindLatestCrashReport(InstancePath As String) As String
        Try
            If String.IsNullOrWhiteSpace(InstancePath) Then Return ""
            Dim Folder As String = IO.Path.Combine(InstancePath, "crash-reports")
            If Not Directory.Exists(Folder) Then Return ""
            Dim Latest As FileInfo = Nothing
            For Each Item As FileInfo In New DirectoryInfo(Folder).GetFiles("crash-*.txt")
                If Latest Is Nothing OrElse Item.LastWriteTimeUtc > Latest.LastWriteTimeUtc Then Latest = Item
            Next
            If Latest Is Nothing Then Return ""
            Return ReadTextTailShared(Latest.FullName, 1048576)
        Catch ex As Exception
            Logger.Warn(ex, "读取崩溃报告失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 从崩溃报告中提取稳定特征（描述、Caused by、异常行），用于指纹去重。
    ''' 不包含 Time 等每次都变的行，避免同一错误被当成新错误反复发信。
    ''' </summary>
    Private Function CrashSignature(CrashReport As String) As String
        If String.IsNullOrWhiteSpace(CrashReport) Then Return ""
        Dim Picked As New List(Of String)
        For Each Line As String In CrashReport.Split({vbCrLf, vbLf}, StringSplitOptions.None)
            Dim Text As String = Line.Trim()
            If Text.Length = 0 Then Continue For
            If Text.StartsWith("Time:") OrElse Text.StartsWith("-- System Details --") Then Continue For
            If Text.StartsWith("Description:") OrElse Text.StartsWith("Caused by:") OrElse
               Text.Contains("Exception") OrElse Text.Contains("Error:") Then
                Picked.Add(Text)
                If Picked.Count >= 25 Then Exit For
            End If
        Next
        Return String.Join(vbLf, Picked)
    End Function

    ''' <summary>读取实例的社团整合包标记（安装社团整合包时写入）。</summary>
    Private Function PackMark(InstancePath As String) As JObject
        Try
            If String.IsNullOrWhiteSpace(InstancePath) Then Return Nothing
            Dim MarkPath As String = IO.Path.Combine(InstancePath, "PCL", "njitmcl-pack.json")
            If Not File.Exists(MarkPath) Then Return Nothing
            Return JObject.Parse(File.ReadAllText(MarkPath))
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>该实例是否来自社团整合包（只有这类实例才上报游戏崩溃）。</summary>
    Public Function IsClubInstance(InstancePath As String) As Boolean
        Dim Mark As JObject = PackMark(InstancePath)
        Return Mark IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(Mark("packId")?.ToString())
    End Function

    ''' <summary>
    ''' 游戏崩溃是否应当上报：实例必须来自社团整合包，且所属服务器在网站接口中标记为在营
    ''' （archived = False）。接口不可用时返回 False，避免把结档服或未知来源误报上来。
    ''' </summary>
    Public Function ShouldReportGameCrash(InstancePath As String) As Boolean
        Dim Mark As JObject = PackMark(InstancePath)
        If Mark Is Nothing Then Return False
        Dim ServerId As String = If(Mark("serverId")?.ToString(), "").Trim()
        If ServerId.Length = 0 Then Return False
        Try
            Dim Servers As JArray = ModNJIT.FetchPackServers()
            For Each Item As JToken In Servers
                Dim Obj As JObject = TryCast(Item, JObject)
                If Obj Is Nothing Then Continue For
                If Not String.Equals(Obj("id")?.ToString(), ServerId, StringComparison.OrdinalIgnoreCase) Then Continue For
                '接口里有这台服务器：只有在营（未结档）才上报
                Return Not Equals(Obj("archived")?.ToObject(Of Boolean)(), True)
            Next
            Logger.Info($"服务器 {ServerId} 不在在档列表中，跳过游戏崩溃上报")
            Return False
        Catch ex As Exception
            Logger.Warn(ex, "判断服务器在营状态失败，跳过游戏崩溃上报")
            Return False
        End Try
    End Function

    ''' <summary>按类型 + 错误信息 + 堆栈生成指纹，服务端据此去重。</summary>
    Private Function Fingerprint(Kind As String, Message As String, Detail As String) As String
        Using Sha As SHA256 = SHA256.Create()
            Dim Bytes As Byte() = Encoding.UTF8.GetBytes(Kind & vbLf & Message & vbLf & Detail)
            Return BitConverter.ToString(Sha.ComputeHash(Bytes)).Replace("-", "").ToLowerInvariant()
        End Using
    End Function

    ''' <summary>
    ''' 上报一条错误报告。Ask=True 时先弹窗征求用户同意。
    ''' </summary>
    ''' <param name="Kind">launcher-crash / game-crash / pack-update / manual</param>
    ''' <param name="InstancePath">实例目录，用于带上整合包与服务器标识。</param>
    Public Sub Report(Kind As String, Message As String, Optional Detail As String = "",
                      Optional InstancePath As String = "", Optional Ask As Boolean = True)
        Try
            Message = If(Message, "").Trim()
            If Message.Length = 0 Then Message = "未知错误"
            Detail = If(Detail, "")
            If Message.Length > 4000 Then Message = Message.Substring(0, 4000)
            If Detail.Length > 200000 Then Detail = Detail.Substring(0, 200000)

            If Ask Then
                Dim Choice As Integer
                Try
                    Choice = RunInUiWait(Function() MyMsgBox(
                        "是否把这份错误报告上传到社团官网？" & vbCrLf &
                        "内容包括完整崩溃报告、最近 1.5 MB 游戏日志与 1 MB 启动器日志（最大约 3 MB），" &
                        "不会包含账号密码。" & vbCrLf &
                        "同一个错误只会给管理员发一次邮件提醒。",
                        "上传错误报告", "上传", "不上传", IsWarn:=True))
                Catch ex As Exception
                    Logger.Warn(ex, "错误报告确认弹窗失败")
                    Return
                End Try
                If Choice <> 1 Then Return
            End If

            Dim Mark As JObject = PackMark(InstancePath)
            '完整材料：优先实例自身的崩溃报告；游戏日志与启动器日志各取尾部，总量控制在 3 MB 以内
            Dim CrashReport As String = FindLatestCrashReport(InstancePath)
            Dim GameLog As String = ""
            If Not String.IsNullOrWhiteSpace(InstancePath) Then
                GameLog = ReadTextTailShared(IO.Path.Combine(InstancePath, "logs", "latest.log"), 1572864)
            End If
            Dim LauncherLog As String = ReadTextTailShared(Paths.Base & "PCL\Log1.txt", 1048576)
            Dim StackText As String = If(CrashReport <> "", CrashReport, Detail)
            Dim LogText As String = ""
            If GameLog <> "" Then
                LogText &= "===== 游戏日志 latest.log（末尾 1.5 MB） =====" & vbCrLf & GameLog & vbCrLf & vbCrLf
            End If
            If LauncherLog <> "" Then
                LogText &= "===== 启动器日志 Log1.txt（末尾 1 MB） =====" & vbCrLf & LauncherLog
            End If
            If LogText.Length > 3145728 Then LogText = LogText.Substring(0, 3145728)
            Dim Signature As String = CrashSignature(CrashReport)
            If Signature.Length = 0 Then Signature = Detail
            Dim Body As New JObject From {
                {"kind", Kind},
                {"launcherVersion", "PCL-NJITMCL " & VersionCode},
                {"mcVersion", ""},
                {"packId", If(Mark?("packId")?.ToString(), "")},
                {"serverId", If(Mark?("serverId")?.ToString(), "")},
                {"message", Message},
                {"stack", StackText},
                {"logTail", LogText},
                {"osInfo", Environment.OSVersion.VersionString & " / " &
                    If(Environment.Is64BitOperatingSystem, "x64", "x86")},
                {"clientTime", Date.Now.ToString("yyyy-MM-dd HH:mm:ss")},
                {"fingerprint", Fingerprint(Kind, Message, Signature)}
            }
            Dim Payload As String = Body.ToString(Newtonsoft.Json.Formatting.None)

            RunInNewThread(
                Sub()
                    Try
                        Dim Raw As String = NetRequestByClientRetry(ReportUrl, HttpMethod.Post,
                            Content:=Payload, ContentType:="application/json; charset=utf-8",
                            RequireJson:=True)
                        Dim Json As JObject = Raw.DeserializeJson()
                        Dim Notified As Boolean = Equals(Json("mailSent")?.ToObject(Of Boolean)(), True)
                        Dim Duplicated As Boolean = Equals(Json("duplicate")?.ToObject(Of Boolean)(), True)
                        Dim Text As String = If(Notified, "错误报告已上传，管理员会收到邮件提醒。",
                                                If(Duplicated, "错误报告已上传，该错误此前已提醒过管理员。",
                                                   "错误报告已上传。"))
                        RunInUi(Sub() Hint(Text, HintType.Green))
                    Catch ex As Exception
                        Logger.Warn(ex, "错误报告上传失败")
                        RunInUi(Sub() Hint("错误报告上传失败，请检查网络后再试。", HintType.Red))
                    End Try
                End Sub, "Error Report")
        Catch ex As Exception
            Logger.Warn(ex, "准备错误报告失败")
        End Try
    End Sub

End Module
