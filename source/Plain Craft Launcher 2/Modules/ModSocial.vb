Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Threading

''' <summary>
''' NJIT MCL 好友与联机模块：设备令牌、好友关系、房间与 frpc 中继。
''' 账号与房间数据由社团官网中控（njitmc.club）维护，游戏流量走 frps 中继。
''' </summary>
Public Module ModSocial

#Region "常量"

    ''' <summary>
    ''' 中控接口根地址。
    ''' </summary>
    Public ReadOnly Property ApiRoot As String
        Get
            Return ModNJIT.ClubHomepage & "/api/launcher"
        End Get
    End Property

    ''' <summary>
    ''' frpc 组件版本，用于本地缓存与更新判断。
    ''' </summary>
    Public Const FrpcVersion As String = "0.72.0"

    ''' <summary>
    ''' frpc 下载源，按顺序尝试。
    ''' </summary>
    Public ReadOnly Property FrpcUrls As String()
        Get
            Return {
                "https://wg.njit-fkcy.top/modpacks/_tools/frpc-" & FrpcVersion & ".exe",
                "https://gh-proxy.com/https://github.com/fatedier/frp/releases/download/v" & FrpcVersion &
                    "/frp_" & FrpcVersion & "_windows_amd64.zip",
                "https://github.com/fatedier/frp/releases/download/v" & FrpcVersion &
                    "/frp_" & FrpcVersion & "_windows_amd64.zip"
            }
        End Get
    End Property

#End Region
#Region "账号"

    ''' <summary>
    ''' 当前设备令牌，为空表示尚未与中控建立登录。
    ''' </summary>
    Public Property DeviceToken As String
        Get
            Return Settings.Get(Of String)("SocialToken")
        End Get
        Set(value As String)
            Settings.Set("SocialToken", value)
        End Set
    End Property

    ''' <summary>
    ''' 中控侧的用户名（唯一标识），为空表示还没设定。
    ''' </summary>
    Public Property SocialName As String
        Get
            Return Settings.Get(Of String)("SocialUsername")
        End Get
        Set(value As String)
            Settings.Set("SocialUsername", value)
        End Set
    End Property

    ''' <summary>
    ''' 中控侧账号 ID，用于判断房间是不是自己建的。
    ''' </summary>
    Public Property MyAccountId As String
        Get
            Return Settings.Get(Of String)("SocialAccountId")
        End Get
        Set(value As String)
            Settings.Set("SocialAccountId", value)
        End Set
    End Property

    ''' <summary>
    ''' 是否已拿到设备令牌。
    ''' </summary>
    Public Function IsLoggedIn() As Boolean
        Return Not String.IsNullOrWhiteSpace(DeviceToken)
    End Function

    ''' <summary>
    ''' 退出联机账号（不清除正版登录状态）。
    ''' </summary>
    Public Sub Logout()
        StopPresence()
        StopFrpc()
        StopVisitor()
        DeviceToken = ""
        SocialName = ""
        MyAccountId = ""
        '退出后顶级导航上的未读数字应立即清零
        Try
            If FrmMain IsNot Nothing Then FrmMain.RefreshMessageBadge()
        Catch ex As Exception
            Logger.Warn(ex, "刷新未读消息数失败")
        End Try
    End Sub

    ''' <summary>
    ''' 把异常转成适合展示给用户的一句话：中文业务错误原样展示，网络类错误统一为友好提示。
    ''' </summary>
    Public Function FriendlyError(ex As Exception) As String
        If ex Is Nothing Then Return "连接服务器失败，请稍后重试"
        Dim Msg As String = If(ex.Message, "")
        If String.IsNullOrWhiteSpace(Msg) Then Return "连接服务器失败，请稍后重试"
        If System.Text.RegularExpressions.Regex.IsMatch(Msg, "[\u4e00-\u9fff]") Then Return Msg
        Return "连接服务器失败，请检查网络后重试"
    End Function

    ''' <summary>
    ''' 确保已取得设备令牌；正版登录用户会自动换取，其余情况抛出提示。
    ''' </summary>
    Public Sub EnsureLogin()
        If IsLoggedIn() Then
            '已登录但本地还没缓存昵称时补拉一次，避免页面显示「未获取」
            If String.IsNullOrWhiteSpace(SocialName) OrElse String.IsNullOrWhiteSpace(MyAccountId) Then
                RefreshProfile()
            End If
            Return
        End If
        Throw New Exception("请先官网授权启动器：启动页 →「社团生活」卡片 → 授权启动器")
    End Sub

    ''' <summary>
    ''' 有正版登录态时直接用正版凭证换取设备令牌（同时把正版身份绑定到网站账号）。
    ''' 没有正版登录态返回 False，由调用方改用设备码授权。
    ''' </summary>
    Public Function AuthorizeWithMojang() As Boolean
        If IsLoggedIn() Then Return True
        Dim Access As String = Settings.Get(Of String)("CacheMsV2Access")
        If String.IsNullOrWhiteSpace(Access) Then Return False
        Dim Result As JObject = Api("POST", "/token", New JObject(
            New JProperty("provider", "mojang"),
            New JProperty("accessToken", Access),
            New JProperty("uuid", Settings.Get(Of String)("CacheMsV2Uuid")),
            New JProperty("name", Settings.Get(Of String)("CacheMsV2Name"))), NeedToken:=False)
        ApplyDeviceToken(Result)
        Return IsLoggedIn()
    End Function

    ''' <summary>
    ''' 统一授权入口：优先用正版登录态换取设备令牌，没有正版登录态才走设备码。
    ''' 必须在后台线程调用；ShowUserCode 在申请到设备码时触发（用于复制到剪贴板并提示）。
    ''' </summary>
    ''' <param name="DeviceName">设备名，用于官网「授权设备」列表。</param>
    ''' <param name="ShowUserCode">拿到用户码后的回调（可为 Nothing）。</param>
    ''' <param name="CancelFlag">返回 True 时终止等待（可为 Nothing）。</param>
    ''' <returns>True 表示已取得设备令牌。</returns>
    Public Function AuthorizeLauncher(DeviceName As String, Optional ShowUserCode As Action(Of String) = Nothing,
                                      Optional CancelFlag As Func(Of Boolean) = Nothing) As Boolean
        If IsLoggedIn() Then Return True
        '有正版登录态：直接换取设备令牌，无需用户操作
        Try
            If AuthorizeWithMojang() Then Return True
        Catch ex As Exception
            Logger.Warn(ex, "正版直授权失败，改用设备码")
        End Try
        Dim Result As JObject = RequestDeviceCode(DeviceName)
        Dim DeviceValue As String = If(Result("deviceCode")?.ToString(), "")
        Dim UserCode As String = If(Result("userCode")?.ToString(), "")
        If String.IsNullOrWhiteSpace(DeviceValue) OrElse String.IsNullOrWhiteSpace(UserCode) Then
            Throw New Exception("申请设备码失败，请稍后重试")
        End If
        If ShowUserCode IsNot Nothing Then ShowUserCode(UserCode)
        Dim Interval As Integer = If(Result("interval")?.ToObject(Of Integer)(), 3) * 1000
        Dim Deadline As Date = Date.Now.AddSeconds(If(Result("expiresIn")?.ToObject(Of Integer)(), 600))
        While Date.Now < Deadline
            If CancelFlag IsNot Nothing AndAlso CancelFlag() Then Return False
            Thread.Sleep(Interval)
            If CancelFlag IsNot Nothing AndAlso CancelFlag() Then Return False
            Dim Poll As JObject = Nothing
            Try
                Poll = PollDevice(DeviceValue)
            Catch ex As Exception
                '单次轮询网络抖动不终止流程，继续下一轮
                Logger.Warn(ex, "设备码轮询失败，继续等待")
                Continue While
            End Try
            If If(Poll("status")?.ToString(), "") = "approved" Then
                ApplyDeviceToken(Poll)
                Return IsLoggedIn()
            End If
        End While
        Return False
    End Function

    ''' <summary>
    ''' 申请设备码：启动器显示用户码，用户在官网确认授权。
    ''' 已登录正版时一并带上正版凭证，官网授权时自动绑定该身份。
    ''' </summary>
    Public Function RequestDeviceCode(DeviceName As String) As JObject
        Dim Payload As New JObject(New JProperty("deviceName", DeviceName))
        Dim Access As String = Settings.Get(Of String)("CacheMsV2Access")
        If Not String.IsNullOrWhiteSpace(Access) Then
            Payload("mojang") = New JObject(
                New JProperty("accessToken", Access),
                New JProperty("uuid", Settings.Get(Of String)("CacheMsV2Uuid")),
                New JProperty("name", Settings.Get(Of String)("CacheMsV2Name")))
        End If
        Return Api("POST", "/device/code", Payload, NeedToken:=False)
    End Function

    ''' <summary>轮询设备码状态：pending / approved / 过期。</summary>
    Public Function PollDevice(DeviceCode As String) As JObject
        Return Api("POST", "/device/token",
                   New JObject(New JProperty("deviceCode", DeviceCode)), NeedToken:=False)
    End Function

    ''' <summary>保存设备码授权结果（写入设备令牌与账号信息）。</summary>
    Public Sub ApplyDeviceToken(Result As JObject)
        SaveTokenResult(Result)
        SocialName = If(Result("username")?.ToString(), SocialName)
        Dim Account As JObject = TryCast(Result("account"), JObject)
        If Account IsNot Nothing Then MyAccountId = If(Account("accountId")?.ToString(), MyAccountId)
    End Sub

    ''' <summary>
    ''' 保存中控返回的设备令牌与账号信息（正版与 MUA 共用）。
    ''' </summary>
    Private Sub SaveTokenResult(Result As JObject)
        DeviceToken = If(Result("token")?.ToString(), "")
        If DeviceToken = "" Then Throw New Exception("中控没有返回设备令牌")
        Dim Account As JObject = TryCast(Result("account"), JObject)
        If Account IsNot Nothing Then
            SocialName = If(Account("username")?.ToString(), "")
            MyAccountId = If(Account("accountId")?.ToString(), "")
        End If
    End Sub

    ''' <summary>
    ''' 头像渲染版本号。服务端改了头像的取图或渲染逻辑时把它 +1：
    ''' 客户端 MyImage 按 URL 哈希把图片缓存 7 天，URL 不变就永远拿不到新图。
    ''' 改这里等于让全量用户的头像缓存一起失效。
    ''' </summary>
    Public Const AvatarRev As String = "5"

    ''' <summary>
    ''' 把中控返回的头像相对路径拼成完整地址，并统一带上渲染版本号。
    ''' 所有头像都必须从这里出发，不要自己拼 /api/launcher/avatar/。
    ''' </summary>
    Public Function AvatarUrl(RelativePath As String) As String
        If String.IsNullOrWhiteSpace(RelativePath) Then Return ""
        Dim Url As String = RelativePath
        If Not Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then Url = ModNJIT.ClubHomepage & Url
        If Url.Contains("v=") Then Return Url
        Return Url & If(Url.Contains("?"), "&", "?") & "v=" & AvatarRev
    End Function

    ''' <summary>
    ''' 按账号 ID 取中控头像地址。所有"给我这个账号的头像"都必须走这里，
    ''' 不要在调用点自己拼 /api/launcher/avatar/。
    ''' </summary>
    Public Function AvatarUrlOf(AccountId As String) As String
        If String.IsNullOrWhiteSpace(AccountId) Then Return ""
        Return AvatarUrl("/api/launcher/avatar/" & AccountId)
    End Function

    ''' <summary>
    ''' 只认 JObject 类型的子节点；值为 null、字符串、数组或父节点不是对象时一律返回 Nothing。
    ''' 不要用 <c>Obj?("a")?("b")</c>：JSON null 在 Newtonsoft 里是 JValue(Type=Null) 而不是 Nothing，
    ''' VB 的 <c>?()</c> 只判 Nothing，会一路取到 JValue 上抛「Cannot access child value on JValue」。
    ''' </summary>
    Public Function JChild(Obj As JToken, Key As String) As JObject
        Dim Parent As JObject = TryCast(Obj, JObject)
        If Parent Is Nothing Then Return Nothing
        Return TryCast(Parent(Key), JObject)
    End Function

    ''' <summary>
    ''' 测量与中继节点的 TCP 连接耗时（毫秒），失败返回 -1。
    ''' </summary>
    Public Function MeasureRelay(Host As String, Port As Integer) As Integer
        If String.IsNullOrWhiteSpace(Host) OrElse Port <= 0 Then Return -1
        Dim Watch As Stopwatch = Stopwatch.StartNew()
        Try
            Using Client As New Net.Sockets.TcpClient()
                Dim Connecting As Task = Client.ConnectAsync(Host, Port)
                If Not Connecting.Wait(3000) Then Return -1
                Watch.Stop()
                Return CInt(Watch.ElapsedMilliseconds)
            End Using
        Catch
            Return -1
        End Try
    End Function

    ''' <summary>
    ''' 拉取当前账号资料（昵称、身份、头像来源）。
    ''' </summary>
    Public Function SocialProfile() As JObject
        EnsureLogin()
        Return Api("GET", "/me", Nothing)
    End Function

    ''' <summary>
    ''' 拉取当前账号资料，缓存联机名与账号 ID。
    ''' </summary>
    Private Sub RefreshProfile()
        Dim Result As JObject = Api("GET", "/me", Nothing)
        Dim Account As JObject = TryCast(Result("account"), JObject)
        If Account Is Nothing Then Return
        SocialName = If(Account("username")?.ToString(), "")
        MyAccountId = If(Account("accountId")?.ToString(), "")
    End Sub

