Imports System.Net
Imports System.Security.Cryptography
Imports System.Threading
Imports Newtonsoft.Json.Linq

''' <summary>
''' njitmcl://publish/&lt;任务ID&gt;：把网站标记的待同步发布包从 CDN 下载后推送到校内网服务器。
''' 鉴权沿用联机模块的设备令牌；上传使用网站签发的短期上传票，启动器不保存任何长期密钥。
''' </summary>
Public Module ModPublishSync

    Private Const ProtocolPrefix As String = "njitmcl://publish/"
    Private Const UploadUserAgent As String = "NJITMCL-Publisher/2.3.1"

    ''' <summary>是否为发布同步协议。</summary>
    Public Function IsPublishProtocol(Message As String) As Boolean
        If String.IsNullOrWhiteSpace(Message) Then Return False
        Return Message.Trim().Trim(""""c).StartsWith(ProtocolPrefix, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>处理 njitmcl://publish/&lt;任务ID&gt;（任务 ID 为空时处理全部待同步任务）。</summary>
    Public Sub HandlePublishProtocol(Message As String)
        Dim Cleaned As String = Message.Trim().Trim(""""c)
        Dim Remain As String = Cleaned.Substring(ProtocolPrefix.Length)
        Dim TaskId As String = Remain.Split("?"c)(0).Trim("/"c).Trim()
        RunInNewThread(
        Sub()
            Try
                Do While FrmMain Is Nothing
                    Thread.Sleep(200)
                Loop
                SyncPending(TaskId)
            Catch ex As Exception
                Logger.Warn(ex, "发布同步失败")
                RunInUi(Sub() Hint("同步到校内服务器失败：" & ex.Message, HintType.Red))
            End Try
        End Sub, "PublishSync")
    End Sub

    ''' <summary>拉取待同步任务并逐个执行。</summary>
    Private Sub SyncPending(TaskId As String)
        If String.IsNullOrWhiteSpace(ModSocial.DeviceToken) Then
            Throw New Exception("请先在启动器完成社团账号登录")
        End If
        Dim Root As JObject = RequestJson("GET", "/api/launcher/sync/tasks", Nothing)
        Dim Items As JArray = TryCast(Root("tasks"), JArray)
        If Items Is Nothing OrElse Items.Count = 0 Then
            RunInUi(Sub() Hint("没有待同步的发布包", HintType.Green))
            Return
        End If
        Dim Selected As New List(Of JObject)
        For Each Item As JToken In Items
            Dim Obj As JObject = TryCast(Item, JObject)
            If Obj Is Nothing Then Continue For
            If TaskId = "" OrElse String.Equals(Obj("id")?.ToString(), TaskId, StringComparison.OrdinalIgnoreCase) Then
                Selected.Add(Obj)
            End If
        Next
        If Selected.Count = 0 Then
            RunInUi(Sub() Hint("没有找到该同步任务，可能已完成", HintType.Green))
            Return
        End If
        RunInUi(Sub() Hint($"开始同步 {Selected.Count} 个发布包到校内服务器", HintType.Blue))
        For Each Task In Selected
            SyncOne(Task)
        Next
        RunInUi(Sub() Hint("发布包已同步到校内服务器", HintType.Green))
    End Sub

    ''' <summary>执行单个任务：下载 → 校验 → 推送 → 回报。</summary>
    Private Sub SyncOne(Task As JObject)
        Dim TaskId As String = If(Task("id")?.ToString(), "")
        Dim FileName As String = IO.Path.GetFileName(If(Task("fileName")?.ToString(), "publish.zip"))
        Dim Url As String = If(Task("url")?.ToString(), "")
        Dim Digest As String = If(Task("sha256")?.ToString(), "").ToLowerInvariant()
        Dim UploadUrl As String = If(Task("upload")?.ToString(), "")
        Dim Ticket As String = If(Task("ticket")?.ToString(), "")
        Dim ExpectedSize As Long = If(Task("size")?.Type = JTokenType.Integer, Task("size").ToObject(Of Long)(), 0L)
        If Url = "" Then Throw New Exception("任务缺少 CDN 地址")
        If UploadUrl = "" OrElse Ticket = "" Then Throw New Exception("任务缺少内网上传凭据，请刷新后台任务")

        Dim Folder As String = IO.Path.Combine(IO.Path.GetTempPath(), "njitmcl-publish")
        IO.Directory.CreateDirectory(Folder)
        Dim LocalPath As String = IO.Path.Combine(Folder, FileName)

        Try
            ' 内网已有同哈希文件时直接跳过传输
            If IsAlreadySynced(UploadUrl, Ticket) Then
                Report(TaskId, "done", Digest, "")
                Logger.Info($"内网已有相同文件，跳过：{FileName}")
                Return
            End If

            RunInUi(Sub() Hint($"正在下载发布包：{FileName}", HintType.Blue))
            Logger.Info($"发布同步下载：{Url} -> {LocalPath}")
            Dim Actual As String = DownloadToFile(Url, LocalPath)
            If Digest <> "" AndAlso Actual <> Digest Then
                Throw New Exception("下载文件 SHA256 校验失败")
            End If
            If ExpectedSize > 0 AndAlso New IO.FileInfo(LocalPath).Length <> ExpectedSize Then
                Throw New Exception("下载文件大小与任务记录不一致")
            End If

            RunInUi(Sub() Hint($"正在推送到校内服务器：{FileName}", HintType.Blue))
            UploadToInner(UploadUrl, Ticket, LocalPath)
            Report(TaskId, "done", Actual, "")
            Logger.Info($"发布同步完成：{FileName}（{Actual}）")
        Catch ex As Exception
            Logger.Warn(ex, $"发布同步失败：{FileName}")
            Try
                Report(TaskId, "failed", "", ex.Message)
            Catch reportError As Exception
                Logger.Warn(reportError, "回报同步失败状态时出错")
            End Try
            Throw
        Finally
            Try
                If IO.File.Exists(LocalPath) Then IO.File.Delete(LocalPath)
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>查询内网是否已有相同 sha256 的目标文件。</summary>
    Private Function IsAlreadySynced(UploadUrl As String, Ticket As String) As Boolean
        Try
            Dim StatUrl As String = UploadUrl.Replace("/receive", "/stat") & "?ticket=" & Uri.EscapeDataString(Ticket)
            Dim Req As HttpWebRequest = DirectCast(WebRequest.Create(StatUrl), HttpWebRequest)
            Req.UserAgent = UploadUserAgent
            Req.Timeout = 15000
            Using Resp As HttpWebResponse = DirectCast(Req.GetResponse(), HttpWebResponse)
                Using Reader As New IO.StreamReader(Resp.GetResponseStream())
                    Dim Root As JObject = Reader.ReadToEnd().DeserializeJson()
                    Return Root IsNot Nothing AndAlso Root("same") IsNot Nothing AndAlso Root("same").ToObject(Of Boolean)()
                End Using
            End Using
        Catch ex As Exception
            Logger.Info("内网存在性检查失败，继续完整传输：" & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>流式下载并返回 sha256。</summary>
    Private Function DownloadToFile(Url As String, Target As String) As String
        Dim Req As HttpWebRequest = DirectCast(WebRequest.Create(Url), HttpWebRequest)
        Req.UserAgent = UploadUserAgent
        Req.Timeout = 30000
        Req.ReadWriteTimeout = 180000
        Req.AllowReadStreamBuffering = False
        Using Resp As HttpWebResponse = DirectCast(Req.GetResponse(), HttpWebResponse)
            Dim Total As Long = If(Resp.ContentLength > 0, Resp.ContentLength, 0)
            Using Input As IO.Stream = Resp.GetResponseStream(),
                  Output As New IO.FileStream(Target, IO.FileMode.Create, IO.FileAccess.Write, IO.FileShare.None)
                Dim Buffer(1024 * 1024 - 1) As Byte
                Dim Done As Long = 0
                Dim LastHint As Long = 0
                Do
                    Dim ReadCount As Integer = Input.Read(Buffer, 0, Buffer.Length)
                    If ReadCount <= 0 Then Exit Do
                    Output.Write(Buffer, 0, ReadCount)
                    Done += ReadCount
                    If Done - LastHint > 64L * 1024 * 1024 Then
                        LastHint = Done
                        Dim Text As String = $"正在下载发布包：{Done \ 1048576} MB" &
                            If(Total > 0, $" / {Total \ 1048576} MB", "")
                        RunInUi(Sub() Hint(Text, HintType.Blue))
                    End If
                Loop
            End Using
        End Using
        Return Sha256OfFile(Target)
    End Function

    ''' <summary>把文件流式 POST 到内网接收接口。</summary>
    Private Sub UploadToInner(Url As String, Ticket As String, LocalPath As String)
        Dim Size As Long = New IO.FileInfo(LocalPath).Length
        Dim Req As HttpWebRequest = DirectCast(WebRequest.Create(Url), HttpWebRequest)
        Req.Method = "POST"
        Req.UserAgent = UploadUserAgent
        Req.ContentType = "application/octet-stream"
        Req.ContentLength = Size
        Req.Timeout = 30000
        Req.ReadWriteTimeout = 300000
        Req.AllowWriteStreamBuffering = False
        Req.Headers.Add("X-Publish-Ticket", Ticket)
        Using Stream As IO.Stream = Req.GetRequestStream(),
              Input As New IO.FileStream(LocalPath, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.Read)
            Dim Buffer(1024 * 1024 - 1) As Byte
            Dim Done As Long = 0
            Dim LastHint As Long = 0
            Do
                Dim ReadCount As Integer = Input.Read(Buffer, 0, Buffer.Length)
                If ReadCount <= 0 Then Exit Do
                Stream.Write(Buffer, 0, ReadCount)
                Done += ReadCount
                If Done - LastHint > 64L * 1024 * 1024 Then
                    LastHint = Done
                    Dim Text As String = $"正在推送到校内服务器：{Done \ 1048576} / {Size \ 1048576} MB"
                    RunInUi(Sub() Hint(Text, HintType.Blue))
                End If
            Loop
        End Using
        Using Resp As HttpWebResponse = DirectCast(Req.GetResponse(), HttpWebResponse)
            Using Reader As New IO.StreamReader(Resp.GetResponseStream())
                Dim Body As String = Reader.ReadToEnd()
                Dim Root As JObject = Body.DeserializeJson()
                If Root Is Nothing OrElse Root("success") Is Nothing OrElse Not Root("success").ToObject(Of Boolean)() Then
                    Throw New Exception(If(Root?("message")?.ToString(), "校内服务器拒绝了上传"))
                End If
            End Using
        End Using
    End Sub

    ''' <summary>回报同步结果到网站。</summary>
    Private Sub Report(TaskId As String, Status As String, Digest As String, Message As String)
        Dim Body As New JObject From {
            {"id", TaskId},
            {"status", Status},
            {"sha256", Digest},
            {"error", Message}
        }
        RequestJson("POST", "/api/launcher/sync/report", Body)
    End Sub

    Private Function RequestJson(Method As String, Path As String, Body As JObject) As JObject
        Dim Headers(0, 1) As String
        Headers(0, 0) = "Authorization"
        Headers(0, 1) = "Bearer " & ModSocial.DeviceToken
        Dim Content As String = If(Body Is Nothing, Nothing, Body.ToString(Newtonsoft.Json.Formatting.None))
        Dim Raw As String = NetRequestByClient(ModNJIT.ClubHomepage & Path,
            If(Method = "POST", HttpMethod.Post, HttpMethod.Get), Content:=Content,
            ContentType:=If(Content Is Nothing, Nothing, "application/json; charset=utf-8"),
            Headers:=Headers, RequireJson:=True, Timeout:=30000)
        Dim Root As JObject = Raw.DeserializeJson()
        If Root Is Nothing Then Throw New Exception("网站返回内容为空")
        If Root("success") IsNot Nothing AndAlso Not Root("success").ToObject(Of Boolean)() Then
            Throw New Exception(If(Root("message")?.ToString(), "请求失败"))
        End If
        Return Root
    End Function

    Private Function Sha256OfFile(Path As String) As String
        Using Sha As SHA256 = SHA256.Create(),
              Stream As IO.FileStream = IO.File.OpenRead(Path)
            Return BitConverter.ToString(Sha.ComputeHash(Stream)).Replace("-", "").ToLowerInvariant()
        End Using
    End Function

End Module
