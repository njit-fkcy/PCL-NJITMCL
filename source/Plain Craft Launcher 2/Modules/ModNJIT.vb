Imports System.ServiceProcess
Imports System.Net.NetworkInformation

''' <summary>
''' NJIT MCL 定制模块：MUA 第三方登录与社团账号对接。
''' </summary>
Public Module ModNJIT

#Region "常量"

    ''' <summary>
    ''' MUA 第三方认证服务器地址（Yggdrasil API Root）。
    ''' 登录页会自行拼 /authserver，因此这里不能带 authserver 后缀。
    ''' </summary>
    Public Const MuaAuthServer As String = "https://skin.mualliance.ltd/api/yggdrasil"
    ''' <summary>
    ''' MUA 在登录页显示的名称。
    ''' </summary>
    Public Const MuaServerName As String = "MUA 用户中心"
    ''' <summary>
    ''' MUA 账号注册页面。
    ''' </summary>
    Public Const MuaRegisterUrl As String = "https://skin.mualliance.ltd/auth/register"


    ''' <summary>
    ''' 社团网站地址。测试时可用环境变量 NJITMCL_SITE 覆盖，发布版默认正式站。
    ''' </summary>
    Public ReadOnly Property ClubHomepage As String
        Get
            Dim SiteOverride As String = Environment.GetEnvironmentVariable("NJITMCL_SITE")
            If Not String.IsNullOrWhiteSpace(SiteOverride) Then Return SiteOverride.TrimEnd("/"c)
            Return "https://njitmc.club"
        End Get
    End Property
    ''' <summary>
    ''' 注册与入服教程。
    ''' </summary>
    Public Const ClubGuideUrl As String = "https://njitmc.club/#guidedetails?id=1"
    ''' <summary>
    ''' 社团反馈页面。
    ''' </summary>
    Public Const ClubFeedbackUrl As String = "https://njitmc.club/#feedback"
    ''' <summary>
    ''' MUA 皮肤站。
    ''' </summary>
    Public Const MuaHomepage As String = "https://skin.mualliance.ltd/"

#End Region
#Region "MUA 登录"

    ''' <summary>
    ''' 是否已经使用 MUA 账号成功登录过。
    ''' </summary>
    Public Function IsMuaLoggedIn() As Boolean
        Return Settings.Get(Of Boolean)("MuaLoggedIn")
    End Function

    ''' <summary>
    ''' 是否允许使用社团资源下载：MUA 已登录，或正版已登录（有 AccessToken），二者有其一即可。
    ''' </summary>
    Public Function IsAccountAllowed() As Boolean
        If IsMuaLoggedIn() Then Return True
        If Not String.IsNullOrWhiteSpace(Settings.Get(Of String)("CacheMsV2Access")) Then Return True
        Return Settings.Get(Of McLoginType)("LoginType") = McLoginType.Ms
    End Function

    ''' <summary>
    ''' 将第三方登录配置指向 MUA，使当前版本启动时注入 authlib-injector。
    ''' 注意：不改写「登录方式」字段，让它保持用户选择的「正版登录或离线登录」。
    ''' </summary>
    Public Sub ApplyMuaAuthSettings()
        '版本已明确选择第三方登录（LittleSkin 等）时，不覆盖其认证服务器配置
        If McInstanceSelected IsNot Nothing AndAlso
           Settings.Get(Of Integer)("VersionServerLogin", Instance:=McInstanceSelected) = 4 Then
            Dim Custom As String = Settings.Get(Of String)("VersionServerAuthServer", Instance:=McInstanceSelected)
            If Not String.IsNullOrWhiteSpace(Custom) AndAlso Custom <> MuaAuthServer Then Return
        End If
        Settings.Set("CacheAuthServerServer", MuaAuthServer)
        Settings.Set("CacheAuthServerName", MuaServerName)
        Settings.Set("CacheAuthServerRegister", MuaRegisterUrl)
        If McInstanceSelected IsNot Nothing Then
            Settings.Set("VersionServerAuthServer", MuaAuthServer, Instance:=McInstanceSelected)
            Settings.Set("VersionServerAuthName", MuaServerName, Instance:=McInstanceSelected)
            Settings.Set("VersionServerAuthRegister", MuaRegisterUrl, Instance:=McInstanceSelected)
        End If
    End Sub

    ''' <summary>
    ''' 当前生效的认证服务器地址。
    ''' 版本明确启用第三方登录（VersionServerLogin = 4）时读取该版本的配置；
    ''' 其余情况（含 MUA 登录）固定为 MUA，避免配置为空拼出相对 URI。
    ''' </summary>
    Public Function GetAuthServer(Optional Target As McInstance = Nothing) As String
        Dim Instance As McInstance = If(Target, McInstanceSelected)
        If Instance IsNot Nothing AndAlso Settings.Get(Of Integer)("VersionServerLogin", Instance:=Instance) = 4 Then
            Dim Custom As String = Settings.Get(Of String)("VersionServerAuthServer", Instance:=Instance)
            If Not String.IsNullOrWhiteSpace(Custom) Then Return Custom
        End If
        Return MuaAuthServer
    End Function

    ''' <summary>
    ''' 当前生效的认证服务器显示名：第三方登录读版本配置，其余固定为 MUA。
    ''' </summary>
    Public Function GetAuthName(Optional Target As McInstance = Nothing) As String
        Dim Instance As McInstance = If(Target, McInstanceSelected)
        If Instance IsNot Nothing AndAlso Settings.Get(Of Integer)("VersionServerLogin", Instance:=Instance) = 4 Then
            Dim Custom As String = Settings.Get(Of String)("VersionServerAuthName", Instance:=Instance)
            If Not String.IsNullOrWhiteSpace(Custom) Then Return Custom
        End If
        Return MuaServerName
    End Function

    ''' <summary>
    ''' 当前生效的注册链接：第三方登录读版本配置，其余固定为 MUA。
    ''' </summary>
    Public Function GetAuthRegister(Optional Target As McInstance = Nothing) As String
        Dim Instance As McInstance = If(Target, McInstanceSelected)
        If Instance IsNot Nothing AndAlso Settings.Get(Of Integer)("VersionServerLogin", Instance:=Instance) = 4 Then
            Dim Custom As String = Settings.Get(Of String)("VersionServerAuthRegister", Instance:=Instance)
            If Not String.IsNullOrWhiteSpace(Custom) Then Return Custom
        End If
        Return MuaRegisterUrl
    End Function

    ''' <summary>
    ''' 在 MUA 登录成功后记录状态。（MUA 密码不再落盘，仅保留登录状态与账号名）
    ''' </summary>
    Public Sub OnMuaLoginSuccess(UserName As String)
        Settings.Set("MuaLoggedIn", True)
        Settings.Set("MuaEmail", UserName)
        ApplyMuaAuthSettings()
    End Sub

    ''' <summary>
    ''' 把启动器里的登录凭证交给社团网站，返回一次性登录码。必须在后台线程调用。
    ''' 正版与 MUA 身份可以同时带上，网站分别校验后自动绑定到同一个账号。
    ''' </summary>
    Public Function RequestSiteLogin(MojangAccess As String, MojangUuid As String, MojangName As String,
                                     Optional MuaAccess As String = "", Optional MuaUuid As String = "",
                                     Optional MuaName As String = "", Optional MuaUserName As String = "") As String
        Dim Request As New JObject()
        If Not String.IsNullOrWhiteSpace(MojangAccess) Then
            Request("mojang") = New JObject(
                New JProperty("accessToken", MojangAccess),
                New JProperty("uuid", If(MojangUuid, "")),
                New JProperty("name", If(MojangName, "")))
        End If
        If Not String.IsNullOrWhiteSpace(MuaAccess) Then
            Request("mua") = New JObject(
                New JProperty("accessToken", MuaAccess),
                New JProperty("uuid", If(MuaUuid, "")),
                New JProperty("name", If(MuaName, "")),
                New JProperty("username", If(MuaUserName, "")))
        End If
        If Request.Count = 0 Then Throw New Exception("请先在启动器登录正版或 MUA 账号")
        Dim JsonText As String = NetRequestByClient(ClubHomepage & "/api/account/launcher-login", HttpMethod.Post,
            Content:=Request.ToString(Newtonsoft.Json.Formatting.None),
            Headers:={{"Accept-Language", "zh-CN"}},
            ContentType:="application/json; charset=utf-8", RequireJson:=True, Timeout:=20000)
        Dim Json As JObject = JsonText.DeserializeJson()
        'MUA 的 refresh 会轮换 accessToken，网站把新值带回来，写回缓存避免下次失效
        Dim NewMua As String = Json("muaAccessToken")?.ToString()
        If Not String.IsNullOrWhiteSpace(NewMua) Then Settings.Set("CacheAuthAccess", NewMua)
        Dim Code As String = Json("code")?.ToString()
        If String.IsNullOrWhiteSpace(Code) Then Throw New Exception("网站没有返回登录码")
        Return Code
    End Function

    ''' <summary>
    ''' 请求第三方认证服务器完成账号认证。会阻塞，必须在后台线程调用。
    ''' </summary>
    ''' <param name="BaseUrl">认证服务器地址，形如 https://…/authserver。</param>
    Public Function MuaAuthenticate(BaseUrl As String, UserName As String, Password As String) As JObject
        If String.IsNullOrWhiteSpace(UserName) Then Throw New Exception("账号不能为空")
        If String.IsNullOrWhiteSpace(Password) Then Throw New Exception("密码不能为空")
        If String.IsNullOrWhiteSpace(BaseUrl) Then Throw New Exception("还没有配置认证服务器地址")
        Dim Request As New JObject(
            New JProperty("agent", New JObject(New JProperty("name", "Minecraft"), New JProperty("version", 1))),
            New JProperty("username", UserName.Trim),
            New JProperty("password", Password),
            New JProperty("requestUser", True))
        Dim JsonText As String = ""
        Dim LastError As Exception = Nothing
        'MUA 服务器较慢时 10 秒默认超时不够，这里用单次 25 秒、最多两次
        For Attempt As Integer = 1 To 2
            Try
                JsonText = NetRequestByClient(BaseUrl.TrimEnd("/"c) & "/authenticate", HttpMethod.Post,
                    Content:=Request.ToString(Newtonsoft.Json.Formatting.None),
                    Headers:={{"Accept-Language", "zh-CN"}},
                    ContentType:="application/json; charset=utf-8", RequireJson:=True, Timeout:=25000)
                LastError = Nothing
                Exit For
            Catch ex As HttpRequestCodeException
                '服务器明确回绝时直接显示它给出的原因，不做无意义重试
                Dim Reason As String = ""
                Try
                    Reason = If(ex.Response?.Trim().DeserializeJson()?("errorMessage")?.ToString(), "")
                Catch
                End Try
                Throw New Exception(If(Reason <> "", Reason, ex.Message))
            Catch ex As Exception
                LastError = ex
                Logger.Warn(ex, $"MUA 认证请求失败（第 {Attempt} 次）")
            End Try
        Next
        If LastError IsNot Nothing Then Throw New Exception("连接认证服务器失败，请检查网络后重试：" & LastError.Message)
        Dim Json As JObject = JsonText.DeserializeJson()
        Dim Profiles = Json("availableProfiles")
        If Profiles Is Nothing OrElse Profiles.Count = 0 Then Throw New Exception("该账号还没有创建角色，请先在用户中心创建角色")
        Return Json
    End Function

    ''' <summary>
    ''' 把认证结果写入登录缓存。必须在 UI 线程调用，否则登录页读到的仍是旧状态。
    ''' </summary>
    Public Sub ApplyAuthLogin(Json As JObject, BaseUrl As String, UserName As String, Password As String)
        If Json Is Nothing Then Throw New Exception("认证服务器没有返回数据")
        Dim AccessToken As String = Json("accessToken")?.ToString()
        Dim ClientToken As String = Json("clientToken")?.ToString()
        If String.IsNullOrWhiteSpace(AccessToken) Then Throw New Exception("认证服务器未返回登录凭证，请稍后再试")
        Dim Profiles = Json("availableProfiles")
        If Profiles Is Nothing OrElse Profiles.Count = 0 Then Throw New Exception("该账号还没有创建角色，请先在用户中心创建角色")
        Dim Profile = If(Json("selectedProfile"), Profiles(0))
        Dim ProfileId As String = Profile?("id")?.ToString()
        Dim ProfileName As String = Profile?("name")?.ToString()
        If String.IsNullOrWhiteSpace(ProfileId) OrElse String.IsNullOrWhiteSpace(ProfileName) Then
            Throw New Exception("认证服务器返回的角色信息不完整，请稍后再试")
        End If
        Settings.Set("CacheAuthAccess", AccessToken)
        If Not String.IsNullOrWhiteSpace(ClientToken) Then Settings.Set("CacheAuthClient", ClientToken)
        Settings.Set("CacheAuthUuid", ProfileId)
        Settings.Set("CacheAuthName", ProfileName)
        Settings.Set("CacheAuthUsername", UserName.Trim)
        Settings.Set("CacheAuthPass", Password)
        '记住账号，下次打开登录页可以直接带出
        Settings.Set("LoginAuthEmail", UserName.Trim)
        If Settings.Get(Of Boolean)("LoginRemember") Then Settings.Set("LoginAuthPass", Password)
        If BaseUrl.StartsWithF(MuaAuthServer) Then OnMuaLoginSuccess(UserName.Trim)
    End Sub

