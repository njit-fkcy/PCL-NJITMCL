Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media

''' <summary>
''' 「联机 → 我的房间」：创建/加入房间、成员排行、关闭房间。
''' </summary>
Public Class PageLinkRoom
    Implements IRefreshable

    Private Built As Boolean = False
    Private RelayNodes As JArray = Nothing
    Private PublicRoomEnabled As Boolean = False
    Private MyRoomCode As String = ""
    '建房表单与「我的房间」卡片各一套公网拉杆，共用同一个开关状态
    Private FormPublicTrack As Border
    Private FormPublicKnob As Border
    Private FormPublicState As TextBlock
    Private CardPublicTrack As Border
    Private CardPublicKnob As Border
    Private CardPublicState As TextBlock
    Private ExpandedRoom As String = ""
    Private RoomsCache As JArray = Nothing
    Private LatencyCache As New System.Collections.Concurrent.ConcurrentDictionary(Of String, Integer)

    Private TxtRoomName As MyTextBox
    Private TxtLocalPort As MyTextBox
    Private TxtPassword As MyTextBox
    Private TxtJoin As MyTextBox
    Private BtnCreate As MyButton
    Private PanCreateForm As StackPanel
    Private PanMyRoomCard As StackPanel
    Private PanOtherRoomList As StackPanel
    Private LabOtherRooms As TextBlock
    '好友房间分组：房主是好友的房间单独展示
    Private PanFriendRoomList As StackPanel
    Private LabFriendRooms As TextBlock
    '好友账号 ID 集合，用于把好友当房主的房间分到「好友房间」
    Private FriendIds As New HashSet(Of String)(StringComparer.Ordinal)
    '自己房间的密码，仅保存在内存，关闭房间后清空
    Private HostPassword As String = ""
    '已加入的别人房间：房间号与密码，用于卡片显示「已加入」并保持展开
    Private JoinedRoomCode As String = ""
    Private JoinedPassword As String = ""
    '防止 20 秒定时刷新与手动刷新并发堆积请求
    Private Refreshing As Boolean = False
    '游戏进程下拉框：房间与进服校验绑到哪一台
    Private ComboProcess As MyComboBox
    Private LabProcess As TextBlock
    Private ProcessPids As New List(Of Integer)
    Private FillingProcessList As Boolean

    Private Sub PageEnterHandler() Handles Me.PageEnter
        If Not Built Then
            Built = True
            BuildTools()
            AddHandler RoomRefreshTimer.Tick, AddressOf RoomRefreshTimer_Tick
        End If
        RefreshAll()
        '进服校验卡片挂在「我的房间」内容区，必须等 BuildTools 建好 PanRoomArea 之后再挂
        ModOnlineMode.Mount(Me)
        '停留在房间页时定期刷新，房主才能看到新加入的成员
        RoomRefreshTimer.Stop()
        RoomRefreshTimer.Start()
    End Sub

    Private Sub PageExitHandler() Handles Me.PageExit
        RoomRefreshTimer.Stop()
    End Sub

    ''' <summary>房间页停留时的轮询间隔。房主关房或改为不公开后，其他端最多这么多秒发现。</summary>
    Private ReadOnly RoomRefreshTimer As New System.Windows.Threading.DispatcherTimer With {
        .Interval = TimeSpan.FromSeconds(8)}

    Private Sub RoomRefreshTimer_Tick(sender As Object, e As EventArgs)
        If LabStatus Is Nothing Then Return
        RefreshAll()
        '每拍顺带刷新进服校验卡片：开 LAN、建房都可能发生在停留期间，
        '不重挂一次卡片就会一直停在进页那一刻的「未检测到」。
        ModOnlineMode.Mount(Me)
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        RefreshAll()
        ModOnlineMode.Mount(Me)
    End Sub

#Region "界面构建"

    Private Sub BuildTools()
        PanRoomArea.Children.Clear()
        PanOtherRooms.Children.Clear()

        PanMyRoomCard = New StackPanel
        PanRoomArea.Children.Add(PanMyRoomCard)
        PanCreateForm = BuildCreateForm()
        PanRoomArea.Children.Add(PanCreateForm)

        LabFriendRooms = SocialSectionTitle("好友房间")
        LabFriendRooms.Margin = New Thickness(0, 16, 0, 0)
        PanOtherRooms.Children.Add(LabFriendRooms)
        PanFriendRoomList = New StackPanel
        PanOtherRooms.Children.Add(PanFriendRoomList)

        LabOtherRooms = SocialSectionTitle("其他房间")
        LabOtherRooms.Margin = New Thickness(0, 16, 0, 0)
        PanOtherRooms.Children.Add(LabOtherRooms)
        PanOtherRoomList = New StackPanel
        PanOtherRooms.Children.Add(PanOtherRoomList)
    End Sub

    Private Function BuildCreateForm() As StackPanel
        Dim Panel As New StackPanel
        Panel.Children.Add(SocialLabel("默认只开校园内网入口；加入时优先校内，无法直连再尝试 P2P 打洞，最后走公网中继。"))
        Dim Row As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 8, 0, 0)}
        TxtRoomName = New MyTextBox With {.Width = 170, .Height = 30, .HintText = "房间名称"}
        TxtLocalPort = New MyTextBox With {.Width = 84, .Height = 30, .Text = "25565", .Margin = New Thickness(8, 0, 0, 0)}
        BtnCreate = New MyButton With {
            .Text = "创建房间", .Height = 30, .MinWidth = 88, .Margin = New Thickness(8, 0, 0, 0),
            .ColorType = MyButton.ColorState.Highlight}
        AddHandler BtnCreate.Click, Sub() DoCreateRoom()
        Row.Children.Add(TxtRoomName)
        Row.Children.Add(TxtLocalPort)
        Row.Children.Add(BtnCreate)
        Panel.Children.Add(Row)

        '房间密码：可选，设置后好友加入必须先输密码
        Dim PwdRow As New StackPanel With {
            .Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 8, 0, 0)}
        TxtPassword = New MyTextBox With {
            .Width = 200, .Height = 30, .HintText = "房间密码（可选，4—32 位）"}
        PwdRow.Children.Add(TxtPassword)
        Panel.Children.Add(PwdRow)
        Panel.Children.Add(SocialLabel("设置密码后，好友加入时需要输入密码；中控只保存密码摘要，不保存明文。"))

        '公网房间：拉杆开关，默认关闭（只开校园内网）
        Panel.Children.Add(BuildPublicSwitch(FormPublicTrack, FormPublicKnob, FormPublicState))
        Panel.Children.Add(SocialLabel("打开为公网房间，关闭为校园内网房间；为节省社团资源，请在确有需要时再开启。"))

        '游戏进程：同时开着两个客户端时，房间与进服校验该绑到哪一台
        Dim ProcTitle As TextBlock = SocialRowTitle("游戏进程")
        ProcTitle.Margin = New Thickness(0, 12, 0, 0)
        Panel.Children.Add(ProcTitle)
        Dim ProcRow As New StackPanel With {
            .Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 6, 0, 0)}
        ComboProcess = New MyComboBox With {.Width = 320, .Height = 30}
        AddHandler ComboProcess.SelectionChanged, Sub() OnProcessPicked()
        Dim BtnProc As New MyButton With {
            .Text = "刷新", .Height = 30, .MinWidth = 64, .Margin = New Thickness(8, 0, 0, 0)}
        AddHandler BtnProc.Click, Sub() RefreshProcessList(True)
        ProcRow.Children.Add(ComboProcess)
        ProcRow.Children.Add(BtnProc)
        Panel.Children.Add(ProcRow)
        LabProcess = SocialRowState("自动：按最近启动的那个进程（只列启动器启动的游戏）。")
        Panel.Children.Add(LabProcess)
        RefreshProcessList(False)

        '进服校验的落点：ModOnlineMode.Mount 会把拉杆塞进这块占位，
        '位置就在「公网房间」下面；有房间时表单收起，拉杆会退回页面末尾继续可用。
        Dim PanMode As New StackPanel With {.Name = "PanOnlineModeForm"}
        Panel.Children.Add(PanMode)

        Dim JoinRow As New StackPanel With {.Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 10, 0, 0)}
        TxtJoin = New MyTextBox With {.Width = 190, .Height = 30, .HintText = "输入 10 位房间号加入"}
        Dim BtnJoin As New MyButton With {
            .Text = "加入房间", .Height = 30, .MinWidth = 88, .Margin = New Thickness(8, 0, 0, 0)}
        AddHandler BtnJoin.Click, Sub() DoJoin()
        JoinRow.Children.Add(TxtJoin)
        JoinRow.Children.Add(BtnJoin)
        Panel.Children.Add(JoinRow)
        Return Panel
    End Function

