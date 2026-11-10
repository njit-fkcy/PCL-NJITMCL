Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading.Tasks

''' <summary>
''' GitHub 域名的本地 CONNECT 代理：只放行白名单域名的 443，裸 TCP 透传，不做 MITM。
''' </summary>
Public Module ModGithubProxy

    Private ReadOnly StartLock As New Object
    Private Listener As TcpListener
    Private Started As Boolean
    Private Disabled As Boolean

    ''' <summary>注入给 HttpClientHandler 的代理选择器。</summary>
    Public ReadOnly Proxy As New NjitGithubWebProxy

    ''' <summary>懒启动代理；开关关闭或启动失败时静默回退直连。</summary>
    Public Sub EnsureStarted()
        SyncLock StartLock
            If Started OrElse Disabled Then Return
            Try
                Dim enabled As Boolean = True
                Try
                    enabled = Settings.Get(Of Boolean)("NjitGithubIpEnabled")
                Catch
                    '设置项缺失时按默认开启处理
                End Try
                If Not enabled Then
                    Disabled = True
                    Logger.Info("GitHub 优选已关闭，使用直连")
                    Return
                End If
                Listener = New TcpListener(IPAddress.Loopback, 0)
                Listener.Start()
                Proxy.Port = DirectCast(Listener.LocalEndpoint, IPEndPoint).Port
                Started = True
                RunInNewThread(AddressOf AcceptLoop, "GithubProxy")
                Logger.Info("GitHub 优选代理已启动：127.0.0.1:" & Proxy.Port)
            Catch ex As Exception
                Disabled = True
                Logger.Warn(ex, "GitHub 优选代理启动失败，回退直连")
            End Try
        End SyncLock
    End Sub

    Private Sub AcceptLoop()
        While True
            Try
                Dim client As TcpClient = Listener.AcceptTcpClient()
                RunInNewThread(Sub() HandleClient(client), "GithubProxyConn")
            Catch
                Thread.Sleep(200)
            End Try
        End While
    End Sub

    Private Sub HandleClient(client As TcpClient)
        Dim upstream As TcpClient = Nothing
        Try
            client.NoDelay = True
            Dim down As NetworkStream = client.GetStream()
            Dim header As String = ReadHeader(down)
            Dim firstLine As String = header.Split(New String() {vbCrLf}, StringSplitOptions.None)(0)
            Dim parts As String() = firstLine.Split(" "c)
            If parts.Length < 2 OrElse parts(0).ToUpperInvariant() <> "CONNECT" Then
                SendText(down, "HTTP/1.1 405 Method Not Allowed")
                Return
            End If

            Dim hostPort As String() = parts(1).Split(":"c)
            If hostPort.Length <> 2 OrElse hostPort(1) <> "443" Then
                SendText(down, "HTTP/1.1 403 Forbidden")
                Logger.Warn("优选代理拒绝非 443 端口：" & parts(1))
                Return
            End If
            Dim host As String = hostPort(0).Trim().ToLowerInvariant()
            If Not ModGithubIp.IsGithubHost(host) Then
                SendText(down, "HTTP/1.1 403 Forbidden")
                Logger.Warn("优选代理拒绝非白名单域名：" & host)
                Return
            End If

            Dim used As String = ""
            For Each ip As String In ModGithubIp.GetRanked(host)
                Try
                    Dim candidate As New TcpClient()
                    candidate.NoDelay = True
                    Dim task = candidate.ConnectAsync(ip, 443)
                    If task.Wait(3000) AndAlso candidate.Connected Then
                        upstream = candidate
                        used = ip
                        Exit For
                    End If
                    ModGithubIp.ReportFailure(host, ip)
                    candidate.Close()
                Catch
                    ModGithubIp.ReportFailure(host, ip)
                End Try
            Next

            If upstream Is Nothing Then
                SendText(down, "HTTP/1.1 502 Bad Gateway")
                Logger.Warn("优选代理没有可用 IP：" & host)
                Return
            End If

            Logger.Info("优选代理：" & host & " -> " & used)
            SendText(down, "HTTP/1.1 200 Connection Established")
            Dim up As NetworkStream = upstream.GetStream()
            Dim t1 As Task = Task.Run(Sub() Pump(down, up))
            Dim t2 As Task = Task.Run(Sub() Pump(up, down))
            Task.WaitAny(t1, t2)
        Catch ex As Exception
            Logger.Warn(ex, "优选代理连接异常")
        Finally
            Try
                upstream?.Close()
            Catch
            End Try
            Try
                client.Close()
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>逐字节读请求头，读到空行结束。</summary>
    Private Function ReadHeader(stream As NetworkStream) As String
        Dim sb As New StringBuilder
        Dim one(0) As Byte
        While stream.Read(one, 0, 1) = 1
            sb.Append(ChrW(one(0)))
            If sb.Length > 8192 Then Exit While
            If sb.Length >= 4 AndAlso sb.ToString().EndsWith(vbCrLf & vbCrLf) Then Exit While
        End While
        Return sb.ToString()
    End Function

    Private Sub SendText(stream As NetworkStream, status As String)
        Dim buf As Byte() = Encoding.ASCII.GetBytes(status & vbCrLf & "Connection: close" & vbCrLf & vbCrLf)
        stream.Write(buf, 0, buf.Length)
        stream.Flush()
    End Sub

    Private Sub Pump(fromStream As NetworkStream, toStream As NetworkStream)
        Try
            fromStream.ReadTimeout = 120000
            Dim buf(32767) As Byte
            Do
                Dim n As Integer = fromStream.Read(buf, 0, buf.Length)
                If n <= 0 Then Exit Do
                toStream.Write(buf, 0, n)
                toStream.Flush()
            Loop
        Catch
            '任一端断开或空闲超时即结束
        End Try
    End Sub

End Module

''' <summary>
''' 按目标域名决定是否走本地优选代理；其余请求沿用系统代理设置。
''' </summary>
Public Class NjitGithubWebProxy
    Implements IWebProxy

    Private ReadOnly Fallback As IWebProxy = WebRequest.GetSystemWebProxy()

    Public Property Port As Integer = -1

    Public Function GetProxy(destination As Uri) As Uri Implements IWebProxy.GetProxy
        If destination Is Nothing Then Return Nothing
        If Port > 0 AndAlso ModGithubIp.IsGithubHost(destination.Host) Then
            Return New Uri("http://127.0.0.1:" & Port)
        End If
        Return Fallback.GetProxy(destination)
    End Function

    Public Function IsBypassed(host As Uri) As Boolean Implements IWebProxy.IsBypassed
        If host Is Nothing Then Return True
        If Port > 0 AndAlso ModGithubIp.IsGithubHost(host.Host) Then Return False
        Return Fallback.IsBypassed(host)
    End Function

    Public Property Credentials As ICredentials Implements IWebProxy.Credentials
End Class
