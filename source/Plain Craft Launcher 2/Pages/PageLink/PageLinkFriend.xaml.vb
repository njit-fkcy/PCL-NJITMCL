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

    '防止 20 秒定时刷新与手动刷新并发堆积请求
    Private Refreshing As Boolean = False

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
        If Refreshing Then Return
        Refreshing = True
        If Not Silent Then LabStatus.Text = "正在连接服务器……"
        RunInNewThread(
        Sub()
            Try
                ModSocial.EnsureLogin()
                Dim Data As JObject = ModSocial.FriendList()
                RunInUi(
                Sub()
                    Render(Data)
                    LabStatus.Text = "已连接社团服务器"
                    '好友申请数随手同步到侧边栏，不额外发请求
                    Dim Incoming As JArray = TryCast(Data("incoming"), JArray)
                    If FrmMain IsNot Nothing AndAlso Incoming IsNot Nothing Then
                        FrmMain.ApplyFriendRequests(Incoming.Count)
                    End If
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "加载好友失败")
                RunInUi(
                Sub()
                    If Not Silent Then LabStatus.Text = ModSocial.FriendlyError(ex)
                End Sub)
            Finally
                Refreshing = False
            End Try
        End Sub, "SocialFriends")
    End Sub

    Private Sub Render(Data As JObject)
        PanIncoming.Children.Clear()
        PanFriends.Children.Clear()
        PanBlocked.Children.Clear()

        Dim Incoming As JArray = TryCast(Data("incoming"), JArray)
        If Incoming IsNot Nothing AndAlso Incoming.Count > 0 Then
            PanIncoming.Children.Add(SocialSectionTitle($"待处理申请（{Incoming.Count}）"))
            For Each Item As JToken In Incoming
                PanIncoming.Children.Add(BuildIncomingRow(TryCast(Item, JObject)))
            Next
        End If

        Dim Friends As JArray = TryCast(Data("friends"), JArray)
        If Friends Is Nothing OrElse Friends.Count = 0 Then
            PanFriends.Children.Add(SocialLabel("还没有好友，先在上方搜索昵称"))
        Else
            PanFriends.Children.Add(SocialSectionTitle($"好友（{Friends.Count}）"))
            For Each Item As JToken In Friends
                PanFriends.Children.Add(BuildFriendRow(TryCast(Item, JObject)))
            Next
        End If

        Dim Blocked As JArray = TryCast(Data("blocked"), JArray)
        If Blocked IsNot Nothing AndAlso Blocked.Count > 0 Then
            PanBlocked.Children.Add(SocialSectionTitle($"黑名单（{Blocked.Count}）"))
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
        Dim Row As New Grid With {.Height = 40}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Name As String = ItemName(Item)
        Dim Person As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Person.Children.Add(SocialAvatar(Item("avatarUrl")?.ToString(), 30, Name))
        Person.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 12.5, .Margin = New Thickness(9, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center, .TextTrimming = TextTrimming.CharacterEllipsis})
        Grid.SetColumn(Person, 0)
        Row.Children.Add(Person)

        Dim Target As String = ItemAccountId(Item)
        Dim Actions As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Dim BtnMessage As New MyButton With {
            .Text = "发消息", .Height = 24, .MinWidth = 64,
            .ColorType = MyButton.ColorState.Highlight}
        AddHandler BtnMessage.Click,
            Sub()
                PageLinkMessage.PendingPeerId = Target
                PageLinkMessage.PendingPeerName = Name
                FrmMain.PageChange(FormMain.PageType.Link, FormMain.PageSubType.LinkMessage)
            End Sub
        Dim BtnRemove As New MyButton With {
            .Text = "删除", .Height = 24, .MinWidth = 56, .Margin = New Thickness(6, 0, 0, 0)}
        AddHandler BtnRemove.Click,
            Sub()
                If MyMsgBox($"确定删除好友「{Name}」吗？", "删除好友", "删除", "取消") <> 1 Then Return
                RunSocialAction(Sub() ModSocial.RemoveFriend(Target), "已删除好友")
            End Sub
        Dim BtnBlock As New MyButton With {
            .Text = "拉黑", .Height = 24, .MinWidth = 56, .Margin = New Thickness(6, 0, 0, 0)}
        AddHandler BtnBlock.Click,
            Sub()
                If MyMsgBox($"拉黑「{Name}」后，双方好友关系会解除，对方无法再向你发起好友申请。确定吗？",
                            "拉黑", "拉黑", "取消") <> 1 Then Return
                RunSocialAction(Sub() ModSocial.BlockFriend(Target), "已拉黑")
            End Sub
        Actions.Children.Add(BtnMessage)
        Actions.Children.Add(BtnRemove)
        Actions.Children.Add(BtnBlock)
        Grid.SetColumn(Actions, 1)
        Row.Children.Add(Actions)
        Return Row
    End Function

    Private Function BuildIncomingRow(Item As JObject) As FrameworkElement
        If Item Is Nothing Then Return New Grid()
        Dim Row As New Grid With {.Height = 40}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Name As String = ItemName(Item)
        Dim Person As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Person.Children.Add(SocialAvatar(Item("avatarUrl")?.ToString(), 30, Name))
        Person.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 12.5, .Margin = New Thickness(9, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center})
        Grid.SetColumn(Person, 0)
        Row.Children.Add(Person)

        Dim Target As String = ItemAccountId(Item)
        Dim Actions As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Dim BtnAccept As New MyButton With {
            .Text = "同意", .Height = 24, .MinWidth = 56, .ColorType = MyButton.ColorState.Highlight}
        Dim BtnReject As New MyButton With {
            .Text = "拒绝", .Height = 24, .MinWidth = 56, .Margin = New Thickness(6, 0, 0, 0)}
        Dim BtnBlock As New MyButton With {
            .Text = "拉黑", .Height = 24, .MinWidth = 56, .Margin = New Thickness(6, 0, 0, 0)}
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
        Dim Row As New Grid With {.Height = 40}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Name As String = ItemName(Item)
        Dim Person As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Person.Children.Add(SocialAvatar(Item("avatarUrl")?.ToString(), 30, Name))
        Person.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 12.5, .Opacity = 0.6, .Margin = New Thickness(9, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center})
        Grid.SetColumn(Person, 0)
        Row.Children.Add(Person)

        Dim Target As String = ItemAccountId(Item)
        Dim BtnUnblock As New MyButton With {
            .Text = "解除拉黑", .Height = 24, .MinWidth = 80, .VerticalAlignment = VerticalAlignment.Center}
        AddHandler BtnUnblock.Click,
            Sub() RunSocialAction(Sub() ModSocial.UnblockFriend(Target), "已解除拉黑")
        Grid.SetColumn(BtnUnblock, 1)
        Row.Children.Add(BtnUnblock)
        Return Row
    End Function

    Private Function BuildSearchRow(Item As JObject) As FrameworkElement
        If Item Is Nothing Then Return New Grid()
        Dim Row As New Grid With {.Height = 40}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Name As String = ItemName(Item)
        Dim Person As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Person.Children.Add(SocialAvatar(Item("avatarUrl")?.ToString(), 30, Name))
        Person.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 12.5, .Margin = New Thickness(9, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center})
        Grid.SetColumn(Person, 0)
        Row.Children.Add(Person)

        Dim Btn As New MyButton With {
            .Text = "加好友", .Height = 24, .MinWidth = 72, .VerticalAlignment = VerticalAlignment.Center,
            .ColorType = MyButton.ColorState.Highlight}
        AddHandler Btn.Click,
            Sub()
                If SocialBusy Then Return
                SocialBusy = True
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
                        RunInUi(Sub() Hint(ModSocial.FriendlyError(ex), HintType.Red))
                    Finally
                        SocialBusy = False
                    End Try
                End Sub, "SocialAddFriend")
            End Sub
        Grid.SetColumn(Btn, 1)
        Row.Children.Add(Btn)
        Return Row
    End Function

    ''' <summary>
    ''' 统一的异步社交操作：执行、提示、刷新。同一时刻只允许一个社交操作，避免连点重复提交。
    ''' </summary>
    Private SocialBusy As Boolean = False

    Private Sub RunSocialAction(Work As Action, SuccessText As String)
        If SocialBusy Then Return
        SocialBusy = True
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
                RunInUi(Sub() Hint(ModSocial.FriendlyError(ex), HintType.Red))
            Finally
                SocialBusy = False
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
                    PanSearch.Children.Add(SocialSectionTitle($"搜索结果（{Results.Count}）"))
                    For Each Item As JToken In Results
                        PanSearch.Children.Add(BuildSearchRow(TryCast(Item, JObject)))
                    Next
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    PanSearch.Children.Clear()
                    PanSearch.Children.Add(SocialLabel(ModSocial.FriendlyError(ex)))
                End Sub)
            End Try
        End Sub, "SocialSearch")
    End Sub

End Class
