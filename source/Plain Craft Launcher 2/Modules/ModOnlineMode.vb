Imports System.Management
Imports System.Net.Sockets

''' <summary>
''' 联机「进服校验」开关。
'''
''' 原理：向正在运行的服务端 JVM 挂载一个 Java 代理，代理改写 authlib 的
''' hasJoinedServer 方法。只保留两档：正版（原样执行）、双验证（先问正版，
''' 问不到再问 MUA）。
'''
''' 与改 server.properties 的区别：不落盘、不重启、随时可逆。
''' 与改 online-mode 字段的区别：不依赖混淆映射，跨版本稳定。
''' </summary>
Public Module ModOnlineMode

#Region "路径与常量"

    ''' <summary>代理主包，内含 ByteBuddy，放在 PCL 文件夹。</summary>
    Public ReadOnly Property AgentJarPath As String
        Get
            Return IO.Path.Combine(IO.Path.GetDirectoryName(PathExe), "PCL", "njit-online-agent.jar")
        End Get
    End Property

    ''' <summary>运行时挂载器，只有这一个类，放在 PCL 文件夹。</summary>
    Public ReadOnly Property AttachJarPath As String
        Get
            Return IO.Path.Combine(IO.Path.GetDirectoryName(PathExe), "PCL", "njit-online-attach.jar")
        End Get
    End Property

    ''' <summary>校验模式，与 Java 代理的取值一致。只保留两档。</summary>
    Public Const ModeMojang As Integer = 1
    ''' <summary>双验证：先问 Mojang，问不到再问 MUA；同名时正版身份胜出。</summary>
    Public Const ModeDualMojangFirst As Integer = 4

    ''' <summary>是否需要 MUA 地址（仅双验证）。</summary>
    Private Function UsesExternal(Mode As Integer) As Boolean
        Return Mode = ModeDualMojangFirst
    End Function

    ''' <summary>排除客户端进程的特征串：整合包客户端同样带版本路径，必须靠这些区分。</summary>
    Private ReadOnly ClientMarkers As String() = {
        "--username", "--gameDir", "--assetsDir", "--assetIndex",
        "net.minecraft.client.main.Main", "launchwrapper"}

    ''' <summary>判定为服务端的特征串。</summary>
    Private ReadOnly ServerMarkers As String() = {
        "server.jar", "minecraft_server", "fabric-server", "paper-", "paper.jar",
        "spigot", "purpur", "velocity", "bungeecord", "-server"}

    ''' <summary>启动器最近一次启动的游戏进程号。LAN 的集成服务端就在这个进程里。</summary>
    Private GameProcessId As Integer = 0

    ''' <summary>
    ''' 启动器自己拉起来的游戏进程：进程号 → 启动时刻。
    '''
    ''' 只认这些进程 —— 房间会把本机端口经社团中继转发出去，如果随便哪个进程都能挂上去，
    ''' 这个中继就成了免费 frp：谁都能拿它转发自己的任意服务（22、数据库、反代…）。
    ''' 记住启动时刻是为了防 PID 回收：同一个号被别的进程重用时不认。
    ''' </summary>
    Private ReadOnly LaunchedAt As New Dictionary(Of Integer, Date)

    ''' <summary>
    ''' 手动指定的游戏进程；0 = 自动。
    '''
    ''' 为什么需要它：同时开着两个客户端时（房主一个、自己另开一个进别人的房间），
    ''' 自动探测可能命中「进来的那台」，模式就推错了进程。房间里选一下最省事。
    ''' </summary>
    Public PinnedPid As Integer = 0

    ''' <summary>改绑定进程；0 表示回到自动。重复设置同一个值不动作。</summary>
    Public Sub Pin(Pid As Integer)
        If PinnedPid = Pid Then Return
        PinnedPid = Pid
        Logger.Info("进服校验：绑定进程改为 " & If(Pid = 0, "自动", "PID " & Pid))
    End Sub

    ''' <summary>这台进程是不是启动器自己拉起来的（仍在运行）。</summary>
    Public Function IsLaunchedByUs(Pid As Integer) As Boolean
        If Pid <= 0 Then Return False
        Dim StartedAt As Date
        SyncLock LaunchedAt
            If Not LaunchedAt.TryGetValue(Pid, StartedAt) Then Return False
        End SyncLock
        Try
            Dim Target As Process = Process.GetProcessById(Pid)
            If StartedAt = Date.MinValue Then Return True
            'PID 会被系统回收：启动时刻对不上就当不是我们那个
            Return Math.Abs((Target.StartTime - StartedAt).TotalSeconds) < 2
        Catch
            Return False
        End Try
    End Function

    ''' <summary>只列启动器自己开出来的 java 进程，给房间页的下拉框用。</summary>
    Public Function ListGameJvms() As List(Of ServerJvm)
        Dim Result As New List(Of ServerJvm)
        For Each Jvm As ServerJvm In ListJvms()
            If IsLaunchedByUs(Jvm.Pid) Then Result.Add(Jvm)
        Next
        Return Result
    End Function

    ''' <summary>下拉框里一行文字：版本名 · PID（已挂代理的标出来）。</summary>
    Public Function DescribeJvm(Jvm As ServerJvm) As String
        If Jvm Is Nothing Then Return ""
        Dim Version As String = ""
        Dim Hit As Text.RegularExpressions.Match =
            Text.RegularExpressions.Regex.Match(Jvm.CommandLine, "--version\s+(\S+)")
        If Hit.Success Then Version = Hit.Groups(1).Value
        If Version = "" Then
            ' 独立服务端没有 --version，退回命令行里第一个 jar 名
            Hit = Text.RegularExpressions.Regex.Match(Jvm.CommandLine, "([^\\/:\s]+\.jar)")
            If Hit.Success Then Version = Hit.Groups(1).Value
        End If
        If Version = "" Then Version = "未知版本"
        Return Version & " · PID " & Jvm.Pid & If(Jvm.AgentLoaded, "（已接管）", "")
    End Function

    ''' <summary>由启动流程回填游戏进程号；没有启动过游戏时为 0。</summary>
    Public Sub RegisterGameProcess(Pid As Integer)
        GameProcessId = Pid
        If Pid <= 0 Then Return
        Dim StartedAt As Date = Date.MinValue
        Try
            StartedAt = Process.GetProcessById(Pid).StartTime
        Catch ex As Exception
            '拿不到启动时刻就记 MinValue，验证时只认进程号
        End Try
        SyncLock LaunchedAt
            LaunchedAt(Pid) = StartedAt
        End SyncLock
    End Sub

