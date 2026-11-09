Imports System.IO.Pipes
Imports System.Text
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
        If Not Message.Trim().Trim(""""c).StartsWith("njitmcl://login", StringComparison.OrdinalIgnoreCase) Then Return
        RunInNewThread(
        Sub()
            Try
                Do While FrmMain Is Nothing
                    Thread.Sleep(200)
                Loop
                ShowSiteLoginBar()
                Dim LoginData As New McLoginMs With {
                    .OAuthRefreshToken = Settings.Get(Of String)("CacheMsV2OAuthRefresh"),
                    .UserName = Settings.Get(Of String)("CacheMsV2Name")}
                If String.IsNullOrWhiteSpace(LoginData.OAuthRefreshToken) Then Throw New Exception("请先在启动器登录微软账号")
                McLoginMsLoader.Start(LoginData, IsForceRestart:=True)
                Do While McLoginMsLoader.State = LoadState.Loading
                    Thread.Sleep(50)
                Loop
                If McLoginMsLoader.State <> LoadState.Finished OrElse McLoginMsLoader.Error IsNot Nothing Then
                    Throw New Exception(If(McLoginMsLoader.Error?.Message, "读取正版登录信息失败"))
                End If
                Dim Output As McLoginResult = McLoginMsLoader.Output
                If String.IsNullOrWhiteSpace(Output.AccessToken) Then Throw New Exception("没有取得正版登录凭证")
                Dim Code As String = ModNJIT.RequestSiteLogin(Output.AccessToken, Output.Uuid, Output.Name)
                OpenWebsite(ModNJIT.ClubHomepage & "/#mc-login?code=" & Uri.EscapeDataString(Code))
                HideSiteLoginBar()
                RunInUi(Sub() Hint("正版账号验证成功，已打开浏览器完成登录", HintType.Green))
            Catch ex As Exception
                HideSiteLoginBar()
                RunInUi(Sub() Hint("登录社团网站失败：" & ex.Message, HintType.Red))
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

End Module