#End Region
#Region "好友"

    ''' <summary>
    ''' 按用户名前缀搜索玩家。
    ''' </summary>
    Public Function SearchUsers(Keyword As String) As JArray
        EnsureLogin()
        Dim Result As JObject = Api("GET", "/friends/search?name=" & Uri.EscapeDataString(Keyword), Nothing)
        Return If(TryCast(Result("results"), JArray), New JArray())
    End Function

    ''' <summary>
    ''' 获取好友列表与待处理申请。
    ''' </summary>
    Public Function FriendList() As JObject
        EnsureLogin()
        Return Api("GET", "/friends", Nothing)
    End Function

    ''' <summary>
    ''' 按用户名发起好友申请。
    ''' </summary>
    Public Function AddFriend(Name As String) As String
        EnsureLogin()
        Dim Result As JObject = Api("POST", "/friends/request",
            New JObject(New JProperty("username", Name)))
        Return If(Result("status")?.ToString(), "pending")
    End Function

    ''' <summary>
    ''' 同意或拒绝好友申请。
    ''' </summary>
    Public Sub RespondFriend(AccountId As String, Accept As Boolean)
        EnsureLogin()
        Api("POST", "/friends/respond", New JObject(
            New JProperty("accountId", AccountId),
            New JProperty("accept", Accept)))
    End Sub

    ''' <summary>
    ''' 删除好友。
    ''' </summary>
    Public Sub RemoveFriend(AccountId As String)
        EnsureLogin()
        Api("DELETE", "/friends/" & Uri.EscapeDataString(AccountId), Nothing)
    End Sub

    ''' <summary>
    ''' 拉黑：会同时解除好友关系。
    ''' </summary>
    Public Sub BlockFriend(AccountId As String)
        EnsureLogin()
        Api("POST", "/friends/block", New JObject(New JProperty("accountId", AccountId)))
    End Sub

    ''' <summary>
    ''' 解除拉黑。
    ''' </summary>
    Public Sub UnblockFriend(AccountId As String)
        EnsureLogin()
        Api("POST", "/friends/unblock", New JObject(New JProperty("accountId", AccountId)))
    End Sub

#End Region
#Region "消息"

    ''' <summary>
    ''' 会话列表：有往来的好友与最后一条消息。
    ''' </summary>
    Public Function MessageConversations() As JArray
        EnsureLogin()
        Dim Result As JObject = Api("GET", "/messages/conversations", Nothing)
        Return If(TryCast(Result("conversations"), JArray), New JArray())
    End Function

    ''' <summary>
    ''' 拉取与某好友的聊天记录；AfterId 为 0 时返回最近一页，否则只返回该消息之后的新消息。
    ''' </summary>
    Public Function MessageList(PeerId As String, Optional AfterId As Long = 0) As JArray
        EnsureLogin()
        Dim Url As String = "/messages?peer=" & Uri.EscapeDataString(PeerId)
        If AfterId > 0 Then Url &= "&after=" & AfterId.ToString()
        Dim Result As JObject = Api("GET", Url, Nothing)
        Return If(TryCast(Result("messages"), JArray), New JArray())
    End Function

    ''' <summary>
    ''' 给好友发送一条文本消息。
    ''' </summary>
    Public Function SendMessage(PeerId As String, Content As String) As JObject
        EnsureLogin()
        Dim Result As JObject = Api("POST", "/messages", New JObject(
            New JProperty("accountId", PeerId),
            New JProperty("content", Content)))
        Return TryCast(Result("message"), JObject)
    End Function

    ''' <summary>
    ''' 系统消息（管理员在网站管理后台发布）。
    ''' </summary>
    Public Function ClubSystemMessages() As JObject
        EnsureLogin()
        Return Api("GET", "/club/system-messages", Nothing)
    End Function

    ''' <summary>
    ''' 把系统消息的已读位置推进到 LastId。
    ''' </summary>
    Public Function MarkSystemMessagesRead(LastId As Long) As JObject
        EnsureLogin()
        Return Api("POST", "/club/system-messages/read",
            New JObject(New JProperty("lastId", LastId)))
    End Function

    ''' <summary>
    ''' 未读消息总数（好友消息 + 系统消息），用于在「联机」导航上显示数字。
    ''' 未登录或接口失败时返回 0，不抛异常。
    ''' </summary>
    Public Function UnreadTotal() As Integer
        If Not IsLoggedIn() Then Return 0
        Dim Total As Integer = 0
        Try
            Dim FriendData As JObject = Api("GET", "/messages/unread", Nothing, Silent:=True)
            If FriendData("total") IsNot Nothing Then
                Total += FriendData("total").ToObject(Of Integer)()
            End If
        Catch ex As Exception
            Logger.Warn(ex, "获取好友未读数失败")
        End Try
        '系统消息接口同时返回全文与未读数；数量很少，直接复用，避免再加一个接口
        Try
            Dim SystemData As JObject = Api("GET", "/club/system-messages", Nothing, Silent:=True)
            If SystemData("unread") IsNot Nothing Then
                Total += SystemData("unread").ToObject(Of Integer)()
            End If
        Catch ex As Exception
            Logger.Warn(ex, "获取系统消息未读数失败")
        End Try
        Return Total
    End Function

    ''' <summary>
    ''' 侧边栏徽章：未读消息总数与待处理好友申请数。未登录或失败时保持 0。
    ''' </summary>
    Public Sub BadgeCounts(ByRef Unread As Integer, ByRef Requests As Integer)
        Unread = 0
        Requests = 0
        If Not IsLoggedIn() Then Return
        Unread = UnreadTotal()
        Try
            Dim Data As JObject = Api("GET", "/friends", Nothing, Silent:=True)
            Dim Incoming As JArray = TryCast(Data("incoming"), JArray)
            If Incoming IsNot Nothing Then Requests = Incoming.Count
        Catch ex As Exception
            Logger.Warn(ex, "获取好友申请数失败")
        End Try
    End Sub

#End Region
#Region "房间"

    ''' <summary>
    ''' 当前进行中的房间列表。
    ''' </summary>
    Public Function RoomList() As JArray
        EnsureLogin()
        Dim Result As JObject = Api("GET", "/rooms", Nothing)
        Return If(TryCast(Result("rooms"), JArray), New JArray())
    End Function

    ''' <summary>
    ''' 查询单个房间。
    ''' </summary>
    Public Function GetRoom(Code As String) As JObject
        EnsureLogin()
        Dim Result As JObject = Api("GET", "/rooms/" & Uri.EscapeDataString(Code), Nothing)
        Return TryCast(Result("room"), JObject)
    End Function

    ''' <summary>
    ''' 加入房间：有密码的房间必须先通过校验，之后才下发中继入口与打洞密钥。
    ''' </summary>
    Public Function JoinRoom(Code As String, Optional Password As String = "") As JObject
        EnsureLogin()
        Dim Payload As New JObject()
        If Not String.IsNullOrWhiteSpace(Password) Then Payload("password") = Password
        Dim Result As JObject = Api("POST", "/rooms/" & Uri.EscapeDataString(Code) & "/join", Payload)
        Return TryCast(Result("room"), JObject)
    End Function

    ''' <summary>
    ''' 拉取可用联机方式：中继节点由中控配置下发，新增节点不需要改客户端。
    ''' </summary>
    Public Function RelayList() As JObject
        Return Api("GET", "/relays", Nothing, NeedToken:=False)
    End Function

    ''' <summary>
    ''' 创建房间；Nodes 为本次要注册的节点编号（校内 / 公网可同时）。
    ''' Password 非空时房间需要密码才能加入，中控只保存摘要。
    ''' </summary>
    Public Function CreateRoom(Name As String, Nodes As IEnumerable(Of String), LocalPort As Integer,
                               Optional Password As String = "") As JObject
        EnsureLogin()
        EnsureFrpc()
        Dim NodeArray As New JArray
        For Each NodeId As String In Nodes
            NodeArray.Add(NodeId)
        Next
        Dim Payload As New JObject(
            New JProperty("name", Name),
            New JProperty("nodes", NodeArray),
            New JProperty("gamePort", LocalPort))
        If Not String.IsNullOrWhiteSpace(Password) Then Payload("password") = Password
        Dim Result As JObject = Api("POST", "/rooms", Payload)
        Dim Room As JObject = TryCast(Result("room"), JObject)
        If Room Is Nothing Then Throw New Exception("中控没有返回房间信息")
        Dim Frpc As JObject = TryCast(Room("frpc"), JObject)
        If Frpc Is Nothing Then Throw New Exception("房间信息缺少中继或打洞配置")
        Dim Code As String = If(Room("code")?.ToString(), "")
        If Code = "" Then Throw New Exception("中控没有返回房间号")
        '房间已在服务端创建成功；本地 frpc 起不来时只提示，不能把建房结果误报成失败
        Try
            StartFrpc(Code, Frpc)
        Catch ex As Exception
            Logger.Warn(ex, "启动联机组件失败（房间已创建）")
            RunInUi(Sub() Hint("房间已创建，但联机组件启动失败，中继可能不可用：" & ex.Message, HintType.Red))
        End Try
        HostedPort = LocalPort
        HostedRoomCode = Code
        StartHeartbeat(Code)
        Return Room
    End Function

    ''' <summary>
    ''' 开启公网入口：中控分配公网端口，客户端为该节点拉起 frpc（保留已有入口）。
    ''' NodeId 为空时由中控回退到默认公网节点。
    ''' </summary>
    Public Function EnablePublic(Code As String, Optional NodeId As String = "") As JObject
        Dim Body As New JObject()
        If Not String.IsNullOrWhiteSpace(NodeId) Then Body("node") = NodeId
        Dim Result As JObject = Api("POST", "/rooms/" & Code & "/public", Body)
        Dim Room As JObject = TryCast(Result("room"), JObject)
        Dim Frpc As JObject = TryCast(Room("frpc"), JObject)
        If Frpc IsNot Nothing Then StartFrpc(Code, Frpc, KeepExisting:=True)
        Return Room
    End Function

    ''' <summary>
    ''' 关闭公网入口：中控回收端口，客户端停止该节点 frpc。
    ''' </summary>
    Public Function DisablePublic(Code As String, NodeId As String) As JObject
        Dim Result As JObject = Api("DELETE", "/rooms/" & Code & "/public/" & NodeId, Nothing)
        StopFrpcNode(Code, NodeId)
        Return TryCast(Result("room"), JObject)
    End Function

