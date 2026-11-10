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

    Private Const CacheTtlMs As Long = 6L * 60 * 60 * 1000
    Private Const ProbeTimeoutMs As Integer = 4000
    Private Const ProbeWorkers As Integer = 12

    Private Class CacheEntry
        Public Ips As String()
        Public Stamp As Long
    End Class

    Private ReadOnly Cache As New Dictionary(Of String, CacheEntry)
    Private ReadOnly CacheLock As New Object

    ''' <summary>运行期连接失败的 IP，10 分钟内不再优先尝试。</summary>
    Private ReadOnly FailedUntil As New Dictionary(Of String, Long)

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

    ''' <summary>返回按首字节延迟排序的可用 IP；全部失败时返回原始候选供上层逐个试连。</summary>
    Public Function GetRanked(domain As String) As List(Of String)
        domain = domain.Trim().ToLowerInvariant()
        SyncLock CacheLock
            Dim hit As CacheEntry = Nothing
            If Cache.TryGetValue(domain, hit) AndAlso
               GetTimeMs() - hit.Stamp < CacheTtlMs AndAlso hit.Ips.Length > 0 Then
                Return FilterFailed(domain, New List(Of String)(hit.Ips))
            End If
        End SyncLock

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

        Dim usable As New List(Of String)
        For Each ip In candidates
            If Not IsDenied(ip) AndAlso Not usable.Contains(ip) Then usable.Add(ip)
        Next
        If usable.Count = 0 Then Return candidates

        Dim scored As New ConcurrentBag(Of Tuple(Of Integer, String))
        Parallel.ForEach(usable, New ParallelOptions With {.MaxDegreeOfParallelism = ProbeWorkers},
            Sub(ip)
                Dim ms As Integer = Probe(domain, ip, ProbeTimeoutMs)
                If ms >= 0 Then scored.Add(Tuple.Create(ms, ip))
            End Sub)

        Dim ordered As List(Of String) = scored.OrderBy(Function(x) x.Item1).
            Select(Function(x) x.Item2).ToList()
        If ordered.Count = 0 Then ordered = usable
        Logger.Info("GitHub IP 优选（" & domain & "）：" &
                    String.Join(", ", ordered.Select(Function(x) x)))

        SyncLock CacheLock
            Cache(domain) = New CacheEntry With {.Ips = ordered.ToArray(), .Stamp = GetTimeMs()}
        End SyncLock
        Return FilterFailed(domain, ordered)
    End Function

    ''' <summary>连接失败时把该 IP 短暂拉黑，下次优先换下一个。</summary>
    Public Sub ReportFailure(domain As String, ip As String)
        SyncLock CacheLock
            FailedUntil(domain.Trim().ToLowerInvariant() & "|" & ip) = GetTimeMs() + 10L * 60 * 1000
        End SyncLock
    End Sub

    Private Function FilterFailed(domain As String, ips As List(Of String)) As List(Of String)
        SyncLock CacheLock
            Dim now As Long = GetTimeMs()
            Dim alive As New List(Of String)
            For Each ip As String In ips
                Dim until As Long = 0
                If FailedUntil.TryGetValue(domain & "|" & ip, until) AndAlso until > now Then Continue For
                alive.Add(ip)
            Next
            If alive.Count > 0 Then Return alive
        End SyncLock
        Return ips
    End Function

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
                        ssl.AuthenticateAsClient(domain, Nothing, SslProtocols.Tls12, True)
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
            sock.ReceiveTimeout = 8000
            sock.SendTimeout = 8000
            Dim task = sock.ConnectAsync(ip, 443)
            If Not task.Wait(6000) Then Throw New TimeoutException("DoH 连接超时")
            Using net As New NetworkStream(sock, False)
                Using ssl As New SslStream(net, False)
                    ssl.ReadTimeout = 8000
                    ssl.WriteTimeout = 8000
                    ssl.AuthenticateAsClient(sni, Nothing, SslProtocols.Tls12, True)
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

End Module