#Region "游戏进程选择"

    ''' <summary>
    ''' 重新枚举 java 进程填进下拉框。
    ''' Manual = 用户点了「刷新」，这时无论列表有没有变化都重建一次。
    '''
    ''' WMI 一次几百毫秒，必须走后台线程；列表没变化时不重建，免得每 8 秒把选择清掉。
    ''' </summary>
    Private Sub RefreshProcessList(Manual As Boolean)
        If ComboProcess Is Nothing Then Return
        RunInNewThread(
        Sub()
            Dim Jvms As List(Of ModOnlineMode.ServerJvm) = Nothing
            Try
                '只列启动器自己启动的游戏进程：房间不挂别人的服务，中继也就不会被当免费 frp 用
                Jvms = ModOnlineMode.ListGameJvms()
            Catch ex As Exception
                Logger.Warn(ex, "枚举游戏进程失败")
            End Try
            RunInUi(Sub() ApplyProcessList(Jvms, Manual))
        End Sub, "RoomProcessList")
    End Sub

    Private Sub ApplyProcessList(Jvms As List(Of ModOnlineMode.ServerJvm), Manual As Boolean)
        If ComboProcess Is Nothing Then Return
        Dim Pids As New List(Of Integer)
        If Jvms IsNot Nothing Then
            For Each Jvm In Jvms
                Pids.Add(Jvm.Pid)
            Next
        End If
        If Not Manual AndAlso Pids.SequenceEqual(ProcessPids) Then Return
        ProcessPids = Pids

        FillingProcessList = True
        Try
            ComboProcess.Items.Clear()
            ComboProcess.Items.Add(New MyComboBoxItem With {
                .Content = "自动（最近启动的那个）", .Tag = 0})
            If Jvms IsNot Nothing Then
                For Each Jvm In Jvms
                    ComboProcess.Items.Add(New MyComboBoxItem With {
                        .Content = ModOnlineMode.DescribeJvm(Jvm), .Tag = Jvm.Pid})
                Next
            End If
            If Jvms Is Nothing OrElse Jvms.Count = 0 Then
                ComboProcess.Items.Add(New MyComboBoxItem With {
                    .Content = "还没有启动器启动的游戏", .Tag = 0, .IsEnabled = False})
            End If
            '还原当前选择；指定的进程已经退出了就回到「自动」
            Dim Want As Integer = ModOnlineMode.PinnedPid
            Dim Index As Integer = 0
            For i = 0 To ComboProcess.Items.Count - 1
                Dim Item As MyComboBoxItem = TryCast(ComboProcess.Items(i), MyComboBoxItem)
                If Item IsNot Nothing AndAlso CInt(Item.Tag) = Want Then
                    Index = i
                    Exit For
                End If
            Next
            ComboProcess.SelectedIndex = Index
        Finally
            FillingProcessList = False
        End Try
        UpdateProcessLabel()
        '点「刷新」= 重新找一遍进程并顺手对齐端口（开局域网晚于进页面时用得上）
        If Manual Then FillPortFromProcess(ModOnlineMode.PinnedPid)
    End Sub

    Private Sub OnProcessPicked()
        If FillingProcessList OrElse ComboProcess Is Nothing Then Return
        Dim Item As MyComboBoxItem = TryCast(ComboProcess.SelectedItem, MyComboBoxItem)
        Dim Pid As Integer = If(Item Is Nothing, 0, CInt(Item.Tag))
        ModOnlineMode.Pin(Pid)
        UpdateProcessLabel()
        '选完进程顺手把它的局域网端口填上 —— 原版端口是随机的，不该由人去聊天栏抄
        FillPortFromProcess(Pid)
        '立刻按新进程重挂一次，进服校验不用等下一拍
        ModOnlineMode.Mount(Me)
    End Sub

    ''' <summary>
    ''' 把「本地端口」填成该进程正在监听的局域网端口。
    '''
    ''' 两个来源都会碰到：集成服务端（LAN 世界）和独立服务端。
    ''' 进程还没开局域网时 LanPortOf 返回 0 —— 这时只提示，不动用户已经填好的值。
    ''' netstat 要几百毫秒，放后台，回来再改界面。
    ''' </summary>
    Private Sub FillPortFromProcess(Pid As Integer)
        If TxtLocalPort Is Nothing Then Return
        RunInNewThread(
        Sub()
            Dim Target As Integer = Pid
            If Target <= 0 Then
                Dim Jvm As ModOnlineMode.ServerJvm = ModOnlineMode.FindServerJvm(McInstanceSelected)
                If Jvm IsNot Nothing Then Target = Jvm.Pid
            End If
            If Target <= 0 Then Return
            Dim Port As Integer = ModOnlineMode.LanPortOf(Target)
            RunInUi(
            Sub()
                If Port <= 0 Then
                    Hint($"PID {Target} 现在没有对局域网开放，端口还没法自动填。" & vbCrLf &
                         "在游戏里按 ESC →「对局域网开放」之后再选一次这个进程即可。", HintType.Blue)
                    Return
                End If
                TxtLocalPort.Text = Port.ToString()
                Hint($"已按 PID {Target} 的局域网端口填好本地端口：{Port}", HintType.Green)
            End Sub)
        End Sub, "RoomAutoPort")
    End Sub

    Private Sub UpdateProcessLabel()
        If LabProcess Is Nothing Then Return
        If ModOnlineMode.PinnedPid > 0 Then
            LabProcess.Text = "已指定 PID " & ModOnlineMode.PinnedPid & "：房间与进服校验都按这台走。"
        ElseIf ProcessPids.Count = 0 Then
            LabProcess.Text = "还没有启动器启动的游戏。外部启动的游戏不能开房间 —— 中继只转发启动器自己的进程。"
        Else
            LabProcess.Text = "自动：按最近启动的那个进程（只列启动器启动的游戏）。"
        End If
    End Sub

#End Region

    ''' <summary>
    ''' 校内节点每天 22:55 关机、次日 09:00 开机。这段窗口内一律走公网，
    ''' 否则房间会注册到一个连不上的入口，访客只会卡在「正在连接」。
    ''' </summary>
    Private Shared ReadOnly CampusDownStart As TimeSpan = New TimeSpan(22, 55, 0)
    Private Shared ReadOnly CampusDownEnd As TimeSpan = New TimeSpan(9, 0, 0)

    Private Shared Function CampusClosedNow() As Boolean
        Dim Now2 As TimeSpan = Date.Now.TimeOfDay
        '时间窗跨零点：22:55 — 次日 09:00
        Return Now2 >= CampusDownStart OrElse Now2 < CampusDownEnd
    End Function

    ''' <summary>
    ''' 按 campusOnly 查找节点；校内取第一个，公网在多个节点中选延迟最低的可达节点。
    ''' 公网节点全不可达时仍返回第一个，交由中控与 frpc 报出具体错误。
    ''' </summary>
    Private Function FindNode(CampusOnly As Boolean) As JObject
        If RelayNodes Is Nothing Then Return Nothing
        Dim First As JObject = Nothing
        Dim Best As JObject = Nothing
        Dim BestMs As Long = Long.MaxValue
        For Each Token As JToken In RelayNodes
            Dim Node As JObject = TryCast(Token, JObject)
            If Node Is Nothing Then Continue For
            If Node("campusOnly")?.ToObject(Of Boolean)() <> CampusOnly Then Continue For
            If First Is Nothing Then First = Node
            If CampusOnly Then Return Node
            Dim Watch As Stopwatch = Stopwatch.StartNew()
            If NodeReachable(Node) Then
                Watch.Stop()
                If Watch.ElapsedMilliseconds < BestMs Then
                    BestMs = Watch.ElapsedMilliseconds
                    Best = Node
                End If
            End If
        Next
        Return If(Best, First)
    End Function

    ''' <summary>
    ''' 测试节点控制端口是否可达（用于决定走校内还是公网）。
    ''' </summary>
    Private Function NodeReachable(Node As JObject) As Boolean
        If Node Is Nothing Then Return False
        Return TcpReachable(Node("host")?.ToString(), If(Node("port")?.ToObject(Of Integer)(), 7000))
    End Function

    Private Function EntryReachable(Entry As JObject) As Boolean
        If Entry Is Nothing Then Return False
        Return TcpReachable(Entry("host")?.ToString(), If(Entry("port")?.ToObject(Of Integer)(), 0))
    End Function

    Private Function TcpReachable(HostValue As String, PortValue As Integer) As Boolean
        If String.IsNullOrWhiteSpace(HostValue) OrElse PortValue <= 0 Then Return False
        Try
            Using Client As New System.Net.Sockets.TcpClient()
                Dim Connecting As System.Threading.Tasks.Task = Client.ConnectAsync(HostValue, PortValue)
                Return Connecting.Wait(2000) AndAlso Client.Connected
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 中继隧道没起来时的红字提示。
    '''
    ''' frpc 绑定远端端口失败时不会退出（进程活着、退出码 0），只在日志里留一行
    ''' `start error: port already used`，界面上的「已开启公网房间」照常亮着 ——
    ''' 于是所有人都在客户端那头看「Connecting to …」，看不出问题在房主这边。
    ''' </summary>
    Private Function BuildRelayFaultNote() As FrameworkElement
        If ModSocial.RelayFault = "" Then Return Nothing
        Dim Reason As String = ModSocial.RelayFault
        '只留冒号后面那段原因，前面是 frpc 的日志前缀和代理名
        Dim Mark As String = "start error:"
        Dim At As Integer = Reason.IndexOf(Mark)
        If At >= 0 Then Reason = Reason.Substring(At + Mark.Length).Trim()
        If Reason.StartsWith("[") AndAlso Reason.Contains("]") Then
            Reason = Reason.Substring(Reason.IndexOf("]") + 1).Trim()
        End If
        Dim Panel As New StackPanel With {.Margin = New Thickness(0, 6, 0, 0)}
        Panel.Children.Add(New TextBlock With {
            .Text = "公网入口没起来：" & If(Reason = "", "frpc 还没绑上远端端口", Reason),
            .FontSize = 11, .Foreground = SocialBrush(SocialRed), .TextWrapping = TextWrapping.Wrap})
        Panel.Children.Add(New TextBlock With {
            .Text = "最常见的原因是还有一个旧的 frpc 在跑（任务管理器搜 frpc.exe，多半是以前测试包留下的）。" &
                    vbCrLf & "把它结束后不用重建房间，这条提示会在半分钟内自己消失。",
            .FontSize = 11, .Opacity = 0.55, .Margin = New Thickness(0, 2, 0, 0),
            .TextWrapping = TextWrapping.Wrap})
        Return Panel
    End Function

    ''' <summary>
    ''' 构建一套「开启公网房间」拉杆，表单与房间卡片共用同一状态。
    ''' 外观直接取自 SocialSwitch —— 与「进服校验」那根拉杆是同一份实现。
    ''' </summary>
    Private Function BuildPublicSwitch(ByRef Track As Border, ByRef Knob As Border, ByRef State As TextBlock) As FrameworkElement
        Dim Switch As New SocialSwitch("开启公网房间", "已关闭：仅校园内网入口")
        Track = Switch.Track
        Knob = Switch.Knob
        State = Switch.StateText
        AddHandler Track.MouseLeftButtonUp, Sub() TogglePublicRoom()
        UpdatePublicButton()
        Return Switch
    End Function

    Private Sub UpdatePublicButton()
        Dim Enable As Boolean = PublicRoomEnabled
        ApplyPublicSwitch(FormPublicTrack, FormPublicKnob, FormPublicState, Enable)
        ApplyPublicSwitch(CardPublicTrack, CardPublicKnob, CardPublicState, Enable)
    End Sub

    Private Sub ApplyPublicSwitch(Track As Border, Knob As Border, State As TextBlock, Enable As Boolean)
        If Track Is Nothing Then Return
        Track.Background = CType(FindResource(If(Enable, "ColorBrush3", "ColorBrush7")),
                                 System.Windows.Media.Brush)
        Track.IsEnabled = True
        If Knob IsNot Nothing Then
            Knob.HorizontalAlignment = If(Enable, HorizontalAlignment.Right, HorizontalAlignment.Left)
            Knob.Margin = If(Enable, New Thickness(0, 0, 3, 0), New Thickness(3, 0, 0, 0))
        End If
        If State IsNot Nothing Then
            If CampusClosedNow() Then
                State.Text = "校内节点已关机，建房强制走公网"
            Else
                State.Text = If(Enable, "已开启：同时注册公网入口", "已关闭：仅校园内网入口")
            End If
        End If
    End Sub

    ''' <summary>
    ''' 建房前只切换标记；已有房间时直接增删公网入口。
    ''' </summary>
    Private Sub TogglePublicRoom()
        If String.IsNullOrWhiteSpace(MyRoomCode) Then
            PublicRoomEnabled = Not PublicRoomEnabled
            UpdatePublicButton()
            Return
        End If
        Dim PublicNode As JObject = FindNode(False)
        If PublicNode Is Nothing Then
            Hint("中控没有配置公网节点", HintType.Red)
            Return
        End If
        '校内节点关机期间不许关公网：关掉之后房间就没有任何可用入口
        If PublicRoomEnabled AndAlso CampusClosedNow() Then
            Hint("校内节点 22:55 起关机，现在关闭公网会让房间没有可用入口，请明早 9 点后再关", HintType.Red)
            Return
        End If
        If FormPublicTrack IsNot Nothing Then FormPublicTrack.IsEnabled = False
        If CardPublicTrack IsNot Nothing Then CardPublicTrack.IsEnabled = False
        Dim Code As String = MyRoomCode
        Dim NodeId As String = PublicNode("id").ToString()
        Dim Enabling As Boolean = Not PublicRoomEnabled
        RunInNewThread(
        Sub()
            Try
                If Enabling Then
                    ModSocial.EnablePublic(Code, NodeId)
                Else
                    '中控要求至少保留一个入口；房间只注册了公网节点时，
                    '先补一个校内入口再删公网，否则会拿到 400。
                    Dim Campus As JObject = FindNode(True)
                    If Campus IsNot Nothing AndAlso NodeReachable(Campus) Then
                        Try
                            ModSocial.EnablePublic(Code, Campus("id").ToString())
                        Catch ex As Exception
                            Logger.Warn(ex, "补校内入口失败，继续尝试关闭公网节点")
                        End Try
                    End If
                    ModSocial.DisablePublic(Code, NodeId)
                End If
                RunInUi(
                Sub()
                    PublicRoomEnabled = Enabling
                    UpdatePublicButton()
                    Hint(If(Enabling, "已开启公网房间", "已关闭公网房间，仅保留校园内网"), HintType.Green)
                    RefreshAll()
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    UpdatePublicButton()
                    Hint("切换公网房间失败：" & ex.Message, HintType.Red)
                End Sub)
            End Try
        End Sub, "SocialPublic")
    End Sub