#Region "联机模式"

    ''' <summary>
    ''' 启动页的联机模式总开关；关闭后联机菜单不可用，访客进服设置一并清空。
    ''' </summary>
    Public ReadOnly Property SocialModeEnabled As Boolean
        Get
            '未设置过时视为开启，只有显式关闭（"0"）才禁用联机入口
            Return Settings.Get(Of String)("SocialModeEnabled") <> "0"
        End Get
    End Property

    ''' <summary>
    ''' 访客加入的房间地址；开启联机模式后，任何方式启动都会优先进入这里。
    ''' </summary>
    Public ReadOnly Property JoinerServer As String
        Get
            Return If(Settings.Get(Of String)("SocialJoinerServer"), "")
        End Get
    End Property

    Public Sub SetJoinerServer(Address As String, Optional RoomCode As String = "")
        Settings.Set("SocialJoinerServer", If(SocialModeEnabled, Address, ""))
        Settings.Set("SocialJoinerRoom", If(SocialModeEnabled, RoomCode, ""))
    End Sub

    ''' <summary>
    ''' 启动进服前重新评估访客地址：当前地址不可达时按 校内 → 公网 重新选一个可达入口。
    ''' 例如从校园网切到手机热点后，原来记录的校内地址会失效。
    ''' </summary>
    Public Function RefreshJoinerAddress() As String
        Dim Current As String = JoinerServer
        If String.IsNullOrWhiteSpace(Current) Then Return ""
        If AddressReachable(Current) Then Return Current
        Dim Code As String = If(Settings.Get(Of String)("SocialJoinerRoom"), "")
        If String.IsNullOrWhiteSpace(Code) Then Return Current
        Try
            Dim Room As JObject = GetRoom(Code)
            If Room Is Nothing Then Return Current
            Dim Campus As String = ""
            Dim PublicAddr As String = ""
            Dim Endpoints As JArray = TryCast(Room("endpoints"), JArray)
            If Endpoints IsNot Nothing Then
                For Each Token As JToken In Endpoints
                    Dim Entry As JObject = TryCast(Token, JObject)
                    If Entry Is Nothing Then Continue For
                    Dim Addr As String = If(Entry("host")?.ToString(), "") & ":" & If(Entry("port")?.ToString(), "")
                    If Addr = ":" Then Continue For
                    If Entry("campusOnly")?.ToObject(Of Boolean)() = True Then
                        If Campus = "" Then Campus = Addr
                    Else
                        If PublicAddr = "" Then PublicAddr = Addr
                    End If
                Next
            End If
            Dim Candidate As String = ""
            If Campus <> "" AndAlso AddressReachable(Campus) Then Candidate = Campus
            If Candidate = "" Then Candidate = PublicAddr
            If Candidate = "" Then Candidate = Current
            If Candidate <> Current Then
                Logger.Info($"访客地址已切换：{Current} -> {Candidate}")
                SetJoinerServer(Candidate, Code)
            End If
            '同步上报的入口方式，避免房主端一直显示切换前的入口
            If Campus <> "" AndAlso Candidate = Campus Then
                PresenceVia = "campus"
            ElseIf PublicAddr <> "" AndAlso Candidate = PublicAddr Then
                PresenceVia = "public"
            End If
            Return Candidate
        Catch ex As Exception
            Logger.Warn(ex, "重新评估访客地址失败")
            Return Current
        End Try
    End Function

    ''' <summary>host:port 形式的地址是否可连（2 秒超时）。</summary>
    Private Function AddressReachable(Address As String) As Boolean
        If String.IsNullOrWhiteSpace(Address) Then Return False
        Dim HostValue As String = Address.Trim()
        Dim PortValue As Integer = 25565
        If HostValue.Contains(":") Then
            Integer.TryParse(HostValue.AfterFirst(":"), PortValue)
            HostValue = HostValue.BeforeFirst(":")
        End If
        If HostValue = "" OrElse PortValue <= 0 Then Return False
        Return MeasureRelay(HostValue, PortValue) >= 0
    End Function

    ''' <summary>
    ''' 切换联机模式；关闭时清理访客地址并停掉本地联机进程。
    ''' </summary>
    Public Sub SetSocialModeEnabled(Enabled As Boolean)
        Settings.Set("SocialModeEnabled", If(Enabled, "1", "0"))
        If Not Enabled Then
            Settings.Set("SocialJoinerServer", "")
            '房主关闭联机模式时也要删除房间，避免残留到心跳超时（C27）
            Dim Code As String = HostedRoomCode
            HostedRoomCode = ""
            If Code <> "" AndAlso IsLoggedIn() Then
                System.Threading.Tasks.Task.Run(
                    Sub()
                        Try
                            Api("DELETE", "/rooms/" & Uri.EscapeDataString(Code), Nothing, Silent:=True)
                        Catch
                        End Try
                    End Sub)
            End If
            StopHeartbeat()
            StopPresence()
            StopFrpc()
            StopVisitor()
            HostedPort = 0
        End If
    End Sub

