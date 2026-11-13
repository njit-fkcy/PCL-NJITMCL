Imports System.Threading
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media

''' <summary>
''' 「联机 → 好友」：搜索、申请、删除、拉黑与解除。
''' </summary>
Public Class PageLinkFriend
    Implements IRefreshable

    Private Built As Boolean = False
    Private TxtSearch As MyTextBox

    '停留在好友页时定期刷新，好友申请与在线状态才不会滞后
    Private FriendTimer As System.Windows.Threading.DispatcherTimer

    Private Sub PageEnterHandler() Handles Me.PageEnter
        If Not Built Then
            Built = True
            BuildTools()
        End If
        RefreshAll()
        If FriendTimer Is Nothing Then
            FriendTimer = New System.Windows.Threading.DispatcherTimer With {
                .Interval = TimeSpan.FromSeconds(20)}
            AddHandler FriendTimer.Tick, Sub() RefreshAll(Silent:=True)
        End If
        FriendTimer.Stop()
        FriendTimer.Start()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        RefreshAll()
    End Sub

    Private DevicePolling As Boolean = False

    ''' <summary>申请设备码并轮询授权结果；用户在官网确认后自动完成登录。</summary>
    Public Sub StartDeviceLogin(sender As Object, e As MouseButtonEventArgs)
        If DevicePolling Then Return
        DevicePolling = True
        LabAccount.Text = "正在申请设备码……"
        RunInNewThread(
        Sub()
            Try
                Dim Result As JObject = ModSocial.RequestDeviceCode("NJIT MCL")
                Dim DeviceValue As String = If(Result("deviceCode")?.ToString(), "")
                RunInUi(
                Sub()
                    PanDeviceCode.Visibility = Visibility.Visible
                    LabDeviceCode.Text = If(Result("userCode")?.ToString(), "")
                    LabAccount.Text = "请在浏览器打开授权页，登录社团官网（正版或 MUA）后输入上方设备码并确认。"
                End Sub)
                OpenWebsite(ModNJIT.ClubHomepage & "/launcher-device")
                Dim Interval As Integer = If(Result("interval")?.ToObject(Of Integer)(), 3) * 1000
                Dim Deadline As Date = Date.Now.AddSeconds(If(Result("expiresIn")?.ToObject(Of Integer)(), 600))
                While Date.Now < Deadline
                    Thread.Sleep(Interval)
                    Dim Poll As JObject = Nothing
                    Try
                        Poll = ModSocial.PollDevice(DeviceValue)
                    Catch ex As Exception
                        '单次轮询网络抖动不终止流程，继续下一轮
                        Logger.Warn(ex, "设备码轮询失败，继续等待")
                        Continue While
                    End Try
                    If If(Poll("status")?.ToString(), "") = "approved" Then
                        ModSocial.ApplyDeviceToken(Poll)
                        RunInUi(
                        Sub()
                            PanDeviceCode.Visibility = Visibility.Collapsed
                            LabDeviceCode.Text = ""
                            LabAccount.Text = "已登录社团官网：" & ModSocial.SocialName
                            RefreshAll()
                        End Sub)
                        Return
                    End If
                End While
                RunInUi(Sub() LabAccount.Text = "设备码已过期，请重新点「登录社团官网」。")
            Catch ex As Exception
                Logger.Warn(ex, "设备码登录失败")
                RunInUi(Sub() LabAccount.Text = ex.Message)
            Finally
                DevicePolling = False
            End Try
        End Sub, "DeviceLogin")
    End Sub

    Public Sub CopyDeviceCode(sender As Object, e As MouseButtonEventArgs)
        If LabDeviceCode.Text <> "" Then Clipboard.SetText(LabDeviceCode.Text)
    End Sub

    Public Sub OpenDevicePage(sender As Object, e As MouseButtonEventArgs)
        OpenWebsite(ModNJIT.ClubHomepage & "/launcher-device")
    End Sub

    Private Sub PageExitHandler() Handles Me.PageExit
        If FriendTimer IsNot Nothing Then FriendTimer.Stop()
    End Sub

    Private Sub BuildTools()
        PanTools.Children.Clear()
        TxtSearch = New MyTextBox With {.Width = 200, .Height = 30, .HintText = "输入昵称前缀搜索"}
        Dim BtnSearch As New MyButton With {
            .Text = "搜索", .Height = 30, .MinWidth = 76, .Margin = New Thickness(8, 0, 0, 0),
            .ColorType = MyButton.ColorState.Highlight}
        AddHandler BtnSearch.Click, Sub() DoSearch()
        Dim BtnRefresh As New MyButton With {
            .Text = "刷新", .Height = 30, .MinWidth = 76, .Margin = New Thickness(8, 0, 0, 0)}
        AddHandler BtnRefresh.Click, Sub() RefreshAll()
        PanTools.Children.Add(TxtSearch)
        PanTools.Children.Add(BtnSearch)
        PanTools.Children.Add(BtnRefresh)
    End Sub

    Private Sub RefreshAll(Optional Silent As Boolean = False)
        If Not Silent Then LabStatus.Text = "正在连接服务器……"
        RunInNewThread(
        Sub()
            Try
                ModSocial.EnsureLogin()
                Dim Data As JObject = ModSocial.FriendList()
                RunInUi(
                Sub()
                    Render(Data)
                    LabStatus.Text = "已连接服务器"
                    LabAccount.Text = "已登录社团官网：" & ModSocial.SocialName
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "加载好友失败")
                RunInUi(
                Sub()
                    If Not Silent Then LabStatus.Text = ex.Message
                End Sub)
            End Try
        End Sub, "SocialFriends")
    End Sub

    Private Sub Render(Data As JObject)
        PanIncoming.Children.Clear()
        PanFriends.Children.Clear()
        PanBlocked.Children.Clear()

        Dim Incoming As JArray = TryCast(Data("incoming"), JArray)
        If Incoming IsNot Nothing AndAlso Incoming.Count > 0 Then
            PanIncoming.Children.Add(SocialLabel($"待处理申请（{Incoming.Count}）"))
            For Each Item As JToken In Incoming
                PanIncoming.Children.Add(BuildIncomingRow(TryCast(Item, JObject)))
            Next
        End If

        Dim Friends As JArray = TryCast(Data("friends"), JArray)
        If Friends Is Nothing OrElse Friends.Count = 0 Then
            PanFriends.Children.Add(SocialLabel("还没有好友，先在上方搜索昵称"))
        Else
            PanFriends.Children.Add(SocialLabel($"好友（{Friends.Count}）"))
            For Each Item As JToken In Friends
                PanFriends.Children.Add(BuildFriendRow(TryCast(Item, JObject)))
            Next
        End If

        Dim Blocked As JArray = TryCast(Data("blocked"), JArray)
        If Blocked IsNot Nothing AndAlso Blocked.Count > 0 Then
            PanBlocked.Children.Add(SocialLabel($"黑名单（{Blocked.Count}）"))
            For Each Item As JToken In Blocked
                PanBlocked.Children.Add(BuildBlockedRow(TryCast(Item, JObject)))
            Next
        End If
    End Sub

    ''' <summary>好友条目里的账号 ID：兼容后端的 accountId 与 account_id 两种写法。</summary>
    Private Function ItemAccountId(Item As JObject) As String
        If Item Is Nothing Then Return ""
        Return If(Item("accountId")?.ToString(), If(Item("account_id")?.ToString(), ""))
    End Function

    ''' <summary>好友条目里的显示名，缺失时给个占位，避免整行渲染失败。</summary>
    Private Function ItemName(Item As JObject) As String
        If Item Is Nothing Then Return ""
        Return If(Item("username")?.ToString(), If(Item("display_name")?.ToString(), "未命名"))
    End Function

    Private Function BuildFriendRow(Item As JObject) As FrameworkElement
        If Item Is Nothing Then Return New Grid()
        Dim Row As New Grid With {.Height = 44, .Margin = New Thickness(0, 0, 0, 2)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Name As String = ItemName(Item)
        Dim Person As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Person.Children.Add(SocialAvatar(Item("avatarUrl")?.ToString(), 32, Name))
        Person.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 13, .Margin = New Thickness(8, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis})
        Grid.SetColumn(Person, 0)
        Row.Children.Add(Person)

        Dim Target As String = ItemAccountId(Item)
        Dim Actions As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Dim BtnRemove As New MyButton With {
            .Text = "删除", .Height = 26, .MinWidth = 64}
        AddHandler BtnRemove.Click,
            Sub()
                If MyMsgBox($"确定删除好友「{Name}」吗？", "删除好友", "删除", "取消") <> 1 Then Return
                RunSocialAction(Sub() ModSocial.RemoveFriend(Target), "已删除好友")
            End Sub
        Dim BtnBlock As New MyButton With {
            .Text = "拉黑", .Height = 26, .MinWidth = 64, .Margin = New Thickness(8, 0, 0, 0)}
        AddHandler BtnBlock.Click,
            Sub()
                If MyMsgBox($"拉黑「{Name}」后，双方好友关系会解除，对方无法再向你发起好友申请。确定吗？",
                            "拉黑", "拉黑", "取消") <> 1 Then Return
                RunSocialAction(Sub() ModSocial.BlockFriend(Target), "已拉黑")
            End Sub
        Actions.Children.Add(BtnRemove)
        Actions.Children.Add(BtnBlock)
        Grid.SetColumn(Actions, 1)
        Row.Children.Add(Actions)
        Return Row
    End Function

    Private Function BuildIncomingRow(Item As JObject) As FrameworkElement
        If Item Is Nothing Then Return New Grid()
        Dim Row As New Grid With {.Height = 44, .Margin = New Thickness(0, 0, 0, 2)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Name As String = ItemName(Item)
        Dim Person As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Person.Children.Add(SocialAvatar(Item("avatarUrl")?.ToString(), 32, Name))
        Person.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 13, .Margin = New Thickness(8, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center})
        Grid.SetColumn(Person, 0)
        Row.Children.Add(Person)

        Dim Target As String = ItemAccountId(Item)
        Dim Actions As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Dim BtnAccept As New MyButton With {
            .Text = "同意", .Height = 26, .MinWidth = 64, .ColorType = MyButton.ColorState.Highlight}
        Dim BtnReject As New MyButton With {
            .Text = "拒绝", .Height = 26, .MinWidth = 64, .Margin = New Thickness(8, 0, 0, 0)}
        Dim BtnBlock As New MyButton With {
            .Text = "拉黑", .Height = 26, .MinWidth = 64, .Margin = New Thickness(8, 0, 0, 0)}
        AddHandler BtnAccept.Click, Sub() RunSocialAction(Sub() ModSocial.RespondFriend(Target, True), "已添加好友")
        AddHandler BtnReject.Click, Sub() RunSocialAction(Sub() ModSocial.RespondFriend(Target, False), "已拒绝申请")
        AddHandler BtnBlock.Click,
            Sub()
                If MyMsgBox($"拉黑「{Name}」后，对方无法再向你发起好友申请。确定吗？",
                            "拉黑", "拉黑", "取消") <> 1 Then Return
                RunSocialAction(Sub() ModSocial.BlockFriend(Target), "已拉黑")
            End Sub
        Actions.Children.Add(BtnAccept)
        Actions.Children.Add(BtnReject)
        Actions.Children.Add(BtnBlock)
        Grid.SetColumn(Actions, 1)
        Row.Children.Add(Actions)
        Return Row
    End Function

    Private Function BuildBlockedRow(Item As JObject) As FrameworkElement
        If Item Is Nothing Then Return New Grid()
        Dim Row As New Grid With {.Height = 44, .Margin = New Thickness(0, 0, 0, 2)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Name As String = ItemName(Item)
        Dim Person As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Person.Children.Add(SocialAvatar(Item("avatarUrl")?.ToString(), 32, Name))
        Person.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 13, .Opacity = 0.6, .Margin = New Thickness(8, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center})
        Grid.SetColumn(Person, 0)
        Row.Children.Add(Person)

        Dim Target As String = ItemAccountId(Item)
        Dim BtnUnblock As New MyButton With {
            .Text = "解除拉黑", .Height = 26, .MinWidth = 88, .VerticalAlignment = VerticalAlignment.Center}
        AddHandler BtnUnblock.Click,
            Sub() RunSocialAction(Sub() ModSocial.UnblockFriend(Target), "已解除拉黑")
        Grid.SetColumn(BtnUnblock, 1)
        Row.Children.Add(BtnUnblock)
        Return Row
    End Function

    Private Function BuildSearchRow(Item As JObject) As FrameworkElement
        If Item Is Nothing Then Return New Grid()
        Dim Row As New Grid With {.Height = 44, .Margin = New Thickness(0, 0, 0, 2)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Name As String = ItemName(Item)
        Dim Person As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Person.Children.Add(SocialAvatar(Item("avatarUrl")?.ToString(), 32, Name))
        Person.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 13, .Margin = New Thickness(8, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center})
        Grid.SetColumn(Person, 0)
        Row.Children.Add(Person)

        Dim Btn As New MyButton With {
            .Text = "加好友", .Height = 26, .MinWidth = 76, .VerticalAlignment = VerticalAlignment.Center,
            .ColorType = MyButton.ColorState.Highlight}
        AddHandler Btn.Click,
            Sub()
                RunInNewThread(
                Sub()
                    Try
                        Dim Status As String = ModSocial.AddFriend(Name)
                        RunInUi(
                        Sub()
                            Hint(If(Status = "accepted", "已互相添加为好友", "好友申请已发送"), HintType.Green)
                            PanSearch.Children.Clear()
                            RefreshAll()
                        End Sub)
                    Catch ex As Exception
                        RunInUi(Sub() Hint(ex.Message, HintType.Red))
                    End Try
                End Sub, "SocialAddFriend")
            End Sub
        Grid.SetColumn(Btn, 1)
        Row.Children.Add(Btn)
        Return Row
    End Function

    ''' <summary>
    ''' 统一的异步社交操作：执行、提示、刷新。
    ''' </summary>
    Private Sub RunSocialAction(Work As Action, SuccessText As String)
        RunInNewThread(
        Sub()
            Try
                Work()
                RunInUi(
                Sub()
                    Hint(SuccessText, HintType.Green)
                    RefreshAll()
                End Sub)
            Catch ex As Exception
                RunInUi(Sub() Hint(ex.Message, HintType.Red))
            End Try
        End Sub, "SocialFriendAction")
    End Sub

    Private Sub DoSearch()
        Dim Keyword As String = TxtSearch.Text.Trim()
        If Keyword.Length < 2 Then
            Hint("至少输入 2 个字符", HintType.Red)
            Return
        End If
        PanSearch.Children.Clear()
        PanSearch.Children.Add(SocialLabel("正在搜索……"))
        RunInNewThread(
        Sub()
            Try
                Dim Results As JArray = ModSocial.SearchUsers(Keyword)
                RunInUi(
                Sub()
                    PanSearch.Children.Clear()
                    If Results.Count = 0 Then
                        PanSearch.Children.Add(SocialLabel("没有找到匹配的昵称"))
                        Return
                    End If
                    PanSearch.Children.Add(SocialLabel($"搜索结果（{Results.Count}）"))
                    For Each Item As JToken In Results
                        PanSearch.Children.Add(BuildSearchRow(TryCast(Item, JObject)))
                    Next
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    PanSearch.Children.Clear()
                    PanSearch.Children.Add(SocialLabel(ex.Message))
                End Sub)
            End Try
        End Sub, "SocialSearch")
    End Sub

End Class
