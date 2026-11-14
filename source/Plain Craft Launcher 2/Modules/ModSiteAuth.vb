Imports System.IO.Pipes
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Windows.Controls
Imports System.Windows.Media.Animation

''' <summary>
''' njitmcl:// 协议注册、单实例参数传递与网站唤起的正版登录。
''' </summary>
Public Module ModSiteAuth

    Private Const ProtocolName As String = "njitmcl"
    Private Const PipeName As String = "NJITMCL_Protocol"

    ''' <summary>
    ''' 启动器未运行时由浏览器直接唤起，参数先暂存，等日志与 UI 初始化后再处理。
    ''' </summary>
    Public PendingProtocol As String = ""

    Public Sub HandlePendingProtocol()
        If String.IsNullOrWhiteSpace(PendingProtocol) Then Return
        Dim Message As String = PendingProtocol
        PendingProtocol = ""
        HandleProtocol(Message)
    End Sub

    ''' <summary>
    ''' 注册 njitmcl:// 协议到当前用户，无需管理员权限。
    ''' </summary>
    Public Sub RegisterProtocol()
        Try
            Using Key As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\Classes\" & ProtocolName)
                Key.SetValue("", "URL:NJITMCL Protocol")
                Key.SetValue("URL Protocol", "")
                Using Command As Microsoft.Win32.RegistryKey = Key.CreateSubKey("shell\open\command")
                    Command.SetValue("", """" & PathExe & """ ""%1""")
                End Using
            End Using
            Logger.Info("已注册 njitmcl:// 协议")
        Catch ex As Exception
            Logger.Warn(ex, "注册 njitmcl:// 协议失败")
        End Try
    End Sub

    ''' <summary>
    ''' 主实例启动命名管道，接收第二个实例转来的协议参数。
    ''' </summary>
    Public Sub StartProtocolPipe()
        RunInNewThread(
        Sub()
            While True
                Try
                    Using Server As New NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None)
                        Server.WaitForConnection()
                        Using Reader As New StreamReader(Server, Encoding.UTF8)
                            Dim Message As String = Reader.ReadToEnd()
                            If Not String.IsNullOrWhiteSpace(Message) Then HandleProtocol(Message)
                        End Using
                    End Using
                Catch ex As Exception
                    Thread.Sleep(1000)
                End Try
            End While
        End Sub, "ProtocolPipe")
    End Sub

    ''' <summary>
    ''' 第二个实例把协议参数发给已运行的主实例。
    ''' </summary>
    Public Function SendProtocol(Message As String) As Boolean
        Try
            Using Client As New NamedPipeClientStream(".", PipeName, PipeDirection.Out)
                Client.Connect(2000)
                Using Writer As New StreamWriter(Client, Encoding.UTF8)
                    Writer.Write(Message)
                    Writer.Flush()
                End Using
            End Using
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "发送协议参数失败")
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 处理网站唤起的登录请求。
    ''' </summary>
    Public Sub HandleProtocol(Message As String)
        Logger.Info("收到 njitmcl 协议调用：" & Message)
        Dim Cleaned As String = Message.Trim().Trim(""""c)
        If Cleaned.StartsWith("njitmcl://download", StringComparison.OrdinalIgnoreCase) Then
            HandleDownloadProtocol(Cleaned)
            Return
        End If
        If Not Cleaned.StartsWith("njitmcl://login", StringComparison.OrdinalIgnoreCase) Then Return
        RunInNewThread(
        Sub()
            Try
                Do While FrmMain Is Nothing
                    Thread.Sleep(200)
                Loop
                ShowSiteLoginBar()
                '正版：有刷新令牌时重新拉取最新凭证，失败则退回本地缓存
                Dim MojangAccess As String = Settings.Get(Of String)("CacheMsV2Access")
                Dim MojangUuid As String = Settings.Get(Of String)("CacheMsV2Uuid")
                Dim MojangName As String = Settings.Get(Of String)("CacheMsV2Name")
                If Not String.IsNullOrWhiteSpace(Settings.Get(Of String)("CacheMsV2OAuthRefresh")) Then
                    Dim LoginData As New McLoginMs With {
                        .OAuthRefreshToken = Settings.Get(Of String)("CacheMsV2OAuthRefresh"),
                        .UserName = Settings.Get(Of String)("CacheMsV2Name")}
                    McLoginMsLoader.Start(LoginData, IsForceRestart:=True)
                    Do While McLoginMsLoader.State = LoadState.Loading
                        Thread.Sleep(50)
                    Loop
                    If McLoginMsLoader.State = LoadState.Finished AndAlso McLoginMsLoader.Error Is Nothing Then
                        Dim Output As McLoginResult = McLoginMsLoader.Output
                        If Not String.IsNullOrWhiteSpace(Output.AccessToken) Then
                            MojangAccess = Output.AccessToken
                            MojangUuid = Output.Uuid
                            MojangName = Output.Name
                        End If
                    Else
                        Logger.Warn("读取正版登录信息失败，改用本地缓存：" & If(McLoginMsLoader.Error?.Message, ""))
                    End If
                End If
                'MUA：登录过就带上皮肤站凭证，网站会调 MUA 复核后自动绑定
                Dim MuaAccess As String = ""
                Dim MuaUuid As String = ""
                Dim MuaName As String = ""
                Dim MuaUserName As String = ""
                If ModNJIT.IsMuaLoggedIn() Then
                    MuaAccess = Settings.Get(Of String)("CacheAuthAccess")
                    MuaUuid = Settings.Get(Of String)("CacheAuthUuid")
                    MuaName = Settings.Get(Of String)("CacheAuthName")
                    MuaUserName = Settings.Get(Of String)("CacheAuthUsername")
                End If
                If String.IsNullOrWhiteSpace(MojangAccess) AndAlso String.IsNullOrWhiteSpace(MuaAccess) Then
                    Throw New Exception("请先在启动器登录正版或 MUA 账号")
                End If
                Dim Code As String = ModNJIT.RequestSiteLogin(MojangAccess, MojangUuid, MojangName,
                                                              MuaAccess, MuaUuid, MuaName, MuaUserName)
                OpenWebsite(ModNJIT.ClubHomepage & "/#mc-login?code=" & Uri.EscapeDataString(Code))
                HideSiteLoginBar()
                RunInUi(Sub() Hint("账号验证成功，已打开浏览器完成登录", HintType.Green))
            Catch ex As Exception
                HideSiteLoginBar()
                Dim Reason As String = If(ex.Message, "")
                If Reason.Contains("MUA 登录已失效") Then
                    'MUA 服务端已作废缓存的凭证（refresh 会轮换 token），清掉标记让用户重新登录
                    Settings.Set("MuaLoggedIn", False)
                    Settings.Set("CacheAuthAccess", "")
                    RunInUi(Sub() Hint("MUA 登录已失效，请在启动器重新登录 MUA 账号后再试", HintType.Red))
                Else
                    RunInUi(Sub() Hint("登录社团网站失败：" & Reason, HintType.Red))
                End If
            End Try
        End Sub, "SiteLogin")
    End Sub

    Private SiteLoginBar As Canvas
    Private SiteLoginScale As ScaleTransform

    ''' <summary>
    ''' 窗口最底边的一条蓝色进度，用 Canvas 绝对定位，避开 PanBack 的 Margin 与负边距布局问题。
    ''' </summary>
    Private Sub ShowSiteLoginBar()
        RunInUi(
        Sub()
            If SiteLoginBar IsNot Nothing OrElse FrmMain Is Nothing Then Return
            Dim Host As New Canvas With {
                .IsHitTestVisible = False,
                .HorizontalAlignment = HorizontalAlignment.Stretch,
                .VerticalAlignment = VerticalAlignment.Stretch}
            Panel.SetZIndex(Host, 9999)
            Dim Bar As New Border With {
                .Height = 4,
                .IsHitTestVisible = False,
                .Background = New SolidColorBrush(Color.FromRgb(19, 119, 219)),
                .RenderTransformOrigin = New Point(0, 0.5)}
            SiteLoginScale = New ScaleTransform(0, 1)
            Bar.RenderTransform = SiteLoginScale
            Host.Children.Add(Bar)
            FrmMain.PanBack.Children.Add(Host)
            Dim Layout As Action =
                Sub()
                    ' 贴住白色内容框底边，与窗口内容区等宽，避开窗口底部 10px 的透明阴影
                    Bar.Width = Math.Max(0, Host.ActualWidth - 16)
                    Canvas.SetLeft(Bar, 8)
                    Canvas.SetTop(Bar, Host.ActualHeight - 12)
                End Sub
            AddHandler Host.SizeChanged, Sub(sender As Object, e As SizeChangedEventArgs) Layout()
            Layout()
            Dim Grow As New DoubleAnimation(0, 1, New Duration(TimeSpan.FromSeconds(4))) With {
                .EasingFunction = New CubicEase With {.EasingMode = EasingMode.EaseOut}}
            SiteLoginScale.BeginAnimation(ScaleTransform.ScaleXProperty, Grow)
            SiteLoginBar = Host
        End Sub)
    End Sub

    Private Sub HideSiteLoginBar()
        RunInUi(
        Sub()
            If SiteLoginBar Is Nothing Then Return
            FrmMain?.PanBack.Children.Remove(SiteLoginBar)
            SiteLoginBar = Nothing
            SiteLoginScale = Nothing
        End Sub)
    End Sub

    ' 处理网站发起的 GitHub 资源下载：njitmcl://download?url=...&name=...&sha256=...
    Private Sub HandleDownloadProtocol(Message As String)
        RunInNewThread(
        Sub()
            Try
                Dim target As Uri = ParseDownloadTarget(Message)
                Dim query As Dictionary(Of String, String) = ParseQuery(Message)
                Dim fileName As String = ""
                If query.ContainsKey("name") Then fileName = IO.Path.GetFileName(query("name"))
                If String.IsNullOrWhiteSpace(fileName) Then fileName = IO.Path.GetFileName(target.AbsolutePath)
                If String.IsNullOrWhiteSpace(fileName) Then fileName = "github-download"
                fileName = SanitizeFileName(fileName)

                Dim folder As String = GetDownloadFolder()
                IO.Directory.CreateDirectory(folder)
                Dim savePath As String = UniquePath(folder, fileName)

                RunInUi(Sub() Hint("开始下载：" & fileName, HintType.Blue))
                Logger.Info("协议下载开始：" & target.AbsoluteUri & " -> " & savePath)

                ModGithubProxy.EnsureStarted()
                '复用 PCL 原生下载引擎与全局进度（LoaderTaskbar -> BtnExtraDownload）
                Dim fileTasks As New List(Of NetFile) From {New NetFile({target.AbsoluteUri}, savePath)}
                Dim downloader As New LoaderDownload("下载 " & fileName, fileTasks)
                Dim combo As New LoaderCombo(Of String)("下载 " & fileName, New List(Of LoaderBase) From {downloader})
                combo.Start()
                LoaderTaskbarAdd(combo)
                RunInUi(Sub() FrmMain?.BtnExtraDownload.ShowRefresh())
                combo.WaitForExit()

                Dim sha As String = ""
                If query.ContainsKey("sha256") Then sha = query("sha256").Trim().ToLowerInvariant()
                If sha <> "" Then
                    Dim actual As String = GetSha256(savePath)
                    If actual <> sha Then
                        IO.File.Delete(savePath)
                        Throw New Exception("SHA256 校验失败，已删除下载文件")
                    End If
                End If

                Dim size As Long = New IO.FileInfo(savePath).Length
                Logger.Info("协议下载完成：" & savePath & "（" & size & " B）")
                RunInUi(Sub() Hint("下载完成：" & fileName & "（" & Math.Round(size / 1024 / 1024, 1) & " MB）", HintType.Green))
                Try
                    Diagnostics.Process.Start("explorer.exe", "/select,""" & savePath & """")
                Catch
                    '打不开资源管理器不影响下载结果
                End Try
            Catch ex As Exception
                Logger.Warn(ex, "协议下载失败")
                RunInUi(Sub() Hint("下载失败：" & ex.Message, HintType.Red))
            End Try
        End Sub, "ProtocolDownload")
    End Sub

    ''' <summary>解析并校验协议中的下载目标，不满足条件直接拒绝。</summary>
    Private Function ParseDownloadTarget(message As String) As Uri
        Dim parsed As Uri = Nothing
        If Not Uri.TryCreate(message, UriKind.Absolute, parsed) Then Throw New Exception("协议参数不是有效链接")
        If Not parsed.Scheme.Equals("njitmcl", StringComparison.OrdinalIgnoreCase) Then Throw New Exception("协议不支持")
        If Not parsed.Host.Equals("download", StringComparison.OrdinalIgnoreCase) Then Throw New Exception("未知的协议动作")

        Dim query As Dictionary(Of String, String) = ParseQuery(message)
        If Not query.ContainsKey("url") Then Throw New Exception("缺少下载地址")
        Dim raw As String = query("url").Trim()
        If raw.Length = 0 OrElse raw.Length > 2048 Then Throw New Exception("下载地址长度不合法")
        If raw.Contains("..") Then Throw New Exception("下载地址包含非法路径")

        Dim target As Uri = Nothing
        If Not Uri.TryCreate(raw, UriKind.Absolute, target) Then Throw New Exception("下载地址不是有效链接")
        If target.Scheme <> Uri.UriSchemeHttps Then Throw New Exception("只允许 https 下载地址")
        If target.Port <> 443 Then Throw New Exception("只允许 443 端口")
        If Not String.IsNullOrEmpty(target.UserInfo) Then Throw New Exception("下载地址不允许包含账号信息")
        If Not ModGithubIp.IsGithubHost(target.Host) Then Throw New Exception("只允许 GitHub 域名：" & target.Host)
        If target.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) AndAlso
           Not Regex.IsMatch(target.AbsolutePath, "^/[^/]+/[^/]+/releases/download/") Then
            Throw New Exception("只允许 GitHub Release 下载地址")
        End If
        Return target
    End Function

    Private Function ParseQuery(message As String) As Dictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim index As Integer = message.IndexOf("?"c)
        If index < 0 Then Return result
        For Each pair As String In message.Substring(index + 1).Split("&"c)
            Dim eq As Integer = pair.IndexOf("="c)
            If eq <= 0 Then Continue For
            Dim key As String = Uri.UnescapeDataString(pair.Substring(0, eq))
            Dim value As String = Uri.UnescapeDataString(pair.Substring(eq + 1))
            result(key) = value
        Next
        Return result
    End Function

    ''' <summary>默认保存到用户「下载」目录，可在设置里改。</summary>
    Private Function GetDownloadFolder() As String
        Dim folder As String = ""
        Try
            folder = Settings.Get(Of String)("NjitGithubDownloadFolder")
        Catch
            '设置项缺失时用默认目录
        End Try
        If String.IsNullOrWhiteSpace(folder) Then
            folder = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        End If
        Return folder
    End Function

    Private Function SanitizeFileName(name As String) As String
        For Each c As Char In IO.Path.GetInvalidFileNameChars()
            name = name.Replace(c, "_"c)
        Next
        If name.Length > 120 Then name = name.Substring(0, 120)
        Return name
    End Function

    Private Function UniquePath(folder As String, fileName As String) As String
        Dim path As String = IO.Path.Combine(folder, fileName)
        If Not IO.File.Exists(path) Then Return path
        Dim baseName As String = IO.Path.GetFileNameWithoutExtension(fileName)
        Dim ext As String = IO.Path.GetExtension(fileName)
        Dim i As Integer = 1
        Do
            path = IO.Path.Combine(folder, baseName & " (" & i & ")" & ext)
            i += 1
        Loop While IO.File.Exists(path)
        Return path
    End Function

    Private Function GetSha256(path As String) As String
        Using sha As System.Security.Cryptography.SHA256 = System.Security.Cryptography.SHA256.Create()
            Using fs As IO.FileStream = IO.File.OpenRead(path)
                Return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant()
            End Using
        End Using
    End Function

End Module