#End Region

    ''' <summary>
    ''' 关闭房间（同时停止 frpc 与心跳）。
    ''' </summary>
    Public Sub CloseRoom(Code As String)
        StopHeartbeat()
        StopPresence()
        StopFrpc()
        HostedPort = 0
        HostedRoomCode = ""
        If IsLoggedIn() Then Api("DELETE", "/rooms/" & Uri.EscapeDataString(Code), Nothing, Silent:=True)
    End Sub

    ''' <summary>
    ''' 退出程序前的联机清理：关闭自己的房间，并停止 frpc、访客进程与所有定时上报。
    ''' 关闭房间的请求最多等 2.5 秒，避免断网时卡住退出流程。
    ''' </summary>
    Public Sub Shutdown()
        Dim Code As String = HostedRoomCode
        HostedRoomCode = ""
        StopHeartbeat()
        StopPresence(2500)
        If Code <> "" AndAlso IsLoggedIn() Then
            Try
                Dim Closing As System.Threading.Tasks.Task = System.Threading.Tasks.Task.Run(
                    Sub()
                        Try
                            Api("DELETE", "/rooms/" & Uri.EscapeDataString(Code), Nothing, Silent:=True)
                        Catch
                        End Try
                    End Sub)
                Closing.Wait(2500)
            Catch ex As Exception
                Logger.Warn(ex, "退出时关闭房间失败")
            End Try
        End If
        StopFrpc()
        StopVisitor()
        HostedPort = 0
    End Sub

    ''' <summary>
    ''' 把本地服务器当前的在线玩家上报给中控，供房间成员列表展示。
    ''' </summary>
    Public Sub ReportPlayers(Code As String, Names As IEnumerable(Of String))
        If String.IsNullOrWhiteSpace(Code) OrElse Not IsLoggedIn() Then Return
        Try
            Api("POST", "/rooms/" & Uri.EscapeDataString(Code) & "/players",
                New JObject(New JProperty("players", New JArray(Names))), Silent:=True)
        Catch ex As Exception
            Logger.Warn(ex, "上报房间成员失败")
        End Try
    End Sub

    ''' <summary>
    ''' 用 Minecraft 服务器列表协议查询本地服务器在线玩家；服务器未启动时返回空列表。
    ''' </summary>
    Public Function PingPlayers(Port As Integer) As List(Of String)
        Dim Names As New List(Of String)
        If Port <= 0 OrElse Port > 65535 Then Return Names
        Try
            Using Client As New Net.Sockets.TcpClient()
                Dim Connecting As Task = Client.ConnectAsync("127.0.0.1", Port)
                If Not Connecting.Wait(1500) Then Return Names
                Using Stream As Net.Sockets.NetworkStream = Client.GetStream()
                    Stream.ReadTimeout = 2000
                    Stream.WriteTimeout = 2000
                    Dim Handshake As New MemoryStream()
                    WriteVarInt(Handshake, 0)
                    WriteVarInt(Handshake, 767)
                    WriteMcString(Handshake, "127.0.0.1")
                    Handshake.WriteByte(CByte((Port >> 8) And &HFF))
                    Handshake.WriteByte(CByte(Port And &HFF))
                    WriteVarInt(Handshake, 1)
                    SendPacket(Stream, Handshake.ToArray())
                    SendPacket(Stream, New Byte() {0})
                    Dim Length As Integer = ReadVarInt(Stream)
                    If Length <= 0 OrElse Length > 4 * 1024 * 1024 Then Return Names
                    Dim Buffer(Length - 1) As Byte
                    Dim Offset As Integer = 0
                    While Offset < Buffer.Length
                        Dim Count As Integer = Stream.Read(Buffer, Offset, Buffer.Length - Offset)
                        If Count <= 0 Then Return Names
                        Offset += Count
                    End While
                    Using Reader As New MemoryStream(Buffer)
                        ReadVarInt(Reader)
                        Dim Root As JObject = ReadMcString(Reader).DeserializeJson()
                        Dim Players As JObject = TryCast(Root("players"), JObject)
                        If Players IsNot Nothing Then
                            Dim Sample As JArray = TryCast(Players("sample"), JArray)
                            If Sample IsNot Nothing Then
                                For Each Item As JToken In Sample
                                    Dim Name As String = If(Item("name")?.ToString(), "")
                                    If Name <> "" AndAlso Not Names.Contains(Name) Then Names.Add(Name)
                                Next
                            End If
                            Dim Online As Integer = 0
                            If Players("online") IsNot Nothing Then Online = Players("online").ToObject(Of Integer)()
                            If Names.Count = 0 AndAlso Online > 0 Then Names.Add("在线玩家 " & Online & " 人")
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Logger.Info("读取本地服务器玩家失败：" & ex.Message)
        End Try
        Return Names
    End Function

    Private Sub SendPacket(Stream As Stream, Payload As Byte())
        WriteVarInt(Stream, Payload.Length)
        Stream.Write(Payload, 0, Payload.Length)
    End Sub

    Private Sub WriteVarInt(Stream As Stream, Value As Integer)
        Dim Current As UInteger = CUInt(Value)
        Do
            Dim Item As Byte = CByte(Current And &H7FUI)
            Current >>= 7
            If Current <> 0UI Then Item = Item Or CByte(&H80)
            Stream.WriteByte(Item)
        Loop While Current <> 0UI
    End Sub

    Private Function ReadVarInt(Stream As Stream) As Integer
        Dim Result As Integer = 0
        Dim Shift As Integer = 0
        Dim Item As Integer = 0
        Do
            Item = Stream.ReadByte()
            If Item < 0 Then Throw New IO.EndOfStreamException()
            Result = Result Or ((Item And &H7F) << Shift)
            Shift += 7
            If Shift > 35 Then Throw New IO.InvalidDataException()
        Loop While (Item And &H80) <> 0
        Return Result
    End Function

    Private Sub WriteMcString(Stream As Stream, Value As String)
        Dim Body As Byte() = Text.Encoding.UTF8.GetBytes(If(Value, ""))
        WriteVarInt(Stream, Body.Length)
        Stream.Write(Body, 0, Body.Length)
    End Sub

    Private Function ReadMcString(Stream As Stream) As String
        Dim Length As Integer = ReadVarInt(Stream)
        If Length < 0 OrElse Length > 1024 * 1024 Then Throw New IO.InvalidDataException()
        Dim Body(Length - 1) As Byte
        Dim Offset As Integer = 0
        While Offset < Length
            Dim Count As Integer = Stream.Read(Body, Offset, Length - Offset)
            If Count <= 0 Then Throw New IO.EndOfStreamException()
            Offset += Count
        End While
        Return Text.Encoding.UTF8.GetString(Body)
    End Function

#End Region
#Region "frpc"

    Private FrpcProcesses As New List(Of Process)
    Private FrpcConfigPaths As New List(Of String)
    'frpc 进程列表会在联机后台线程与退出清理/UI 读取间并发访问，统一加锁（竞态）
    Private FrpcLock As New Object
    Private VisitorProcess As Process
    Private VisitorConfigPath As String = ""
    Private VisitorLocalPort As Integer = 0
    Private HeartbeatTimer As Timer
    Private PresenceTimer As Timer
    Private PresenceCode As String = ""
    Private PresencePassword As String = ""
    ''' <summary>本次加入房间选择的入口方式：campus / p2p / public。</summary>
    Private PresenceVia As String = ""
    Private HostedPort As Integer = 0
    '当前房主房间号；关闭程序时据此释放房间与端口
    Private HostedRoomCode As String = ""

    ''' <summary>
    ''' frpc 本地路径。
    ''' </summary>
    Public ReadOnly Property FrpcPath As String
        Get
            Return IO.Path.Combine(IO.Path.GetDirectoryName(PathExe), "frpc.exe")
        End Get
    End Property

    ''' <summary>
    ''' 确保 frpc 存在；缺失时从内网或 GitHub 下载。
    ''' </summary>
    Public Sub EnsureFrpc()
        If FileUtils.Exists(FrpcPath) AndAlso New FileInfo(FrpcPath).Length > 8L * 1024 * 1024 Then Return
        RunInUi(Sub() Hint("正在下载联机组件……"))
        Dim LastError As Exception = Nothing
        For Each Url As String In FrpcUrls
            Try
                Logger.Info($"下载 frpc：{Url}")
                If Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then
                    DownloadFrpcFromZip(Url)
                Else
                    Using Client As New WebClient()
                        Client.Headers("User-Agent") = "NJITMCL/" & VersionBaseName
                        Client.DownloadFile(Url, FrpcPath & ".tmp")
                    End Using
                    If FileUtils.Exists(FrpcPath & ".tmp") Then
                        '先移走旧文件再改名，避免 File.Move 因目标已存在而抛异常
                        If FileUtils.Exists(FrpcPath) Then FileUtils.Delete(FrpcPath)
                        IO.File.Move(FrpcPath & ".tmp", FrpcPath)
                    End If
                End If
                If FileUtils.Exists(FrpcPath) AndAlso New FileInfo(FrpcPath).Length > 8L * 1024 * 1024 Then Return
            Catch ex As Exception
                LastError = ex
                Logger.Warn(ex, "下载 frpc 失败")
            End Try
        Next
        Throw New Exception("联机组件下载失败：" & If(LastError?.Message, "没有可用的下载源"))
    End Sub

    ''' <summary>
    ''' 从 frp 官方 zip 中提取 frpc.exe。
    ''' </summary>
    Private Sub DownloadFrpcFromZip(Url As String)
        Dim ZipPath As String = PathTemp & "Cache\frp.zip"
        Directory.CreateDirectory(PathTemp & "Cache\")
        'WebClient 无超时控制，断网时会挂起很久；改用 HttpWebRequest 并显式 30 秒超时（C17）
        Dim Request As Net.HttpWebRequest = DirectCast(Net.HttpWebRequest.Create(Url), Net.HttpWebRequest)
        Request.UserAgent = "NJITMCL/" & VersionBaseName
        Request.Timeout = 30000
        Using Response As Net.HttpWebResponse = DirectCast(Request.GetResponse(), Net.HttpWebResponse)
            Using Source As IO.Stream = Response.GetResponseStream()
                Using Target As New IO.FileStream(ZipPath, IO.FileMode.Create)
                    Source.CopyTo(Target)
                End Using
            End Using
        End Using
        Using Archive As IO.Compression.ZipArchive =
                IO.Compression.ZipFile.OpenRead(ZipPath)
            For Each Entry As IO.Compression.ZipArchiveEntry In Archive.Entries
                If Entry.Name.Equals("frpc.exe", StringComparison.OrdinalIgnoreCase) Then
                    Entry.ExtractToFile(FrpcPath, True)
                    Exit For
                End If
            Next
        End Using
        FileUtils.Delete(ZipPath)
    End Sub

    ''' <summary>
    ''' 生成配置并启动 frpc；Config 含 nodes 时每个节点各起一个进程。
    ''' </summary>
    Private Sub StartFrpc(Code As String, Config As JObject, Optional KeepExisting As Boolean = False)
        If Not KeepExisting Then StopFrpc()
        Dim Nodes As JArray = TryCast(Config("nodes"), JArray)
        Try
            If Nodes IsNot Nothing Then
                For Each Token As JToken In Nodes
                    Dim Node As JObject = TryCast(Token, JObject)
                    If Node IsNot Nothing Then StartFrpcNode(Code, Node)
                Next
            Else
                StartFrpcNode(Code, Config)
            End If
        Catch ex As Exception
            '任一节点启动失败时清掉已启动的进程，避免残留（C12）
            StopFrpc()
            Throw
        End Try
    End Sub

    Private Sub StartFrpcNode(Code As String, Config As JObject)
        Dim Folder As String = PathTemp & "Social\"
        Directory.CreateDirectory(Folder)
        Dim NodeId As String = If(Config("node")?.ToString(), "")
        Dim ConfPath As String = IO.Path.Combine(Folder, "frpc-" & Code &
                                                 If(NodeId = "", "", "-" & NodeId) & ".toml")
        Dim ServerAddr As String = If(Config("serverAddr")?.ToString(), "")
        If ServerAddr = "" Then Throw New Exception("联机节点配置缺少服务器地址")
        'frpc 自己写文件日志；不要再重定向 stdout，否则没人读管道会让进程写阻塞
        Dim LogPath As String = PathTemp & "Social\frpc-" & Code &
            If(NodeId = "", "", "-" & NodeId) & ".log"
        Dim Lines As New List(Of String) From {
            "serverAddr = """ & ServerAddr & """",
            "serverPort = " & If(Config("serverPort")?.ToString(), "7000"),
            "auth.method = ""token""",
            "auth.token = """ & If(Config("token")?.ToString(), "") & """",
            "log.to = """ & LogPath.Replace("\", "/") & """",
            "log.level = ""info""",
            "log.maxDays = 3"
        }
        Dim Proxies As JArray = TryCast(Config("proxies"), JArray)
        If Proxies IsNot Nothing Then
            '每个节点：TCP 中继 + XTCP 打洞 + STCP 回退
            For Each Token As JToken In Proxies
                Dim Proxy As JObject = TryCast(Token, JObject)
                If Proxy Is Nothing Then Continue For
                Dim ProxyType As String = If(Proxy("type")?.ToString(), "tcp")
                Lines.Add("")
                Lines.Add("[[proxies]]")
                Lines.Add("name = """ & If(Proxy("name")?.ToString(), "") & """")
                Lines.Add("type = """ & ProxyType & """")
                '只有 XTCP / STCP 才有共享密钥，TCP 中继没有该字段
                If ProxyType = "xtcp" OrElse ProxyType = "stcp" Then
                    Lines.Add("secretKey = """ & If(Proxy("secretKey")?.ToString(), "") & """")
                End If
                Lines.Add("localIP = """ & If(Proxy("localIP")?.ToString(), "127.0.0.1") & """")
                Lines.Add("localPort = " & If(Proxy("localPort")?.ToString(), "0"))
                If Proxy("remotePort") IsNot Nothing Then
                    Lines.Add("remotePort = " & Proxy("remotePort").ToString())
                End If
            Next
        Else
            Lines.Add("")
            Lines.Add("[[proxies]]")
            Lines.Add("name = """ & If(Config("name")?.ToString(), "njitmcl-" & Code) & """")
            Lines.Add("type = ""tcp""")
            Lines.Add("localIP = ""127.0.0.1""")
            Lines.Add("localPort = " & If(Config("localPort")?.ToString(), "0"))
            Lines.Add("remotePort = " & If(Config("remotePort")?.ToString(), "0"))
        End If
        IO.File.WriteAllText(ConfPath, String.Join(vbCrLf, Lines), New Text.UTF8Encoding(False))
        Dim Info As New ProcessStartInfo(FrpcPath, "-c """ & ConfPath & """") With {
            .CreateNoWindow = True,
            .UseShellExecute = False}
        Dim Proc As Process = Process.Start(Info)
        If Proc Is Nothing Then Throw New Exception("frpc 启动失败")
        '进程意外退出时留一条日志，便于排查中继是否还活着
        Proc.EnableRaisingEvents = True
        AddHandler Proc.Exited,
            Sub(sender As Object, e As EventArgs)
                Try
                    Logger.Warn($"frpc 已退出：房间 {Code}，节点 {If(NodeId = "", "-", NodeId)}，退出码 {Proc.ExitCode}")
                Catch
                End Try
            End Sub
        SyncLock FrpcLock
            FrpcProcesses.Add(Proc)
            FrpcConfigPaths.Add(ConfPath)
        End SyncLock
        Logger.Info($"已启动 frpc：房间 {Code}，节点 {If(NodeId = "", "-", NodeId)}")
    End Sub

    ''' <summary>
    ''' 加入 P2P 房间：启动 frpc visitor（XTCP 打洞 + STCP 回退），返回本机直连地址。
    ''' 必须在后台线程调用。
    ''' </summary>
    Public Function VisitRoom(Code As String, Xtcp As JObject) As String
        If Xtcp Is Nothing Then Throw New Exception("房间缺少打洞参数")
        EnsureFrpc()
        StopVisitor()
        Dim LocalPort As Integer = FindFreePort(25577)
        Dim Folder As String = PathTemp & "Social\"
        Directory.CreateDirectory(Folder)
        VisitorConfigPath = IO.Path.Combine(Folder, "visitor-" & Code & ".toml")
        Dim ProxyName As String = If(Xtcp("proxyName")?.ToString(), "")
        Dim StcpName As String = If(Xtcp("stcpName")?.ToString(), "")
        If ProxyName = "" OrElse StcpName = "" Then Throw New Exception("房间缺少打洞参数")
        Dim VisitorName As String = ProxyName & "-visitor"
        Dim StcpVisitor As String = StcpName & "-visitor"
        Dim LogPath As String = PathTemp & "Social\visitor-" & Code & ".log"
        Dim Lines As New List(Of String) From {
            "serverAddr = """ & If(Xtcp("serverAddr")?.ToString(), "") & """",
            "serverPort = " & If(Xtcp("serverPort")?.ToString(), "7000"),
            "auth.method = ""token""",
            "auth.token = """ & If(Xtcp("token")?.ToString(), "") & """",
            "log.to = """ & LogPath.Replace("\", "/") & """",
            "log.level = ""info""",
            "log.maxDays = 3",
            "",
            "[[visitors]]",
            "name = """ & StcpVisitor & """",
            "type = ""stcp""",
            "serverName = """ & StcpName & """",
            "secretKey = """ & If(Xtcp("secretKey")?.ToString(), "") & """",
            "bindPort = -1",
            "",
            "[[visitors]]",
            "name = """ & VisitorName & """",
            "type = ""xtcp""",
            "serverName = """ & ProxyName & """",
            "secretKey = """ & If(Xtcp("secretKey")?.ToString(), "") & """",
            "bindAddr = ""127.0.0.1""",
            "bindPort = " & LocalPort,
            "fallbackTo = """ & StcpVisitor & """",
            "fallbackTimeoutMs = 500"
        }
        IO.File.WriteAllText(VisitorConfigPath, String.Join(vbCrLf, Lines), New Text.UTF8Encoding(False))
        VisitorProcess = Process.Start(New ProcessStartInfo(FrpcPath, "-c """ & VisitorConfigPath & """") With {
            .CreateNoWindow = True,
            .UseShellExecute = False})
        VisitorLocalPort = LocalPort
        '等待本地监听就绪；打洞在首次连接时进行，这里只确认 frpc 已起监听
        Dim Watch As Stopwatch = Stopwatch.StartNew()
        Dim Ready As Boolean = False
        Do While Watch.ElapsedMilliseconds < 6000
            Try
                Using Client As New Net.Sockets.TcpClient()
                    Dim Connecting As System.Threading.Tasks.Task = Client.ConnectAsync("127.0.0.1", LocalPort)
                    If Connecting.Wait(300) Then
                        Ready = True
                        Exit Do
                    End If
                End Using
            Catch
            End Try
            Threading.Thread.Sleep(200)
        Loop
        If Not Ready Then
            '访问器没起来时不能把 127.0.0.1 交给游戏，否则只会得到 Connection refused
            StopVisitor()
            Throw New Exception("P2P 打洞未能建立连接")
        End If
        Logger.Info($"已启动 P2P 访问器：房间 {Code}，本地端口 {LocalPort}")
        Return "127.0.0.1:" & LocalPort
    End Function

    ''' <summary>
    ''' 找一个空闲的本地 TCP 端口。
    ''' </summary>
    Private Function FindFreePort(Start As Integer) As Integer
        For Port As Integer = Start To Start + 200
            Dim InUse As Boolean = False
            Try
                Dim Listener As New Net.Sockets.TcpListener(Net.IPAddress.Loopback, Port)
                Listener.Start()
                Listener.Stop()
            Catch
                InUse = True
            End Try
            If Not InUse Then Return Port
        Next
        Return Start
    End Function

    ''' <summary>
    ''' 停止 P2P 访问器并清理临时配置。
    ''' </summary>
    Public Sub StopVisitor()
        Try
            If VisitorProcess IsNot Nothing AndAlso Not VisitorProcess.HasExited Then
                VisitorProcess.Kill()
            End If
        Catch ex As Exception
            Logger.Warn(ex, "停止 P2P 访问器失败")
        End Try
        VisitorProcess = Nothing
        If Not String.IsNullOrWhiteSpace(VisitorConfigPath) Then
            Try
                FileUtils.Delete(VisitorConfigPath)
            Catch
                ' 忽略清理失败
            End Try
        End If
        VisitorConfigPath = ""
        VisitorLocalPort = 0
    End Sub

    ''' <summary>
    ''' 房主端是否还有 frpc 在运行（判断房间中继是否还活着）。
    ''' </summary>
    Public ReadOnly Property FrpcAlive As Boolean
        Get
            SyncLock FrpcLock
                For Each Item As Process In FrpcProcesses
                    Try
                        If Item IsNot Nothing AndAlso Not Item.HasExited Then Return True
                    Catch
                    End Try
                Next
            End SyncLock
            Return False
        End Get
    End Property

    ''' <summary>
    ''' 停止 frpc 并清理配置。
    ''' </summary>
    Public Sub StopFrpc()
        SyncLock FrpcLock
            For Each Item As Process In FrpcProcesses
                Try
                    If Item IsNot Nothing AndAlso Not Item.HasExited Then Item.Kill()
                Catch ex As Exception
                    Logger.Warn(ex, "停止 frpc 失败")
                End Try
            Next
            FrpcProcesses.Clear()
            For Each Path As String In FrpcConfigPaths
                Try
                    FileUtils.Delete(Path)
                Catch
                    ' 忽略清理失败
                End Try
            Next
            FrpcConfigPaths.Clear()
        End SyncLock
    End Sub

    ''' <summary>
    ''' 停止指定节点的 frpc（关闭公网房间时使用）。
    ''' </summary>
    Public Sub StopFrpcNode(Code As String, NodeId As String)
        '与 StartFrpcNode 的文件命名保持一致：NodeId 为空时不带 "-" 后缀
        Dim Target As String = If(NodeId = "", "frpc-" & Code & ".toml", "frpc-" & Code & "-" & NodeId & ".toml")
        SyncLock FrpcLock
            For Index As Integer = FrpcConfigPaths.Count - 1 To 0 Step -1
                If IO.Path.GetFileName(FrpcConfigPaths(Index)) <> Target Then Continue For
                Try
                    If FrpcProcesses(Index) IsNot Nothing AndAlso Not FrpcProcesses(Index).HasExited Then
                        FrpcProcesses(Index).Kill()
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "停止节点 frpc 失败")
                End Try
                Try
                    FileUtils.Delete(FrpcConfigPaths(Index))
                Catch
                    ' 忽略清理失败
                End Try
                FrpcProcesses.RemoveAt(Index)
                FrpcConfigPaths.RemoveAt(Index)
            Next
        End SyncLock
    End Sub

    ''' <summary>
    ''' 启动房间心跳，每 25 秒续期一次。
    ''' </summary>
    Private Sub StartHeartbeat(Code As String)
        StopHeartbeat()
        HeartbeatTimer = New Timer(
            Sub(State As Object)
                Try
                    Api("POST", "/rooms/" & Uri.EscapeDataString(Code) & "/heartbeat", New JObject(), Silent:=True)
                    If HostedPort > 0 Then ReportPlayers(Code, PingPlayers(HostedPort))
                Catch ex As Exception
                    Logger.Warn(ex, "房间心跳失败")
                End Try
            End Sub, Nothing, 2000, 25000)
    End Sub

    ''' <summary>
    ''' 停止心跳。
    ''' </summary>
    Public Sub StopHeartbeat()
        If HeartbeatTimer IsNot Nothing Then
            HeartbeatTimer.Dispose()
            HeartbeatTimer = Nothing
        End If
    End Sub

    ''' <summary>
    ''' 立即上报一次「我在这个房间」，用于加入后马上刷新成员列表（必须在后台线程调用）。
    ''' Via 为进入方式（campus / p2p / public），房主端据此显示每个成员的入口。
    ''' </summary>
    Public Sub ReportPresenceOnce(Code As String, Optional Password As String = "", Optional Via As String = "")
        If String.IsNullOrWhiteSpace(Code) OrElse Not IsLoggedIn() Then Return
        Dim Payload As New JObject()
        If Password <> "" Then Payload("password") = Password
        If Via <> "" Then Payload("via") = Via
        Api("POST", "/rooms/" & Uri.EscapeDataString(Code) & "/presence", Payload, Silent:=True)
    End Sub

    ''' <summary>
    ''' 加入房间后定期上报「我在这个房间」，房主端成员列表据此展示。
    ''' </summary>
    Public Sub StartPresence(Code As String, Optional Password As String = "", Optional Via As String = "")
        StopPresence()
        If String.IsNullOrWhiteSpace(Code) OrElse Not IsLoggedIn() Then Return
        PresenceCode = Code
        PresencePassword = If(Password, "")
        PresenceVia = If(Via, "")
        PresenceTimer = New Timer(
            Sub(State As Object)
                Try
                    If PresenceCode = "" Then Return
                    ReportPresenceOnce(PresenceCode, PresencePassword, PresenceVia)
                Catch ex As Exception
                    Logger.Warn(ex, "房间在线状态上报失败")
                End Try
            End Sub, Nothing, 0, 30000)
    End Sub

    ''' <summary>
    ''' 立即上报一次「我离开这个房间」，用于退出后马上刷新成员列表（必须在后台线程调用）。
    ''' </summary>
    Public Sub RemovePresenceOnce(Code As String)
        If String.IsNullOrWhiteSpace(Code) OrElse Not IsLoggedIn() Then Return
        Api("POST", "/rooms/" & Uri.EscapeDataString(Code) & "/leave", New JObject(), Silent:=True)
    End Sub

    ''' <summary>
    ''' 停止房间在线状态上报，并通知中控把自己从成员列表移除。
    ''' leave 请求默认放后台执行，避免断网时阻塞 UI 线程；WaitMs &gt; 0 时最多等待该毫秒数。
    ''' SendLeave 为 False 时只停定时器，由调用方自行同步上报离开。
    ''' </summary>
    Public Sub StopPresence(Optional WaitMs As Integer = 0, Optional SendLeave As Boolean = True)
        Dim Code As String = PresenceCode
        PresenceCode = ""
        PresencePassword = ""
        PresenceVia = ""
        If PresenceTimer IsNot Nothing Then
            PresenceTimer.Dispose()
            PresenceTimer = Nothing
        End If
        If Not SendLeave OrElse Code = "" OrElse Not IsLoggedIn() Then Return
        Dim LeaveTask As System.Threading.Tasks.Task = System.Threading.Tasks.Task.Run(
            Sub()
                Try
                    RemovePresenceOnce(Code)
                Catch ex As Exception
                    Logger.Warn(ex, "退出房间状态上报失败")
                End Try
            End Sub)
        If WaitMs > 0 Then LeaveTask.Wait(WaitMs)
    End Sub

#End Region
#Region "请求"

    ''' <summary>
    ''' 调用中控接口；失败时抛出带中控提示的异常。
    ''' </summary>
    Private Function Api(Method As String, Path As String, Body As JObject,
                         Optional NeedToken As Boolean = True,
                         Optional Silent As Boolean = False) As JObject
        Dim Url As String = ApiRoot & Path
        Dim Headers As New List(Of KeyValuePair(Of String, String))
        If NeedToken Then
            If String.IsNullOrWhiteSpace(DeviceToken) Then Throw New Exception("联机账号尚未登录")
            Headers.Add(New KeyValuePair(Of String, String)("Authorization", "Bearer " & DeviceToken))
        End If
        Dim Http As HttpMethod = If(Method = "GET", HttpMethod.Get,
            If(Method = "POST", HttpMethod.Post,
            If(Method = "PUT", HttpMethod.Put, HttpMethod.Delete)))
        Dim Content As String = If(Body Is Nothing, Nothing, Body.ToString(Newtonsoft.Json.Formatting.None))
        Dim Raw As String
        Try
            Raw = NetRequestByClient(Url, Http, Content:=Content,
                ContentType:=If(Content Is Nothing, Nothing, "application/json; charset=utf-8"),
                Headers:=ToHeaderArray(Headers), RequireJson:=True, Timeout:=15000, MakeLog:=Not Silent)
        Catch ex As Exception
            If Not Silent Then Logger.Warn(ex, $"联机接口失败：{Path}")
            Throw
        End Try
        Dim Root As JObject = Raw.DeserializeJson()
        If Root Is Nothing Then Throw New Exception("中控返回内容为空")
        If Root("success") IsNot Nothing AndAlso Not Root("success").ToObject(Of Boolean)() Then
            Dim Message As String = If(Root("message")?.ToString(), "请求失败")
            If Message.Contains("登录状态已失效") Then
                DeviceToken = ""
                '授权被撤销后立即刷新启动页授权状态，避免持续显示「已授权」假状态
                RunInUi(Sub() FrmLaunchRight.RefreshDeviceAuth())
            End If
            Throw New Exception(Message)
        End If
        Return Root
    End Function

    Private Function ToHeaderArray(Items As List(Of KeyValuePair(Of String, String))) As String(,)
        If Items.Count = 0 Then Return Nothing
        Dim Result(Items.Count - 1, 1) As String
        For i = 0 To Items.Count - 1
            Result(i, 0) = Items(i).Key
            Result(i, 1) = Items(i).Value
        Next
        Return Result
    End Function

#End Region

#Region "社团生活"

    ''' <summary>
    ''' 拉取社团生活汇总：账号、信息模板、活动、投票与个人记录。
    ''' </summary>
    Public Function ClubBootstrap() As JObject
        Return Api("GET", "/club/bootstrap", Nothing)
    End Function

    ''' <summary>
    ''' 读取活动详情（含报名表头与我的报名状态）。
    ''' </summary>
    Public Function ClubEventDetail(EventId As String) As JObject
        Return Api("GET", "/club/events/" & Uri.EscapeDataString(EventId), Nothing)
    End Function

    ''' <summary>
    ''' 报名活动；Custom 只需包含自定义列。
    ''' </summary>
    Public Function ClubSignup(EventId As String, Custom As JObject) As JObject
        Return Api("POST", "/club/events/" & Uri.EscapeDataString(EventId) & "/signup",
                   New JObject(New JProperty("custom", If(Custom, New JObject()))))
    End Function

    ''' <summary>
    ''' 取消报名。
    ''' </summary>
    Public Function ClubCancelSignup(EventId As String) As JObject
        Return Api("POST", "/club/events/" & Uri.EscapeDataString(EventId) & "/cancel-signup", New JObject())
    End Function

    ''' <summary>
    ''' 活动签到：用当前设备令牌对应的账号身份，签到码为现场公开的 6 位数字。
    ''' </summary>
    Public Function ClubCheckin(EventId As String, Code As String) As JObject
        Return Api("POST", "/club/events/" & Uri.EscapeDataString(EventId) & "/checkin",
                   New JObject(New JProperty("code", Code)))
    End Function

    ''' <summary>
    ''' 读取投票详情（含我的投票状态）。
    ''' </summary>
    Public Function ClubPollDetail(PollId As String) As JObject
        Return Api("GET", "/club/polls/" & Uri.EscapeDataString(PollId), Nothing)
    End Function

    ''' <summary>
    ''' 投票；Version 用于防止选项在确认期间被改动。
    ''' </summary>
    Public Function ClubVote(PollId As String, OptionId As String, Version As Integer) As JObject
        Return Api("POST", "/club/polls/" & Uri.EscapeDataString(PollId) & "/vote",
                   New JObject(New JProperty("optionId", OptionId), New JProperty("version", Version)))
    End Function

    '（信息模板编辑与隐私 PIN 已移至官网个人中心：ClubSaveProfile / ClubSendEmailCode /
    '  ClubVerifyPin / ClubSetPin 均为死代码，已删除）

#End Region

End Module