#End Region
#Region "刷新"

    Private Sub RefreshAll()
        If Refreshing Then Return
        Refreshing = True
        LabStatus.Text = "正在连接服务器……"
        RunInNewThread(
        Sub()
            Try
                ModSocial.EnsureLogin()
                Dim Relays As JObject = ModSocial.RelayList()
                Dim Rooms As JArray = ModSocial.RoomList()
                '好友列表只用于「好友房间」分组，失败不影响房间列表展示
                Dim Friends As New HashSet(Of String)(StringComparer.Ordinal)
                Try
                    Dim FriendData As JObject = ModSocial.FriendList()
                    Dim FriendArray As JArray = TryCast(FriendData("friends"), JArray)
                    If FriendArray IsNot Nothing Then
                        For Each Token As JToken In FriendArray
                            Dim Id As String = Token?("accountId")?.ToString()
                            If Not String.IsNullOrWhiteSpace(Id) Then Friends.Add(Id)
                        Next
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "加载好友列表失败，房间暂按其他房间显示")
                End Try
                RunInUi(
                Sub()
                    RelayNodes = TryCast(Relays("relays"), JArray)
                    UpdatePublicButton()
                    FriendIds = Friends
                    RoomsCache = Rooms
                    RenderRooms()
                    '房间还在但本地中继进程没了时给出明确提示，避免房主以为还能连
                    LabStatus.Text = If(MyRoomCode <> "" AndAlso Not ModSocial.FrpcAlive,
                                        "联机组件已断开，访客无法加入，请关闭房间后重新创建",
                                        "已连接服务器")
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "加载房间失败")
                RunInUi(Sub() LabStatus.Text = ModSocial.FriendlyError(ex))
            Finally
                Refreshing = False
            End Try
        End Sub, "SocialRooms")
    End Sub

    Private Sub RenderRooms()
        PanMyRoomCard.Children.Clear()
        PanFriendRoomList.Children.Clear()
        PanOtherRoomList.Children.Clear()
        Dim Rooms As JArray = If(RoomsCache, New JArray())
        Dim Mine As JObject = Nothing
        Dim Others As New List(Of JObject)
        For Each Item As JToken In Rooms
            Dim Room As JObject = TryCast(Item, JObject)
            If Room Is Nothing Then Continue For
            If IsMyRoom(Room) Then
                Mine = Room
                MyRoomCode = Room("code")?.ToString()
                PublicRoomEnabled = HasPublicEntry(Room)
                UpdatePublicButton()
            Else
                Others.Add(Room)
            End If
        Next

        '我的房间已被回收：清掉本地密码与房间号，避免继续显示可切换公网
        If Mine Is Nothing AndAlso MyRoomCode <> "" Then
            MyRoomCode = ""
            HostPassword = ""
            UpdatePublicButton()
            Hint("房间已被服务器回收，访客已无法加入，请重新创建", HintType.Red)
        End If
        '已加入的房间已结束：自动退出，清空进服地址并停止上报
        If JoinedRoomCode <> "" Then
            Dim StillOpen As Boolean = False
            For Each Room As JObject In Others
                If Room("code")?.ToString() = JoinedRoomCode Then
                    StillOpen = True
                    Exit For
                End If
            Next
            If Not StillOpen Then
                JoinedRoomCode = ""
                JoinedPassword = ""
                ModSocial.SetJoinerServer("")
                RunInNewThread(Sub() ModSocial.StopPresence(), "SocialLeave")
                '房主关房或改成不公开后，房间里的人不会收到推送，只能靠这里的轮询发现
                Hint("房主已关闭房间或改为不公开，已自动退出", HintType.Red)
            End If
        End If

        PanCreateForm.Visibility = If(Mine Is Nothing, Visibility.Visible, Visibility.Collapsed)
        If Mine IsNot Nothing Then PanMyRoomCard.Children.Add(BuildMyRoomCard(Mine))
        '表单刚显示或收起，进服校验拉杆要跟着换位置（表单里 → 页面末尾，或反过来）
        ModOnlineMode.Mount(Me)
        '进程列表跟着刷新（列表没变化时不会重建，选择不会丢）
        RefreshProcessList(False)

        '房间分组：房主是好友的进「好友房间」，其余进「其他房间」
        Dim FriendRooms As New List(Of JObject)
        Dim OtherRooms As New List(Of JObject)
        For Each Room As JObject In Others
            If FriendIds.Contains(If(Room("ownerId")?.ToString(), "")) Then
                FriendRooms.Add(Room)
            Else
                OtherRooms.Add(Room)
            End If
        Next
        PanFriendRoomList.Children.Clear()
        LabFriendRooms.Visibility = If(FriendRooms.Count = 0, Visibility.Collapsed, Visibility.Visible)
        PanFriendRoomList.Visibility = LabFriendRooms.Visibility
        For Each Room As JObject In FriendRooms
            PanFriendRoomList.Children.Add(BuildOtherRoomRow(Room))
        Next

        LabOtherRooms.Visibility = If(OtherRooms.Count = 0, Visibility.Collapsed, Visibility.Visible)
        PanOtherRoomList.Visibility = LabOtherRooms.Visibility
        For Each Room As JObject In OtherRooms
            PanOtherRoomList.Children.Add(BuildOtherRoomRow(Room))
        Next
        '延迟测量：自己房间与其他房间的**每个入口**各量一次。
        '旧实现只量 RoomAddress（公网优先），于是校内那个入口从来没有延迟数据，
        '新加的「校内」行就永远是空的。同一地址只量一次，量过的进缓存不再重复。
        Dim PendingAddresses As New List(Of String)
        Dim PendingHosts As New List(Of String)
        Dim PendingPorts As New List(Of Integer)
        Dim Scopes As New List(Of JObject)
        If Mine IsNot Nothing Then Scopes.Add(Mine)
        For Each Room As JObject In Others
            Scopes.Add(Room)
        Next
        For Each Room As JObject In Scopes
            If Room("relay")?.ToString() = "p2p" Then Continue For
            Dim Queued As Boolean = False
            Dim Endpoints As JArray = TryCast(Room("endpoints"), JArray)
            If Endpoints IsNot Nothing Then
                For Each Token As JToken In Endpoints
                    Dim Entry As JObject = TryCast(Token, JObject)
                    If Entry Is Nothing Then Continue For
                    Dim HostValue As String = If(Entry("host")?.ToString(), "")
                    Dim PortValue As Integer = If(Entry("port")?.ToObject(Of Integer)(), 0)
                    If HostValue = "" OrElse PortValue <= 0 Then Continue For
                    Dim Addr As String = HostValue & ":" & PortValue
                    If LatencyCache.ContainsKey(Addr) OrElse PendingAddresses.Contains(Addr) Then
                        Queued = True
                        Continue For
                    End If
                    PendingAddresses.Add(Addr)
                    PendingHosts.Add(HostValue)
                    PendingPorts.Add(PortValue)
                    Queued = True
                Next
            End If
            '旧版中控或非成员只给 host/port，没有 endpoints
            If Not Queued Then
                Dim HostValue As String = If(Room("host")?.ToString(), "")
                Dim PortValue As Integer = If(Room("port")?.ToObject(Of Integer)(), 0)
                If HostValue = "" OrElse PortValue <= 0 Then Continue For
                Dim Addr As String = HostValue & ":" & PortValue
                If LatencyCache.ContainsKey(Addr) OrElse PendingAddresses.Contains(Addr) Then Continue For
                PendingAddresses.Add(Addr)
                PendingHosts.Add(HostValue)
                PendingPorts.Add(PortValue)
            End If
        Next
        If PendingHosts.Count = 0 Then Return
        RunInNewThread(
        Sub()
            For Index As Integer = 0 To PendingHosts.Count - 1
                Try
                    LatencyCache(PendingAddresses(Index)) = ModSocial.MeasureRelay(
                        PendingHosts(Index), PendingPorts(Index))
                Catch ex As Exception
                    '量失败也要落缓存，否则每次刷新都会拿同一批地址重测，界面一直转
                    LatencyCache(PendingAddresses(Index)) = -1
                    Logger.Warn(ex, "测量入口延迟失败：" & PendingAddresses(Index))
                End Try
            Next
            RunInUi(Sub() RenderRooms())
        End Sub, "SocialLatency")
    End Sub

    Private Function BuildMyRoomCard(Item As JObject) As FrameworkElement
        Dim IsP2p As Boolean = Item("relay")?.ToString() = "p2p"
        Dim Address As String = If(IsP2p, "127.0.0.1:" & If(Item("gamePort")?.ToString(), ""),
                                   FirstAddress(Item))
        Dim Card As Border = SocialCard()

        Dim Layout As New StackPanel
        Dim Head As New Grid
        Head.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Head.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Head.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Avatar As FrameworkElement = SocialAvatar(Item("ownerAvatar")?.ToString(), 44, Item("ownerName")?.ToString())
        Avatar.Margin = New Thickness(0, 0, 12, 0)
        Grid.SetColumn(Avatar, 0)
        Head.Children.Add(Avatar)

        Dim Info As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        '节点名与上面展示的地址保持一致（公网入口优先）
        Dim NodeText As String = If(FirstEntry(Item)?("name")?.ToString(), If(Item("nodeName")?.ToString(), ""))
        Info.Children.Add(New TextBlock With {
            .Text = If(Item("name")?.ToString(), "未命名房间"), .FontSize = 15, .FontWeight = FontWeights.SemiBold})
        Dim Headline As String = $"房间号 {Item("code")}"
        If NodeText <> "" Then Headline &= $" ｜ 节点 {NodeText}"
        Info.Children.Add(New TextBlock With {
            .Text = Headline,
            .FontSize = 11, .Opacity = 0.6, .Margin = New Thickness(0, 3, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis})
        '入口逐个列出：同时开着公网与校内时两个都在，各自可复制
        If IsP2p Then
            Info.Children.Add(ModSocialUi.SocialAddressRow("本机", Address, SocialBlue, "P2P 直连"))
        Else
            Info.Children.Add(BuildAddressPanel(Item))
        End If
        '房主自己设的密码只在本机保存，方便随时复制发给好友
        If HostPassword <> "" Then
            Dim PwdLine As New StackPanel With {
                .Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 5, 0, 0)}
            PwdLine.Children.Add(New TextBlock With {
                .Text = "房间密码：" & HostPassword, .FontSize = 11, .Opacity = 0.6,
                .VerticalAlignment = VerticalAlignment.Center})
            Dim BtnCopyPwd As New MyButton With {
                .Text = "复制密码", .Height = 22, .MinWidth = 72, .Margin = New Thickness(8, 0, 0, 0)}
            AddHandler BtnCopyPwd.Click,
                Sub()
                Try
                    Clipboard.SetText(HostPassword)
                    Hint("房间密码已复制", HintType.Green)
                Catch ex As Exception
                    Hint("复制失败：剪贴板被其他程序占用", HintType.Red)
                End Try
                End Sub
            PwdLine.Children.Add(BtnCopyPwd)
            Info.Children.Add(PwdLine)
        End If
        Grid.SetColumn(Info, 1)
        Head.Children.Add(Info)

        Dim Actions As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        '不再放「复制地址」：入口已经按 校内 / 公网 逐行列出，每行自带复制按钮，
        '再放一个大按钮反而是第二个入口、还得解释它复制的是哪一个
        Dim BtnLaunch As New MyButton With {
            .Text = "启动并进入", .Height = 26, .MinWidth = 96,
            .ColorType = MyButton.ColorState.Highlight}
        AddHandler BtnLaunch.Click, Sub() LaunchRoom(Address)
        Dim BtnClose As New MyButton With {
            .Text = "关闭房间", .Height = 26, .MinWidth = 84, .Margin = New Thickness(8, 0, 0, 0)}
        AddHandler BtnClose.Click, Sub() CloseRoomWithConfirm(Item, BtnClose)
        Actions.Children.Add(BtnLaunch)
        Actions.Children.Add(BtnClose)
        Grid.SetColumn(Actions, 2)
        Head.Children.Add(Actions)
        Layout.Children.Add(Head)

        '房间已建好后也能随时开关公网入口
        Layout.Children.Add(BuildPublicSwitch(CardPublicTrack, CardPublicKnob, CardPublicState))
        'frpc 起不来时界面必须说话：它不退出、只写日志，「已开启公网房间」会骗人
        Dim RelayNote As FrameworkElement = BuildRelayFaultNote()
        If RelayNote IsNot Nothing Then Layout.Children.Add(RelayNote)
        '进服校验的落点：房间建好后表单收起，拉杆跟着到卡片里这根公网拉杆下面
        Dim PanMode As New StackPanel With {.Name = "PanOnlineModeCard"}
        Layout.Children.Add(PanMode)
        Layout.Children.Add(BuildMemberSection(Item))
        Card.Child = Layout
        Return Card
    End Function

    ''' <summary>
    ''' 房间成员：与网站「游玩动态排名」一致，序号 + 头像 + 名字 + 状态。
    ''' </summary>
    Private Function BuildMemberSection(Item As JObject) As FrameworkElement
        Dim Panel As New StackPanel With {.Margin = New Thickness(0, 12, 0, 0)}
        Dim Members As JArray = TryCast(Item("members"), JArray)
        Panel.Children.Add(SocialLabel($"房间成员（{If(Members Is Nothing, 0, Members.Count)}）"))
        If Members Is Nothing OrElse Members.Count = 0 Then
            Panel.Children.Add(New TextBlock With {
                .Text = "暂无成员。把房间号发给好友，对方点「加入房间」后会显示在这里。",
                .FontSize = 11, .Opacity = 0.45})
            Return Panel
        End If
        Dim Index As Integer = 0
        Dim OwnerId As String = If(Item("ownerId")?.ToString(), "")
        For Each Member As JToken In Members
            Index += 1
            Panel.Children.Add(BuildMemberRow(Index, TryCast(Member, JObject), OwnerId))
        Next
        Return Panel
    End Function

    ''' <summary>进入方式 → 语义色：校内绿、P2P 蓝、公网灰。</summary>
    Private Function ViaColor(Via As String) As Color
        Select Case Via
            Case "campus"
                Return SocialGreen
            Case "p2p"
                Return SocialBlue
            Case Else
                Return SocialGray
        End Select
    End Function

    Private Function BuildMemberRow(Index As Integer, Member As JObject, OwnerId As String) As FrameworkElement
        If Member Is Nothing Then Return New Grid()
        Dim Row As New Grid With {.Height = 36, .Margin = New Thickness(0, 0, 0, 2)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(30)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Rank As New TextBlock With {
            .Text = Index.ToString("00"), .FontSize = 11, .Opacity = 0.35,
            .VerticalAlignment = VerticalAlignment.Center, .FontFamily = New FontFamily("Consolas")}
        Grid.SetColumn(Rank, 0)
        Row.Children.Add(Rank)
        Dim Name As String = If(Member("name")?.ToString(), "")
        Dim Avatar As FrameworkElement = SocialAvatar(Member("avatarUrl")?.ToString(), 26, Name)
        Avatar.Margin = New Thickness(0, 0, 9, 0)
        Grid.SetColumn(Avatar, 1)
        Row.Children.Add(Avatar)
        Dim Label As New TextBlock With {
            .Text = Name, .FontSize = 13, .VerticalAlignment = VerticalAlignment.Center,
            .TextTrimming = TextTrimming.CharacterEllipsis}
        Grid.SetColumn(Label, 2)
        Row.Children.Add(Label)
        '右侧：进入方式（房主固定显示「房主」）+ 在线状态
        Dim Right As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Dim AccountId As String = If(Member("accountId")?.ToString(), "")
        Dim Via As String = If(Member("via")?.ToString(), "")
        Dim ViaName As String = If(Member("viaName")?.ToString(), "")
        If AccountId <> "" AndAlso AccountId = OwnerId Then
            Dim OwnerBadge As Border = SocialBadge("房主", SocialBlue)
            OwnerBadge.Margin = New Thickness(0)
            Right.Children.Add(OwnerBadge)
        ElseIf ViaName <> "" Then
            Dim ViaBadge As Border = SocialBadge(ViaName, ViaColor(Via))
            ViaBadge.Margin = New Thickness(0)
            Right.Children.Add(ViaBadge)
        End If
        Dim Online As Boolean = Member("online")?.ToObject(Of Boolean)()
        Right.Children.Add(New TextBlock With {
            .Text = If(Online, "游戏中", "已加入"),
            .FontSize = 11, .Margin = New Thickness(8, 0, 0, 0),
            .Foreground = SocialBrush(If(Online, SocialGreen, SocialGray)),
            .VerticalAlignment = VerticalAlignment.Center})
        Grid.SetColumn(Right, 3)
        Row.Children.Add(Right)
        Return Row
    End Function

    Private Function BuildOtherRoomRow(Item As JObject) As FrameworkElement
        Dim IsP2p As Boolean = Item("relay")?.ToString() = "p2p"
        Dim Address As String = If(IsP2p, "P2P 打洞", RoomAddress(Item))
        Dim Code As String = Item("code").ToString()
        '已加入的房间服务端可能不再下发入口，用本地记录的中转地址补上
        If Address = "" AndAlso JoinedRoomCode <> "" AndAlso JoinedRoomCode = Code Then
            Address = ModSocial.JoinerServer
        End If
        Dim Members As JArray = TryCast(Item("members"), JArray)
        Dim Count As Integer = If(Members Is Nothing, 0, Members.Count)
        Dim Expanded As Boolean = (ExpandedRoom = Code)

        Dim Card As Border = SocialCard()
        Card.Padding = New Thickness(12, 10, 12, 10)
        Card.Margin = New Thickness(0, 0, 0, 6)
        Card.Cursor = Cursors.Hand

        Dim Layout As New StackPanel
        Dim Head As New Grid
        Head.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Head.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Head.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Avatar As FrameworkElement = SocialAvatar(Item("ownerAvatar")?.ToString(), 36, Item("ownerName")?.ToString())
        Avatar.Margin = New Thickness(0, 0, 10, 0)
        Grid.SetColumn(Avatar, 0)
        Head.Children.Add(Avatar)

        Dim Info As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Info.Children.Add(New TextBlock With {
            .Text = Item("name").ToString(), .FontSize = 14, .FontWeight = FontWeights.SemiBold})
        Dim Joined As Boolean = JoinedRoomCode <> "" AndAlso JoinedRoomCode = Code
        Dim HasPwd As Boolean = Item("hasPassword")?.ToObject(Of Boolean)() = True
        Dim Tail As String = If(HasPwd, " ｜ 需要密码", "") & If(Joined, " ｜ 已加入", "")
        '地址只给房主与房间内成员；拿不到时不要显示「测量中」这种假状态
        Dim LinkText As String = If(IsP2p, "P2P 打洞",
                                    If(String.IsNullOrWhiteSpace(Address), "加入后可见延迟",
                                       LatencyText(Address)))
        Info.Children.Add(New TextBlock With {
            .Text = $"房主 {Item("ownerName")} ｜ {Count} 人在线 ｜ {LinkText}" & Tail,
            .FontSize = 11, .Opacity = 0.6, .Margin = New Thickness(0, 3, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis})
        Grid.SetColumn(Info, 1)
        Head.Children.Add(Info)

        Dim Actions As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Dim BtnJoin As New MyButton With {
            .Text = If(Joined, "已加入", "加入"), .Height = 26, .MinWidth = 72}
        BtnJoin.IsEnabled = Not Joined
        If Not Joined Then BtnJoin.ColorType = MyButton.ColorState.Highlight
        AddHandler BtnJoin.Click, Sub() JoinRoom(Item)
        Actions.Children.Add(BtnJoin)
        Grid.SetColumn(Actions, 2)
        Head.Children.Add(Actions)
        Layout.Children.Add(Head)

        If Expanded Then
            Layout.Children.Add(BuildMemberSection(Item))
            '非成员拿不到入口地址，明确写成「加入后可见」而不是留空。
            '成员（已加入）这边按入口逐行列出并带复制按钮 —— 以前只有一行纯文本，
            '想抄个地址得自己选中，手机上看还要长按。
            Layout.Children.Add(SocialLabel("房间号：" & Code))
            If String.IsNullOrWhiteSpace(Address) Then
                Layout.Children.Add(SocialLabel("地址：加入后可见"))
            Else
                Layout.Children.Add(BuildAddressPanel(Item))
            End If
            If Joined Then
                Dim BtnLeave As New MyButton With {
                    .Text = "退出房间", .Height = 26, .MinWidth = 88, .Margin = New Thickness(0, 8, 0, 0)}
                AddHandler BtnLeave.Click, Sub() LeaveJoinedRoom()
                Layout.Children.Add(BtnLeave)
            End If
        End If
        Card.Child = Layout

        AddHandler Card.MouseLeftButtonUp,
            Sub(sender As Object, e As MouseButtonEventArgs)
                Dim Node As DependencyObject = TryCast(e.OriginalSource, DependencyObject)
                While Node IsNot Nothing
                    If TypeOf Node Is MyButton Then Return
                    '地址行自带复制行为，点它不该顺手把卡片收起
                    Dim Element As FrameworkElement = TryCast(Node, FrameworkElement)
                    If Element IsNot Nothing AndAlso TryCast(Element.Tag, String) = ModSocialUi.CopyRegionTag Then Return
                    Node = VisualTreeHelper.GetParent(Node)
                End While
                ExpandedRoom = If(ExpandedRoom = Code, "", Code)
                RenderRooms()
            End Sub
        Return Card
    End Function

    ''' <summary>
    ''' 房间探活地址：优先公网入口（校外视角），否则第一个入口。
    ''' </summary>
    Private Function RoomAddress(Room As JObject) As String
        Dim Endpoints As JArray = TryCast(Room("endpoints"), JArray)
        Dim Fallback As String = ""
        If Endpoints IsNot Nothing Then
            For Each Token As JToken In Endpoints
                Dim Entry As JObject = TryCast(Token, JObject)
                If Entry Is Nothing Then Continue For
                Dim Address As String = Entry("host").ToString() & ":" & Entry("port").ToString()
                If Entry("campusOnly")?.ToObject(Of Boolean)() = False Then Return Address
                If Fallback = "" Then Fallback = Address
            Next
            If Fallback <> "" Then Return Fallback
        End If
        '服务端对非成员不下发 host/port，这时不要拼出「:0」
        Dim Host As String = If(Room("host")?.ToString(), "")
        Dim PortText As String = If(Room("port")?.ToString(), "0")
        If Host = "" OrElse PortText = "0" Then Return ""
        Return Host & ":" & PortText
    End Function

    ''' <summary>
    ''' 从 endpoints 里取出第一个校内入口与第一个公网入口。
    ''' 取不到就都是空串，由调用方决定退路。
    ''' </summary>
    Private Sub SplitEndpoints(Room As JObject, ByRef Campus As String, ByRef PublicAddr As String)
        Campus = ""
        PublicAddr = ""
        If Room Is Nothing Then Return
        Dim Endpoints As JArray = TryCast(Room("endpoints"), JArray)
        If Endpoints Is Nothing Then Return
        For Each Token As JToken In Endpoints
            Dim Entry As JObject = TryCast(Token, JObject)
            If Entry Is Nothing Then Continue For
            Dim HostValue As String = If(Entry("host")?.ToString(), "")
            Dim PortValue As String = If(Entry("port")?.ToString(), "")
            If HostValue = "" OrElse PortValue = "" Then Continue For
            Dim Addr As String = HostValue & ":" & PortValue
            If Entry("campusOnly")?.ToObject(Of Boolean)() = True Then
                If Campus = "" Then Campus = Addr
            Else
                If PublicAddr = "" Then PublicAddr = Addr
            End If
        Next
    End Sub

    ''' <summary>
    ''' 一句话概括房间的所有入口，用于建房成功后的状态行与提示：
    ''' 「校内 10.1.2.3:25565 ｜ 公网 hp.…:30001」。
    ''' </summary>
    Private Function EndpointSummary(Room As JObject) As String
        Dim Campus As String = ""
        Dim PublicAddr As String = ""
        SplitEndpoints(Room, Campus, PublicAddr)
        Dim Parts As New List(Of String)
        If Campus <> "" Then Parts.Add("校内 " & Campus)
        If PublicAddr <> "" Then Parts.Add("公网 " & PublicAddr)
        If Parts.Count = 0 Then Return RoomAddress(Room)
        Return String.Join(" ｜ ", Parts)
    End Function

    ''' <summary>
    ''' 把房间的入口拆成「校内 / 公网」逐行列出，各自可复制。
    '''
    ''' 一个房间开启公网后会同时注册两个入口，而卡片上原来只拼一个字符串，
    ''' 公网优先的规则把校内那个吃掉了 —— 在校内的人反而看不到能直连的地址。
    ''' </summary>
    Private Function BuildAddressPanel(Room As JObject, Optional ShowLatency As Boolean = True) As FrameworkElement
        Dim Panel As New StackPanel
        Dim Campus As String = ""
        Dim PublicAddr As String = ""
        SplitEndpoints(Room, Campus, PublicAddr)
        If Campus = "" AndAlso PublicAddr = "" Then
            '服务端只给了单个 host/port（非成员、或旧版中控）
            Dim Addr As String = RoomAddress(Room)
            If Addr <> "" Then
                Panel.Children.Add(ModSocialUi.SocialAddressRow("地址", Addr, SocialGray,
                    If(ShowLatency, LatencyNote(Addr), "")))
            End If
            Return Panel
        End If
        If Campus <> "" Then
            Panel.Children.Add(ModSocialUi.SocialAddressRow("校内", Campus, SocialGreen,
                If(ShowLatency, LatencyNote(Campus), "")))
        End If
        If PublicAddr <> "" Then
            Panel.Children.Add(ModSocialUi.SocialAddressRow("公网", PublicAddr, SocialBlue,
                If(ShowLatency, LatencyNote(PublicAddr), "")))
        End If
        Return Panel
    End Function

    ''' <summary>地址备注：延迟只在量过之后显示，量不到就不写「测量中」这种假状态。</summary>
    Private Function LatencyNote(Address As String) As String
        If Address = "" OrElse Not LatencyCache.ContainsKey(Address) Then Return ""
        Dim Value As Integer = LatencyCache(Address)
        Return If(Value < 0, "不可达", Value & " ms")
    End Function

    Private Function LatencyText(Address As String) As String
        If Not LatencyCache.ContainsKey(Address) Then Return "延迟：测量中…"
        Dim Value As Integer = LatencyCache(Address)
        Return If(Value < 0, "延迟：不可达", $"延迟：{Value} ms")
    End Function

    Private Function IsMyRoom(Item As JObject) As Boolean
        Return ModSocial.MyAccountId <> "" AndAlso Item("ownerId")?.ToString() = ModSocial.MyAccountId
    End Function

    Private Sub CloseRoomWithConfirm(Item As JObject, Trigger As MyButton)
        Dim Code As String = Item("code").ToString()
        Dim RoomName As String = Item("name").ToString()
        If MyMsgBox($"确定关闭房间「{RoomName}」吗？{vbCrLf}关闭后房间号与中继端口会立即释放。",
                    "关闭房间", "关闭", "取消") <> 1 Then Return
        Trigger.IsEnabled = False
        Trigger.Text = "关闭中……"
        RunInNewThread(
        Sub()
            Try
                ModSocial.CloseRoom(Code)
                RunInUi(
                Sub()
                    MyRoomCode = ""
                    HostPassword = ""
                    PublicRoomEnabled = False
                    UpdatePublicButton()
                    ModSocial.SetJoinerServer("")
                    Hint("房间已关闭", HintType.Green)
                    RefreshAll()
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    Trigger.IsEnabled = True
                    Trigger.Text = "关闭房间"
                    Hint("关闭房间失败：" & ex.Message, HintType.Red)
                End Sub)
            End Try
        End Sub, "SocialCloseRoom")
    End Sub

    ''' <summary>从房间卡片加入：有密码时先询问，校验通过后才记录并展开。</summary>
    Private Sub JoinRoom(Item As JObject)
        Dim Code As String = Item("code").ToString()
        Dim Password As String = ""
        If Item("hasPassword")?.ToObject(Of Boolean)() = True Then
            Password = AskRoomPassword(Item("name")?.ToString(), Code)
            If Password Is Nothing Then Return
        End If
        DoJoinRoom(Code, Password)
    End Sub

    ''' <summary>弹出房间密码输入框；用户取消时返回 Nothing。</summary>
    Private Function AskRoomPassword(RoomName As String, Code As String) As String
        Dim Value As String = MyMsgBoxInput(
            "输入房间密码",
            $"「{RoomName}」需要密码才能加入（房间号 {Code}）。",
            HintText:="房间密码")
        If Value Is Nothing Then Return Nothing
        Value = Value.Trim()
        If Value = "" Then
            Hint("请输入房间密码", HintType.Red)
            Return Nothing
        End If
        Return Value
    End Function

    ''' <summary>实际加入：拿完整房间信息（含密钥）→ 协商地址 → 记录已加入状态。</summary>
    Private Sub DoJoinRoom(Code As String, Password As String)
        RunInNewThread(Sub() JoinAndEnter(Code, Password), "SocialJoin")
    End Sub

    ''' <summary>加入房间时选定的入口：地址 + 方式（campus / p2p / public）。</summary>
    Private Class JoinRoute
        Public ReadOnly Address As String
        Public ReadOnly Via As String
        Public Sub New(Address As String, Via As String)
            Me.Address = Address
            Me.Via = Via
        End Sub
    End Class

    ''' <summary>在后台线程完成加入并回到 UI 更新状态。</summary>
    Private Sub JoinAndEnter(Code As String, Password As String)
        Try
            Dim Room As JObject = ModSocial.JoinRoom(Code, Password)
            If Room Is Nothing Then Throw New Exception("房间不存在或已结束")
            Dim Route As JoinRoute = ChooseRoute(Code, Room)
            Dim Address As String = Route.Address
            '先同步上报一次在线状态，再回到 UI 刷新，避免成员列表要等下一轮定时刷新
            Try
                ModSocial.ReportPresenceOnce(Code, Password, Route.Via)
            Catch ex As Exception
                Logger.Warn(ex, "加入房间后上报在线状态失败")
            End Try
            RunInUi(
            Sub()
                Try
                    Clipboard.SetText(Address)
                Catch ex As Exception
                    Logger.Warn(ex, "加入房间后复制地址失败")
                End Try
                ModSocial.SetJoinerServer(Address, Code)
                '让房主端的成员列表能看到自己
                ModSocial.StartPresence(Code, Password, Route.Via)
                JoinedRoomCode = Code
                JoinedPassword = Password
                ExpandedRoom = Code
                LabStatus.Text = $"已加入「{Room("name")}」，地址 {Address}（启动游戏后直接进入）"
                Hint("已加入房间，启动游戏会直接进入，无需手动添加服务器", HintType.Green)
                RefreshAll()
            End Sub)
        Catch ex As Exception
            RunInUi(Sub() Hint("加入房间失败：" & ex.Message, HintType.Red))
        End Try
    End Sub

    ''' <summary>退出已加入的房间，清空进服地址并停止在线状态上报。</summary>
    Private Sub LeaveJoinedRoom()
        If JoinedRoomCode = "" Then Return
        Dim Code As String = JoinedRoomCode
        '只停定时器，离开请求在下面同步上报，退出后立刻刷新成员列表
        ModSocial.StopPresence(SendLeave:=False)
        ModSocial.SetJoinerServer("")
        JoinedRoomCode = ""
        JoinedPassword = ""
        LabStatus.Text = "正在退出房间……"
        RunInNewThread(
        Sub()
            Try
                ModSocial.RemovePresenceOnce(Code)
            Catch ex As Exception
                Logger.Warn(ex, "退出房间状态上报失败")
            End Try
            RunInUi(
            Sub()
                Hint("已退出房间", HintType.Green)
                RefreshAll()
            End Sub)
        End Sub, "SocialLeaveRoom")
    End Sub

    ''' <summary>
    ''' 自动协商：校内入口可达直接走校内；否则先尝试 P2P 打洞，仍不行再走公网中继，并记录对应方式。
    ''' </summary>
    Private Function ChooseRoute(Code As String, Room As JObject) As JoinRoute
        Dim CampusEntry As JObject = Nothing
        Dim PublicEntry As JObject = Nothing
        Dim Endpoints As JArray = TryCast(Room("endpoints"), JArray)
        If Endpoints IsNot Nothing Then
            For Each Token As JToken In Endpoints
                Dim Entry As JObject = TryCast(Token, JObject)
                If Entry Is Nothing Then Continue For
                If Entry("campusOnly")?.ToObject(Of Boolean)() = True Then
                    If CampusEntry Is Nothing Then CampusEntry = Entry
                Else
                    If PublicEntry Is Nothing Then PublicEntry = Entry
                End If
            Next
        End If
        '1. 校内入口可达：直接走校内，延迟最低
        If CampusEntry IsNot Nothing AndAlso EntryReachable(CampusEntry) Then
            Return New JoinRoute(CampusEntry("host").ToString() & ":" & CampusEntry("port").ToString(),
                                 "campus")
        End If
        '2. P2P 打洞：由公网入口协助
        If PublicEntry IsNot Nothing Then
            Try
                Dim XtcpCompat As JObject = TryCast(Room("xtcp"), JObject)
                Dim FrpToken As String = If(XtcpCompat IsNot Nothing,
                                           XtcpCompat("token")?.ToString(), "")
                Dim Xtcp As New JObject(
                    New JProperty("serverAddr", PublicEntry("host")),
                    New JProperty("serverPort", PublicEntry("nodePort")),
                    New JProperty("token", FrpToken),
                    New JProperty("proxyName", "njitmcl-" & Code),
                    New JProperty("stcpName", "njitmcl-" & Code & "-stcp"),
                    New JProperty("secretKey", PublicEntry("secret")))
                Return New JoinRoute(ModSocial.VisitRoom(Code, Xtcp), "p2p")
            Catch ex As Exception
                Logger.Warn(ex, "P2P 打洞失败，回退公网中继")
            End Try
            '3. 公网中继兜底
            Return New JoinRoute(PublicEntry("host").ToString() & ":" & PublicEntry("port").ToString(),
                                 "public")
        End If
        If CampusEntry IsNot Nothing Then
            Return New JoinRoute(CampusEntry("host").ToString() & ":" & CampusEntry("port").ToString(),
                                 "campus")
        End If
        '旧房间兼容
        Dim Legacy As JObject = TryCast(Room("xtcp"), JObject)
        If Legacy IsNot Nothing Then
            Try
                Return New JoinRoute(ModSocial.VisitRoom(Code, Legacy), "p2p")
            Catch ex As Exception
                Logger.Warn(ex, "P2P 打洞失败，回退中继地址")
            End Try
        End If
        Return New JoinRoute(Room("host").ToString() & ":" & Room("port").ToString(), "public")
    End Function

    ''' <summary>
    ''' 房间是否包含公网入口。
    ''' </summary>
    Private Function HasPublicEntry(Room As JObject) As Boolean
        Dim Endpoints As JArray = TryCast(Room("endpoints"), JArray)
        If Endpoints Is Nothing Then Return Room("relay")?.ToString() <> "campus"
        For Each Token As JToken In Endpoints
            Dim Entry As JObject = TryCast(Token, JObject)
            If Entry IsNot Nothing AndAlso Entry("campusOnly")?.ToObject(Of Boolean)() = False Then
                Return True
            End If
        Next
        Return False
    End Function

    ''' <summary>
    ''' 优先用于分享的入口：有公网入口时优先公网（校外好友也能加入），否则第一个入口。
    ''' </summary>
    Private Function FirstEntry(Room As JObject) As JObject
        Dim Endpoints As JArray = TryCast(Room("endpoints"), JArray)
        If Endpoints Is Nothing OrElse Endpoints.Count = 0 Then Return Nothing
        For Each Token As JToken In Endpoints
            Dim Entry As JObject = TryCast(Token, JObject)
            If Entry Is Nothing Then Continue For
            If Entry("campusOnly")?.ToObject(Of Boolean)() <> True Then Return Entry
        Next
        Return TryCast(Endpoints(0), JObject)
    End Function

    Private Function FirstAddress(Room As JObject) As String
        Dim Entry As JObject = FirstEntry(Room)
        If Entry IsNot Nothing Then
            Return Entry("host").ToString() & ":" & Entry("port").ToString()
        End If
        Return Room("host").ToString() & ":" & Room("port").ToString()
    End Function

    Private Sub LaunchRoom(Address As String)
        If McInstanceSelected Is Nothing Then
            Hint("请先在「启动」页选择一个游戏版本", HintType.Red)
            Return
        End If
        Try
            '先回到启动页，避免停留在联机页看不到启动进度
            FrmMain.PageChange(FormMain.PageType.Launch)
            McLaunchStart(New McLaunchOptions With {.ServerIp = Address})
        Catch ex As Exception
            Hint("启动游戏失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

#End Region
#Region "操作"

    Private Sub DoCreateRoom()
        Dim Name As String = TxtRoomName.Text.Trim()
        If Name.Length < 1 OrElse Name.Length > 24 Then
            Hint("房间名称需为 1—24 个字符", HintType.Red)
            Return
        End If
        Dim LocalPort As Integer
        If Not Integer.TryParse(TxtLocalPort.Text.Trim(), LocalPort) OrElse LocalPort < 1 OrElse LocalPort > 65535 Then
            Hint("本地端口无效，默认应为 25565", HintType.Red)
            Return
        End If
        Dim Password As String = TxtPassword.Text.Trim()
        If Password <> "" AndAlso (Password.Length < 4 OrElse Password.Length > 32) Then
            Hint("房间密码需为 4—32 个字符", HintType.Red)
            Return
        End If
        '房间会把本机端口经社团中继转发出去，所以只允许挂「启动器自己启动的游戏」的端口：
        '谁都能挂的话，这个中继就成了免费 frp，可以拿来转发任意本机服务。
        Dim PortPid As Integer = ModOnlineMode.FindPidByPort(LocalPort)
        If PortPid <= 0 Then
            Hint($"端口 {LocalPort} 上没有任何服务在监听。" & vbCrLf &
                 "请先在游戏里按 ESC →「对局域网开放」，再把聊天栏显示的端口填到这里。", HintType.Red)
            Return
        End If
        If Not ModOnlineMode.IsLaunchedByUs(PortPid) Then
            Hint($"端口 {LocalPort} 属于 PID {PortPid}，不是启动器启动的游戏，不能开房间。" & vbCrLf &
                 "这条限制是为了不让社团中继被拿去转发别的服务。", HintType.Red)
            Return
        End If
        LabStatus.Text = "正在创建房间……"
        BtnCreate.IsEnabled = False
        BtnCreate.Text = "创建中……"
        Dim WantPublic As Boolean = PublicRoomEnabled
        RunInNewThread(
        Sub()
            Try
                Dim CampusNode As JObject = FindNode(True)
                Dim PublicNode As JObject = FindNode(False)
                Dim Nodes As New List(Of String)
                '校内可达就只开校内；不可达或已过关机时间则自动降级到公网
                Dim CampusOk As Boolean = CampusNode IsNot Nothing AndAlso
                    Not CampusClosedNow() AndAlso NodeReachable(CampusNode)
                If CampusOk Then Nodes.Add(CampusNode("id").ToString())
                If WantPublic OrElse Not CampusOk Then
                    If PublicNode IsNot Nothing Then Nodes.Add(PublicNode("id").ToString())
                End If
                If Nodes.Count = 0 Then Throw New Exception("中控没有配置可用的联机节点")
                Dim Room As JObject = ModSocial.CreateRoom(Name, Nodes, LocalPort, Password)
                RunInUi(
                Sub()
                    BtnCreate.IsEnabled = True
                    BtnCreate.Text = "创建房间"
                    MyRoomCode = Room("code").ToString()
                    HostPassword = Password
                    PublicRoomEnabled = HasPublicEntry(Room)
                    Dim Address As String = FirstAddress(Room)
                    Dim Summary As String = EndpointSummary(Room)
                    If Summary = "" Then Summary = Address
                    UpdatePublicButton()
                    '后续这些属于附加动作，失败不能把"建房成功"误报成失败
                    Try
                        ModSocial.SetJoinerServer("")
                    Catch ex As Exception
                        Logger.Warn(ex, "清理访客地址失败")
                    End Try
                    Try
                        Clipboard.SetText(Address)
                    Catch ex As Exception
                        Logger.Warn(ex, "复制房间地址失败")
                    End Try
                    '剪贴板里是首选入口（能直接粘进「添加服务器」），两个入口都在状态行里写全，
                    '卡片里那两行各自还能单独复制
                    LabStatus.Text = $"房间已创建：{Summary}，首选地址已复制到剪贴板"
                    Hint("房间号 " & MyRoomCode & "，地址已复制", HintType.Green)
                    RefreshAll()
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    BtnCreate.IsEnabled = True
                    BtnCreate.Text = "创建房间"
                    If ex.Message.Contains("409") OrElse ex.Message.Contains("已有进行中的房间") Then
                        LabStatus.Text = "你已有进行中的房间，请先关闭"
                        Hint("你已有进行中的房间，请先在下方关闭后再创建", HintType.Red)
                        RefreshAll()
                    Else
                        LabStatus.Text = ex.Message
                        Hint("创建房间失败：" & ex.Message, HintType.Red)
                    End If
                End Sub)
            End Try
        End Sub, "SocialCreateRoom")
    End Sub

    Private Sub DoJoin()
        Dim Code As String = TxtJoin.Text.Trim()
        If Code.Length <> 10 OrElse Not IsNumeric(Code) Then
            Hint("请输入 10 位数字房间号", HintType.Red)
            Return
        End If
        RunInNewThread(
        Sub()
            Try
                Dim Room As JObject = ModSocial.GetRoom(Code)
                If Room Is Nothing Then Throw New Exception("房间不存在或已结束")
                Dim Password As String = ""
                If Room("hasPassword")?.ToObject(Of Boolean)() = True Then
                    '密码框必须在 UI 线程弹出，拿到结果后再回后台继续
                    RunInUiWait(Sub() Password = AskRoomPassword(Room("name")?.ToString(), Code))
                    If Password Is Nothing Then Return
                End If
                JoinAndEnter(Code, Password)
            Catch ex As Exception
                RunInUi(Sub() Hint(ex.Message, HintType.Red))
            End Try
        End Sub, "SocialJoin")
    End Sub

#End Region

End Class
