Imports System.Collections.Concurrent
Imports System.Net
Imports System.Net.Security
Imports System.Net.Sockets
Imports System.Security.Authentication
Imports System.Text
Imports System.Threading.Tasks

''' <summary>
''' GitHub 域名的 IP 优选：内置种子 + DoH + meta 采集、并发探测、排序缓存。
''' </summary>
Public Module ModGithubIp

    ''' <summary>允许走优选代理的域名。</summary>
    Public ReadOnly GithubHosts As String() = {
        "github.com", "api.github.com", "codeload.github.com",
        "objects.githubusercontent.com", "release-assets.githubusercontent.com"}

    ''' <summary>实测稳定的死段，直接不探测。</summary>
    Private ReadOnly DenyPrefix As String() = {
        "140.82.115.", "140.82.117.", "140.82.118.", "140.82.119.",
        "143.55.64.", "143.55.65.", "143.55.66.", "143.55.67.", "143.55.68.", "143.55.69.", "143.55.70.",
        "192.30.252.", "192.30.253.", "192.30.254.", "192.30.255."}

    Private ReadOnly BuiltinSeeds As New Dictionary(Of String, String()) From {
        {"github.com",
         New String() {"20.27.177.113", "20.200.245.247", "20.205.243.166",
                       "140.82.114.3", "140.82.121.3", "140.82.112.3", "140.82.113.3", "140.82.116.3"}},
        {"api.github.com",
         New String() {"20.205.243.168", "140.82.112.5", "140.82.113.5"}},
        {"codeload.github.com",
         New String() {"20.205.243.165", "140.82.112.9"}},
        {"objects.githubusercontent.com",
         New String() {"185.199.110.133", "185.199.108.133", "185.199.109.133", "185.199.111.133"}},
        {"release-assets.githubusercontent.com",
         New String() {"185.199.110.133", "185.199.108.133", "185.199.109.133", "185.199.111.133"}}}

    Private Const ProbeTimeoutMs As Integer = 9000 '实测 GitHub 单次连接常需 3~4 秒，超时过短会误判可用 IP
    Private Const ProbeWorkers As Integer = 12

    Private ReadOnly CacheLock As New Object

    ''' <summary>缓存的 release 测速路径（签名地址），避免每次测速都重新请求 302。</summary>
    Private SampleCache As String = ""
    Private SampleStamp As Long = 0

    ''' <summary>运行期连接失败的 IP，90 秒内不再优先尝试。</summary>
    Private ReadOnly FailedUntil As New Dictionary(Of String, Long)

    ''' <summary>正在全量优选的域名，避免多个连接线程各自选一个 IP 互相覆盖。</summary>
    Private ReadOnly Ranking As New HashSet(Of String)

    ''' <summary>域名级熔断：本轮候选全部探测失败后，短时间内不再重复探测。</summary>
    Private ReadOnly BlackoutUntil As New Dictionary(Of String, Long)
    Private Const BlackoutMs As Long = 30L * 1000

    ''' <summary>选定后持久化的 IP：下次启动优先沿用，不再重新排序。</summary>
    Private Const SavedFile1 As String = "github-ip.json"

    ''' <summary>本次进程内已确认可用的 IP（带时间戳，过期后需要重新校验）。</summary>
    Private ReadOnly Validated As New ConcurrentDictionary(Of String, Long)
    Private Const ValidatedTtlMs As Long = 2L * 60 * 1000

    ''' <summary>保护 github-ip.json 的读写，避免并发写坏文件。</summary>
    Private ReadOnly SavedLock As New Object()

    Private Function SavedPath() As String
        Dim base As String = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
        Return IO.Path.Combine(base, "PCL", SavedFile1)
    End Function

    ''' <summary>读取上次选定的 IP；没有记录时返回空串。</summary>
    Private Function ReadSaved(domain As String) As String
        SyncLock SavedLock
            Try
                Dim path As String = SavedPath()
                If Not IO.File.Exists(path) Then Return ""
                Dim json As Newtonsoft.Json.Linq.JObject = IO.File.ReadAllText(path).DeserializeJson()
                Return If(json Is Nothing, "", If(json(domain)?.ToString(), ""))
            Catch
                Return ""
            End Try
        End SyncLock
    End Function

    ''' <summary>记住选定的 IP；传空串表示删除该域名的记录。</summary>
    Private Sub WriteSaved(domain As String, ip As String)
        SyncLock SavedLock
            Try
                Dim path As String = SavedPath()
                Dim json As New Newtonsoft.Json.Linq.JObject()
                If IO.File.Exists(path) Then
                    Try
                        json = IO.File.ReadAllText(path).DeserializeJson()
                    Catch
                        json = New Newtonsoft.Json.Linq.JObject()
                    End Try
                End If
                If json Is Nothing Then json = New Newtonsoft.Json.Linq.JObject()
                If String.IsNullOrWhiteSpace(ip) Then
                    json.Remove(domain)
                Else
                    json(domain) = ip
                End If
                Dim folder As String = IO.Path.GetDirectoryName(path)
                If Not IO.Directory.Exists(folder) Then IO.Directory.CreateDirectory(folder)
                IO.File.WriteAllText(path, json.ToString(Newtonsoft.Json.Formatting.None))
            Catch ex As Exception
                Logger.Warn(ex, "写入 GitHub IP 记忆失败")
            End Try
        End SyncLock
    End Sub

    Private Function IsRanking(domain As String) As Boolean
        SyncLock CacheLock
            Return Ranking.Contains(domain)
        End SyncLock
    End Function

    Private Sub SetRanking(domain As String, value As Boolean)
        SyncLock CacheLock
            If value Then
                Ranking.Add(domain)
            Else
                Ranking.Remove(domain)
            End If
        End SyncLock
    End Sub

    ''' <summary>是否处于「全网探测失败」的熔断期。</summary>
    Private Function IsBlackout(domain As String) As Boolean
        SyncLock CacheLock
            Dim until As Long = 0
            Return BlackoutUntil.TryGetValue(domain, until) AndAlso until > GetTimeMs()
        End SyncLock
    End Function

    Private Sub MarkBlackout(domain As String)
        SyncLock CacheLock
            BlackoutUntil(domain) = GetTimeMs() + BlackoutMs
        End SyncLock
    End Sub

    Private Function IsValidated(domain As String, ip As String) As Boolean
        Dim stamp As Long = 0
        Dim key As String = domain & "|" & ip
        If Not Validated.TryGetValue(key, stamp) Then Return False
        If GetTimeMs() - stamp < ValidatedTtlMs Then Return True
        Dim removed As Long = 0
        Validated.TryRemove(key, removed)
        Return False
    End Function

    Private Sub MarkValidated(domain As String, ip As String)
        Validated(domain & "|" & ip) = GetTimeMs()
    End Sub

    Private Function IsFailed(domain As String, ip As String) As Boolean
        SyncLock CacheLock
            Dim until As Long = 0
            Return FailedUntil.TryGetValue(domain & "|" & ip, until) AndAlso until > GetTimeMs()
        End SyncLock
    End Function

    ''' <summary>是否属于需要优选的 GitHub 域名。</summary>
    Public Function IsGithubHost(host As String) As Boolean
        If String.IsNullOrEmpty(host) Then Return False
        Return GithubHosts.Contains(host.Trim().ToLowerInvariant())
    End Function

    Private Function IsDenied(ip As String) As Boolean
        For Each prefix In DenyPrefix
            If ip.StartsWith(prefix, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    ''' <summary>返回可用 IP 列表；本轮全部不可用时返回空列表，让上层立刻切到中转。</summary>
    Public Function GetRanked(domain As String) As List(Of String)
        domain = domain.Trim().ToLowerInvariant()

        '已经选定过且还能用：直接沿用，不再重新探测排序，避免每次启动都换 IP
        Dim saved As String = ReadSaved(domain)
        If Not IsFailed(domain, saved) AndAlso Not String.IsNullOrWhiteSpace(saved) Then
            Dim SavedOk As Boolean = IsValidated(domain, saved)
            If Not SavedOk Then
                If domain = "release-assets.githubusercontent.com" Then
                    '坏 IP 也能完成握手，必须看真实下载吞吐，否则会一直守着一条几乎不动的连接
                    Dim Sample As String = ResolveSamplePath()
                    If Not String.IsNullOrWhiteSpace(Sample) Then
                        SavedOk = MeasureIp(domain, saved, Sample, 1048576, 6000) >= 1048576.0
                    Else
                        SavedOk = Probe(domain, saved, 8000) >= 0
                    End If
                Else
                    SavedOk = Probe(domain, saved, 8000) >= 0
                End If
            End If
            If SavedOk Then
                MarkValidated(domain, saved)
                Logger.Info("GitHub IP 固定沿用（" & domain & "）：" & saved)
                Return New List(Of String) From {saved}
            End If
            ReportFailure(domain, saved)
            Logger.Info("GitHub IP 暂时不可用，本次重新优选（" & domain & "）：" & saved)
        End If

        '刚探测完全部失败：熔断期内不再重复探测（saved 已在上面校验过，能用就不会走到这里）
        If IsBlackout(domain) Then Return New List(Of String)()

        '别的线程正在优选同一域名：短暂等待它写回结果，避免两个线程各选一个 IP 互相覆盖
        If IsRanking(domain) Then
            For i = 1 To 80
                Threading.Thread.Sleep(500)
                Dim now As String = ReadSaved(domain)
                If Not String.IsNullOrWhiteSpace(now) AndAlso IsValidated(domain, now) Then
                    Return New List(Of String) From {now}
                End If
                If Not IsRanking(domain) Then Exit For
            Next
            If IsBlackout(domain) Then Return New List(Of String)()
            '等了 40 秒仍在优选：这次直接放弃，避免再起一条重复的探测
            If IsRanking(domain) Then Return New List(Of String)()
        End If

        SetRanking(domain, True)
        Try
            Dim candidates As New List(Of String)
            Dim seeds As String() = Nothing
            If BuiltinSeeds.TryGetValue(domain, seeds) Then candidates.AddRange(seeds)
            Try
                For Each ip In FetchDoh(domain)
                    If Not candidates.Contains(ip) AndAlso Not IsDenied(ip) Then candidates.Add(ip)
                Next
            Catch ex As Exception
                Logger.Warn(ex, "DoH 解析 GitHub 候选失败（" & domain & "）")
            End Try
            Logger.Info("GitHub IP 候选收集完成（" & domain & "）：" & candidates.Count & " 个")

            Dim usable As New List(Of String)
            For Each ip In candidates
                If Not IsDenied(ip) AndAlso Not usable.Contains(ip) Then usable.Add(ip)
            Next
            If usable.Count = 0 Then
                MarkBlackout(domain)
                Return New List(Of String)()
            End If
            Logger.Info("GitHub IP 开始探测（" & domain & "）：" & usable.Count & " 个候选")

            Dim scored As New ConcurrentBag(Of Tuple(Of Integer, String))
            Parallel.ForEach(usable, New ParallelOptions With {.MaxDegreeOfParallelism = ProbeWorkers},
                Sub(ip)
                    Dim ms As Integer = Probe(domain, ip, ProbeTimeoutMs)
                    If ms >= 0 Then scored.Add(Tuple.Create(ms, ip))
                End Sub)

            Dim ordered As List(Of String) = scored.OrderBy(Function(x) x.Item1).
                Select(Function(x) x.Item2).ToList()
            If ordered.Count = 0 Then
                '候选全部连不上：熔断 30 秒，让上层快速失败去走中转，而不是每次重等超时
                MarkBlackout(domain)
                Logger.Warn("GitHub IP 全部不可用（" & domain & "），30 秒内不再探测")
                Return New List(Of String)()
            End If
            Logger.Info("GitHub IP 优选（" & domain & "）：" &
                        String.Join(", ", ordered.Select(Function(x) x)))
            'release 资产：延迟只用来筛掉连不上的 IP，最终按真实下载速度排序
            If domain = "release-assets.githubusercontent.com" AndAlso ordered.Count > 1 Then
                Dim sample As String = ResolveSamplePath()
                If Not String.IsNullOrWhiteSpace(sample) Then
                    Dim probe As New List(Of String)
                    For Each Ip As String In ordered
                        probe.Add(Ip)
                        If probe.Count >= 4 Then Exit For
                    Next
                    Dim scoredRelease = ScoreReleaseAssets(domain, probe, sample, 262144, 9000)
                    If scoredRelease.Count > 0 Then
                        Dim merged As New List(Of String)
                        For Each Item In scoredRelease
                            merged.Add(Item.Item2)
                        Next
                        For Each Ip As String In ordered
                            If Not merged.Contains(Ip) Then merged.Add(Ip)
                        Next
                        ordered = merged
                        Logger.Info("GitHub IP 下载测速（" & domain & "）：" & String.Join(", ", scoredRelease.Select(Function(x) x.Item2)))
                    End If
                End If
            End If

            '保存最终选定的 IP（测速后的第一名），下次启动直接沿用
            If ordered.Count > 0 AndAlso Not IsFailed(domain, ordered(0)) Then
                WriteSaved(domain, ordered(0))
                MarkValidated(domain, ordered(0))
            End If
            '本轮刚探测通过的 IP 视为已恢复，清掉旧的拉黑记录，避免刚验证过又被过滤
            SyncLock CacheLock
                For Each Ip As String In ordered
                    FailedUntil.Remove(domain & "|" & Ip)
                Next
            End SyncLock
            Return New List(Of String)(ordered)
        Finally
            SetRanking(domain, False)
        End Try
    End Function

    ''' <summary>连接失败时把该 IP 短暂拉黑，下次优先换下一个。</summary>
    Public Sub ReportFailure(domain As String, ip As String)
        SyncLock CacheLock
            '只拉黑 90 秒：网络抖动时若长时间拉黑，全部 IP 会被轮番禁用，导致代理持续 502
            FailedUntil(domain.Trim().ToLowerInvariant() & "|" & ip) = GetTimeMs() + 90L * 1000
        End SyncLock
    End Sub

    ''' <summary>单个候选的 TCP + TLS + 首字节探测，返回毫秒，失败返回 -1。</summary>
    Public Function Probe(domain As String, ip As String, timeoutMs As Integer) As Integer
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Try
            Using sock As New Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                Dim task = sock.ConnectAsync(ip, 443)
                If Not task.Wait(timeoutMs) Then Return -1
                If Not sock.Connected Then Return -1
                sock.ReceiveTimeout = timeoutMs
                sock.SendTimeout = timeoutMs
                Using net As New NetworkStream(sock, False)
                    Using ssl As New SslStream(net, False)
                        ssl.ReadTimeout = timeoutMs
                        ssl.WriteTimeout = timeoutMs
                        '不做吊销检查：国内访问 CA 吊销服务器常卡住几十秒，且这里只用于连通性判断
                        ssl.AuthenticateAsClient(domain, Nothing, SslProtocols.Tls12, False)
                        Dim req As String = "HEAD / HTTP/1.1" & vbCrLf &
                                            "Host: " & domain & vbCrLf &
                                            "User-Agent: NJITMCL/" & VersionBaseName & vbCrLf &
                                            "Connection: close" & vbCrLf & vbCrLf
                        Dim buf As Byte() = Encoding.ASCII.GetBytes(req)
                        ssl.Write(buf, 0, buf.Length)
                        ssl.Flush()
                        Dim tmp(1023) As Byte
                        If ssl.Read(tmp, 0, tmp.Length) <= 0 Then Return -1
                        Return CInt(watch.ElapsedMilliseconds)
                    End Using
                End Using
            End Using
        Catch
            Return -1
        End Try
    End Function

    ''' <summary>取更新清单里 GitHub 直链的 release 资产路径（含签名），用于下载测速。</summary>
    Private Function ResolveSamplePath() As String
        SyncLock CacheLock
            '签名地址有效期约 5 分钟，缓存 3 分钟
            If SampleCache <> "" AndAlso GetTimeMs() - SampleStamp < 180000L Then Return SampleCache
        End SyncLock
        Try
            Dim manifest As String = NetRequestByClient("https://njitmc.club/update/njitmcl.json",
                                                        RequireJson:=True, Timeout:=8000)
            Dim json As Newtonsoft.Json.Linq.JObject = manifest.DeserializeJson()
            Dim url As String = If(json Is Nothing, "", If(json("url")?.ToString(), ""))
            If String.IsNullOrWhiteSpace(url) Then Return ""
            '复用社团服务器的 302 解析：主站被墙时也能拿到签名地址
            Dim result As String = ToSamplePath(ResolveGithubAssetUrl(url))
            If result <> "" Then
                SyncLock CacheLock
                    SampleCache = result
                    SampleStamp = GetTimeMs()
                End SyncLock
            End If
            Return result
        Catch ex As Exception
            Logger.Warn(ex, "获取 GitHub 测速样本失败")
        End Try
        Return ""
    End Function

    Private Function ToSamplePath(Location As String) As String
        If String.IsNullOrWhiteSpace(Location) Then Return ""
        Try
            Return New Uri(Location, UriKind.RelativeOrAbsolute).PathAndQuery
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>对候选 IP 并发做真实下载测速，返回按速度降序的 (速度, IP)；全部失败返回空列表。</summary>
    Private Function ScoreReleaseAssets(domain As String, Ips As List(Of String), SamplePath As String,
                                        MaxBytes As Integer, TimeoutMs As Integer) As List(Of Tuple(Of Double, String))
        Dim Scored As New ConcurrentBag(Of Tuple(Of Double, String))
        Parallel.ForEach(Ips, New ParallelOptions With {.MaxDegreeOfParallelism = 4},
            Sub(Ip)
                Dim Speed As Double = MeasureIp(domain, Ip, SamplePath, MaxBytes, TimeoutMs)
                If Speed > 0 Then Scored.Add(Tuple.Create(Speed, Ip))
            End Sub)
        Return Scored.OrderByDescending(Function(x) x.Item1).ToList()
    End Function

    ''' <summary>在指定 IP 上下载样本的前 256 KB，返回字节/秒；失败返回 0。</summary>
    Private Function MeasureIp(domain As String, Ip As String, SamplePath As String, MaxBytes As Integer, TimeoutMs As Integer) As Double
        Dim Watch As Stopwatch = Stopwatch.StartNew()
        Try
            Using Sock As New Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                Dim Connecting = Sock.ConnectAsync(Ip, 443)
                If Not Connecting.Wait(TimeoutMs) OrElse Not Sock.Connected Then Return 0
                Sock.ReceiveTimeout = TimeoutMs
                Sock.SendTimeout = TimeoutMs
                Using Net As New NetworkStream(Sock, False)
                    Using Ssl As New SslStream(Net, False)
                        Ssl.AuthenticateAsClient(domain, Nothing, SslProtocols.Tls12, False)
                        Ssl.ReadTimeout = TimeoutMs
                        Ssl.WriteTimeout = TimeoutMs
                        Dim Req As String = "GET " & SamplePath & " HTTP/1.1" & vbCrLf &
                                            "Host: " & domain & vbCrLf &
                                            "User-Agent: NJITMCL/" & VersionBaseName & vbCrLf &
                                            "Range: bytes=0-" & (MaxBytes - 1) & vbCrLf &
                                            "Connection: close" & vbCrLf & vbCrLf
                        Dim Out = Encoding.ASCII.GetBytes(Req)
                        Ssl.Write(Out, 0, Out.Length)
                        Ssl.Flush()
                        Dim Buf(8191) As Byte
                        Dim Head As New StringBuilder
                        While Head.ToString().IndexOf(vbCrLf & vbCrLf) < 0
                            Dim N As Integer = Ssl.Read(Buf, 0, Buf.Length)
                            If N <= 0 Then Return 0
                            Head.Append(Encoding.ASCII.GetString(Buf, 0, N))
                            If Head.Length > 65536 Then Return 0
                        End While
                        Dim HeadText As String = Head.ToString()
                        If Not (HeadText.StartsWith("HTTP/1.1 200") OrElse HeadText.StartsWith("HTTP/1.1 206")) Then Return 0
                        Dim Received As Integer = 0
                        While Received < MaxBytes
                            Dim N As Integer = Ssl.Read(Buf, 0, Math.Min(Buf.Length, MaxBytes - Received))
                            If N <= 0 Then Exit While
                            Received += N
                        End While
                        If Received <= 0 Then Return 0
                        Return Received * 1000.0 / Math.Max(1L, Watch.ElapsedMilliseconds)
                    End Using
                End Using
            End Using
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>用 DoH 取候选；单源失败不影响整体。</summary>
    Private Function FetchDoh(domain As String) As List(Of String)
        Dim result As New List(Of String)
        Dim endpoints As String()() = {
            New String() {"223.5.5.5", "dns.alidns.com", "/resolve?name=" & domain & "&type=A"},
            New String() {"120.53.53.53", "doh.pub", "/dns-query?name=" & domain & "&type=A"}}
        For Each ep In endpoints
            Try
                Dim body As String = HttpsGetRaw(ep(0), ep(1), ep(2))
                Dim json As Newtonsoft.Json.Linq.JObject = Newtonsoft.Json.Linq.JObject.Parse(body)
                Dim answers = TryCast(json("Answer"), Newtonsoft.Json.Linq.JArray)
                If answers Is Nothing Then Continue For
                For Each item In answers
                    If CInt(item("type")) = 1 Then
                        Dim ip As String = item("data").ToString()
                        If Not result.Contains(ip) Then result.Add(ip)
                    End If
                Next
            Catch ex As Exception
                Logger.Warn(ex, "DoH 源失败（" & ep(1) & "）")
            End Try
        Next
        Return result
    End Function

    ''' <summary>指定 IP + SNI 的原始 HTTPS GET，证书校验保持严格。</summary>
    Private Function HttpsGetRaw(ip As String, sni As String, path As String) As String
        Using sock As New Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            sock.ReceiveTimeout = 4000
            sock.SendTimeout = 4000
            Dim task = sock.ConnectAsync(ip, 443)
            If Not task.Wait(4000) Then Throw New TimeoutException("DoH 连接超时")
            Using net As New NetworkStream(sock, False)
                Using ssl As New SslStream(net, False)
                    ssl.ReadTimeout = 4000
                    ssl.WriteTimeout = 4000
                    ssl.AuthenticateAsClient(sni, Nothing, SslProtocols.Tls12, False)
                    Dim req As String = "GET " & path & " HTTP/1.1" & vbCrLf &
                                        "Host: " & sni & vbCrLf &
                                        "User-Agent: NJITMCL/" & VersionBaseName & vbCrLf &
                                        "Accept: application/dns-json" & vbCrLf &
                                        "Connection: close" & vbCrLf & vbCrLf
                    Dim buf As Byte() = Encoding.ASCII.GetBytes(req)
                    ssl.Write(buf, 0, buf.Length)
                    ssl.Flush()
                    Using ms As New IO.MemoryStream
                        Dim tmp(8191) As Byte
                        Do
                            Dim n As Integer = ssl.Read(tmp, 0, tmp.Length)
                            If n <= 0 Then Exit Do
                            ms.Write(tmp, 0, n)
                        Loop
                        Dim raw As String = Encoding.UTF8.GetString(ms.ToArray())
                        Dim split As Integer = raw.IndexOf(vbCrLf & vbCrLf)
                        If split < 0 Then Throw New Exception("DoH 响应不完整")
                        Return raw.Substring(split + 4)
                    End Using
                End Using
            End Using
        End Using
    End Function

    ''' <summary>
    ''' 后台定期复检 release 资产的 IP 吞吐：这几个 IP 会几分钟轮换一次失效，
    ''' 死守一个固定值会出现「昨天很快、今天只有几十 KB/s」。
    ''' </summary>
    Public Sub StartHealthLoop()
        RunInNewThread(
        Sub()
            Try
                For i = 1 To 45
                    Threading.Thread.Sleep(1000)
                Next
                While True
                    Try
                        CheckReleaseAssets()
                    Catch ex As Exception
                        Logger.Warn(ex, "GitHub release IP 复检失败")
                    End Try
                    Threading.Thread.Sleep(60000)
                End While
            Catch
                '后台线程退出不影响主流程
            End Try
        End Sub, "GithubIpHealth")
    End Sub

    Private Sub CheckReleaseAssets()
        Const Domain As String = "release-assets.githubusercontent.com"
        '正在高速下载时不要复检：测速会和下载抢带宽，把当前线路误判成低速，反而切走好 IP
        If NetManager.Speed > 1048576L Then Return
        Dim seeds As String() = Nothing
        If Not BuiltinSeeds.TryGetValue(Domain, seeds) OrElse seeds Is Nothing Then Return
        Dim Sample As String = ResolveSamplePath()
        If String.IsNullOrWhiteSpace(Sample) Then Return
        Dim scored = ScoreReleaseAssets(Domain, New List(Of String)(seeds), Sample, 1048576, 8000)
        If scored.Count = 0 Then Return
        Dim best = scored(0)
        '新 IP 至少 2 MB/s 才值得替换，避免在几条慢线路之间来回切
        If best.Item1 < 2097152.0 Then Return
        Dim old As String = ReadSaved(Domain)
        If best.Item2 = old Then Return
        If Not String.IsNullOrWhiteSpace(old) Then
            '只有当新 IP 明显更快才换，避免两个 IP 之间来回抖动
            Dim oldSpeed As Double = 0
            For Each item In scored
                If item.Item2 = old Then oldSpeed = item.Item1
            Next
            If oldSpeed > 0 AndAlso best.Item1 < oldSpeed * 2.0 Then Return
        End If
        '写回前再确认一次：优选线程可能刚好改过，避免两边互相覆盖
        If ReadSaved(Domain) <> old Then Return
        WriteSaved(Domain, best.Item2)
        MarkValidated(Domain, best.Item2)
        Logger.Info($"GitHub release IP 复检切换：{If(String.IsNullOrWhiteSpace(old), "无", old)} -> {best.Item2}（{best.Item1 / 1048576:F1} MB/s）")
    End Sub

End Module
