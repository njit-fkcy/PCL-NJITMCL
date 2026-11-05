Imports System.ServiceProcess
Imports System.Net.NetworkInformation

''' <summary>
''' NJIT MCL 定制模块：MUA 第三方登录与校内 WireGuard 组网。
''' </summary>
Public Module ModNJIT

#Region "常量"

    ''' <summary>
    ''' MUA 第三方认证服务器地址（Yggdrasil API Root）。
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
    ''' 校内组网门户地址（已启用 Let's Encrypt 证书）。
    ''' </summary>
    Public Const WgPortalApi As String = "https://wg.njit-fkcy.top"
    ''' <summary>
    ''' WireGuard 隧道名，同时决定配置文件名与系统服务名。
    ''' </summary>
    Public Const WgTunnelName As String = "NJITMCL"

    ''' <summary>
    ''' 社团官网。
    ''' </summary>
    Public Const ClubHomepage As String = "https://njitmc.club"
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
#Region "路径"

    ''' <summary>
    ''' 便携 WireGuard 组件目录（与 PCL 可执行文件同级）。
    ''' </summary>
    Public ReadOnly Property WgPortableFolder As String
        Get
            Dim Base As String = Path.GetDirectoryName(PathExe) & "\"
            '优先使用 WireGuard 子目录；部分发布包把组件平铺在根目录，需兼容
            If Not FileUtils.Exists(Base & "WireGuard\wireguard.exe") AndAlso
               FileUtils.Exists(Base & "wireguard.exe") Then
                Return Base
            End If
            Return Base & "WireGuard\"
        End Get
    End Property
    ''' <summary>
    ''' 便携 WireGuard 主程序路径。
    ''' </summary>
    Public ReadOnly Property WgPortableExe As String
        Get
            Return WgPortableFolder & "wireguard.exe"
        End Get
    End Property
    ''' <summary>
    ''' WireGuard 配置存放目录。
    ''' </summary>
    Public ReadOnly Property WgConfigFolder As String
        Get
            Return PathPure.Value & "NJITMCL\"
        End Get
    End Property
    ''' <summary>
    ''' WireGuard 配置文件路径，文件名必须与隧道名一致。
    ''' </summary>
    Public ReadOnly Property WgConfigFile As String
        Get
            Return WgConfigFolder & WgTunnelName & ".conf"
        End Get
    End Property
    ''' <summary>
    ''' WireGuard 隧道对应的系统服务名。
    ''' </summary>
    Public ReadOnly Property WgServiceName As String
        Get
            Return "WireGuardTunnel$" & WgTunnelName
        End Get
    End Property

#End Region
#Region "MUA 登录"

    ''' <summary>
    ''' 是否已经使用 MUA 账号成功登录过。
    ''' </summary>
    Public Function IsMuaLoggedIn() As Boolean
        Return Settings.Get(Of Boolean)("MuaLoggedIn")
    End Function

    ''' <summary>
    ''' 是否允许使用社团资源下载：MUA 已登录，或当前选择正版登录。
    ''' </summary>
    Public Function IsAccountAllowed() As Boolean
        If IsMuaLoggedIn() Then Return True
        Return Settings.Get(Of McLoginType)("LoginType") = McLoginType.Ms
    End Function

    ''' <summary>
    ''' 将第三方登录配置指向 MUA，使当前版本启动时注入 authlib-injector。
    ''' 注意：不改写「登录方式」字段，让它保持用户选择的「正版登录或离线登录」。
    ''' </summary>
    Public Sub ApplyMuaAuthSettings()
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
    ''' 在 MUA 登录成功后记录状态，并触发一次校内组网自动连接。
    ''' </summary>
    Public Sub OnMuaLoginSuccess(UserName As String)
        Settings.Set("MuaLoggedIn", True)
        Settings.Set("MuaEmail", UserName)
        ApplyMuaAuthSettings()
        Logger.Info("MUA 登录成功，准备自动连接校内组网")
        RunInThread(AddressOf WgAutoConnectSafe)
    End Sub

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
    ''' 认证服务器是 MUA 时还会触发组网自动连接。
    ''' </summary>
    Public Sub ApplyAuthLogin(Json As JObject, BaseUrl As String, UserName As String, Password As String)
        Dim Profiles = Json("availableProfiles")
        Dim Profile = If(Json("selectedProfile"), Profiles(0))
        Settings.Set("CacheAuthAccess", Json("accessToken").ToString)
        Settings.Set("CacheAuthClient", Json("clientToken").ToString)
        Settings.Set("CacheAuthUuid", Profile("id").ToString)
        Settings.Set("CacheAuthName", Profile("name").ToString)
        Settings.Set("CacheAuthUsername", UserName.Trim)
        Settings.Set("CacheAuthPass", Password)
        '记住账号，下次打开登录页可以直接带出
        Settings.Set("LoginAuthEmail", UserName.Trim)
        If Settings.Get(Of Boolean)("LoginRemember") Then Settings.Set("LoginAuthPass", Password)
        If BaseUrl.StartsWithF(MuaAuthServer) Then OnMuaLoginSuccess(UserName.Trim)
    End Sub

