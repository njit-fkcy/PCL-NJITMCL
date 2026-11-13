''' <summary>
''' 「联机 → 服务器」：社团服务器列表、整合包下载与修订更新。
''' </summary>
Public Class PageLinkServer
    Implements IRefreshable

#Region "服务器整合包"

    Private PackServers As New JArray()
    Private PackLoaded As Boolean = False
    Private PackDownloading As Boolean = False
    '当前下载使用的 PCL 原生下载加载器，取消时直接中断
    Private PackLoader As LoaderDownload = Nothing
    '用户是否已经请求取消（测速阶段也要能取消）
    Private PackCancelRequested As Boolean = False
    '当前下载对应的服务器条目键，用于把进度写到它自己那一行，而不是只写在卡片顶部
    Private PackCurrentKey As String = ""

    ''' <summary>
    ''' 单个服务器条目上的下载进度区域（PCL 原生进度 + 取消按钮）。
    ''' </summary>
    Private Class PackProgressUi
        Public Box As Border
        Public Loading As MyLoading
        Public Cancel As MyTextButton
    End Class

    Private ReadOnly PackProgressMap As New Dictionary(Of String, PackProgressUi)()
    '不接加载器时用的状态，仅让 PCL 原生进度区域保持转圈并显示文字
    Private ReadOnly PackRunState As New MyLoadingStateSimulator With {.LoadingState = MyLoading.MyLoadingState.Run}
    Private ReadOnly PackIdleState As New MyLoadingStateSimulator()
    Private ArchivedExpanded As Boolean = False
    Private ArchivedCount As Integer = 0
    Private OnlineExpanded As Boolean = True
    Private OnlineCount As Integer = 0
    '整合包修订正在应用时，禁止刷新、检查更新与重复点立即更新
    Private PackUpdating As Boolean = False
    Private ReadOnly PackUpdateWatch As Stopwatch = Stopwatch.StartNew()
    Private LastPackUpdateUiMs As Long = -1000

    ''' <summary>
    ''' 进入页面时拉取一次服务器列表，短时间内的重复进入沿用缓存。
    ''' </summary>
    Private Sub PageEnterHandler() Handles Me.PageEnter
        If PackDownloading OrElse PackUpdating Then Return
        If PackLoaded AndAlso (Date.Now - PackFetchedAt).TotalSeconds < 30 Then Return
        PackLoaded = True
        RunInThread(AddressOf RefreshPackList)
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        If PackDownloading OrElse PackUpdating Then Return
        RunInThread(AddressOf RefreshPackList)
    End Sub

    ''' <summary>
    ''' 全局刷新：重新拉服务器列表与账号信息。
    ''' </summary>
    Private Sub BtnRefreshAll_Click(sender As Object, e As EventArgs) Handles BtnRefreshAll.Click
        If PackDownloading OrElse PackUpdating Then
            Hint("有任务正在进行，完成后再刷新")
            Return
        End If
        LabPackStatus.Text = "正在获取服务器状态……"
        Hint("正在刷新……")
        FrmLaunchLeft?.RefreshPage(True, True)
        RunInThread(Sub() RefreshPackList())
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

    ''' <summary>是否还有未应用的整合包修订。</summary>
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
    ''' 联机模式启动前协商社团服务器地址。
    ''' 先按实例的整合包标记（serverId）找到所属服务器，找不到再按自动进服地址匹配；
    ''' 命中在营服务器后按 内网/组网 → 穿透 → 公网 的顺序探测，返回第一个可达地址。
    ''' 与该实例无关时返回 Nothing（正常启动），全部不可达时返回空字符串（视为离线）。
    ''' </summary>
    Public Function ResolveClubServerForLaunch(Instance As McInstance) As String
        If Instance Is Nothing Then Return Nothing
        '启动前实时拉一次列表，避免拿到过期的地址；网络失败时沿用上次结果
        If (Date.Now - PackFetchedAt).TotalSeconds > 20 Then
            Try
                Dim FetchTask = Threading.Tasks.Task.Run(Function() FetchPackServers())
                If FetchTask.Wait(4000) Then
                    PackServers = FetchTask.Result
                    PackFetchedAt = Date.Now
                Else
                    Logger.Warn("启动前获取社团服务器列表超时，沿用上次结果")
                End If
            Catch ex As Exception
                Logger.Warn(ex, "启动前获取社团服务器列表失败")
            End Try
        End If
        If PackServers.Count = 0 Then Return Nothing

        '确定实例所属的社团服务器：优先整合包标记，其次自动进服地址
        Dim ServerId As String = ""
        Dim Target As String = Settings.Get(Of String)("VersionServerEnter", Instance)
        If Instance IsNot Nothing Then
            Dim Mark As JObject = ModPackRevision.ReadMark(Instance.PathVersion)
            If Mark IsNot Nothing Then ServerId = If(Mark("serverId")?.ToString(), "").Trim()
        End If
        Dim Matched As JObject = Nothing
        For Each Token As JToken In PackServers
            Dim Server As JObject = TryCast(Token, JObject)
            If Server Is Nothing OrElse IsArchivedServer(Server) Then Continue For
            If ServerId <> "" AndAlso String.Equals(Server("id")?.ToString(), ServerId, StringComparison.OrdinalIgnoreCase) Then
                Matched = Server
                Exit For
            End If
        Next
        If Matched Is Nothing AndAlso Not String.IsNullOrWhiteSpace(Target) Then
            Dim TargetHost As String = AddressHost(Target)
            If TargetHost <> "" Then
                For Each Token As JToken In PackServers
                    Dim Server As JObject = TryCast(Token, JObject)
                    If Server Is Nothing OrElse IsArchivedServer(Server) Then Continue For
                    For Each One As String In ServerAddressList(Server)
                        If String.Equals(AddressHost(One), TargetHost, StringComparison.OrdinalIgnoreCase) Then
                            Matched = Server
                            Exit For
                        End If
                    Next
                    If Matched IsNot Nothing Then Exit For
                Next
            End If
        End If
        If Matched Is Nothing Then Return Nothing
        Return ResolveServerAddress(Matched)
    End Function

    ''' <summary>
    ''' 探测指定服务器配置的接入地址：内网/组网 → 穿透 → 公网，全不通返回空字符串。
    ''' 官网标记离线的地址直接剔除；官网标记在线的公网/穿透地址可直接信任，
    ''' 内网/组网与状态未知的地址仍在本机实测（域名可能靠 SRV 记录）。
    ''' </summary>
    Private Function ResolveServerAddress(Server As JObject) As String
        Dim Ordered As List(Of String) = ServerAddressList(Server).
            Where(Function(Address) AddressState(Server, Address) <> "offline").
            OrderBy(Function(Address) AddressRank(Server, Address)).ToList()
        If Ordered.Count = 0 Then Return ""
        Dim Needy As New List(Of String)
        For Each Address As String In Ordered
            If IsTrustedAddress(Server, Address) Then Continue For
            Needy.Add(Address)
        Next
        Dim Reachable As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If Needy.Count > 0 Then
            '并行探测，避免串行超时把启动拖太久
            Dim Probes As New List(Of Threading.Tasks.Task(Of Boolean))
            For Each Address As String In Needy
                Dim Value As String = Address
                Probes.Add(Threading.Tasks.Task.Run(Function() TcpReachableAddress(Value)))
            Next
            Try
                Threading.Tasks.Task.WaitAll(Probes.ToArray(), 4000)
            Catch ex As Exception
                Logger.Warn(ex, "探测社团服务器地址失败")
            End Try
            For Index As Integer = 0 To Needy.Count - 1
                If Probes(Index).IsCompleted AndAlso Not Probes(Index).IsFaulted AndAlso Not Probes(Index).IsCanceled AndAlso
                    Probes(Index).Result Then
                    Reachable.Add(Needy(Index))
                End If
            Next
        End If
        '按 内网 → 穿透 → 公网 的顺序取第一个可用地址
        For Each Address As String In Ordered
            If IsTrustedAddress(Server, Address) OrElse Reachable.Contains(Address) Then Return Address
        Next
        Return ""
    End Function

    ''' <summary>官网标记在线的公网/穿透地址可直接使用；内网/组网地址仍要在本机探测。</summary>
    Private Function IsTrustedAddress(Server As JObject, Address As String) As Boolean
        If AddressState(Server, Address) <> "online" Then Return False
        Dim Kind As String = AddressType(Server, Address)
        Return Kind = "public" OrElse Kind = "frp"
    End Function

    ''' <summary>地址类型：public / frp / network（没有记录返回空串）。</summary>
    Private Function AddressType(Server As JObject, Address As String) As String
        Dim Addresses As JArray = TryCast(Server("addresses"), JArray)
        If Addresses Is Nothing Then Return ""
        For Each One As JToken In Addresses
            Dim Item As JObject = TryCast(One, JObject)
            If Item Is Nothing Then Continue For
            If Not String.Equals(If(Item("address")?.ToString(), "").Trim(), Address, StringComparison.OrdinalIgnoreCase) Then Continue For
            Return If(Item("type")?.ToString(), "")
        Next
        Return ""
    End Function

    ''' <summary>服务器是否已结档。</summary>
    Private Function IsArchivedServer(Server As JObject) As Boolean
        Return Equals(Server("archived")?.ToObject(Of Boolean)(), True)
    End Function

    ''' <summary>服务器配置的全部接入地址（按接口返回顺序，含公网、穿透、内网/组网）。</summary>
    Private Function ServerAddressList(Server As JObject) As List(Of String)
        Dim Items As New List(Of String)
        Dim Addresses As JArray = TryCast(Server("addresses"), JArray)
        If Addresses IsNot Nothing Then
            For Each One As JToken In Addresses
                Dim Item As JObject = TryCast(One, JObject)
                If Item Is Nothing Then Continue For
                Dim Value As String = If(Item("address")?.ToString(), "").Trim()
                If Value <> "" Then Items.Add(Value)
            Next
        End If
        If Items.Count = 0 Then
            Dim OnlyOne As String = If(Server("address")?.ToString(), "").Trim()
            If OnlyOne <> "" Then Items.Add(OnlyOne)
        End If
        Return Items
    End Function

    ''' <summary>地址在官网列表里的状态：online / offline / unknown（没有记录返回空串）。</summary>
    Private Function AddressState(Server As JObject, Address As String) As String
        Dim Addresses As JArray = TryCast(Server("addresses"), JArray)
        If Addresses Is Nothing Then Return ""
        For Each One As JToken In Addresses
            Dim Item As JObject = TryCast(One, JObject)
            If Item Is Nothing Then Continue For
            If Not String.Equals(If(Item("address")?.ToString(), "").Trim(), Address, StringComparison.OrdinalIgnoreCase) Then Continue For
            Return If(Item("state")?.ToString(), "")
        Next
        Return ""
    End Function

    ''' <summary>地址优先级数值，越小越优先：内网/组网 0，穿透 1，公网 2。</summary>
    Private Function AddressRank(Server As JObject, Address As String) As Integer
        Dim Addresses As JArray = TryCast(Server("addresses"), JArray)
        If Addresses IsNot Nothing Then
            For Each One As JToken In Addresses
                Dim Item As JObject = TryCast(One, JObject)
                If Item Is Nothing Then Continue For
                If Not String.Equals(If(Item("address")?.ToString(), "").Trim(), Address, StringComparison.OrdinalIgnoreCase) Then Continue For
                Select Case If(Item("type")?.ToString(), "")
                    Case "network" : Return 0
                    Case "frp" : Return 1
                    Case "public" : Return 2
                End Select
                Exit For
            Next
        End If
        Return 3
    End Function

    ''' <summary>取出地址中的主机部分（去掉端口，兼容 IPv6 的方括号写法）。</summary>
    Private Function AddressHost(Address As String) As String
        If String.IsNullOrWhiteSpace(Address) Then Return ""
        Dim Text As String = Address.Trim()
        If Text.StartsWith("[") Then
            Dim CloseIndex As Integer = Text.IndexOf("]")
            Return If(CloseIndex > 0, Text.Substring(1, CloseIndex - 1), Text)
        End If
        If Text.Count(Function(c) c = ":"c) = 1 Then Return Text.BeforeFirst(":")
        Return Text
    End Function

    ''' <summary>探测服务器地址（host:port，缺省 25565）是否可连通。</summary>
    Private Function TcpReachableAddress(Address As String) As Boolean
        Dim HostValue As String = AddressHost(Address)
        If HostValue = "" Then Return False
        Dim PortValue As Integer = 25565
        Dim Text As String = Address.Trim()
        If Text.StartsWith("[") Then
            Dim CloseIndex As Integer = Text.IndexOf("]")
            If CloseIndex > 0 AndAlso CloseIndex + 1 < Text.Length AndAlso Text.Chars(CloseIndex + 1) = ":"c Then
                Integer.TryParse(Text.Substring(CloseIndex + 2), PortValue)
            End If
        ElseIf Text.Count(Function(c) c = ":"c) = 1 Then
            Integer.TryParse(Text.AfterFirst(":"), PortValue)
        End If
        If PortValue <= 0 Then PortValue = 25565
        Try
            Using Client As New System.Net.Sockets.TcpClient()
                Dim Connecting As Threading.Tasks.Task = Client.ConnectAsync(HostValue, PortValue)
                Return Connecting.Wait(1500) AndAlso Client.Connected
            End Using
        Catch
            Return False
        End Try
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
            RunInUi(Sub()
                        If IsLoaded Then RenderPackList()
                    End Sub)
            CheckPackUpdates(Notify)
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
        '每个条目自己的下载进度：默认隐藏，下载时只在这一行显示 PCL 原生下载进度
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
        Dim ProHead As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .HorizontalAlignment = HorizontalAlignment.Right
        }
        ProHead.Children.Add(ProCancel)

        Dim ProLoading As New MyLoading With {
            .Text = "下载中",
            .ShowProgress = True,
            .MinHeight = 72,
            .Margin = New Thickness(0, 4, 0, 0),
            .HorizontalAlignment = HorizontalAlignment.Stretch
        }

        Dim ProStack As New StackPanel
        ProStack.Children.Add(ProHead)
        ProStack.Children.Add(ProLoading)

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
            .Box = ProBox, .Loading = ProLoading, .Cancel = ProCancel
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
            '本机已装过该服务器整合包时，按钮直接变成「启动」
            Dim InstalledName As String = If(Archived, "", FindInstalledInstanceName(Server, Pack))
            Dim Btn As New MyButton With {
                .Text = If(Archived, "下载整合包",
                           If(InstalledName <> "", "启动", If(IsAccountAllowed(), "下载并安装", "登录后下载"))),
                .Height = If(Archived, 28, 32),
                .Width = If(Archived, 92, 104),
                .Margin = New Thickness(0, 0, 6, 6),
                .VerticalAlignment = VerticalAlignment.Center
            }
            Dim LocalServer As JObject = Server
            Dim LocalPack As JObject = Pack
            If Not Archived AndAlso InstalledName <> "" Then
                Btn.ColorType = MyButton.ColorState.Highlight
                Dim LocalInstName As String = InstalledName
                AddHandler Btn.Click, Sub(sender As Object, e As MouseButtonEventArgs)
                                          LaunchPackInstance(LocalServer, LocalInstName)
                                      End Sub
            Else
                AddHandler Btn.Click, Sub(sender As Object, e As MouseButtonEventArgs)
                                          StartPackDownload(LocalServer, LocalPack)
                                      End Sub
            End If
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
    ''' 本机是否已安装该服务器的整合包：按修订标记里的 packId / serverId 判断。
    ''' 返回实例名，没装过返回空字符串。
    ''' </summary>
    Private Function FindInstalledInstanceName(Server As JObject, Pack As JObject) As String
        Dim ServerId As String = If(Server("id")?.ToString(), "")
        Dim PackId As String = If(Pack("id")?.ToString(), "")
        If PackId = "" Then Return ""
        For Each One In ModPackRevision.ScanLocalMarks()
            Dim Mark As JObject = One.Mark
            If Not String.Equals(If(Mark("packId")?.ToString(), ""), PackId, StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim LocalServer As String = If(Mark("serverId")?.ToString(), "")
            If LocalServer <> "" AndAlso ServerId <> "" AndAlso
                Not String.Equals(LocalServer, ServerId, StringComparison.OrdinalIgnoreCase) Then Continue For
            Return One.InstanceName
        Next
        Return ""
    End Function

    ''' <summary>
    ''' 从服务器列表直接启动本机客户端：切到该实例并自动进入服务器，无视启动页当前选的版本。
    ''' </summary>
    Private Sub LaunchPackInstance(Server As JObject, InstanceName As String)
        If String.IsNullOrWhiteSpace(InstanceName) Then Return
        RunInNewThread(
        Sub()
            Try
                '有未应用的修订时先拦下，避免带着旧模组进服
                If HasPendingUpdatesNow() Then
                    RunInUi(Sub() Hint("该整合包有新修订，请先在「整合包修订」卡片点「立即更新」", HintType.Red))
                    Return
                End If
                Dim Inst As McInstance = New McInstance(InstanceName).Load()
                If Inst Is Nothing OrElse Inst.State = McInstance.McInstanceState.Error Then
                    RunInUi(Sub() Hint("无法启动该服务器客户端：" & If(Inst?.Info, "实例不存在"), HintType.Red))
                    Return
                End If
                '内网 → 穿透 → 公网 依次探测，都不可达就视为离线
                Dim Address As String = ResolveServerAddress(Server)
                If Address = "" Then
                    RunInUi(Sub() Hint("社团服务器离线，已取消启动：" & If(Server("name")?.ToString(), ""), HintType.Red))
                    Return
                End If
                RunInUi(
                Sub()
                    '先回到启动页，避免停留在服务器列表看不到启动进度
                    FrmMain.PageChange(FormMain.PageType.Launch)
                    McLaunchStart(New McLaunchOptions With {.Instance = Inst, .ServerIp = Address})
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "启动社团服务器客户端失败")
                RunInUi(Sub() Hint("启动失败：" & ex.Message, HintType.Red))
            End Try
        End Sub, "SocialLaunchPack")
    End Sub

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
    ''' 在当前任务对应的条目上显示 PCL 原生下载进度（绑定加载器）。
    ''' </summary>
    Private Sub SetPackLoader(Loader As ILoadingTrigger, Text As String)
        RunInUi(
        Sub()
            Dim Item As PackProgressUi = Nothing
            If PackCurrentKey = "" OrElse Not PackProgressMap.TryGetValue(PackCurrentKey, Item) Then Return
            Item.Box.Visibility = Visibility.Visible
            Item.Loading.Text = Text
            Item.Loading.State = Loader
        End Sub)
    End Sub

    ''' <summary>
    ''' 当前任务没有加载器时（测速、安装、拼接），让原生进度区继续转圈并显示文字。
    ''' </summary>
    Private Sub SetPackStatus(Text As String)
        RunInUi(
        Sub()
            Dim Item As PackProgressUi = Nothing
            If PackCurrentKey = "" OrElse Not PackProgressMap.TryGetValue(PackCurrentKey, Item) Then Return
            Item.Box.Visibility = Visibility.Visible
            Item.Loading.Text = Text
            Item.Loading.State = PackRunState
        End Sub)
    End Sub

    ''' <summary>
    ''' 隐藏当前条目的进度区与取消按钮。
    ''' </summary>
    Private Sub HidePackProgress()
        RunInUi(
        Sub()
            Dim Item As PackProgressUi = Nothing
            If PackCurrentKey = "" OrElse Not PackProgressMap.TryGetValue(PackCurrentKey, Item) Then Return
            Item.Box.Visibility = Visibility.Collapsed
            Item.Loading.State = PackIdleState
        End Sub)
    End Sub

    ''' <summary>
    ''' 取消当前下载任务（条目上的「取消」和顶部按钮共用）。
    ''' </summary>
    Private Sub CancelPackDownload()
        If Not PackDownloading Then Return
        PackCancelRequested = True
        LabPackStatus.Text = "正在取消下载……"
        '条目上也要给出反馈，并收起取消按钮，避免重复点击
        SetPackStatus("正在取消下载……")
        If PackCurrentKey <> "" AndAlso PackProgressMap.ContainsKey(PackCurrentKey) Then
            RunInUi(Sub()
                        PackProgressMap(PackCurrentKey).Cancel.Visibility = Visibility.Collapsed
                    End Sub)
        End If
        PackLoader?.Cancel()
    End Sub

    Private Function FormatPackSize(Bytes As Long) As String
        If Bytes <= 0 Then Return "大小未知"
        If Bytes >= 1024L * 1024 * 1024 Then Return $"{Bytes / 1024.0 / 1024 / 1024:F2} GB"
        Return $"{Bytes / 1024.0 / 1024:F1} MB"
    End Function

    ''' <summary>
    ''' 用 PCL 原生下载引擎下载文件：进度直接交给条目上的 MyLoading 展示。
    ''' 与 NetDownloadByLoader 不同，这里自己驱动加载器，以便中途取消后不会再次启动。
    ''' </summary>
    Private Sub DownloadByLoader(Urls As List(Of String), Target As String, ExpectedHash As String, Text As String)
        Dim Checker As New FileChecker With {.MinSize = 1024}
        If Not String.IsNullOrWhiteSpace(ExpectedHash) Then Checker.Hash = ExpectedHash
        Dim Task As New LoaderDownload("社团整合包 " & GetUuid() & "#",
                                       New List(Of NetFile) From {New NetFile(Urls, Target, Checker)})
        RunLoader(Task, Text)
    End Sub

    ''' <summary>
    ''' 启动 PCL 原生下载加载器并等待结束，进度写到当前条目。
    ''' </summary>
    Private Sub RunLoader(Task As LoaderDownload, Text As String)
        PackLoader = Task
        SetPackLoader(Task, Text)
        Task.Start()
        '取消可能发生在加载器创建之前，启动后补一次
        If PackCancelRequested Then Task.Cancel()
        Do While Task.State = LoadState.Loading
            Threading.Thread.Sleep(50)
        Loop
        PackLoader = Nothing
        If Task.State = LoadState.Canceled Then Throw New OperationCanceledException()
        If Task.State = LoadState.Failed Then
            Throw New Exception(If(Task.Error Is Nothing, "下载失败", Task.Error.Message), Task.Error)
        End If
    End Sub

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
    ''' 分片用同一个 PCL 原生下载任务并行下载，进度条显示整体进度。
    ''' </summary>
    Private Sub DownloadSaveParts(Parts As JArray, Target As String)
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
        Dim Files As New List(Of NetFile)
        For Each Item As JObject In Ordered
            Dim PartName As String = Item("file").ToString()
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
            Dim Checker As New FileChecker With {.MinSize = 1024}
            If Expected <> "" Then Checker.Hash = Expected
            Files.Add(New NetFile(Urls, PartFolder & PartName, Checker))
        Next
        RunLoader(New LoaderDownload("社团存档 " & GetUuid() & "#", Files), "正在下载存档")
        SetPackStatus("正在拼接存档分片……")
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
        PackCancelRequested = False
        PackCurrentKey = ServerKey(Server)
        SetPackStatus("正在准备下载存档……")
        RunInThread(
        Sub()
            Dim Cancelled As Boolean = False
            Try
                Directory.CreateDirectory(PathTemp & "Cache\Saves\")
                Dim Ready As Boolean = False
                '优先整包源（通常是校园内网，速度最快）
                If Urls.Count > 0 Then
                    SetPackStatus("正在测速选择下载源……")
                    Dim Ordered As List(Of String) = RankPackSources(Urls)
                    If PackCancelRequested Then Throw New OperationCanceledException()
                    Try
                        DownloadByLoader(Ordered, Target, ExpectedHash, "正在下载存档")
                        Ready = True
                    Catch ex As OperationCanceledException
                        Throw
                    Catch ex As Exception
                        Logger.Warn($"存档下载源失败：{ex.Message}")
                    End Try
                End If
                '整包源不可用时，回退到按 split 规范下载分片并拼接
                If Not Ready AndAlso HasParts Then
                    DownloadSaveParts(Parts, Target)
                    If ExpectedHash <> "" AndAlso GetPackSha256(Target) <> ExpectedHash Then Throw New Exception("存档分片拼接后校验失败")
                    Ready = True
                End If
                If Not Ready Then Throw New Exception("所有下载源都失败了")
                If PackCancelRequested Then Throw New OperationCanceledException()
                SetPackStatus("正在解压存档……")
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
                    PackLoader = Nothing
                    PackCancelRequested = False
                    BtnPackCancel.Visibility = Visibility.Collapsed
                    HidePackProgress()
                    PackCurrentKey = ""
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
    ''' 下载并安装指定整合包：多源测速 → PCL 原生下载 → 校验 → 调 PCL 安装。
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
        PackCancelRequested = False
        PackCurrentKey = ServerKey(Server)
        SetPackStatus("正在准备下载……")
        RunInThread(
        Sub()
            Dim Cancelled As Boolean = False
            Try
                Directory.CreateDirectory(PathTemp & "Cache\Modpacks\")
                SetPackStatus("正在测速选择下载源……")
                Dim Ordered As List(Of String) = RankPackSources(Urls)
                If PackCancelRequested Then Throw New OperationCanceledException()
                DownloadByLoader(Ordered, Target, ExpectedHash, "正在下载整合包")
                If PackCancelRequested Then Throw New OperationCanceledException()
                SetPackStatus("正在安装整合包……")
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
                    PackLoader = Nothing
                    PackCancelRequested = False
                    BtnPackCancel.Visibility = Visibility.Collapsed
                    HidePackProgress()
                    PackCurrentKey = ""
                    RenderPackList()
                    If Cancelled Then LabPackStatus.Text = "已取消下载"
                End Sub)
            End Try
        End Sub)
    End Sub

#End Region

End Class