#End Region

#Region "服务器整合包"

    ''' <summary>
    ''' 社团站点的整合包接口地址（可用环境变量 PCL_PACK_API 指向测试站）。
    ''' </summary>
    Public ReadOnly PackApiUrl As String =
        WithPackVersion(If(Environment.GetEnvironmentVariable("PCL_PACK_API"), ClubHomepage & "/api/launcher/servers"))

    ''' <summary>
    ''' 给接口地址附加缓存版本参数。
    ''' 旧版启动器会在本地留下 CacheCow 缓存，文件一旦损坏，服务端改 no-store 也删不掉，
    ''' 因此用参数换一个缓存键，让残留的坏缓存永久失效。
    ''' </summary>
    Private Function WithPackVersion(Url As String) As String
        Return Url & If(Url.Contains("?"), "&", "?") & "pack=2"
    End Function

    ''' <summary>
    ''' 拉取在档服务器与整合包列表。
    ''' </summary>
    Public Function FetchPackServers() As JArray
        Dim Raw As String = NetRequestByClientRetry(PackApiUrl, RequireJson:=True)
        Dim Json As JObject = Raw.DeserializeJson()
        Return If(TryCast(Json("servers"), JArray), New JArray())
    End Function

#Region "整合包源预热"

    ''' <summary>校内整合包源是否可达（后台预热结果）。</summary>
    Public Property CampusSourceReachable As Boolean = False

    ''' <summary>是否已完成一次校内源探测；未探测时不能把 CampusSourceReachable=False 当成「不可用」。</summary>
    Public Property CampusSourceProbed As Boolean = False

    Private ReadOnly WarmLock As New Object()

    ''' <summary>预热测得的 GitHub 直链速度（字节/秒）。中转不再测速。</summary>
    Public Property PackDirectSpeed As Double = 0

    ''' <summary>直链测速用的样本文件（跟随当前版本号）。</summary>
    Private ReadOnly WarmTestUrl As String = $"https://github.com/njit-fkcy/PCL-NJITMCL/releases/download/v{VersionBaseName}/PCL-NJITMCL.exe"

    Private Property PackWarmStamp As Long = 0
    Private Const PackWarmTtlMs As Long = 600000L
    ''' <summary>直链测不通时的预热有效期：短一些，网络恢复后能尽快重新测速。</summary>
    Private Const PackWarmFailTtlMs As Long = 60000L

    ''' <summary>作废预热结果（下载失败时调用），下次选源会重新测直链。</summary>
    Public Sub InvalidatePackWarm()
        SyncLock WarmLock
            PackWarmStamp = 0
        End SyncLock
    End Sub

    ''' <summary>后台静默预热：先探校内源，通就直接用校内；不通才预热 GitHub 优选并测一次直链。</summary>
    Public Sub WarmPackSources()
        RunInNewThread(
        Sub()
            Try
                Dim Reachable As Boolean = ProbeCampusSource()
                SyncLock WarmLock
                    CampusSourceReachable = Reachable
                    CampusSourceProbed = True
                    PackWarmStamp = Environment.TickCount
                End SyncLock
                If Reachable Then
                    Logger.Info("整合包源预热：校内源可用，优先校内")
                    Return
                End If
                '先测直链：走社团服务器换签名地址，不依赖 github.com 主站，启动初期就能出结果
                Dim Direct As Double = MeasurePackSource(WarmTestUrl, 15000)
                If Direct < 1048576.0 Then
                    Dim Retry As Double = MeasurePackSource(WarmTestUrl, 15000)
                    If Retry > Direct Then Direct = Retry
                End If
                SyncLock WarmLock
                    PackDirectSpeed = Direct
                End SyncLock
                Logger.Info($"整合包源预热：GitHub 直链 {Direct / 1024:F0} KB/s")
                '再预热 IP 优选，供后续本地代理连接使用
                Logger.Info("整合包源预热：开始 GitHub IP 优选")
                ModGithubIp.GetRanked("github.com")
                ModGithubIp.GetRanked("release-assets.githubusercontent.com")
            Catch ex As Exception
                Logger.Warn(ex, "整合包源预热失败")
            End Try
        End Sub, "PackWarm")
    End Sub

    ''' <summary>探测校内源是否可达：3 秒内能拿到响应就算通。</summary>
    Private Function ProbeCampusSource() As Boolean
        Try
            Dim Req As Net.HttpWebRequest = DirectCast(Net.WebRequest.Create("https://wg.njit-fkcy.top/modpacks/"), Net.HttpWebRequest)
            Req.Method = "HEAD"
            Req.Timeout = 3000
            Req.ReadWriteTimeout = 3000
            Using Resp As Net.HttpWebResponse = DirectCast(Req.GetResponse(), Net.HttpWebResponse)
                Return CInt(Resp.StatusCode) < 500
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>是否为校园内网的整合包源。</summary>
    Public Function IsCampusUrl(Url As String) As Boolean
        If String.IsNullOrWhiteSpace(Url) Then Return False
        Return Url.IndexOf("wg.njit-fkcy.top", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

#End Region
#End Region

    Private Function PackWarmValid() As Boolean
        SyncLock WarmLock
            If PackWarmStamp <= 0 Then Return False
            Dim Ttl As Long = If(PackDirectSpeed > 0, PackWarmTtlMs, PackWarmFailTtlMs)
            Return CLng(Environment.TickCount) - PackWarmStamp < Ttl
        End SyncLock
    End Function

    ''' <summary>
    ''' 实测单个下载源的速率（字节/秒），失败返回 0。取前 4 MB 计时，连接与跳转耗时不计入。
    ''' 仅用于 GitHub 直链；中转不再测速。
    ''' </summary>
    Public Function MeasurePackSource(Url As String, TimeoutMs As Integer) As Double
        Try
            Dim Target As String = Url
            If Target.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) Then
                Dim Resolved As String = ResolveGithubAssetUrl(Target)
                '一律走签名地址：解析不出来说明社团服务器或数据面当时不可用，再去打被墙的主站只会白等
                If Resolved = "" Then Return 0
                Target = Resolved
            End If
            Dim Req As Net.HttpWebRequest = DirectCast(Net.WebRequest.Create(Target), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Proxy = ModGithubProxy.Proxy
            Req.Timeout = TimeoutMs
            Req.ReadWriteTimeout = TimeoutMs
            Req.AllowAutoRedirect = True
            Req.UserAgent = $"PCL2/{VersionBaseName}.{CInt(BuildType)}"
            Req.AddRange(0, 4194303)
            Using Resp As Net.HttpWebResponse = DirectCast(Req.GetResponse(), Net.HttpWebResponse)
                Dim Code As Integer = CInt(Resp.StatusCode)
                If Code < 200 OrElse Code >= 400 Then Return 0
                '建连、302 跳转、TLS 握手不计入：这里要的是吞吐，不是首字节延迟
                Dim Watch As Stopwatch = Stopwatch.StartNew()
                Using Stream As IO.Stream = Resp.GetResponseStream()
                    Dim Buffer(65535) As Byte
                    Dim ReadTotal As Long = 0
                    Do While ReadTotal < 4194304 AndAlso Watch.ElapsedMilliseconds < TimeoutMs
                        Dim Count As Integer = Stream.Read(Buffer, 0, Buffer.Length)
                        If Count <= 0 Then Exit Do
                        ReadTotal += Count
                    Loop
                    Dim Elapsed As Long = Math.Max(1L, Watch.ElapsedMilliseconds)
                    If ReadTotal <= 0 Then Return 0
                    Return ReadTotal * 1000.0 / Elapsed
                End Using
            End Using
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' 返回下载顺序：只测 GitHub 直链。直链够快就排第一，否则按服务端下发的顺序走中转。
    ''' 中转不再由客户端测速，改由网站后台统一配置与排序。
    ''' </summary>
    Public Function RankPackSources(Urls As List(Of String)) As List(Of String)
        If Urls Is Nothing OrElse Urls.Count = 0 Then Return New List(Of String)

        Dim DirectIndex As Integer = -1
        For Index = 0 To Urls.Count - 1
            If IsGithubDirectUrl(Urls(Index)) Then
                DirectIndex = Index
                Exit For
            End If
        Next
        '没有 GitHub 直链：完全按服务端顺序
        If DirectIndex < 0 Then Return New List(Of String)(Urls)

        '直链速度优先复用预热结果，预热过期再现场测一次
        Dim DirectSpeed As Double = If(PackWarmValid(), PackDirectSpeed, MeasurePackSource(Urls(DirectIndex), 20000))

        Dim Ordered As New List(Of String)
        '测不出速度（0）说明是测速链路本身没通，不代表直链不能下：这种情况仍先试直链
        If DirectSpeed <= 0 OrElse DirectSpeed >= 2097152.0 Then
            '直链可用或测不出：直链排第一，其余按服务端顺序
            Ordered.Add(Urls(DirectIndex))
            For Each One As String In Urls
                If One <> Urls(DirectIndex) Then Ordered.Add(One)
            Next
            Logger.Info($"整合包选源：GitHub 直链 {DirectSpeed / 1024:F0} KB/s，直链优先")
        Else
            '直链确实偏慢：按服务端顺序先走中转，直链放最后兜底
            For Each One As String In Urls
                If One <> Urls(DirectIndex) Then Ordered.Add(One)
            Next
            Ordered.Add(Urls(DirectIndex))
            Logger.Info($"整合包选源：GitHub 直链 {DirectSpeed / 1024:F0} KB/s，改用服务端下发的中转")
        End If
        Return Ordered
    End Function

    ''' <summary>
    ''' 计算文件 SHA256（小写十六进制）。
    ''' </summary>
    Public Function GetPackSha256(Target As String) As String
        Using Stream As New IO.FileStream(Target, IO.FileMode.Open, IO.FileAccess.Read, IO.FileShare.Read)
            Using Hasher As Security.Cryptography.SHA256 = Security.Cryptography.SHA256.Create()
                Return BitConverter.ToString(Hasher.ComputeHash(Stream)).Replace("-", "").ToLowerInvariant()
            End Using
        End Using
    End Function

End Module