#End Region
#Region "WireGuard"

    ''' <summary>
    ''' 读取本地保存的 WireGuard 配置，没有则返回空字符串。
    ''' </summary>
    Public Function ReadWgConfig() As String
        If FileUtils.Exists(WgConfigFile) Then Return FileUtils.ReadAsString(WgConfigFile) Else Return ""
    End Function

    ''' <summary>
    ''' 保存 WireGuard 配置到本地。
    ''' </summary>
    Public Sub SaveWgConfig(Content As String)
        Directory.CreateDirectory(WgConfigFolder)
        FileUtils.Write(WgConfigFile, Content)
    End Sub

    ''' <summary>
    ''' 使用 MUA 账号向校内组网门户申领临时客户端配置。
    ''' 同一账号 1 小时内重复申领会复用同一份配置，超过 1 小时无握手则由服务器回收。
    ''' </summary>
    Public Function FetchWgConfig(Email As String, Password As String) As String
        Dim Body As New JObject
        Body("username") = Email
        Body("password") = Password
        Dim Response As String = NetRequestByClientRetry(WgPortalApi & "/api/lease", HttpMethod.Post,
            Content:=Body.ToString(), ContentType:="application/json", RequireJson:=True)
        Dim Json As JObject = Response.DeserializeJson()
        If Not Equals(Json("success")?.ToObject(Of Boolean)(), True) Then
            Throw New Exception(If(Json("message")?.ToString(), "校内组网服务器拒绝了本次登录"))
        End If
        Dim Content As String = Json("config")?.ToString()
        If String.IsNullOrWhiteSpace(Content) Then
            Throw New Exception("校内组网服务器未返回 WireGuard 配置，请检查账号状态")
        End If
        Return Content
    End Function

    ''' <summary>
    ''' 使用正版账号向校内组网门户申领临时客户端配置。
    ''' </summary>
    Public Function FetchWgConfigMs(UserName As String) As String
        Dim Body As New JObject
        Body("username") = UserName
        Dim Response As String = NetRequestByClientRetry(WgPortalApi & "/api/lease-ms", HttpMethod.Post,
            Content:=Body.ToString(), ContentType:="application/json", RequireJson:=True)
        Dim Json As JObject = Response.DeserializeJson()
        If Not Equals(Json("success")?.ToObject(Of Boolean)(), True) Then
            Throw New Exception(If(Json("message")?.ToString(), "校内组网服务器拒绝了本次登录"))
        End If
        Dim Content As String = Json("config")?.ToString()
        If String.IsNullOrWhiteSpace(Content) Then
            Throw New Exception("校内组网服务器未返回 WireGuard 配置，请检查账号状态")
        End If
        Return Content
    End Function

    ''' <summary>
    ''' 读取本地 WireGuard 配置中的指定字段，例如 Address、Endpoint、AllowedIPs。
    ''' </summary>
    Public Function WgConfigValue(Key As String) As String
        Dim Content As String = ReadWgConfig()
        If String.IsNullOrWhiteSpace(Content) Then Return ""
        For Each Line As String In Content.Split(New Char() {vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim Trimmed As String = Line.Trim()
            If Trimmed.StartsWith(Key & " =", StringComparison.OrdinalIgnoreCase) Then
                Dim SepIndex As Integer = Trimmed.IndexOf("="c)
                If SepIndex >= 0 Then Return Trimmed.Substring(SepIndex + 1).Trim()
            End If
        Next
        Return ""
    End Function

    ''' <summary>
    ''' 由配置中的私钥推导本机公钥（调用内置 wg.exe）。
    ''' </summary>
    Public Function WgPublicKey() As String
        Static CachedPrivateKey As String = ""
        Static CachedPublicKey As String = ""
        Try
            Dim PrivateKey As String = WgConfigValue("PrivateKey")
            If String.IsNullOrWhiteSpace(PrivateKey) Then Return ""
            If PrivateKey = CachedPrivateKey Then Return CachedPublicKey
            Dim ExePath As String = WgPortableFolder & "wg.exe"
            If Not FileUtils.Exists(ExePath) Then Return ""
            Dim Info As New ProcessStartInfo(ExePath) With {
                .Arguments = "pubkey",
                .RedirectStandardInput = True,
                .RedirectStandardOutput = True,
                .UseShellExecute = False,
                .CreateNoWindow = True
            }
            Using Proc As Process = Process.Start(Info)
                Proc.StandardInput.WriteLine(PrivateKey)
                Proc.StandardInput.Close()
                CachedPublicKey = Proc.StandardOutput.ReadToEnd().Trim()
                Proc.WaitForExit(3000)
            End Using
            CachedPrivateKey = PrivateKey
            Return CachedPublicKey
        Catch ex As Exception
            Logger.Warn(ex, "计算 WireGuard 公钥失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 读取 WireGuard 隧道适配器的累计收发字节数（无需管理员权限）。
    ''' </summary>
    Public Function WgTraffic(ByRef Received As Long, ByRef Sent As Long) As Boolean
        Received = 0
        Sent = 0
        Try
            '优先按隧道名精确匹配，避免误取本机其他 WireGuard 隧道
            Dim Target As NetworkInterface = Nothing
            For Each Adapter As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                If Adapter.Name = WgTunnelName Then
                    Target = Adapter
                    Exit For
                End If
            Next
            If Target Is Nothing Then
                For Each Adapter As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                    If Adapter.Description.Contains("WireGuard") Then
                        Target = Adapter
                        Exit For
                    End If
                Next
            End If
            If Target Is Nothing Then Return False
            Dim Stats As IPv4InterfaceStatistics = Target.GetIPv4Statistics()
            Received = Stats.BytesReceived
            Sent = Stats.BytesSent
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "读取 WireGuard 流量统计失败")
        End Try
        Return False
    End Function

    ''' <summary>
    ''' WireGuard 隧道服务是否已安装。
    ''' </summary>
    Public Function IsWgInstalled() As Boolean
        Try
            Using Controller As New ServiceController(WgServiceName)
                Dim Unused = Controller.Status
            End Using
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

    ''' <summary>
    ''' WireGuard 隧道服务是否正在运行。
    ''' </summary>
    Public Function IsWgConnected() As Boolean
        Try
            Using Controller As New ServiceController(WgServiceName)
                Return Controller.Status = ServiceControllerStatus.Running
            End Using
        Catch ex As Exception
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 当前校内组网状态描述。
    ''' </summary>
    Public Function WgStatusText() As String
        If Not IsMuaLoggedIn() Then Return "尚未登录 MUA 账号"
        If IsWgConnected() Then Return "已连接校内组网"
        If IsWgInstalled() Then Return "隧道已安装，未连接"
        If FileUtils.Exists(WgConfigFile) Then Return "已获取配置，未连接"
        Return "已登录 MUA，尚未获取配置"
    End Function

    ''' <summary>
    ''' 连接校内组网。需要管理员权限，将弹出 UAC 提示。
    ''' </summary>
    Public Sub WgConnect()
        If IsWgConnected() Then Return
        If Not FileUtils.Exists(WgPortableExe) Then Throw New Exception("缺少内置 WireGuard 组件：" & WgPortableExe)
        If Not FileUtils.Exists(WgConfigFile) Then Throw New Exception("尚未获取到 WireGuard 配置")
        '移除旧隧道，确保加载的是最新配置
        If IsWgInstalled() Then RunWgExeAdmin("/uninstalltunnelservice", WgTunnelName)
        'RunWgExeAdmin 会自动给参数加引号，这里不能再自己包一层
        RunWgExeAdmin("/installtunnelservice", WgConfigFile)
        '等待服务启动
        For i As Integer = 1 To 50
            If IsWgConnected() Then Return
            Thread.Sleep(100)
        Next
        Throw New Exception("WireGuard 隧道启动超时，请检查配置是否有效")
    End Sub

    ''' <summary>
    ''' 断开校内组网，并删除本地临时配置。
    ''' </summary>
    Public Sub WgDisconnect()
        If IsWgInstalled() Then RunWgExeAdmin("/uninstalltunnelservice", WgTunnelName)
        Try
            If FileUtils.Exists(WgConfigFile) Then IO.File.Delete(WgConfigFile)
        Catch ex As Exception
            Logger.Warn(ex, "删除本地 WireGuard 临时配置失败")
        End Try
    End Sub

    ''' <summary>
    ''' 程序退出时断开校内组网。失败只记录日志，不阻塞退出流程。
    ''' </summary>
    Public Sub WgDisconnectOnExit()
        Try
            If Not IsWgInstalled() Then Return
            Logger.Info("程序退出，正在断开校内组网")
            WgDisconnect()
            If IsWgInstalled() Then
                Logger.Warn("已尝试断开校内组网，但隧道服务仍然存在")
            Else
                Logger.Info("已断开校内组网，隧道服务已移除")
            End If
        Catch ex As Exception
            Logger.Warn(ex, "退出时断开校内组网失败")
        End Try
    End Sub

    ''' <summary>
    ''' 统一便携 WireGuard 组件布局：子目录存在时删掉根目录平铺的副本。
    ''' </summary>
    Public Sub NormalizeWgLayout()
        Try
            Dim Base As String = Path.GetDirectoryName(PathExe) & "\"
            If Not FileUtils.Exists(Base & "WireGuard\wireguard.exe") Then Return
            For Each FileName As String In New String() {"wg.exe", "wireguard.exe"}
                Dim FlatPath As String = Base & FileName
                If FileUtils.Exists(FlatPath) Then
                    FileUtils.Delete(FlatPath)
                    Logger.Info($"已清理根目录平铺的 WireGuard 组件：{FileName}")
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, "统一 WireGuard 组件布局失败")
        End Try
    End Sub

    ''' <summary>
    ''' 以管理员权限调用便携 WireGuard。
    ''' </summary>
    Private Sub RunWgExeAdmin(Argument As String, Value As String)
        Dim Info As New ProcessStartInfo(WgPortableExe) With {
            .Arguments = Argument & " """ & Value & """",
            .UseShellExecute = True,
            .Verb = "runas",
            .WindowStyle = ProcessWindowStyle.Hidden
        }
        Try
            Using Proc As Process = Process.Start(Info)
                Proc.WaitForExit()
                If Proc.ExitCode <> 0 Then Throw New Exception("WireGuard 返回错误代码 " & Proc.ExitCode)
            End Using
        Catch ex As System.ComponentModel.Win32Exception
            '1223：用户取消了 UAC 提权；2：找不到文件（组件缺失或被安全软件拦截）
            If ex.NativeErrorCode = 2 Then Throw New Exception("缺少内置 WireGuard 组件：" & WgPortableExe)
            Throw New Exception("已取消管理员授权，无法操作校内组网")
        End Try
    End Sub

    ''' <summary>
    ''' 自动连接校内组网：每次连接都向服务器申领临时客户端，断开时删除本地配置。
    ''' </summary>
    Public Function WgAutoConnect() As String
        If IsWgConnected() Then Return WgStatusText()
        If IsMuaLoggedIn() Then
            Dim Email As String = Settings.Get(Of String)("CacheAuthUsername")
            Dim Password As String = Settings.Get(Of String)("CacheAuthPass")
            If String.IsNullOrWhiteSpace(Email) OrElse String.IsNullOrWhiteSpace(Password) Then
                Return "尚未使用 MUA 登录"
            End If
            SaveWgConfig(FetchWgConfig(Email, Password))
        ElseIf Settings.Get(Of McLoginType)("LoginType") = McLoginType.Ms Then
            Dim MsName As String = Settings.Get(Of String)("CacheMsV2Name")
            If String.IsNullOrWhiteSpace(MsName) Then Return "尚未登录正版账号"
            SaveWgConfig(FetchWgConfigMs(MsName))
        Else
            Return "尚未登录正版或 MUA 账号"
        End If
        Logger.Info("已从校内组网服务器申领临时客户端配置")
        WgConnect()
        Return WgStatusText()
    End Function

    ''' <summary>
    ''' 自动连接的后台包装，失败只记录日志，不打断主流程。
    ''' </summary>
    Public Sub WgAutoConnectSafe()
        Try
            '登录动作刚结束就申领容易撞上服务端 1 秒限流，稍等一下再连
            Thread.Sleep(1800)
            Logger.Info("开始自动连接校内组网：" & WgAutoConnect())
        Catch ex As Exception
            Logger.Warn(ex, "自动连接校内组网失败")
        End Try
    End Sub

#End Region

#Region "服务器整合包"

    ''' <summary>
    ''' 社团站点的整合包接口地址（可用环境变量 PCL_PACK_API 指向测试站）。
    ''' </summary>
    Public ReadOnly PackApiUrl As String =
        If(Environment.GetEnvironmentVariable("PCL_PACK_API"), ClubHomepage & "/api/launcher/servers")

    ''' <summary>
    ''' 拉取在档服务器与整合包列表。
    ''' </summary>
    Public Function FetchPackServers() As JArray
        Dim Raw As String = NetRequestByClientRetry(PackApiUrl, RequireJson:=True)
        Dim Json As JObject = Raw.DeserializeJson()
        Return If(TryCast(Json("servers"), JArray), New JArray())
    End Function

    ''' <summary>
    ''' 实测单个下载源的速率（字节/秒），失败返回 0。只取前 1 MB 计时。
    ''' </summary>
    Public Function MeasurePackSource(Url As String, TimeoutMs As Integer) As Double
        Try
            Dim Req As Net.HttpWebRequest = DirectCast(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = TimeoutMs
            Req.ReadWriteTimeout = TimeoutMs
            Req.AllowAutoRedirect = True
            Req.UserAgent = $"PCL2/{VersionBaseName}.{CInt(BuildType)}"
            Req.AddRange(0, 1048575)
            Dim Watch As Stopwatch = Stopwatch.StartNew()
            Using Resp As Net.HttpWebResponse = DirectCast(Req.GetResponse(), Net.HttpWebResponse)
                Dim Code As Integer = CInt(Resp.StatusCode)
                If Code < 200 OrElse Code >= 400 Then Return 0
                Using Stream As IO.Stream = Resp.GetResponseStream()
                    Dim Buffer(65535) As Byte
                    Dim ReadTotal As Long = 0
                    Do While ReadTotal < 1048576 AndAlso Watch.ElapsedMilliseconds < TimeoutMs
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
    ''' 并行实测各源速率，返回下载顺序：第一个源（校内组网）可用时优先，其余按速率从高到低。
    ''' </summary>
    Public Function RankPackSources(Urls As List(Of String)) As List(Of String)
        If Urls Is Nothing OrElse Urls.Count = 0 Then Return New List(Of String)
        Dim Speeds(Urls.Count - 1) As Double
        Dim Tasks As New List(Of Task)
        For Index = 0 To Urls.Count - 1
            Dim Current As Integer = Index
            Tasks.Add(Task.Run(Sub() Speeds(Current) = MeasurePackSource(Urls(Current), 5000)))
        Next
        Try
            Task.WaitAll(Tasks.ToArray(), 5600)
        Catch
            ' 超时就按已完成的结果排序
        End Try
        For Index = 0 To Urls.Count - 1
            Logger.Info($"整合包源测速：{Urls(Index)} → {Speeds(Index) / 1024:F0} KB/s")
        Next
        Dim Ordered As New List(Of String)
        If Speeds(0) > 0 Then Ordered.Add(Urls(0))
        Dim Rest = Enumerable.Range(0, Urls.Count).
            Where(Function(I) Not (I = 0 AndAlso Speeds(0) > 0)).
            OrderByDescending(Function(I) Speeds(I))
        For Each Index In Rest
            Ordered.Add(Urls(Index))
        Next
        Return Ordered
    End Function

    ''' <summary>
    ''' 整合包下载的取消控制。
    ''' </summary>
    Public Class PackDownloadControl
        Public Cancelled As Boolean = False
        Public Request As Net.HttpWebRequest = Nothing

        Public Sub Cancel()
            Cancelled = True
            Try
                Request?.Abort()
            Catch
                ' 忽略中止异常
            End Try
        End Sub
    End Class

    ''' <summary>
    ''' 下载整合包到本地，带进度回调；下载后按 SHA256 校验。
    ''' </summary>
    Public Sub DownloadPackFile(Url As String, Target As String, ExpectedHash As String,
                                Progress As Action(Of Long, Long), Optional Control As PackDownloadControl = Nothing)
        Try
            DownloadPackFileCore(Url, Target, ExpectedHash, Progress, Control)
        Catch ex As Exception
            If Control IsNot Nothing AndAlso Control.Cancelled Then Throw New OperationCanceledException()
            Throw
        End Try
    End Sub

    Private Sub DownloadPackFileCore(Url As String, Target As String, ExpectedHash As String,
                                     Progress As Action(Of Long, Long), Control As PackDownloadControl)
        If Control IsNot Nothing AndAlso Control.Cancelled Then Throw New OperationCanceledException()
        Dim Req As Net.HttpWebRequest = DirectCast(Net.WebRequest.Create(Url), Net.HttpWebRequest)
        Req.Method = "GET"
        Req.Timeout = 15000
        Req.ReadWriteTimeout = 60000
        Req.AllowAutoRedirect = True
        Req.UserAgent = $"PCL2/{VersionBaseName}.{CInt(BuildType)}"
        If Control IsNot Nothing Then Control.Request = Req
        Using Resp As Net.HttpWebResponse = DirectCast(Req.GetResponse(), Net.HttpWebResponse)
            Dim Total As Long = Resp.ContentLength
            Using Stream As IO.Stream = Resp.GetResponseStream()
                Using TargetStream As New IO.FileStream(Target, IO.FileMode.Create, IO.FileAccess.Write, IO.FileShare.None)
                    Dim Buffer(65535) As Byte
                    Dim Done As Long = 0
                    Do
                        If Control IsNot Nothing AndAlso Control.Cancelled Then
                            Req.Abort()
                            Throw New OperationCanceledException()
                        End If
                        Dim ReadCount As Integer = Stream.Read(Buffer, 0, Buffer.Length)
                        If ReadCount <= 0 Then Exit Do
                        TargetStream.Write(Buffer, 0, ReadCount)
                        Done += ReadCount
                        If Progress IsNot Nothing Then Progress(Done, Total)
                    Loop
                End Using
            End Using
        End Using
        If ExpectedHash <> "" Then
            Dim Actual As String = GetPackSha256(Target)
            If Not Equals(Actual, ExpectedHash.ToLowerInvariant()) Then
                FileUtils.Delete(Target)
                Throw New Exception("整合包校验失败（SHA256 不一致）")
            End If
        End If
    End Sub

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

#End Region

End Module
