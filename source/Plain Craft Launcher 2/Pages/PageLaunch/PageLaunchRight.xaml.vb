Imports System.Windows.Threading

Public Class PageLaunchRight
    Implements IRefreshable, IDispatcherUnhandledException

    Private Sub Init() Handles Me.Loaded
        PanBack.ScrollToHome()
        PanLog.Visibility = ModeDebug.ToVisibility
    End Sub

#Region "主页"

    ''' <summary>
    ''' 刷新主页。
    ''' </summary>
    Private Sub Refresh() Handles Me.Loaded
        RunInThread(
        Sub()
            Try
                SyncLock RefreshLock
                    RefreshReal()
                End SyncLock
            Catch ex As Exception
                Logger.Error(ex, "加载 PCL 主页自定义信息失败", If(ModeDebug, LogBehavior.Alert, LogBehavior.Toast))
            End Try
        End Sub)
    End Sub
    Private Sub RefreshReal()
        Dim Content As String = Nothing, Url As String = Nothing
        Select Case Settings.Get(Of Integer)("UiCustomType")
            Case 1
                '加载本地文件
                Logger.Info("主页自定义数据来源：本地文件")
                Content = FileUtils.TryReadAsString(Paths.Base & "PCL\Custom.xaml")
            Case 2
                '联网下载
                Url = Settings.Get(Of String)("UiCustomNet")
            Case 3
                '预设
                Select Case Settings.Get(Of Integer)("UiCustomPreset")
                    Case 0
                        Logger.Info("主页预设：你知道吗")
                        Content = "
                            <local:MyCard Title=""你知道吗？"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{hint}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>"
                    Case 1
                        Logger.Info("主页预设：回声洞")
                        Content = "
                            <local:MyCard Title=""回声洞"" Margin=""0,0,0,15"">
                                <TextBlock Margin=""25,38,23,15"" FontSize=""13.5"" IsHitTestVisible=""False"" Text=""{cave}"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ColorBrush1}"" />
                                <local:MyIconButton Height=""22"" Width=""22"" Margin=""9"" VerticalAlignment=""Top"" HorizontalAlignment=""Right"" 
                                    EventType=""刷新主页"" EventData=""/""
                                    Logo=""M512.0 838.3c-80.2 0-153.4-29.3-210.2-77.4l75.5-75.5c11.5-11.5 25.8-22.0 25.8-37.0a27.2 27.2 0 0 0-27.1-27.1H104.0c-27.1 0-27.1 23.9-27.1 27.1v271.9a27.1 27.1 0 0 0 27.1 27.1c15.0 0 27.8-16.6 42.5-31.2l77.9-77.9c76.6 67.7 177.1 108.9 287.4 108.9 221.7 0 404.5-166.0 431.2-380.6h-109.8c-25.9 154.2-159.7 271.9-321.3 271.9zM919.9 76.6c-15.0 0-27.8 16.6-42.5 31.3L799.5 185.8c-76.5-67.7-177.1-108.9-287.4-108.9-221.8 0-404.5 166.1-431.3 380.6H190.6c25.9-154.2 159.7-271.9 321.4-271.9 80.2 0 153.4 29.3 210.1 77.4l-75.5 75.5c-11.6 11.5-25.8 22.0-25.8 37.1a27.2 27.2 0 0 0 27.1 27.1h271.9c27.1 0 27.1-23.9 27.1-27.1V103.8a27.1 27.1 0 0 0-27.1-27.1z"" />
                            </local:MyCard>"
                    Case 2
                        Logger.Info("主页预设：Minecraft 新闻")
                        Url = "https://mcnews.meloong.com"
                    Case 3
                        Logger.Info("主页预设：简单主页")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/MFn233/Custom.xaml"
                    Case 4
                        Logger.Info("主页预设：每日整合包推荐")
                        Url = "https://pclsub.sodamc.com/"
                    Case 5
                        Logger.Info("主页预设：Minecraft 皮肤推荐")
                        Url = "https://forgepixel.com/pcl_sub_file"
                    Case 6
                        Logger.Info("主页预设：OpenBMCLAPI 仪表盘 Lite")
                        Url = "https://pcl-bmcl.milu.ink/"
                    Case 7
                        Logger.Info("主页预设：主页市场")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/p/Homepages/Homepage.Market/Custom.xaml"
                    Case 8
                        Logger.Info("主页预设：更新日志")
                        Url = "https://pclhomeplazaoss.lingyunawa.top:26994/d/Homepages/Joker2184/UpdateHomepage.xaml"
                    Case 9
                        Logger.Info("主页预设：PCL 新功能说明书")
                        Url = "https://raw.gitcode.com/WForst-Breeze/WhatsNewPCL/raw/main/Custom.xaml"
                    Case 10
                        Logger.Info("主页预设：OpenMCIM Dashboard")
                        Url = "https://files.mcimirror.top/PCL"
                    Case 11
                        Logger.Info("主页预设：杂志主页")
                        Url = "https://gh-proxy.com/https://github.com/Neclyon/Magazine-Homepage-PCL/raw/main/output/Custom.xaml"
                    Case 12
                        Logger.Info("主页预设：PCL GitHub 仪表盘")
                        Url = "https://ddf.pcl-community.top/Custom.xaml"
                    Case 13
                        Logger.Info("主页预设：PCL 更新摘要")
                        Url = "https://raw.gitcode.com/ENC_Euphony/PCL-AI-Summary-HomePage/raw/master/Custom.xaml"
                    Case 14
                        Logger.Info("主页预设：今日新闻热点")
                        Url = "https://pcl.wyc-w.top/index.xaml"
                    Case 15
                        Logger.Info("主页预设：Minecraft 芝士站")
                        Url = "https://mkss-services.awaeric.xyz"
                    Case 16
                        Logger.Info("主页预设：整合包推荐引擎")
                        Url = "https://qawsedrftgyhujiko.fun/pcl2/Custom.xaml"
                    Case 17
                        Logger.Info("主页预设：Bangumi 番剧主页")
                        Url = "https://bangumi.p.kaphia.top"
                    Case 18
                        Logger.Info("主页预设：Bilibili 热门")
                        Url = "https://bilibili.p.kaphia.top"
                    Case 19
                        Logger.Info("主页预设：Music 云音乐热门")
                        Url = "https://cloudmusic.p.kaphia.top"
                    Case 20
                        Logger.Info("主页预设：模组推荐")
                        Url = "https://g-fish.dpdns.org/download/ModPage.xaml"
                    Case 21
                        Logger.Info("主页预设：每日游戏推荐")
                        Url = "https://pcl.xn--9iq209asnop94b.top/download/GamePage.xaml"
                    Case 22
                        Logger.Info("主页预设：Wallpaper 每日壁纸")
                        Url = "https://wallpaper.p.kaphia.top"
                    Case 23
                        Logger.Info("主页预设：Weather 天气预报")
                        Url = "https://weather-p.kaphia.top/{variable:PCLDailyWeatherPage:choose}.xaml"
                    Case 24
                        Logger.Info("主页预设：新一代狗屁王大杂烩主页")
                        Url = "https://end.g-fish.dpdns.org/Custom.xaml"
                End Select
        End Select
        '联网下载
        If Not String.IsNullOrWhiteSpace(Url) Then
            '替换自定义变量与设置
            Url = ArgumentReplace(Url, AddressOf WebUtility.HtmlEncode)
            If Url = Settings.Get(Of String)("CacheSavedPageUrl") AndAlso FileUtils.Exists(PathTemp & "Cache\Custom.xaml") Then
                '缓存可用
                Logger.Info("主页自定义数据来源：联网缓存文件")
                Content = FileUtils.TryReadAsString(PathTemp & "Cache\Custom.xaml")
                '后台更新缓存
                OnlineLoader.Start((Url, False))
            Else
                '缓存不可用
                Logger.Info("主页自定义数据来源：联网全新下载")
                Hint("正在加载主页……")
                RunInUiWait(Sub() LoadContent(Nothing)) '在加载结束前清空页面
                Settings.Set("CacheSavedPageVersion", "")
                OnlineLoader.Start((Url, True)) '下载完成后将会再次触发更新
                Return
            End If
        End If
        '同步到 UI
        RunInUi(Sub() LoadContent(Content))
    End Sub
    Private RefreshLock As New Object

    '联网获取主页文件
    Private OnlineLoader As New LoaderTask(Of (Address As String, ShouldRefresh As Boolean), Integer)("下载主页", AddressOf OnlineLoaderSub) With {.ReloadTimeout = 10 * 60 * 1000}
    Private Sub OnlineLoaderSub(Task As LoaderTask(Of (Address As String, ShouldRefresh As Boolean), Integer))
        Dim Address As String = Task.Input.Address '#3721 中连续触发两次导致内容变化
        Dim ShouldRefresh As Boolean = Task.Input.ShouldRefresh
        Try
            '获取版本校验地址
            Dim VersionAddress As String
            If Address.Contains(".xaml") Then
                VersionAddress = Address.Replace(".xaml", ".xaml.ini")
            Else
                VersionAddress = Address.BeforeFirst("?")
                If Not VersionAddress.EndsWithF("/") Then VersionAddress += "/"
                VersionAddress += "version"
                If Address.Contains("?") Then VersionAddress += "?" & Address.AfterFirst("?")
            End If
            '校验版本
            Dim Version As String = ""
            Try
                Version = NetRequestByClientRetry(VersionAddress)
                If Version.Length > 1000 Then Throw New Exception($"获取的主页版本过长（{Version.Length} 字符）")
                Dim CurrentVersion As String = Settings.Get(Of String)("CacheSavedPageVersion")
                If Version <> "" AndAlso CurrentVersion <> "" AndAlso Version = CurrentVersion Then
                    Logger.Info($"当前缓存的主页已为最新，当前版本：{Version}，检查源：{VersionAddress}")
                    Return
                End If
                Logger.Info($"需要下载联网主页，当前版本：{Version}，检查源：{VersionAddress}")
            Catch exx As Exception
                Logger.Warn(exx, $"联网获取主页版本失败")
                Logger.Info($"无法检查联网主页版本，将直接下载，检查源：{VersionAddress}")
            End Try
            '实际下载
            Dim FileContent As String = NetRequestByClientRetry(Address)
            Logger.Info($"已联网下载主页，内容长度：{FileContent.Length}，来源：{Address}")
            Settings.Set("CacheSavedPageUrl", Address)
            Settings.Set("CacheSavedPageVersion", Version)
            FileUtils.Write(PathTemp & "Cache\Custom.xaml", FileContent)
            '若内容变更则要求刷新
            If LoadedContentHash <> FileContent.GetStableHashCode() AndAlso ShouldRefresh Then Refresh()
        Catch ex As Exception
            Logger.Error(ex, $"下载主页失败（{Address}）", If(ModeDebug, LogBehavior.Alert, LogBehavior.Toast))
        End Try
    End Sub

    ''' <summary>
    ''' 立即强制刷新主页。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Public Sub ForceRefresh() Implements IRefreshable.Refresh
        Logger.Info("要求强制刷新主页")
        ClearCache()
        '实际的刷新
        If FrmMain.PageCurrent.Page = FormMain.PageType.Launch Then
            PanBack.ScrollToHome()
            Refresh()
        Else
            FrmMain.PageChange(FormMain.PageType.Launch)
        End If
    End Sub

    ''' <summary>
    ''' 清空主页缓存信息。
    ''' </summary>
    Private Sub ClearCache()
        LoadedContentHash = Nothing
        OnlineLoader.Input = ("", True)
        Settings.Set("CacheSavedPageUrl", "")
        Settings.Set("CacheSavedPageVersion", "")
        Logger.Info("已清空主页缓存")
    End Sub

    ''' <summary>
    ''' 从文本内容中加载主页。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Private Sub LoadContent(Content As String)
        Try
            SyncLock LoadContentLock
                '如果加载目标内容一致则不加载
                Dim Hash = If(Content, "").GetStableHashCode()
                If Hash = LoadedContentHash Then Return
                LoadedContentHash = Hash
                '实际加载内容
                PanCustom.Children.Clear()
                If String.IsNullOrWhiteSpace(Content) Then
                    Logger.Info($"实例化：清空主页 UI，来源为空")
                    Return
                End If
                Dim LoadStartTime As Date = Date.Now
                '修改时应同时修改 PageOtherHelpDetail.Init
                Content = ArgumentReplace(Content, AddressOf StringUtils.XmlEscape)
                Do While Content.Contains("xmlns")
                    Content = Content.RegexReplace("xmlns[^""']*(""|')[^""']*(""|')", "").Replace("xmlns", "") '禁止声明命名空间
                Loop
                Content = "<StackPanel xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:sys=""clr-namespace:System;assembly=mscorlib"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""clr-namespace:PCL;assembly=Plain Craft Launcher 2"" xmlns:core=""clr-namespace:MeloongCore;assembly=MeloongCore"" xmlns:corewpf=""clr-namespace:MeloongCore.Wpf;assembly=MeloongCore.Wpf"">" & Content & "</StackPanel>"
                Logger.Info($"实例化：加载主页 UI 开始，最终内容长度：{Content.Count}")
                PanCustom.Children.Add(GetObjectFromXML(Content))
                '加载计时
                Dim LoadCostTime = (Date.Now - LoadStartTime).Milliseconds
                Logger.Info($"实例化：加载主页 UI 完成，耗时 {LoadCostTime}ms")
                If LoadCostTime > 3000 Then Hint($"主页加载过于缓慢（花费了 {Math.Round(LoadCostTime / 1000, 1)} 秒），请向主页作者反馈此问题，或暂时停止使用该主页")
            End SyncLock
        Catch ex As Exception
            Logger.Warn(ex, $"加载失败的主页内容：{vbCrLf}{Content}")
            OnLoadContentFailed(ex)
        End Try
    End Sub
    ''' <summary>
    ''' 加载主页失败时调用。
    ''' </summary>
    Private Sub OnLoadContentFailed(ex As Exception)
        If ModeDebug OrElse Settings.Get(Of Integer)("UiCustomType") = 1 Then
            Logger.Warn(ex, "加载主页失败")
            If MyMsgBox(If(TypeOf ex Is UnauthorizedAccessException, ex.Message, $"主页内容编写有误，请根据下列错误信息进行检查：{vbCrLf}{ex.GetDisplay(False)}"),
                        "加载主页失败", "重试", "取消") = 1 Then ForceRefresh()
        Else
            Logger.Error(ex, "加载主页失败", LogBehavior.Toast)
        End If
    End Sub
    ''' <summary>
    ''' 捕获主页在 Measure 和 Arrange 阶段抛出的异常。
    ''' </summary>
    Private Sub DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs) Implements IDispatcherUnhandledException.DispatcherUnhandledException
        If TypeOf e.Exception IsNot Markup.XamlParseException Then Return
        e.Handled = True
        LoadContent(Nothing)
        OnLoadContentFailed(e.Exception)
    End Sub

    Private LoadedContentHash As ULong? = Nothing
    Private LoadContentLock As New Object