#End Region
#Region "进程发现"

    ''' <summary>一个候选 JVM。</summary>
    Public Class ServerJvm
        Public Pid As Integer
        Public JavaPath As String = ""
        Public CommandLine As String = ""
        Public AgentLoaded As Boolean
        Public Overrides Function ToString() As String
            Return $"PID {Pid} · {If(CommandLine.Length > 110, CommandLine.Substring(0, 110), CommandLine)}"
        End Function
    End Class

    ''' <summary>
    ''' 枚举全部 java 进程（含命令行与可执行文件路径）。
    ''' WMI 单次要几百毫秒，同一次探测里会被问好几遍，所以缓存 1.5 秒。
    ''' </summary>
    Public Function ListJvms() As List(Of ServerJvm)
        If (Date.Now - LastJvmCheck).TotalMilliseconds < 1500 Then Return LastJvms
        LastJvmCheck = Date.Now
        LastJvms = EnumerateJvms()
        Return LastJvms
    End Function

    Private LastJvms As New List(Of ServerJvm)
    Private LastJvmCheck As Date = Date.MinValue

    Private Function EnumerateJvms() As List(Of ServerJvm)
        Dim Result As New List(Of ServerJvm)
        Try
            Using Searcher As New ManagementObjectSearcher(
                    "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process " &
                    "WHERE Name = 'java.exe' OR Name = 'javaw.exe'")
                For Each Item As ManagementBaseObject In Searcher.Get()
                    Try
                        Dim Command As String = If(TryCast(Item("CommandLine"), String), "")
                        If Command.Length = 0 Then Continue For
                        Result.Add(New ServerJvm With {
                            .Pid = CInt(Item("ProcessId")),
                            .JavaPath = If(TryCast(Item("ExecutablePath"), String), ""),
                            .CommandLine = Command,
                            .AgentLoaded = Command.IndexOf("-javaagent:", StringComparison.OrdinalIgnoreCase) >= 0})
                    Catch ex As Exception
                        Logger.Warn(ex, "读取单个 Java 进程信息失败")
                    End Try
                Next
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "枚举 Java 进程失败")
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' 找出与指定版本对应的服务端 JVM。找不到返回 Nothing。
    ''' 评分制：命中版本路径 + 命中服务端特征最高，只命中服务端特征次之；
    ''' 带 --username 等客户端参数的直接排除。
    ''' </summary>
    Public Function FindServerJvm(Instance As McInstance) As ServerJvm
        Dim Keys As New List(Of String)
        If Instance IsNot Nothing Then
            For Each Raw As String In {Instance.PathVersion, Instance.PathIndie}
                If String.IsNullOrWhiteSpace(Raw) Then Continue For
                Try
                    Keys.Add(IO.Path.GetFullPath(Raw).TrimEnd("\"c).ToLowerInvariant())
                Catch
                    '路径非法时忽略该键
                End Try
            Next
        End If
        Dim Best As ServerJvm = Nothing
        Dim BestScore As Integer = 0
        Dim AllJvms As List(Of ServerJvm) = ListJvms()
        '只认启动器自己拉起来的进程 —— 房间会把端口经社团中继转发出去，
        '绑到别人的服务上就等于把中继当免费 frp 用
        Dim Jvms As New List(Of ServerJvm)
        For Each Jvm As ServerJvm In AllJvms
            If IsLaunchedByUs(Jvm.Pid) Then Jvms.Add(Jvm)
        Next
        '手动指定了进程就只认它 —— 两个客户端同版本时，评分分不出「开房的那台」
        If PinnedPid > 0 Then
            Dim Pinned As ServerJvm = Jvms.Find(Function(Jvm) Jvm.Pid = PinnedPid)
            If Pinned IsNot Nothing Then
                LastProbeNote = "按手动指定的 PID " & PinnedPid & "（启动器起的 java 共 " & Jvms.Count & " 个）"
                Return Pinned
            End If
            Logger.Info("进服校验：手动指定的 PID " & PinnedPid & " 已不在，退回自动探测")
            LastProbeNote = "手动指定的 PID " & PinnedPid & " 已退出，退回自动"
            PinnedPid = 0
        End If
        For Each Jvm As ServerJvm In Jvms
            Dim Score As Integer = ScoreJvm(Jvm, Keys)
            Dim PreferThis As Boolean = Score = BestScore AndAlso Score > 0 AndAlso
                Jvm.AgentLoaded AndAlso Best IsNot Nothing AndAlso Not Best.AgentLoaded
            If Score > BestScore OrElse PreferThis Then
                BestScore = Score
                Best = Jvm
            End If
        Next
        'LAN：集成服务端跑在游戏客户端进程里，上面的评分会把它当客户端排除。
        '三条判据任何一条成立就认，不再依赖「必须先建房」：
        '  A 启动器记下的游戏 PID（有房间，或该进程已在非回环地址上监听）
        '  B 任意 java 进程在 0.0.0.0/局域网地址上监听（LAN 世界已开放，PID 没记上也能认）
        '  C 房间端口反查
        If Best Is Nothing AndAlso GameProcessId > 0 Then
            If ModSocial.IsRoomHost OrElse LanListenerPids().Contains(GameProcessId) Then
                Best = Jvms.Find(Function(Jvm) Jvm.Pid = GameProcessId)
            End If
        End If
        If Best Is Nothing Then
            Dim LanPids As List(Of Integer) = LanListenerPids()
            If LanPids.Count > 0 Then
                Best = Jvms.Find(Function(Jvm) LanPids.Contains(Jvm.Pid))
            End If
        End If
        If Best Is Nothing Then
            Dim Port As Integer = ModSocial.HostedGamePort
            If Port > 0 Then
                Dim PortPid As Integer = FindPidByPort(Port)
                If PortPid > 0 Then Best = Jvms.Find(Function(Jvm) Jvm.Pid = PortPid)
            End If
        End If
        WriteProbeNote(Best, Jvms, LanListenerPids())
        Return Best
    End Function

    ''' <summary>最近一次探测的事实，卡片上直接显示，省得只能翻日志。</summary>
    Public LastProbeNote As String = ""

    Private LastProbeSignature As String = ""

    ''' <summary>把探测依据写进日志与卡片；内容没变就不重复写。</summary>
    Private Sub WriteProbeNote(Found As ServerJvm, Jvms As List(Of ServerJvm), Lan As List(Of Integer))
        Dim Game As Integer = GameProcessId
        Dim Alive As Boolean = Game > 0 AndAlso Jvms.Any(Function(Jvm) Jvm.Pid = Game)
        Dim Note As String = "游戏PID " & If(Game = 0, "无", Game.ToString()) &
            If(Game = 0, "", If(Alive, "(在java列表)", "(不在java列表)")) &
            " / 房间 " & If(ModSocial.IsRoomHost, "有", "无") &
            " / 端口 " & ModSocial.HostedGamePort &
            " / LAN监听 " & If(Lan.Count = 0, "无", String.Join(",", Lan.Take(6))) &
            " / java " & Jvms.Count & "个"
        LastProbeNote = If(Found Is Nothing, Note, "已命中 PID " & Found.Pid)
        If Note <> LastProbeSignature Then
            LastProbeSignature = Note
            Logger.Info("联机进程探测：" & Note & If(Found Is Nothing, " → 未命中", " → 命中 " & Found.Pid))
        End If
    End Sub

    Private LastLanPids As New List(Of Integer)
    Private LastLanCheck As Date = Date.MinValue

    ''' <summary>
    ''' 在「非回环地址」上监听 TCP 的进程号。LAN 世界开放后游戏会出现在这里；
    ''' 代理自己的控制端口只绑 127.0.0.1，所以不会被误判成 LAN。
    ''' 结果缓存 5 秒，避免房间页每拍都起一次 netstat。
    ''' </summary>
    Private Function LanListenerPids() As List(Of Integer)
        If (Date.Now - LastLanCheck).TotalSeconds < 10 Then Return LastLanPids
        LastLanCheck = Date.Now
        Dim Pids As New List(Of Integer)
        Try
            Dim Info As New ProcessStartInfo("netstat.exe", "-ano -p TCP") With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True}
            Using Proc As Process = Process.Start(Info)
                Dim Output As String = Proc.StandardOutput.ReadToEnd()
                Proc.WaitForExit(3000)
                For Each Line As String In Output.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    Dim Parts As String() = Line.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                    If Parts.Length >= 5 AndAlso Parts(0) = "TCP" AndAlso Parts(3) = "LISTENING" Then
                        Dim Addr As String = Parts(1)
                        Dim Loopback As Boolean = Addr.StartsWith("127.", StringComparison.Ordinal) OrElse
                            Addr.StartsWith("[::1]", StringComparison.Ordinal)
                        If Not Loopback Then
                            Dim Pid As Integer
                            If Integer.TryParse(Parts(4), Pid) AndAlso Not Pids.Contains(Pid) Then Pids.Add(Pid)
                        End If
                    End If
                Next
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "枚举 LAN 监听端口失败")
        End Try
        LastLanPids = Pids
        Return Pids
    End Function

    Private LastPortPid As Integer = 0
    Private LastPortChecked As Integer = 0
    Private LastPortCheck As Date = Date.MinValue

    ''' <summary>
    ''' 按本机监听端口反查进程号，用于没有启动器 PID 时的 LAN 兜底，
    ''' 以及开房间前核对「这个端口是不是启动器起的那个游戏」。
    ''' 结果按端口缓存 5 秒，避免每次房间页刷新都起一个 netstat。
    ''' </summary>
    Public Function FindPidByPort(Port As Integer) As Integer
        If Port <= 0 Then Return 0
        If LastPortChecked = Port AndAlso (Date.Now - LastPortCheck).TotalSeconds < 5 Then Return LastPortPid
        LastPortChecked = Port
        LastPortCheck = Date.Now
        LastPortPid = 0
        Try
            Dim Info As New ProcessStartInfo("netstat.exe", "-ano -p TCP") With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True}
            Using Proc As Process = Process.Start(Info)
                Dim Output As String = Proc.StandardOutput.ReadToEnd()
                Proc.WaitForExit(3000)
                For Each Line As String In Output.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    Dim Parts As String() = Line.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                    If Parts.Length >= 5 AndAlso Parts(0) = "TCP" AndAlso Parts(3) = "LISTENING" _
                        AndAlso Parts(1).EndsWith(":" & Port, StringComparison.Ordinal) Then
                        Dim Pid As Integer
                        If Integer.TryParse(Parts(4), Pid) Then
                            LastPortPid = Pid
                            Exit For
                        End If
                    End If
                Next
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "按端口查找游戏进程失败")
        End Try
        Return LastPortPid
    End Function

    ''' <summary>
    ''' 某个进程当前监听的「对外」端口，用于选完进程自动填本地端口。
    '''
    ''' 原版从 1.7.10 起，「对局域网开放」用的端口是随机的（今天这台是 52106），
    ''' 跟 25565 没关系。选好了进程还要去聊天栏抄一遍端口，是这套流程里最没道理的一步。
    ''' 只认 0.0.0.0 / [::] 这种通配绑定 —— 绑在 127.0.0.1 上的外面连不进来。
    ''' </summary>
    Public Function LanPortOf(Pid As Integer) As Integer
        If Pid <= 0 Then Return 0
        Dim Ports As New List(Of Integer)
        Try
            Dim Info As New ProcessStartInfo("netstat.exe", "-ano -p TCP") With {
                .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True}
            Using Proc As Process = Process.Start(Info)
                Dim Output As String = Proc.StandardOutput.ReadToEnd()
                Proc.WaitForExit(3000)
                For Each Line As String In Output.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    Dim Parts As String() = Line.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                    If Parts.Length < 5 OrElse Parts(0) <> "TCP" OrElse Parts(3) <> "LISTENING" Then Continue For
                    Dim Owner As Integer
                    If Not Integer.TryParse(Parts(4), Owner) OrElse Owner <> Pid Then Continue For
                    Dim Addr As String = Parts(1)
                    If Not (Addr.StartsWith("0.0.0.0:", StringComparison.Ordinal) OrElse
                            Addr.StartsWith("[::]:", StringComparison.Ordinal)) Then Continue For
                    Dim Port As Integer
                    Dim Parsed As Boolean = Integer.TryParse(Addr.Substring(Addr.LastIndexOf(":"c) + 1), Port)
                    If Parsed AndAlso Port > 0 AndAlso Not Ports.Contains(Port) Then Ports.Add(Port)
                Next
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "按进程查找局域网端口失败")
        End Try
        If Ports.Count = 0 Then Return 0
        '局域网端口是随机高端口；万一同一个进程还开了别的监听，取最大的那个
        Dim Best As Integer = 0
        For Each Port As Integer In Ports
            If Port > Best Then Best = Port
        Next
        Return Best
    End Function

    Private Function ScoreJvm(Jvm As ServerJvm, Keys As List(Of String)) As Integer
        Dim Command As String = Jvm.CommandLine.ToLowerInvariant()
        For Each Marker As String In ClientMarkers
            If Command.Contains(Marker.ToLowerInvariant()) Then Return 0
        Next
        Dim PathHit As Boolean
        For Each Key As String In Keys
            If Command.Contains(Key) Then
                PathHit = True
                Exit For
            End If
        Next
        Dim ServerHit As Boolean
        For Each Marker As String In ServerMarkers
            If Command.Contains(Marker) Then
                ServerHit = True
                Exit For
            End If
        Next
        If ServerHit AndAlso PathHit Then Return 40
        If ServerHit Then Return 30
        If PathHit Then Return 10
        Return 0
    End Function

#End Region
#Region "握手与通信"

    ''' <summary>代理写在临时目录的握手文件，按进程号命名。</summary>
    Private Function HandshakePath(Pid As Integer) As String
        Return IO.Path.Combine(IO.Path.GetTempPath(), "njit-online-" & Pid & ".json")
    End Function

    Private Function ReadHandshake(Pid As Integer) As JObject
        Try
            Dim Target As String = HandshakePath(Pid)
            If Not File.Exists(Target) Then Return Nothing
            Dim Text As String = File.ReadAllText(Target)
            If String.IsNullOrWhiteSpace(Text) Then Return Nothing
            Return JObject.Parse(Text)
        Catch ex As Exception
            Logger.Warn(ex, "读取代理握手文件失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>向代理发一条指令，返回它的一行答复；没连上返回 Nothing。</summary>
    Private Function SendCommand(Pid As Integer, Command As String) As String
        Dim Handshake As JObject = ReadHandshake(Pid)
        If Handshake Is Nothing Then Return Nothing
        Dim Port As Integer = If(Handshake("port") Is Nothing, 0, CInt(Handshake("port")))
        Dim Token As String = If(Handshake("token")?.ToString(), "")
        If Port <= 0 OrElse Token = "" Then Return Nothing
        Using Client As New TcpClient()
            Try
                Dim Connecting As Task = Client.ConnectAsync("127.0.0.1", Port)
                If Not Connecting.Wait(3000) Then Return Nothing
                Using Stream As NetworkStream = Client.GetStream()
                    Stream.ReadTimeout = 3000
                    Dim Payload As Byte() = Encoding.UTF8.GetBytes(Token & " " & Command & vbLf)
                    Stream.Write(Payload, 0, Payload.Length)
                    Stream.Flush()
                    Dim Buffer As New List(Of Byte)
                    Dim One(0) As Byte
                    Do While Buffer.Count < 256
                        Dim Count As Integer = Stream.Read(One, 0, 1)
                        If Count <= 0 Then Exit Do
                        If One(0) = 10 Then Exit Do
                        Buffer.Add(One(0))
                    Loop
                    Return Encoding.UTF8.GetString(Buffer.ToArray()).Trim()
                End Using
            Catch ex As Exception
                Logger.Warn(ex, "验证代理通信失败（PID " & Pid & "）")
                Return Nothing
            End Try
        End Using
    End Function

    ''' <summary>代理是否已在目标进程里活着（握手文件可能是上次的残留，必须实探一次）。</summary>
    Public Function IsInjected(Pid As Integer) As Boolean
        If Pid <= 0 Then Return False
        Return SendCommand(Pid, "PING") IsNot Nothing
    End Function

    ''' <summary>查询服务端当前的校验模式：1 正版 / 4 双验证。</summary>
    Public Function TryQueryMode(Pid As Integer, ByRef Mode As Integer) As Boolean
        Dim Reply As String = SendCommand(Pid, "STATE")
        If Reply Is Nothing OrElse Not Reply.StartsWith("OK ") Then Return False
        Dim Raw As String = Reply.Substring(3).Trim()
        If Not Integer.TryParse(Raw, Mode) Then Return False
        Return True
    End Function

    ''' <summary>切换校验模式，返回代理确认后的模式。</summary>
    ''' <param name="Mode">1 = 正版（Mojang）；4 = 双验证。</param>
    ''' <param name="MuaApi">双验证里 MUA 一侧要用的 Yggdrasil 根地址，留空则用代理内置的 MUA 地址。</param>
    Public Function SetMode(Pid As Integer, Mode As Integer, Optional MuaApi As String = "") As Integer
        Dim Command As String = "SET " & Mode
        If UsesExternal(Mode) AndAlso Not String.IsNullOrWhiteSpace(MuaApi) Then Command &= " " & MuaApi.Trim()
        Dim Reply As String = SendCommand(Pid, Command)
        If Reply Is Nothing Then Throw New Exception("验证代理没有响应，可能已随服务端退出")
        If Not Reply.StartsWith("OK ") Then Throw New Exception("验证代理拒绝指令：" & Reply)
        Dim Actual As Integer = Mode
        Integer.TryParse(Reply.Substring(3).Trim(), Actual)
        Return Actual
    End Function

    ''' <summary>
    ''' 把校验模式推给同一实例下**所有**已挂载的游戏进程，返回最终生效的模式。
    '''
    ''' 为什么必须广播：对局域网开放时，集成服务端跑在客户端进程里，而同一台机器上完全
    ''' 可能同时开着两个游戏（房主一个、自己另开一个）。GameProcessId 只会记住最近启动的
    ''' 那一个，探测于是可能命中「进来的那台」而不是「开房的那台」——模式发给了旁观者，
    ''' 房主还停在正版档，外置账号一来就是「验证用户名失败！」。
    '''
    ''' 模式只影响服务端侧的 hasJoinedServer，多发给一台没有副作用。
    ''' Applied 回传实际推送成功的进程数，调用方据此决定要不要退回单进程路径。
    ''' </summary>
    Public Function SetModeEverywhere(Instance As McInstance, Mode As Integer, MuaApi As String,
                                      ByRef Applied As Integer) As Integer
        Dim Keys As New List(Of String)
        If Instance IsNot Nothing Then
            For Each Raw As String In {Instance.PathVersion, Instance.PathIndie}
                If String.IsNullOrWhiteSpace(Raw) Then Continue For
                Try
                    Keys.Add(IO.Path.GetFullPath(Raw).TrimEnd("\"c).ToLowerInvariant())
                Catch
                    '路径非法时忽略该键
                End Try
            Next
        End If
        Dim Result As Integer = Mode
        Applied = 0
        For Each Jvm As ServerJvm In ListJvms()
            If Not Jvm.AgentLoaded Then Continue For
            '手动指定了进程就只推那一个
            If PinnedPid > 0 AndAlso Jvm.Pid <> PinnedPid Then Continue For
            If Keys.Count > 0 Then
                Dim Command As String = Jvm.CommandLine.ToLowerInvariant()
                Dim Hit As Boolean = False
                For Each Key As String In Keys
                    If Command.Contains(Key) Then
                        Hit = True
                        Exit For
                    End If
                Next
                If Not Hit Then Continue For
            End If
            If Not IsInjected(Jvm.Pid) Then Continue For
            Try
                Result = SetMode(Jvm.Pid, Mode, MuaApi)
                Applied += 1
            Catch ex As Exception
                '单个进程失败不影响其它进程，房主那台才是关键的
                Logger.Warn(ex, "向 PID " & Jvm.Pid & " 推送校验模式失败")
            End Try
        Next
        Return Result
    End Function

    ''' <summary>
    ''' 读取实际注入点：类#方法 | 返回类型 | 已改写方法数。
    ''' 1.20.2 前后以及 26.x 的注入点不同，这里直接把真实命中的那个报上来。
    ''' </summary>
    Public Function TryQueryInfo(Pid As Integer, ByRef Target As String, ByRef ReturnType As String,
                                 ByRef Patched As Integer) As Boolean
        Dim Reply As String = SendCommand(Pid, "INFO")
        If Reply Is Nothing OrElse Not Reply.StartsWith("OK ") Then Return False
        Dim Parts As String() = Reply.Substring(3).Split("|"c)
        Target = If(Parts.Length > 0, Parts(0), "")
        ReturnType = If(Parts.Length > 1, Parts(1), "")
        If Parts.Length > 2 Then Integer.TryParse(Parts(2), Patched)
        Return True
    End Function

#End Region
#Region "挂载"

    ''' <summary>
    ''' 把代理挂到目标服务端 JVM。必须在后台线程调用。
    ''' 需要目标 JVM 是完整 JDK（含 jdk.attach）；精简 JRE 不支持运行时挂载。
    ''' </summary>
    Public Sub Inject(Jvm As ServerJvm)
        If Jvm Is Nothing Then Throw New Exception("没有找到服务端进程")
        If IsInjected(Jvm.Pid) Then Return
        If Not File.Exists(AgentJarPath) Then
            Throw New Exception("缺少联机组件：PCL\njit-online-agent.jar 不存在")
        End If
        If Not File.Exists(AttachJarPath) Then
            Throw New Exception("缺少联机组件：PCL\njit-online-attach.jar 不存在")
        End If
        Dim Java As String = Jvm.JavaPath
        If String.IsNullOrWhiteSpace(Java) OrElse Not File.Exists(Java) Then
            Throw New Exception("无法取得服务端使用的 java 路径")
        End If
        Dim Arguments As String = "-cp """ & AttachJarPath & """ club.njitmcl.agent.Attacher " &
            Jvm.Pid & " """ & AgentJarPath & """"
        Dim Info As New ProcessStartInfo(Java) With {
            .Arguments = Arguments,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True}
        Dim Output As String = ""
        Dim Failure As String = ""
        Using Proc As Process = Process.Start(Info)
            Output = Proc.StandardOutput.ReadToEnd()
            Failure = Proc.StandardError.ReadToEnd()
            Proc.WaitForExit(20000)
            If Proc.ExitCode <> 0 Then
                Throw New Exception("挂载失败：" & If(Failure.Trim() <> "", Failure.Trim(), Output.Trim()))
            End If
        End Using
        '代理装完会写握手文件，最多等 5 秒
        For i = 1 To 50
            If IsInjected(Jvm.Pid) Then
                Logger.Info("已向 PID " & Jvm.Pid & " 挂载正版验证代理")
                Return
            End If
            Thread.Sleep(100)
        Next
        Throw New Exception("挂载后代理没有响应，请确认服务端使用完整 JDK 而非精简 JRE")
    End Sub

#End Region
#Region "页面挂载"

    Private MountedHost As Panel
    Private MountedCard As OnlineModeCard
    Private MountedDivider As Border
    Private RetryScheduled As Boolean

    ''' <summary>
    ''' 把拉杆插进房间页。
    '''
    ''' 位置跟着页面状态走，永远是「公网房间那根拉杆下面」：
    ''' · 还没有房间 → 建房表单里「公网房间」说明之后的占位 PanOnlineModeForm
    ''' · 已经有房间 → 房间卡片里「开启公网房间」之后的占位 PanOnlineModeCard
    ''' · 两个占位都不在（别的页面）→ 退回内容区末尾
    '''
    ''' 房间页每 8 秒调一次本方法，既用来跟住位置，也顺手让拉杆重新探测。
    ''' </summary>
    Public Sub Mount(Page As FrameworkElement)
        If Page Is Nothing Then Return
        Dim InForm As Panel = TryCast(FindChild(Page, "PanOnlineModeForm"), Panel)
        Dim InCard As Panel = TryCast(FindChild(Page, "PanOnlineModeCard"), Panel)
        Dim Host As Panel = Nothing
        If InForm IsNot Nothing AndAlso VisiblyEnabled(InForm) Then
            Host = InForm
        ElseIf InCard IsNot Nothing AndAlso VisiblyEnabled(InCard) Then
            Host = InCard
        Else
            Host = TryCast(FindChild(Page, "PanRoomArea"), Panel)
            If Host Is Nothing Then Host = TryCast(FindChild(Page, "PanContent"), Panel)
            If Host Is Nothing Then Host = TryCast(FindChild(Page, "PanMain"), Panel)
        End If
        If Host Is Nothing Then
            '页面可能还没完成加载，排队等一次布局后再试
            If Not RetryScheduled Then
                RetryScheduled = True
                RunInUi(
                Sub()
                    RetryScheduled = False
                    Mount(Page)
                End Sub)
            End If
            Return
        End If

        If MountedCard IsNot Nothing Then
            If MountedHost Is Host AndAlso Host.Children.Contains(MountedCard) Then
                MountedCard.Refresh()
                Return
            End If
            '换了位置：从原容器摘下来，和分隔线一起搬过去
            If MountedHost IsNot Nothing Then
                If MountedDivider IsNot Nothing AndAlso MountedHost.Children.Contains(MountedDivider) Then
                    MountedHost.Children.Remove(MountedDivider)
                End If
                If MountedHost.Children.Contains(MountedCard) Then MountedHost.Children.Remove(MountedCard)
            End If
        Else
            MountedDivider = ModSocialUi.SocialDivider()
            MountedDivider.Margin = New Thickness(0, 18, 0, 0)
            MountedCard = New OnlineModeCard
        End If

        '两块占位本身都有上下内容夹着，再补一条线是多余的
        MountedDivider.Visibility =
            If(ReferenceEquals(Host, InForm) OrElse ReferenceEquals(Host, InCard),
               Visibility.Collapsed, Visibility.Visible)

        If TypeOf Host Is Grid Then
            Dim GridHost As Grid = DirectCast(Host, Grid)
            For Each Item As FrameworkElement In New FrameworkElement() {MountedDivider, MountedCard}
                GridHost.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
                Grid.SetRow(Item, GridHost.RowDefinitions.Count - 1)
                Grid.SetColumn(Item, 0)
                Grid.SetColumnSpan(Item, Math.Max(1, GridHost.ColumnDefinitions.Count))
            Next
        End If
        Host.Children.Add(MountedDivider)
        Host.Children.Add(MountedCard)
        MountedHost = Host
        '搬家不会重触发 Loaded，这里补一次探测，免得换位置后停在旧状态
        MountedCard.Refresh()
        Logger.Info("联机页已挂载进服校验拉杆")
    End Sub

    ''' <summary>
    ''' 自身及其所有祖先都没有 Collapsed/Hidden 才算显示。
    ''' 比 IsVisible 稳：页面刚进、还没走布局时 IsVisible 是 False，会误判成「表单收起了」。
    ''' </summary>
    Private Function VisiblyEnabled(Element As FrameworkElement) As Boolean
        Dim Walk As DependencyObject = Element
        Do While Walk IsNot Nothing
            Dim Node As FrameworkElement = TryCast(Walk, FrameworkElement)
            If Node IsNot Nothing AndAlso Node.Visibility <> Visibility.Visible Then Return False
            Walk = VisualTreeHelper.GetParent(Walk)
        Loop
        Return True
    End Function

    Private Function FindChild(Parent As DependencyObject, Name As String) As FrameworkElement
        If Parent Is Nothing Then Return Nothing
        Dim Self As FrameworkElement = TryCast(Parent, FrameworkElement)
        If Self IsNot Nothing AndAlso Self.Name = Name Then Return Self
        Dim Count As Integer = VisualTreeHelper.GetChildrenCount(Parent)
        For i = 0 To Count - 1
            Dim Found As FrameworkElement = FindChild(VisualTreeHelper.GetChild(Parent, i), Name)
            If Found IsNot Nothing Then Return Found
        Next
        Return Nothing
    End Function

#End Region

End Module

''' <summary>
''' 联机页上的「进服校验」拉杆：关 = 正版验证，开 = 双验证。
''' 检测服务端、挂载代理、切换模式都在这里完成，不需要用户碰命令行。
''' </summary>
Public Class OnlineModeCard
    Inherits SocialSwitch

    Private Busy As Boolean
    ''' <summary>
    ''' 上次切换的时刻。本机 TCP 往返只有几毫秒，Busy 那道闸几乎立刻放开，
    ''' 一次双击就会把刚打开的档位又切回去 —— 日志里「SET 4 → SET 1」同秒连发就是这样。
    ''' 所以切换后 700ms 内不接受第二次点击。
    ''' </summary>
    Private LastSwitchAt As Date = Date.MinValue
    Private Const SwitchCooldownMs As Integer = 700
    ''' <summary>后台探测在跑；房间页每 8 秒会来一次，避免堆叠。</summary>
    Private Probing As Boolean
    ''' <summary>上次命中的进程。房间页每 8 秒会来问一次，缓存一下免得每拍查 WMI 卡界面。</summary>
    Private CachedJvm As ModOnlineMode.ServerJvm

    Public Sub New()
        MyBase.New("进服校验", "正在检测服务端……")
        AddHandler Track.MouseLeftButtonUp, Sub() Toggle()
        AddHandler Me.Loaded, Sub() Refresh()
    End Sub

    ''' <summary>进程是否还在；用来判断缓存的 PID 有没有过期。</summary>
    Private Shared Function ProcessAlive(Pid As Integer) As Boolean
        If Pid <= 0 Then Return False
        Try
            Return Not System.Diagnostics.Process.GetProcessById(Pid).HasExited
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 取缓存里那台进程，前提是它还活着，**而且没有被「游戏进程」下拉框换掉**。
    '''
    ''' 少了后半个判断就会出现这种情况：在下拉框里改成 B，拉杆仍然对着先前缓存的 A ——
    ''' 状态行写着「已接管（PID A）」，点切换也把模式推给 A，看起来就像选择没生效。
    ''' </summary>
    Private Function UsableCached() As ModOnlineMode.ServerJvm
        Dim Jvm As ModOnlineMode.ServerJvm = CachedJvm
        If Jvm Is Nothing Then Return Nothing
        If Not ProcessAlive(Jvm.Pid) Then Return Nothing
        Dim Pinned As Integer = ModOnlineMode.PinnedPid
        If Pinned > 0 AndAlso Jvm.Pid <> Pinned Then Return Nothing
        Return Jvm
    End Function

    ''' <summary>
    ''' 重新探测。探测里有 WMI 与 netstat，同步跑会卡住界面，必须放后台线程。
    ''' </summary>
    Public Sub Refresh()
        If Busy OrElse Probing Then Return
        Probing = True
        Dim Last As ModOnlineMode.ServerJvm = UsableCached()
        RunInNewThread(
        Sub()
            Dim Jvm As ModOnlineMode.ServerJvm = Last
            Dim Mode As Integer = -1
            Dim Injected As Boolean = False
            Try
                If Jvm IsNot Nothing AndAlso Not ProcessAlive(Jvm.Pid) Then Jvm = Nothing
                If Jvm Is Nothing Then Jvm = ModOnlineMode.FindServerJvm(McInstanceSelected)
                If Jvm IsNot Nothing Then
                    ModOnlineMode.TryQueryMode(Jvm.Pid, Mode)
                    '读不到模式但代理还在跑：说明接管过，只是这一拍没答上来
                    If Mode < 0 Then Injected = ModOnlineMode.IsInjected(Jvm.Pid)
                End If
            Catch ex As Exception
                Logger.Warn(ex, "探测进服校验状态失败")
            End Try
            RunInUi(
            Sub()
                Probing = False
                CachedJvm = Jvm
                ApplyState(Jvm, Mode, Injected)
            End Sub)
        End Sub, "OnlineModeProbe")
    End Sub

    ''' <summary>把探测结果写到拉杆上；只在 UI 线程调用。</summary>
    Private Sub ApplyState(Jvm As ModOnlineMode.ServerJvm, Mode As Integer, Injected As Boolean)
        If Jvm Is Nothing Then
            IsOn = False
            SetInteractive(False)
            StateText.Text = "未检测到游戏进程：启动游戏并「对局域网开放」后会自动识别，" &
                "也可以把端口填进上面的本地端口再建房。" & vbCrLf &
                "探测：" & ModOnlineMode.LastProbeNote
            Return
        End If
        If Mode < 0 Then
            IsOn = False
            SetInteractive(True)
            If Injected Then
                StateText.Text = "已接管（PID " & Jvm.Pid & "），但这一拍读不到模式；点一下按双验证重设。"
            Else
                StateText.Text = "已找到游戏进程（PID " & Jvm.Pid & "），打开即接管。"
            End If
            Return
        End If
        IsOn = (Mode = ModOnlineMode.ModeDualMojangFirst)
        SetInteractive(True)
        If Mode = ModOnlineMode.ModeMojang Then
            StateText.Text = "已接管（PID " & Jvm.Pid & "）：正版验证，新登录必须持有微软会话。"
        Else
            StateText.Text = "已接管（PID " & Jvm.Pid & "）：双验证，正版与 MUA 都能进，同名时正版优先。"
        End If
    End Sub

    ''' <summary>点轨道：关 → 开（双验证），开 → 关（正版）。</summary>
    Private Sub Toggle()
        If Busy Then Return
        '刚切过就别理这一下：双击的第二下会把刚打开的档位原样切回去，
        '界面上看起来就是「点了没反应」。
        If (Date.Now - LastSwitchAt).TotalMilliseconds < SwitchCooldownMs Then Return
        Switch(If(IsOn, ModOnlineMode.ModeMojang, ModOnlineMode.ModeDualMojangFirst))
    End Sub

    Private Sub Switch(Mode As Integer)
        If Busy Then Return
        Busy = True
        LastSwitchAt = Date.Now
        SetInteractive(False)
        StateText.Text = "正在处理……"
        Dim MuaApi As String = If(Mode = ModOnlineMode.ModeMojang, "", ModNJIT.MuaAuthServer)
        Dim Last As ModOnlineMode.ServerJvm = UsableCached()
        RunInNewThread(
        Sub()
            Dim Message As String = ""
            Dim Success As Boolean = False
            Dim Jvm As ModOnlineMode.ServerJvm = Last
            Try
                '探测同样放后台：WMI 与 netstat 在 UI 线程上会卡住拉杆响应
                If Jvm Is Nothing OrElse Not ProcessAlive(Jvm.Pid) Then
                    Jvm = ModOnlineMode.FindServerJvm(McInstanceSelected)
                End If
                If Jvm Is Nothing Then
                    Message = "没有找到正在运行的游戏，请先启动游戏并「对局域网开放」后再试"
                    RunInUi(
                    Sub()
                        Busy = False
                        SetInteractive(False)
                        Hint(Message, HintType.Red)
                        Refresh()
                    End Sub)
                    Return
                End If
                CachedJvm = Jvm
                If Not ModOnlineMode.IsInjected(Jvm.Pid) Then
                    RunInUi(Sub() StateText.Text = "正在挂载验证代理……")
                    ModOnlineMode.Inject(Jvm)
                End If
                '先广播：同时开着两个游戏时，探测命中的可能是「进来的那台」，
                '模式必须落到房主那台上，否则外置账号会被拒
                Dim AppliedCount As Integer = 0
                Dim Actual As Integer = ModOnlineMode.SetModeEverywhere(McInstanceSelected, Mode, MuaApi, AppliedCount)
                If AppliedCount = 0 Then
                    '一台都没推送成功（例如探测到的是没挂代理的进程），退回单进程路径
                    Actual = ModOnlineMode.SetMode(Jvm.Pid, Mode, MuaApi)
                    AppliedCount = 1
                End If
                Success = True
                If Actual = ModOnlineMode.ModeMojang Then
                    Message = "已切回正版验证：新登录必须持有微软会话。"
                Else
                    Message = "已启用双验证：先问正版，问不到再问 MUA，两边账号都能进；同名时正版身份优先。"
                End If
                If AppliedCount > 1 Then Message &= "（已推送到 " & AppliedCount & " 个游戏进程）"
            Catch ex As Exception
                Message = "切换失败：" & ex.Message
            End Try
            RunInUi(
            Sub()
                Busy = False
                '冷却从「这次真的做完」开始算，探测慢一点也不会被连点穿透
                LastSwitchAt = Date.Now
                Refresh()
                Hint(Message, If(Success, HintType.Green, HintType.Red))
            End Sub)
        End Sub, "OnlineMode")
    End Sub

End Class