#End Region

#Region "校内组网"

    Private WgAutoTried As Boolean = False
    Private WgTimer As DispatcherTimer

    Private Sub InitWg() Handles Me.Loaded
        RefreshWgUi()
        '信息填充后回到页面顶部，避免标题被滚动遮挡
        RunInUi(Sub()
                    PanBack.ScrollToHome()
                End Sub, DispatcherPriority.Loaded)
        '定时刷新连接状态
        If WgTimer Is Nothing Then
            WgTimer = New DispatcherTimer With {.Interval = New TimeSpan(0, 0, 5)}
            AddHandler WgTimer.Tick, Sub() RefreshWgUi()
            WgTimer.Start()
        End If
        '已登录正版或 MUA 则自动尝试连接
        If WgAutoTried Then Return
        WgAutoTried = True
        If IsAccountAllowed() AndAlso Settings.Get(Of Boolean)("NJITWgAutoConnect") Then
            WgConnectAsync(False)
        End If
    End Sub

    ''' <summary>
    ''' 刷新校内组网卡片的状态与隧道完整信息。
    ''' </summary>
    Public Sub RefreshWgUi()
        If Not IsLoaded Then Return
        LabWgStatus.Text = WgStatusText()
        BtnWgConnect.Text = If(IsWgConnected(), "断开校内组网", "连接校内组网服务器")
        '未登录时提供独立登录入口；已登录则显示账号并置灰
        If IsMuaLoggedIn() Then
            Dim MuaName As String = Settings.Get(Of String)("MuaEmail")
            BtnWgLoginMua.Content = If(MuaName = "", "MUA 已登录", "已登录：" & MuaName)
            BtnWgLoginMua.IsEnabled = False
        Else
            BtnWgLoginMua.Content = "登录 MUA 账号"
            BtnWgLoginMua.IsEnabled = True
        End If
        '隧道固有信息
        LabWgTunnel.Text = WgTunnelName
        LabWgConf.Text = WgConfigFile
        LabWgConf.ToolTip = WgConfigFile
        '配置详情
        If FileUtils.Exists(WgConfigFile) Then
            Dim Address As String = WgConfigValue("Address")
            Dim Keepalive As String = WgConfigValue("PersistentKeepalive")
            Dim PublicKey As String = WgPublicKey()
            LabWgIp.Text = If(String.IsNullOrWhiteSpace(Address), "—", Address.BeforeFirst("/"))
            LabWgServer.Text = If(String.IsNullOrWhiteSpace(WgConfigValue("Endpoint")), "—", WgConfigValue("Endpoint"))
            LabWgServer.ToolTip = LabWgServer.Text
            LabWgSubnet.Text = If(String.IsNullOrWhiteSpace(WgConfigValue("AllowedIPs")), "—", WgConfigValue("AllowedIPs"))
            LabWgSubnet.ToolTip = LabWgSubnet.Text
            LabWgDns.Text = If(String.IsNullOrWhiteSpace(WgConfigValue("DNS")), "—", WgConfigValue("DNS"))
            LabWgDns.ToolTip = LabWgDns.Text
            LabWgKeepalive.Text = If(String.IsNullOrWhiteSpace(Keepalive), "—", Keepalive & " 秒")
            LabWgPublicKey.Text = If(String.IsNullOrWhiteSpace(PublicKey), "—", PublicKey)
            LabWgPublicKey.ToolTip = LabWgPublicKey.Text
            '收发流量（适配器不存在时显示 —）
            Dim Received As Long = 0, Sent As Long = 0
            If WgTraffic(Received, Sent) Then
                LabWgTraffic.Text = $"{StringUtils.FormatByteSize(Received)} / {StringUtils.FormatByteSize(Sent)}"
            Else
                LabWgTraffic.Text = "—"
            End If
        Else
            LabWgIp.Text = "—"
            LabWgServer.Text = "—"
            LabWgSubnet.Text = "—"
            LabWgDns.Text = "—"
            LabWgKeepalive.Text = "—"
            LabWgPublicKey.Text = "—"
            LabWgTraffic.Text = "—"
        End If
    End Sub

    Private Sub BtnWgGuide_Click(sender As Object, e As EventArgs) Handles BtnWgGuide.Click
        OpenWebsite(ClubGuideUrl)
    End Sub

    ''' <summary>
    ''' 在校内组网卡片里直接进入 MUA 登录页。
    ''' </summary>
    Private Sub BtnWgLoginMua_Click(sender As Object, e As EventArgs) Handles BtnWgLoginMua.Click
        If FrmLaunchLeft Is Nothing Then Return
        FrmLaunchLeft.SwitchToMuaLogin()
        Hint("在左侧填入 MUA 账号密码后，点「仅登录，不启动游戏」")
    End Sub

    Private Sub BtnClubHome_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnClubHome.Click
        OpenWebsite(ClubHomepage)
    End Sub

    Private Sub BtnClubSite_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnClubSite.Click
        OpenWebsite(WgPortalApi)
    End Sub

    Private Sub BtnClubMua_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnClubMua.Click
        OpenWebsite(MuaHomepage)
    End Sub

    Private Sub BtnWgConnect_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnWgConnect.Click
        If Not IsAccountAllowed() Then
            Hint("请先登录正版或 MUA 账号")
            If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.SwitchToMuaLogin()
            Return
        End If
        If IsWgConnected() Then
            WgDisconnectAsync()
        Else
            WgConnectAsync(True)
        End If
    End Sub

    ''' <summary>
    ''' 在后台连接校内组网。
    ''' </summary>
    Private Sub WgConnectAsync(ShowError As Boolean)
        BtnWgConnect.IsEnabled = False
        LabWgStatus.Text = "正在连接校内组网……"
        RunInThread(
        Sub()
            Try
                WgAutoConnect()
            Catch ex As Exception
                Logger.Warn(ex, "连接校内组网失败")
                RunInUi(Sub()
                            LabWgStatus.Text = "连接失败：" & ex.Message
                            If ShowError Then MyMsgBox(ex.Message, "连接校内组网失败")
                        End Sub)
            Finally
                RunInUi(Sub()
                            BtnWgConnect.IsEnabled = True
                            RefreshWgUi()
                        End Sub)
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 在后台断开校内组网。
    ''' </summary>
    Private Sub WgDisconnectAsync()
        BtnWgConnect.IsEnabled = False
        LabWgStatus.Text = "正在断开校内组网……"
        RunInThread(
        Sub()
            Try
                WgDisconnect()
            Catch ex As Exception
                Logger.Warn(ex, "断开校内组网失败")
                RunInUi(Sub() MyMsgBox(ex.Message, "断开校内组网失败"))
            Finally
                RunInUi(Sub()
                            BtnWgConnect.IsEnabled = True
                            RefreshWgUi()
                        End Sub)
            End Try
        End Sub)
    End Sub

#End Region

#Region "服务器整合包"

    Private PackServers As New JArray()
    Private PackLoaded As Boolean = False
    Private PackDownloading As Boolean = False
    Private PackControl As PackDownloadControl = Nothing
    '当前下载对应的服务器条目键，用于把进度写到它自己那一行，而不是只写在卡片顶部
    Private PackCurrentKey As String = ""

    ''' <summary>
    ''' 单个服务器条目上的下载进度区域（文字 + 进度条 + 取消按钮）。
    ''' </summary>
    Private Class PackProgressUi
        Public Box As Border
        Public Text As TextBlock
        Public Bar As ProgressBar
        Public Cancel As MyTextButton
    End Class

    Private ReadOnly PackProgressMap As New Dictionary(Of String, PackProgressUi)()
    Private ArchivedExpanded As Boolean = False
    Private ArchivedCount As Integer = 0
    Private OnlineExpanded As Boolean = True
    Private OnlineCount As Integer = 0
    Private ReadOnly ProgressWatch As Stopwatch = Stopwatch.StartNew()
    Private LastProgressMs As Long = 0
    '整合包修订正在应用时，禁止刷新、检查更新与重复点立即更新
    Private PackUpdating As Boolean = False
    Private ReadOnly PackUpdateWatch As Stopwatch = Stopwatch.StartNew()
    Private LastPackUpdateUiMs As Long = -1000

    Private Sub InitPack() Handles Me.Loaded
        If PackLoaded Then Return
        PackLoaded = True
        RunInThread(AddressOf RefreshPackList)
    End Sub

    ''' <summary>
    ''' 全局刷新：重新拉服务器列表、组网状态、账号页与主页数据。
    ''' </summary>
    Private Sub BtnRefreshAll_Click(sender As Object, e As EventArgs) Handles BtnRefreshAll.Click
        If PackDownloading OrElse PackUpdating Then
            Hint("有任务正在进行，完成后再刷新")
            Return
        End If
        LabPackStatus.Text = "正在获取服务器状态……"
        Hint("正在刷新……")
        FrmLaunchLeft?.RefreshPage(True, True)
        RefreshWgUi()
        RunInThread(Sub() RefreshPackList())
        Refresh()
    End Sub

    ''' <summary>
    ''' 主动检测整合包修订：重拉服务器列表并重新比对，结果用提示条反馈。
    ''' </summary>
    Private Sub BtnPackCheck_Click(sender As Object, e As EventArgs) Handles BtnPackCheck.Click
        If PackDownloading OrElse PackUpdating Then
            Hint("有任务正在进行，完成后再检查更新")
            Return
        End If
        BtnPackCheck.IsEnabled = False
        LabPackStatus.Text = "正在检查整合包更新……"
        RunInThread(
        Sub()
            Try
                RefreshPackList(True)
            Finally
                RunInUi(Sub() BtnPackCheck.IsEnabled = True)
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 重置本机整合包实例的修订标记: 先清空, 再按实际文件自动识别并补回.
    ''' </summary>
    Private Sub BtnPackRegister_Click(sender As Object, e As EventArgs) Handles BtnPackRegister.Click
        If PackDownloading OrElse PackUpdating Then
            Hint("有任务正在进行，完成后再重置标记")
            Return
        End If
        Dim Targets As New List(Of ModPackRevision.PackInstance)(PackInstances)
        If Targets.Count = 0 Then
            Hint("还没有识别到社团整合包实例，可以先点「检查更新」", HintType.Blue)
            Return
        End If
        BtnPackRegister.IsEnabled = False
        Hint("正在重置整合包标记……")
        RunInThread(
        Sub()
            Dim Cleared As Integer = 0
            For Each One As ModPackRevision.PackInstance In Targets
                Try
                    Dim MarkPath As String = IO.Path.Combine(One.InstancePath, ModPackRevision.MarkFile)
                    If IO.File.Exists(MarkPath) Then
                        IO.File.Delete(MarkPath)
                        Cleared += 1
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "清空整合包标记失败: " & One.InstanceName)
                End Try
            Next
            Dim Matched As Integer = 0
            Dim Unmatched As Integer = 0
            Try
                Dim Servers As JArray = FetchPackServers()
                Dim Updated As List(Of ModPackRevision.PackInstance) = ModPackRevision.FindPackInstances(Servers)
                For Each One As ModPackRevision.PackInstance In Updated
                    For Each Old As ModPackRevision.PackInstance In Targets
                        If Not String.Equals(Old.InstancePath, One.InstancePath, StringComparison.OrdinalIgnoreCase) Then Continue For
                        If One.CurrentRevision > 0 Then Matched += 1 Else Unmatched += 1
                        Exit For
                    Next
                Next
                RunInUi(
                Sub()
                    PackServers = Servers
                    PackFetchedAt = Date.Now
                    CheckPackUpdates()
                    If Unmatched = 0 Then
                        Hint($"已重置 {Cleared} 个标记，{Matched} 个实例自动识别成功", HintType.Green)
                    Else
                        Hint($"已重置 {Cleared} 个标记，{Matched} 个识别成功，{Unmatched} 个无法识别（建议重新下载）", HintType.Blue)
                    End If
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "重置整合包标记失败")
                RunInUi(Sub() Hint("重置失败: " & ex.Message, HintType.Red))
            Finally
                RunInUi(Sub() BtnPackRegister.IsEnabled = True)
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 待应用的整合包修订：只有本机装过该整合包（有修订标记）且服务端 revision 更高时才会出现。
    ''' </summary>
    Private PendingUpdates As New List(Of ModPackRevision.PackUpdateInfo)

    ''' <summary>本机能和社团整合包对上的实例（含已登记与未登记）。</summary>
    Private PackInstances As New List(Of ModPackRevision.PackInstance)

    ''' <summary>是否还有未应用的整合包修订（启动前检查用）。</summary>
    Public ReadOnly Property HasPendingUpdates As Boolean
        Get
            Return PendingUpdates.Count > 0
        End Get
    End Property

    Private PackFetchedAt As Date = Date.MinValue

    ''' <summary>
    ''' 启动前实时检查：距上次拉取超过 20 秒时重新拉一次，
    ''' 避免刚发布的修订因为列表缓存没更新而漏掉拦截。网络失败时沿用上次结果。
    ''' </summary>
    Public Function HasPendingUpdatesNow() As Boolean
        If (Date.Now - PackFetchedAt).TotalSeconds > 20 Then
            Try
                Dim FetchTask = Threading.Tasks.Task.Run(Function() FetchPackServers())
                If FetchTask.Wait(4000) Then
                    PackServers = FetchTask.Result
                    PackFetchedAt = Date.Now
                    CheckPackUpdates()
                Else
                    Logger.Warn("启动前检查整合包修订超时，沿用上次结果")
                End If
            Catch ex As Exception
                Logger.Warn(ex, "启动前检查整合包修订失败")
            End Try
        End If
        Return PendingUpdates.Count > 0
    End Function

    ''' <summary>
    ''' 对比服务端 revision 与本机标记，刷新横幅提示。
    ''' Notify 为 True 时（用户主动点检查更新）用提示条反馈检测结果。
    ''' </summary>
    Private Sub CheckPackUpdates(Optional Notify As Boolean = False)
        Try
            PendingUpdates = ModPackRevision.FindUpdates(PackServers)
            PackInstances = ModPackRevision.FindPackInstances(PackServers)
            Dim Unmarked As New List(Of ModPackRevision.PackInstance)
            For Each One As ModPackRevision.PackInstance In PackInstances
                If One.CurrentRevision <= 0 Then Unmarked.Add(One)
            Next
            If PendingUpdates.Count = 0 AndAlso Unmarked.Count = 0 Then
                RunInUi(
                Sub()
                    PanPackUpdate.Visibility = Visibility.Collapsed
                    If Notify AndAlso IsLoaded Then Hint("已是最新版本，没有检测到整合包修订", HintType.Green)
                End Sub)
                Return
            End If
            Dim Parts As New List(Of String)
            If PendingUpdates.Count > 0 Then
                Dim names As New List(Of String)
                For Each item As ModPackRevision.PackUpdateInfo In PendingUpdates
                    Dim localTag As String = If(item.LocalSha256.Length >= 6, item.LocalSha256.Substring(0, 6), "旧版")
                    Dim remoteTag As String = If(item.RemoteSha256.Length >= 6, item.RemoteSha256.Substring(0, 6), "新版")
                    names.Add($"{item.InstanceName}（{localTag} → {remoteTag}）")
                Next
                Parts.Add($"检测到 {PendingUpdates.Count} 个已安装的整合包有新修订，更新后才能继续游戏：" &
                          vbCrLf & String.Join(vbCrLf, names))
            End If
            If Unmarked.Count > 0 Then
                Dim names As New List(Of String)
                For Each item As ModPackRevision.PackInstance In Unmarked
                    names.Add(item.InstanceName)
                Next
                Parts.Add($"另有 {Unmarked.Count} 个实例缺少修订标记，收不到后续更新：" &
                          vbCrLf & String.Join(vbCrLf, names) &
                          vbCrLf & "点右侧「重置标记」会按实际文件自动识别版本并补上标记。" &
                          vbCrLf & "识别不了说明本地文件和任何版本都对不上，建议重新下载整合包。")
            End If
            Dim Text As String = String.Join(vbCrLf & vbCrLf, Parts)
            Dim Count As Integer = PendingUpdates.Count
            Dim UnmarkedCount As Integer = Unmarked.Count
            Dim HasUpdate As Boolean = Count > 0
            RunInUi(
            Sub()
                If Not IsLoaded Then Return
                LabPackUpdate.Text = Text
                BtnPackUpdate.Visibility = If(HasUpdate, Visibility.Visible, Visibility.Collapsed)
                PanPackUpdate.Visibility = Visibility.Visible
                If Notify Then
                    If HasUpdate Then
                        Hint($"检测到 {Count} 个整合包需要更新", HintType.Red)
                    Else
                        Hint($"有 {UnmarkedCount} 个实例缺少标记，可点「重置标记」自动识别", HintType.Blue)
                    End If
                End If
            End Sub)
            Logger.Info($"整合包检查：待修订 {Count} 个，未标记实例 {UnmarkedCount} 个")
        Catch ex As Exception
            Logger.Warn(ex, "检查整合包修订失败")
        End Try
    End Sub


    ''' <summary>
    ''' 在后台应用全部待更新的修订，UI 线程只负责刷新进度，避免界面卡死。
    ''' </summary>
    Private Sub BtnPackUpdate_Click(sender As Object, e As RoutedEventArgs) Handles BtnPackUpdate.Click
        If PackDownloading Then
            Hint("有下载任务正在进行，完成后再更新")
            Return
        End If
        If PackUpdating Then Return
        If PendingUpdates.Count = 0 Then
            PanPackUpdate.Visibility = Visibility.Collapsed
            Return
        End If
        '拷一份局部列表，后台线程不再读共享字段
        Dim Tasks As New List(Of ModPackRevision.PackUpdateInfo)(PendingUpdates)
        PackUpdating = True
        LastPackUpdateUiMs = -1000
        BtnPackUpdate.IsEnabled = False
        BarPackProgress.Value = 0
        LabPackProgress.Text = "正在准备更新……"
        PanPackProgress.Visibility = Visibility.Visible
        LabPackUpdate.Text = $"正在更新 {Tasks.Count} 个整合包，请不要关闭启动器……"
        RunInThread(
        Sub()
            Dim Applied As Integer = 0
            Dim Failed As New List(Of String)
            For Index As Integer = 0 To Tasks.Count - 1
                Dim Item As ModPackRevision.PackUpdateInfo = Tasks(Index)
                Dim Slot As Integer = Index
                Try
                    ModPackRevision.ApplyUpdate(Item,
                        Sub(Message As String, Percent As Double)
                            ReportPackUpdateProgress(Slot, Tasks.Count, Item.InstanceName, Message, Percent)
                        End Sub)
                    Applied += 1
                Catch ex As Exception
                    Logger.Warn(ex, $"应用整合包修订失败：{Item.PackId}")
                    ModErrorReport.Report("pack-update",
                                          $"整合包 {Item.PackId} 更新失败：{ex.Message}",
                                          ex.ToString(), Item.InstancePath)
                    Failed.Add(Item.InstanceName)
                End Try
            Next
            RunInUi(
            Sub()
                PackUpdating = False
                BtnPackUpdate.IsEnabled = True
                PanPackProgress.Visibility = Visibility.Collapsed
                If Failed.Count = 0 Then
                    PendingUpdates.Clear()
                    PanPackUpdate.Visibility = Visibility.Collapsed
                    Hint($"已更新 {Applied} 个整合包", HintType.Green)
                Else
                    LabPackUpdate.Text = $"有 {Failed.Count} 个整合包更新失败：{String.Join("、", Failed)}" & vbCrLf &
                                         "可以再次点「立即更新」重试，或重新下载整合包。"
                    PanPackUpdate.Visibility = Visibility.Visible
                    Hint("有整合包更新失败，详情见上方提示", HintType.Red)
                End If
            End Sub)
            RefreshPackList()
        End Sub)
    End Sub

    ''' <summary>
    ''' 把单个整合包的进度折算成整体进度并节流写 UI，避免回调堆积造成卡顿。
    ''' </summary>
    Private Sub ReportPackUpdateProgress(Index As Integer, Total As Integer, Name As String,
                                         Message As String, Percent As Double)
        Dim Now As Long = PackUpdateWatch.ElapsedMilliseconds
        '完成态务必刷新，其余最多 10 次/秒
        If Percent < 0.999 AndAlso Now - LastPackUpdateUiMs < 100 Then Return
        LastPackUpdateUiMs = Now
        Dim Overall As Double = (Index + Math.Max(0, Math.Min(1, Percent))) / Math.Max(1, Total)
        RunInUi(
        Sub()
            BarPackProgress.Value = Overall * 100
            LabPackProgress.Text = $"{Name}：{Message}"
        End Sub)
    End Sub

    ''' <summary>
    ''' 从社团站点拉取在档服务器与整合包信息。
    ''' Notify 透传给修订检测，用于用户主动检查时给出提示条反馈。
    ''' </summary>
    Private Sub RefreshPackList(Optional Notify As Boolean = False)
        Try
            PackServers = FetchPackServers()
            PackFetchedAt = Date.Now
            Logger.Info($"服务器整合包列表已加载：{PackServers.Count} 台服务器")
            RunInUi(
            Sub()
                If Not IsLoaded Then Return
                RenderPackList()
                CheckPackUpdates(Notify)
            End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "获取服务器整合包列表失败")
            RunInUi(
            Sub()
                If Not IsLoaded Then Return
                LabPackStatus.Text = "暂时无法获取服务器列表：" & ex.Message
                If Notify Then Hint("检查更新失败：" & ex.Message, HintType.Red)
            End Sub)
        End Try
    End Sub

    ''' <summary>
    ''' 重建卡片里的服务器列表。
    ''' </summary>
    Private Sub RenderPackList()
        PanPackOnline.Children.Clear()
        PanPackArchived.Children.Clear()
        PackProgressMap.Clear()
        Dim Total As Integer = 0
        Dim PackCount As Integer = 0
        Dim ArchivedTotal As Integer = 0
        For Each Token As JToken In PackServers
            Dim Server As JObject = TryCast(Token, JObject)
            If Server Is Nothing Then Continue For
            Total += 1
            Dim Packs As JArray = TryCast(Server("modpacks"), JArray)
            If Equals(Server("archived")?.ToObject(Of Boolean)(), True) Then
                PanPackArchived.Children.Add(BuildServerRow(Server))
                ArchivedTotal += 1
            Else
                '统计只算在营服务器与其可下载整合包
                If Packs IsNot Nothing AndAlso Packs.Count > 0 Then PackCount += 1
                PanPackOnline.Children.Add(BuildServerRow(Server))
            End If
        Next
        ArchivedCount = ArchivedTotal
        OnlineCount = Total - ArchivedTotal
        BtnToggleOnline.Visibility = If(OnlineCount = 0, Visibility.Collapsed, Visibility.Visible)
        BtnToggleArchived.Visibility = If(ArchivedTotal = 0, Visibility.Collapsed, Visibility.Visible)
        ApplyGroupState()
        LabPackStatus.Text = If(Total = 0, "暂时没有服务器信息", $"在营 {OnlineCount} 台服务器 · {PackCount} 个可下载整合包")
    End Sub

    ''' <summary>
    ''' 应用「在营 / 已结档」分组的展开状态。
    ''' </summary>
    Private Sub ApplyGroupState()
        PanPackOnline.Visibility = If(OnlineExpanded, Visibility.Visible, Visibility.Collapsed)
        BtnToggleOnline.Content = If(OnlineExpanded, "▾", "▸") & $" 在营 {OnlineCount} 台"
        PanPackArchived.Visibility = If(ArchivedExpanded, Visibility.Visible, Visibility.Collapsed)
        BtnToggleArchived.Content = If(ArchivedExpanded, "▾", "▸") & $" 已结档 {ArchivedCount} 台"
    End Sub

    Private Sub BtnToggleOnline_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnToggleOnline.Click
        OnlineExpanded = Not OnlineExpanded
        ApplyGroupState()
    End Sub

    Private Sub BtnToggleArchived_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnToggleArchived.Click
        ArchivedExpanded = Not ArchivedExpanded
        ApplyGroupState()
    End Sub

    ''' <summary>
    ''' 生成一行：服务器名与状态、地址、在线人数，有整合包时附带下载按钮。
    ''' </summary>
    Private Function BuildServerRow(Server As JObject) As FrameworkElement
        Dim Row As New Grid With {.Margin = New Thickness(0, 0, 0, 12)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim StatusObj As JObject = TryCast(Server("status"), JObject)
        Dim StateRaw As String = If(StatusObj?("state")?.ToString(), "unknown")
        Dim StateText As String = If(StateRaw = "online", "在线", If(StateRaw = "offline", "离线", "待确认"))
        Dim Archived As Boolean = Equals(Server("archived")?.ToObject(Of Boolean)(), True)

        Dim ServerName As String = If(Server("name")?.ToString(), "未知服务器")
        Dim Title As New TextBlock With {
            .Text = If(Archived, ServerName, ServerName & " · " & StateText),
            .FontSize = 13.5,
            .Foreground = TryCast(FindResource("ColorBrush1"), Brush),
            .TextTrimming = TextTrimming.CharacterEllipsis
        }

        Dim Address As String = If(Server("address")?.ToString(), "")
        Dim MetaText As String = If(Archived, "结档", "在营") & " · MC " & If(Server("mcVersion")?.ToString(), "?")
        Dim Players As JToken = StatusObj?("players")
        Dim Limit As JToken = StatusObj?("max")
        If Not Archived AndAlso Players IsNot Nothing AndAlso Players.Type <> JTokenType.Null Then
            MetaText &= " · 玩家 " & Players.ToString()
            If Limit IsNot Nothing AndAlso Limit.Type <> JTokenType.Null Then MetaText &= "/" & Limit.ToString()
        End If
        Dim Meta As New TextBlock With {
            .Text = MetaText,
            .FontSize = 11.5,
            .Foreground = TryCast(FindResource("ColorBrushGray3"), Brush),
            .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 3, 0, 0)
        }

        Dim Texts As New StackPanel
        Texts.Children.Add(Title)
        Texts.Children.Add(Meta)

        '逐个列出可用地址，并标注地址类型
        Dim Addresses As JArray = TryCast(Server("addresses"), JArray)
        If Addresses IsNot Nothing Then
            For Each One As JToken In Addresses
                Dim Item As JObject = TryCast(One, JObject)
                If Item Is Nothing Then Continue For
                Dim AddressValue As String = If(Item("address")?.ToString(), "")
                If String.IsNullOrWhiteSpace(AddressValue) Then Continue For
                Dim AddressLabel As String = If(Item("label")?.ToString(), "地址")
                Dim AddressState As String = If(Item("state")?.ToString(), "")
                Dim AddressTag As String = If(AddressState = "online", "在线", If(AddressState = "offline", "离线", "待确认"))
                '已结档服务器不再显示探测状态
                Dim AddressText As String = If(Archived, $"{AddressLabel}｜{AddressValue}", $"{AddressLabel}｜{AddressValue} · {AddressTag}")
                Texts.Children.Add(BuildAddressBlock(AddressText, AddressValue))
            Next
        ElseIf Address <> "" Then
            Texts.Children.Add(BuildAddressBlock(Address, Address))
        End If

        Dim Packs As JArray = TryCast(Server("modpacks"), JArray)
        Dim Pack As JObject = Nothing
        If Packs IsNot Nothing AndAlso Packs.Count > 0 Then
            For Each One As JToken In Packs
                Dim Item As JObject = TryCast(One, JObject)
                If Item IsNot Nothing AndAlso Equals(Item("default")?.ToObject(Of Boolean)(), True) Then
                    Pack = Item
                    Exit For
                End If
            Next
            If Pack Is Nothing Then Pack = TryCast(Packs(0), JObject)
        End If
        If Pack IsNot Nothing Then
            Texts.Children.Add(New TextBlock With {
                .Text = $"整合包 {If(Pack("name")?.ToString(), "")} {If(Pack("version")?.ToString(), "")} · {FormatPackSize(If(Pack("size")?.ToObject(Of Long)(), 0L))}",
                .FontSize = 11.5,
                .Foreground = TryCast(FindResource("ColorBrushGray3"), Brush),
                .TextWrapping = TextWrapping.Wrap,
                .Margin = New Thickness(0, 3, 0, 0)
            })
        End If
        '每个条目自己的下载进度：默认隐藏，下载时只在这一行显示（含取消按钮）
        Dim ProText As New TextBlock With {
            .FontSize = 11.5,
            .Foreground = TryCast(FindResource("ColorBrush1"), Brush),
            .TextTrimming = TextTrimming.CharacterEllipsis,
            .VerticalAlignment = VerticalAlignment.Center
        }
        Dim ProCancel As New MyTextButton With {
            .Content = "取消",
            .FontSize = 11.5,
            .Margin = New Thickness(12, 0, 0, 0),
            .Padding = New Thickness(0),
            .VerticalAlignment = VerticalAlignment.Center,
            .HorizontalAlignment = HorizontalAlignment.Right
        }
        AddHandler ProCancel.Click, Sub(sender As Object, e As EventArgs)
                                        CancelPackDownload()
                                    End Sub
        Dim ProHead As New Grid
        ProHead.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        ProHead.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Grid.SetColumn(ProText, 0)
        Grid.SetColumn(ProCancel, 1)
        ProHead.Children.Add(ProText)
        ProHead.Children.Add(ProCancel)

        Dim ProBar As New ProgressBar With {
            .Height = 6,
            .Minimum = 0,
            .Maximum = 100,
            .Value = 0,
            .BorderThickness = New Thickness(0),
            .Background = TryCast(FindResource("ColorBrush7"), Brush),
            .Foreground = TryCast(FindResource("ColorBrush3"), Brush),
            .Margin = New Thickness(0, 6, 0, 0)
        }

        Dim ProStack As New StackPanel
        ProStack.Children.Add(ProHead)
        ProStack.Children.Add(ProBar)

        '浅色圆角底板，让进度区看起来是一块独立的小卡片
        Dim ProBox As New Border With {
            .CornerRadius = New CornerRadius(4),
            .Background = TryCast(FindResource("ColorBrush8"), Brush),
            .Padding = New Thickness(10, 7, 10, 9),
            .Margin = New Thickness(0, 7, 0, 0),
            .Visibility = Visibility.Collapsed
        }
        ProBox.Child = ProStack

        Texts.Children.Add(ProBox)
        PackProgressMap(ServerKey(Server)) = New PackProgressUi With {
            .Box = ProBox, .Text = ProText, .Bar = ProBar, .Cancel = ProCancel
        }
        Grid.SetColumn(Texts, 0)
        Row.Children.Add(Texts)

        Dim Actions As New WrapPanel With {
            .Margin = New Thickness(12, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center,
            .HorizontalAlignment = HorizontalAlignment.Right,
            .MaxWidth = 200
        }
        If Pack IsNot Nothing Then
            Dim Btn As New MyButton With {
                .Text = If(Archived, "下载整合包", If(IsAccountAllowed(), "下载并安装", "登录后下载")),
                .Height = If(Archived, 28, 32),
                .Width = If(Archived, 92, 104),
                .Margin = New Thickness(0, 0, 6, 6),
                .VerticalAlignment = VerticalAlignment.Center
            }
            Dim LocalServer As JObject = Server
            Dim LocalPack As JObject = Pack
            AddHandler Btn.Click, Sub(sender As Object, e As MouseButtonEventArgs)
                                      StartPackDownload(LocalServer, LocalPack)
                                  End Sub
            Actions.Children.Add(Btn)
        End If
        '只有已结档服务器提供存档下载入口
        Dim Saves As JArray = TryCast(Server("saves"), JArray)
        If Archived AndAlso Saves IsNot Nothing Then
            For Each One As JToken In Saves
                Dim SaveItem As JObject = TryCast(One, JObject)
                If SaveItem Is Nothing Then Continue For
                Dim BtnSave As New MyButton With {
                    .Text = "下载存档",
                    .Height = 28,
                    .Width = 92,
                    .ColorType = MyButton.ColorState.Highlight,
                    .Margin = New Thickness(0, 0, 6, 6),
                    .VerticalAlignment = VerticalAlignment.Center
                }
                BtnSave.ToolTip = $"{If(SaveItem("name")?.ToString(), "存档")} · {FormatPackSize(If(SaveItem("size")?.ToObject(Of Long)(), 0L))}"
                Dim LocalServer As JObject = Server
                Dim LocalSave As JObject = SaveItem
                AddHandler BtnSave.Click, Sub(sender As Object, e As MouseButtonEventArgs)
                                              StartSaveDownload(LocalServer, LocalSave)
                                          End Sub
                Actions.Children.Add(BtnSave)
            Next
        End If
        If Actions.Children.Count > 0 Then
            Grid.SetColumn(Actions, 1)
            Row.Children.Add(Actions)
        End If
        Return Row
    End Function

    ''' <summary>
    ''' 生成可点击复制的地址行。
    ''' </summary>
    Private Function BuildAddressBlock(Display As String, CopyValue As String) As TextBlock
        Dim Block As New TextBlock With {
            .Text = Display,
            .FontSize = 11.5,
            .Foreground = TryCast(FindResource("ColorBrushGray3"), Brush),
            .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 3, 0, 0),
            .Cursor = Cursors.Hand,
            .ToolTip = "点击复制地址"
        }
        AddHandler Block.MouseLeftButtonUp,
            Sub(sender As Object, e As MouseButtonEventArgs)
                Try
                    My.Computer.Clipboard.SetText(CopyValue)
                    Hint("已复制服务器地址：" & CopyValue)
                Catch ex As Exception
                    Logger.Warn(ex, "复制服务器地址失败")
                End Try
                e.Handled = True
            End Sub
        Return Block
    End Function

    ''' <summary>
    ''' 服务器条目的唯一键，用于把下载进度定位到对应那一行。
    ''' </summary>
    Private Function ServerKey(Server As JObject) As String
        Dim Id As String = If(Server("id")?.ToString(), "")
        If String.IsNullOrWhiteSpace(Id) Then Id = If(Server("name")?.ToString(), "")
        Return Id
    End Function

    ''' <summary>
    ''' 把下载进度写到当前任务对应的服务器条目上；卡片顶部只留一句汇总。
    ''' </summary>
    Private Sub SetPackProgress(Text As String, Optional Percent As Integer = -1)
        RunInUi(
        Sub()
            If PackCurrentKey = "" OrElse Not PackProgressMap.ContainsKey(PackCurrentKey) Then Return
            Dim Item = PackProgressMap(PackCurrentKey)
            Item.Box.Visibility = Visibility.Visible
            Item.Text.Text = Text
            If Percent >= 0 Then Item.Bar.Value = Percent
        End Sub)
    End Sub

    ''' <summary>
    ''' 隐藏当前条目的进度区与取消按钮。
    ''' </summary>
    Private Sub HidePackProgress()
        RunInUi(
        Sub()
            If PackCurrentKey = "" OrElse Not PackProgressMap.ContainsKey(PackCurrentKey) Then Return
            Dim Item = PackProgressMap(PackCurrentKey)
            Item.Box.Visibility = Visibility.Collapsed
            Item.Bar.Value = 0
        End Sub)
    End Sub

    ''' <summary>
    ''' 取消当前下载任务（条目上的「取消」和顶部按钮共用）。
    ''' </summary>
    Private Sub CancelPackDownload()
        If PackControl Is Nothing Then Return
        LabPackStatus.Text = "正在取消下载……"
        '条目上也要给出反馈，并收起取消按钮，避免重复点击
        SetPackProgress("正在取消下载……")
        If PackCurrentKey <> "" AndAlso PackProgressMap.ContainsKey(PackCurrentKey) Then
            RunInUi(Sub()
                        PackProgressMap(PackCurrentKey).Cancel.Visibility = Visibility.Collapsed
                    End Sub)
        End If
        PackControl.Cancel()
    End Sub

    Private Function FormatPackSize(Bytes As Long) As String
        If Bytes <= 0 Then Return "大小未知"
        If Bytes >= 1024L * 1024 * 1024 Then Return $"{Bytes / 1024.0 / 1024 / 1024:F2} GB"
        Return $"{Bytes / 1024.0 / 1024:F1} MB"
    End Function

    ''' <summary>
    ''' 计算存档对应的实例名：优先按 packId 找整合包，否则用默认整合包。
    ''' </summary>
    Private Function SaveInstanceName(Server As JObject, Save As JObject) As String
        Dim PackId As String = If(Save("packId")?.ToString(), "")
        Dim Version As String = ""
        Dim Packs As JArray = TryCast(Server("modpacks"), JArray)
        If Packs IsNot Nothing Then
            For Each One As JToken In Packs
                Dim Item As JObject = TryCast(One, JObject)
                If Item Is Nothing Then Continue For
                If PackId <> "" Then
                    If String.Equals(Item("id")?.ToString(), PackId, StringComparison.OrdinalIgnoreCase) Then
                        Version = If(Item("version")?.ToString(), "")
                        Exit For
                    End If
                ElseIf Equals(Item("default")?.ToObject(Of Boolean)(), True) Then
                    Version = If(Item("version")?.ToString(), "")
                    Exit For
                End If
            Next
        End If
        Return (If(Server("name")?.ToString(), "") & " " & Version).Trim()
    End Function

    ''' <summary>
    ''' 判断压缩包顶层是否已经是单个世界文件夹（含 level.dat）。
    ''' </summary>
    Private Function ZipHasWorldFolder(FilePath As String) As Boolean
        Using Zip As IO.Compression.ZipArchive = FileUtils.OpenZip(FilePath)
            For Each Entry As IO.Compression.ZipArchiveEntry In Zip.Entries
                Dim Parts As String() = Entry.FullName.Split("/"c)
                If Parts.Length = 2 AndAlso Parts(1).Equals("level.dat", StringComparison.OrdinalIgnoreCase) Then Return True
            Next
        End Using
        Return False
    End Function

    ''' <summary>
    ''' 按 split 规范下载分片并按序拼接成完整存档，每片单独校验 SHA256。
    ''' </summary>
    Private Sub DownloadSaveParts(Parts As JArray, Target As String, Control As PackDownloadControl)
        Dim PartFolder As String = PathTemp & "Cache\Saves\parts\"
        If Directory.Exists(PartFolder) Then Directory.Delete(PartFolder, True)
        Directory.CreateDirectory(PartFolder)
        Dim Ordered As New List(Of JObject)
        For Each One As JToken In Parts
            Dim Item As JObject = TryCast(One, JObject)
            If Item IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(Item("file")?.ToString()) Then Ordered.Add(Item)
        Next
        'part01、part02 按名称升序即拼接顺序
        Ordered.Sort(Function(A, B) String.CompareOrdinal(A("file").ToString(), B("file").ToString()))
        Dim Index As Integer = 0
        For Each Item As JObject In Ordered
            Index += 1
            If Control.Cancelled Then Throw New OperationCanceledException()
            Dim PartName As String = Item("file").ToString()
            Dim PartPath As String = PartFolder & PartName
            Dim Expected As String = If(Item("sha256")?.ToString(), "").ToLowerInvariant()
            Dim Urls As New List(Of String)
            Dim Sources As JArray = TryCast(Item("sources"), JArray)
            If Sources IsNot Nothing Then
                For Each One As JToken In Sources
                    Dim Source As JObject = TryCast(One, JObject)
                    Dim Url As String = If(Source?("url")?.ToString(), "")
                    If Not String.IsNullOrWhiteSpace(Url) Then Urls.Add(Url)
                Next
            End If
            If Urls.Count = 0 Then Throw New Exception($"分片没有下载地址：{PartName}")
            Dim Current As Integer = Index
            SetPackProgress($"正在下载存档分片 {Current} / {Ordered.Count}……")
            Dim Got As Boolean = False
            Dim LastError As String = ""
            For Each Url As String In RankPackSources(Urls)
                Try
                    DownloadPackFile(Url, PartPath, Expected, AddressOf UpdatePackProgress, Control)
                    Got = True
                    Exit For
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    If Control.Cancelled Then Throw New OperationCanceledException()
                    LastError = ex.Message
                    Logger.Warn($"存档分片下载失败（{PartName} ← {Url}）：{ex.Message}")
                End Try
            Next
            If Not Got Then Throw New Exception($"分片下载失败：{PartName}（{LastError}）")
        Next
        SetPackProgress("正在拼接存档分片……", 100)
        Using Output As New IO.FileStream(Target, IO.FileMode.Create, IO.FileAccess.Write)
            For Each Item As JObject In Ordered
                Using Input As New IO.FileStream(PartFolder & Item("file").ToString(), IO.FileMode.Open, IO.FileAccess.Read)
                    Input.CopyTo(Output)
                End Using
            Next
        End Using
        Try
            Directory.Delete(PartFolder, True)
        Catch ex As Exception
            Logger.Warn(ex, "清理存档分片缓存失败")
        End Try
    End Sub

    ''' <summary>
    ''' 下载存档并解压到对应整合包实例的 saves 目录。
    ''' </summary>
    Private Sub StartSaveDownload(Server As JObject, Save As JObject)
        If PackDownloading Then
            Hint("已有下载任务正在进行")
            Return
        End If
        Dim Instance As String = SaveInstanceName(Server, Save)
        If Instance = "" Then
            Hint("该存档没有关联到整合包版本，暂时无法确定安装位置")
            Return
        End If
        Dim VersionFolder As String = McFolderSelected & "versions\" & Instance & "\"
        If Not (Directory.Exists(VersionFolder) AndAlso FileUtils.Exists(VersionFolder & Instance & ".json")) Then
            MyMsgBox($"下载存档需要先安装对应版本「{Instance}」。{vbCrLf}{vbCrLf}请先在同一张卡片点击「下载并安装」，整合包安装完成后再回来下载存档。",
                     "未找到对应版本")
            Return
        End If
        Dim Urls As New List(Of String)
        Dim Sources As JArray = TryCast(Save("sources"), JArray)
        If Sources IsNot Nothing Then
            For Each One As JToken In Sources
                Dim Source As JObject = TryCast(One, JObject)
                Dim Url As String = If(Source?("url")?.ToString(), "")
                If Not String.IsNullOrWhiteSpace(Url) Then Urls.Add(Url)
            Next
        End If
        Dim Parts As JArray = TryCast(Save("parts"), JArray)
        Dim HasParts As Boolean = Parts IsNot Nothing AndAlso Parts.Count > 0
        If Urls.Count = 0 AndAlso Not HasParts Then
            Hint("该存档没有可用的下载地址")
            Return
        End If
        Dim FileName As String = If(Save("file")?.ToString(), "")
        If String.IsNullOrWhiteSpace(FileName) Then FileName = If(Save("id")?.ToString(), "save") & ".zip"
        Dim ExpectedHash As String = If(Save("sha256")?.ToString(), "").ToLowerInvariant()
        Dim SaveName As String = If(Save("name")?.ToString(), "社团存档")
        Dim Target As String = PathTemp & "Cache\Saves\" & FileName
        PackDownloading = True
        PackControl = New PackDownloadControl
        PackCurrentKey = ServerKey(Server)
        SetPackProgress("正在准备下载存档……", 0)
        RunInThread(
        Sub()
            Dim LastError As String = ""
            Dim Cancelled As Boolean = False
            Try
                Directory.CreateDirectory(PathTemp & "Cache\Saves\")
                Dim Ready As Boolean = FileUtils.Exists(Target) AndAlso ExpectedHash <> "" AndAlso GetPackSha256(Target) = ExpectedHash
                If Not Ready Then
                    '优先整包源（通常是校内组网，速度最快）
                    If Urls.Count > 0 Then
                        SetPackProgress("正在测速选择下载源……")
                        Dim Ordered As List(Of String) = RankPackSources(Urls)
                        If PackControl.Cancelled Then Throw New OperationCanceledException()
                        For Each Url As String In Ordered
                            Try
                                SetPackProgress("正在下载存档……")
                                DownloadPackFile(Url, Target, ExpectedHash, AddressOf UpdatePackProgress, PackControl)
                                Ready = True
                                Exit For
                            Catch ex As OperationCanceledException
                                Throw
                            Catch ex As Exception
                                If PackControl.Cancelled Then Throw New OperationCanceledException()
                                LastError = ex.Message
                                Logger.Warn($"存档下载源失败（{Url}）：{ex.Message}")
                            End Try
                        Next
                    End If
                    '整包源不可用时，回退到按 split 规范下载分片并拼接
                    If Not Ready AndAlso HasParts Then
                        SetPackProgress("正在下载存档分片……")
                        DownloadSaveParts(Parts, Target, PackControl)
                        If ExpectedHash <> "" AndAlso GetPackSha256(Target) <> ExpectedHash Then Throw New Exception("存档分片拼接后校验失败")
                        Ready = True
                    End If
                    If Not Ready Then Throw New Exception("所有下载源都失败了：" & LastError)
                End If
                If PackControl.Cancelled Then Throw New OperationCanceledException()
                SetPackProgress("正在解压存档……", 100)
                Dim SavesRoot As String = New McInstance(VersionFolder).PathIndie & "saves\"
                Directory.CreateDirectory(SavesRoot)
                '压缩包内没有世界文件夹时，用存档名建一层，避免文件散落在 saves 根目录
                Dim Destination As String = If(ZipHasWorldFolder(Target), SavesRoot, SavesRoot & SaveName & "\")
                FileUtils.ExtractToDirectory(Target, Destination, Nothing)
                Try
                    FileUtils.Delete(Target)
                Catch ex As Exception
                    Logger.Warn(ex, "删除存档缓存失败")
                End Try
            Catch ex As OperationCanceledException
                Cancelled = True
                Try
                    FileUtils.Delete(Target)
                Catch
                    ' 忽略删除失败
                End Try
                Logger.Info("用户取消了存档下载")
            Catch ex As Exception
                Logger.Warn(ex, "下载或解压存档失败")
                RunInUi(
                Sub()
                    LabPackStatus.Text = "存档处理失败：" & ex.Message
                    MyMsgBox(ex.Message, "存档下载失败")
                End Sub)
            Finally
                RunInUi(
                Sub()
                    PackDownloading = False
                    PackControl = Nothing
                    BtnPackCancel.Visibility = Visibility.Collapsed
                    HidePackProgress()
                    RenderPackList()
                    If Cancelled Then LabPackStatus.Text = "已取消下载存档"
                End Sub)
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 取消正在进行的整合包下载。
    ''' </summary>
    Private Sub BtnPackCancel_Click(sender As Object, e As EventArgs) Handles BtnPackCancel.Click
        CancelPackDownload()
    End Sub

    ''' <summary>
    ''' 下载并安装指定整合包：多源测速 → 下载 → 校验 → 调 PCL 安装。
    ''' </summary>
    Private Sub StartPackDownload(Server As JObject, Pack As JObject)
        If PackDownloading Then
            Hint("已有整合包正在下载")
            Return
        End If
        If Not IsAccountAllowed() Then
            Hint("请先登录正版或 MUA 账号")
            If FrmLaunchLeft IsNot Nothing Then FrmLaunchLeft.SwitchToMuaLogin()
            Return
        End If
        Dim Urls As New List(Of String)
        Dim Sources As JArray = TryCast(Pack("sources"), JArray)
        If Sources IsNot Nothing Then
            For Each One As JToken In Sources
                Dim Source As JObject = TryCast(One, JObject)
                Dim Url As String = If(Source?("url")?.ToString(), "")
                If Not String.IsNullOrWhiteSpace(Url) Then Urls.Add(Url)
            Next
        End If
        If Urls.Count = 0 Then
            Hint("该整合包没有可用的下载地址")
            Return
        End If
        Dim FileName As String = If(Pack("file")?.ToString(), "")
        If String.IsNullOrWhiteSpace(FileName) Then FileName = If(Pack("id")?.ToString(), "modpack") & ".zip"
        Dim ExpectedHash As String = If(Pack("sha256")?.ToString(), "").ToLowerInvariant()
        Dim Target As String = PathTemp & "Cache\Modpacks\" & FileName
        Dim InstanceName As String = If(Server("name")?.ToString(), "社团整合包") & " " & If(Pack("version")?.ToString(), "")
        PackDownloading = True
        PackControl = New PackDownloadControl
        PackCurrentKey = ServerKey(Server)
        SetPackProgress("正在准备下载……", 0)
        RunInThread(
        Sub()
            Dim LastError As String = ""
            Dim Cancelled As Boolean = False
            Try
                Directory.CreateDirectory(PathTemp & "Cache\Modpacks\")
                If FileUtils.Exists(Target) AndAlso ExpectedHash <> "" AndAlso GetPackSha256(Target) = ExpectedHash Then
                    SetPackProgress("本地已有相同整合包，直接安装……", 100)
                Else
                    SetPackProgress("正在测速选择下载源……")
                    Dim Ordered As List(Of String) = RankPackSources(Urls)
                    If PackControl.Cancelled Then Throw New OperationCanceledException()
                    Dim Done As Boolean = False
                    For Each Url As String In Ordered
                        Try
                            SetPackProgress("正在连接下载源……")
                            DownloadPackFile(Url, Target, ExpectedHash, AddressOf UpdatePackProgress, PackControl)
                            Done = True
                            Exit For
                        Catch ex As OperationCanceledException
                            Throw
                        Catch ex As Exception
                            If PackControl.Cancelled Then Throw New OperationCanceledException()
                            LastError = ex.Message
                            Logger.Warn($"整合包下载源失败（{Url}）：{ex.Message}")
                        End Try
                    Next
                    If Not Done Then Throw New Exception("所有下载源都失败了：" & LastError)
                End If
                If PackControl.Cancelled Then Throw New OperationCanceledException()
                SetPackProgress("正在安装整合包……", 100)
                Dim Install As LoaderCombo(Of String) = ModpackInstall(Target, InstanceName)
                '安装是异步加载器：必须等它结束再删缓存包，否则解压时文件已被删除
                If Install IsNot Nothing Then
                    Dim CachedPack As String = Target
                    Dim MarkServer As String = If(Server("id")?.ToString(), "")
                    Dim MarkPack As String = If(Pack("id")?.ToString(), "")
                    Dim MarkRevision As Integer = If(Pack("revision")?.ToObject(Of Integer)(), 1)
                    Dim MarkSha As String = If(Pack("sha256")?.ToString(), "").ToLowerInvariant()
                    Dim MarkJava As Integer = If(Pack("javaVersion")?.ToObject(Of Integer)(), 0)
                    Dim MarkInstance As String = InstanceName
                    Dim MarkDone As Boolean = False
                    Dim MarkLock As New Object()
                    '安装结束时收尾：删缓存、写修订标记。可能被事件与状态补检查调用两次，用 MarkDone 去重
                    Dim HandleInstallEnd As Action(Of LoadState) =
                        Sub(NewState As LoadState)
                            If NewState <> LoadState.Finished AndAlso NewState <> LoadState.Failed AndAlso NewState <> LoadState.Canceled Then Return
                            SyncLock MarkLock
                                If MarkDone Then Return
                                MarkDone = True
                            End SyncLock
                            Try
                                FileUtils.Delete(CachedPack)
                            Catch ex As Exception
                                Logger.Warn(ex, "删除整合包缓存失败")
                            End Try
                            '安装成功才写修订标记；以后据此判断是否收到强制更新
                            If NewState = LoadState.Finished AndAlso MarkPack <> "" Then
                                Try
                                    Dim Inst As McInstance = New McInstance(MarkInstance).Load()
                                    ModPackRevision.WriteMark(Inst.PathVersion, MarkPack, MarkServer, MarkRevision, Inst.PathIndie, MarkSha, MarkJava)
                                    Logger.Info($"已写入整合包修订标记：{MarkInstance} revision {MarkRevision}")
                                Catch ex As Exception
                                    Logger.Warn(ex, "写入整合包修订标记失败")
                                End Try
                            End If
                        End Sub
                    AddHandler Install.OnStateChangedThread,
                        Sub(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
                            HandleInstallEnd(NewState)
                        End Sub
                    '必须打开开关，否则 OnStateChangedThread 根本不会触发
                    Install.HasOnStateChangedThread = True
                    '安装器在返回前已经 Start，若期间已结束则补一次
                    RunInThread(Sub() HandleInstallEnd(Install.State))
                End If
            Catch ex As OperationCanceledException
                Cancelled = True
                Try
                    FileUtils.Delete(Target)
                Catch
                    ' 忽略删除失败
                End Try
                Logger.Info("用户取消了整合包下载")
            Catch ex As Exception
                Logger.Warn(ex, "下载或安装整合包失败")
                RunInUi(
                Sub()
                    LabPackStatus.Text = "整合包处理失败：" & ex.Message
                    MyMsgBox(ex.Message, "整合包下载失败")
                End Sub)
            Finally
                RunInUi(
                Sub()
                    PackDownloading = False
                    PackControl = Nothing
                    BtnPackCancel.Visibility = Visibility.Collapsed
                    HidePackProgress()
                    RenderPackList()
                    If Cancelled Then LabPackStatus.Text = "已取消下载"
                End Sub)
            End Try
        End Sub)
    End Sub

    Private Sub UpdatePackProgress(Done As Long, Total As Long)
        '已经在取消时，不要再让下载进度覆盖「正在取消下载……」的提示
        If PackControl IsNot Nothing AndAlso PackControl.Cancelled Then Return
        Dim NowMs As Long = ProgressWatch.ElapsedMilliseconds
        If NowMs - LastProgressMs < 500 AndAlso (Total <= 0 OrElse Done < Total) Then Return
        LastProgressMs = NowMs
        Dim Percent As Integer = If(Total <= 0, 0, CInt(Done * 100.0 / Total))
        SetPackProgress($"正在下载整合包 {Percent}%（{FormatPackSize(Done)} / {FormatPackSize(Total)}）", Percent)
    End Sub

#End Region

End Class
