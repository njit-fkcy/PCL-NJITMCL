Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Threading

''' <summary>
''' 「联机 → 消息」：系统消息、好友会话列表与聊天卡片。
''' </summary>
Public Class PageLinkMessage
    Implements IRefreshable

    Private Built As Boolean = False
    Private Refreshing As Boolean = False
    Private LoadingChat As Boolean = False
    Private Sending As Boolean = False
    Private ListTick As Integer = 0
    Private PollTimer As DispatcherTimer

    '当前打开的聊天
    Private CurrentPeer As String = ""
    Private CurrentPeerName As String = ""
    Private LastMessageId As Long = 0

    '从好友页点「发消息」时先记下目标，进入本页后自动打开聊天
    Public Shared PendingPeerId As String = ""
    Public Shared PendingPeerName As String = ""

    Private Sub PageEnterHandler() Handles Me.PageEnter
        If Not Built Then
            Built = True
            BuildTools()
        End If
        If Not String.IsNullOrWhiteSpace(PendingPeerId) Then
            Dim Peer As String = PendingPeerId
            Dim PeerName As String = PendingPeerName
            PendingPeerId = ""
            PendingPeerName = ""
            OpenChat(Peer, PeerName)
        Else
            CloseChat()
            RefreshAll()
        End If
        If PollTimer Is Nothing Then
            PollTimer = New DispatcherTimer With {.Interval = TimeSpan.FromSeconds(5)}
            AddHandler PollTimer.Tick, Sub() Poll()
        End If
        PollTimer.Stop()
        PollTimer.Start()
    End Sub

    Private Sub PageExitHandler() Handles Me.PageExit
        If PollTimer IsNot Nothing Then PollTimer.Stop()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        If PanChat.Visibility = Visibility.Visible AndAlso CurrentPeer <> "" Then
            LoadChat(LastMessageId, False)
        Else
            RefreshAll()
        End If
    End Sub

    ''' <summary>停留在列表时每 20 秒刷新一次，聊天中每 5 秒拉取新消息。</summary>
    Private Sub Poll()
        If PanChat.Visibility = Visibility.Visible Then
            If CurrentPeer <> "" Then LoadChat(LastMessageId, False)
            Return
        End If
        ListTick += 1
        If ListTick >= 4 Then
            ListTick = 0
            RefreshAll(Silent:=True)
        End If
    End Sub

    Private Sub BuildTools()
        PanTools.Children.Clear()
        Dim BtnRefresh As New MyButton With {.Text = "刷新", .Height = 30, .MinWidth = 76}
        AddHandler BtnRefresh.Click, Sub() RefreshAll()
        PanTools.Children.Add(BtnRefresh)
    End Sub

#Region "列表"

    Private Sub RefreshAll(Optional Silent As Boolean = False)
        If Refreshing Then Return
        Refreshing = True
        If Not Silent Then LabStatus.Text = "正在连接服务器……"
        RunInNewThread(
        Sub()
            Try
                ModSocial.EnsureLogin()
                Dim Conversations As JArray = ModSocial.MessageConversations()
                Dim SystemData As JObject = ModSocial.ClubSystemMessages()
                RunInUi(
                Sub()
                    RenderSystem(SystemData)
                    RenderConversations(Conversations)
                    LabStatus.Text = "已连接社团服务器"
                    ApplyUnreadTotal(Conversations)
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "加载消息失败")
                RunInUi(
                Sub()
                    If Not Silent Then LabStatus.Text = ModSocial.FriendlyError(ex)
                End Sub)
            Finally
                Refreshing = False
            End Try
        End Sub, "SocialMessages")
    End Sub

    ''' <summary>
    ''' 用刚拉到的会话算未读总数；系统消息在渲染时已标记已读，不再计入。
    ''' </summary>
    Private Sub ApplyUnreadTotal(Conversations As JArray)
        Dim Total As Integer = 0
        If Conversations IsNot Nothing Then
            For Each Token As JToken In Conversations
                Dim Item As JObject = TryCast(Token, JObject)
                If Item IsNot Nothing AndAlso Item("unread") IsNot Nothing Then
                    Total += Item("unread").ToObject(Of Integer)()
                End If
            Next
        End If
        If FrmMain IsNot Nothing Then FrmMain.ApplyUnreadBadge(Total)
    End Sub

    Private Sub RenderConversations(Items As JArray)
        PanConversations.Children.Clear()
        If Items Is Nothing OrElse Items.Count = 0 Then
            PanConversations.Children.Add(SocialSectionTitle("好友消息"))
            PanConversations.Children.Add(SocialLabel("还没有和好友聊过天。到「好友」页面点击「发消息」就能开始。"))
            Return
        End If
        PanConversations.Children.Add(SocialSectionTitle($"好友消息（{Items.Count}）"))
        For Each Item As JToken In Items
            Dim Row As FrameworkElement = BuildConversationRow(TryCast(Item, JObject))
            If Row IsNot Nothing Then PanConversations.Children.Add(Row)
        Next
    End Sub

    Private Function BuildConversationRow(Item As JObject) As FrameworkElement
        If Item Is Nothing Then Return Nothing
        Dim PeerId As String = If(Item("accountId")?.ToString(), "")
        If String.IsNullOrWhiteSpace(PeerId) Then Return Nothing
        Dim Name As String = If(Item("name")?.ToString(), "好友")
        Dim Content As String = If(Item("content")?.ToString(), "")
        Dim Mine As Boolean = Item("mine") IsNot Nothing AndAlso Item("mine").ToObject(Of Boolean)()
        Dim Unread As Integer = 0
        If Item("unread") IsNot Nothing Then Unread = Item("unread").ToObject(Of Integer)()
        Dim Avatar As String = Item("avatarUrl")?.ToString()

        Dim Row As New Grid With {.Height = 46, .Background = Brushes.Transparent, .Cursor = Cursors.Hand}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim Person As New StackPanel With {
            .Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Person.Children.Add(SocialAvatar(Avatar, 32, Name))
        Dim Info As New StackPanel With {.Margin = New Thickness(9, 0, 0, 0), .VerticalAlignment = VerticalAlignment.Center}
        Info.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 12.5, .Foreground = SocialBrush(Color.FromRgb(&H33, &H33, &H33)),
            .TextTrimming = TextTrimming.CharacterEllipsis})
        Info.Children.Add(New TextBlock With {
            .Text = If(Mine, "我：" & Content, Content), .FontSize = 11, .Opacity = 0.55,
            .Margin = New Thickness(0, 2, 0, 0), .MaxWidth = 380,
            .TextTrimming = TextTrimming.CharacterEllipsis})
        Person.Children.Add(Info)
        Grid.SetColumn(Person, 0)
        Row.Children.Add(Person)

        If Unread > 0 Then
            Dim Badge As New Border With {
                .CornerRadius = New CornerRadius(9), .MinWidth = 18, .Height = 18,
                .Padding = New Thickness(5, 0, 5, 0),
                .Background = SocialBrush(SocialRed), .VerticalAlignment = VerticalAlignment.Center,
                .Child = New TextBlock With {
                    .Text = If(Unread > 99, "99+", Unread.ToString()), .FontSize = 10.5,
                    .Foreground = Brushes.White, .HorizontalAlignment = HorizontalAlignment.Center,
                    .VerticalAlignment = VerticalAlignment.Center}}
            Grid.SetColumn(Badge, 1)
            Row.Children.Add(Badge)
        End If

        AddHandler Row.MouseLeftButtonUp, Sub() OpenChat(PeerId, Name)
        Return Row
    End Function

    Private Sub RenderSystem(Data As JObject)
        PanSystem.Children.Clear()
        If Data Is Nothing Then Return
        Dim Items As JArray = TryCast(Data("items"), JArray)
        If Items Is Nothing OrElse Items.Count = 0 Then Return
        PanSystem.Children.Add(SocialSectionTitle("系统消息"))
        Dim MaxId As Long = 0
        For Each Token As JToken In Items
            Dim Item As JObject = TryCast(Token, JObject)
            If Item Is Nothing Then Continue For
            Dim Id As Long
            If Long.TryParse(Item("id")?.ToString(), Id) AndAlso Id > MaxId Then MaxId = Id
            PanSystem.Children.Add(BuildSystemCard(Item))
        Next
        Dim Unread As Integer = 0
        If Data("unread") IsNot Nothing Then Unread = Data("unread").ToObject(Of Integer)()
        If Unread > 0 AndAlso MaxId > 0 Then
            RunInNewThread(
            Sub()
                Try
                    ModSocial.MarkSystemMessagesRead(MaxId)
                Catch ex As Exception
                    Logger.Warn(ex, "标记系统消息已读失败")
                End Try
            End Sub, "SocialSystemRead")
        End If
    End Sub

    Private Function BuildSystemCard(Item As JObject) As FrameworkElement
        Dim Unread As Boolean = Item("unread") IsNot Nothing AndAlso Item("unread").ToObject(Of Boolean)()
        Dim Card As New Border With {
            .CornerRadius = New CornerRadius(8), .Padding = New Thickness(14, 10, 14, 10),
            .Margin = New Thickness(0, 6, 0, 0),
            .BorderThickness = New Thickness(1),
            .BorderBrush = SocialBrush(Color.FromArgb(20, 0, 0, 0)),
            .Background = SocialBrush(Color.FromArgb(10, 0, 0, 0))}
        Dim Stack As New StackPanel
        Dim Head As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Head.Children.Add(New TextBlock With {
            .Text = If(String.IsNullOrWhiteSpace(Item("title")?.ToString()), "系统通知", Item("title").ToString()),
            .FontSize = 12.5, .FontWeight = FontWeights.SemiBold, .TextWrapping = TextWrapping.Wrap, .MaxWidth = 400,
            .Foreground = SocialBrush(Color.FromRgb(&H33, &H33, &H33))})
        If Unread Then Head.Children.Add(SocialBadge("新", SocialBlue))
        Head.Children.Add(New TextBlock With {
            .Text = FormatChatTime(Item("at")?.ToString()), .FontSize = 10.5, .Opacity = 0.5,
            .Margin = New Thickness(8, 0, 0, 0), .VerticalAlignment = VerticalAlignment.Center})
        Stack.Children.Add(Head)
        Stack.Children.Add(New TextBlock With {
            .Text = If(Item("content")?.ToString(), ""), .FontSize = 12, .TextWrapping = TextWrapping.Wrap,
            .Opacity = 0.85, .Margin = New Thickness(0, 6, 0, 0)})
        Card.Child = Stack
        Return Card
    End Function

#End Region
#Region "聊天"

    Private Sub OpenChat(PeerId As String, PeerName As String)
        If String.IsNullOrWhiteSpace(PeerId) Then Return
        CurrentPeer = PeerId
        CurrentPeerName = If(String.IsNullOrWhiteSpace(PeerName), "好友", PeerName)
        LastMessageId = 0
        LabChatName.Text = CurrentPeerName
        PanChatAvatar.Content = SocialAvatar(ModSocial.AvatarUrlOf(PeerId), 26, CurrentPeerName)
        PanChat.Visibility = Visibility.Visible
        PanChatList.Children.Clear()
        PanChatList.Children.Add(SocialLabel("正在加载聊天记录……"))
        LoadChat(0, True)
    End Sub

    Private Sub CloseChat()
        PanChat.Visibility = Visibility.Collapsed
        CurrentPeer = ""
        CurrentPeerName = ""
        LastMessageId = 0
        PanChatList.Children.Clear()
    End Sub

    Private Sub BtnChatBack_Click() Handles BtnChatBack.Click
        If Sending Then Hint("消息正在发送，请稍候", HintType.Red)
        CloseChat()
        RefreshAll(Silent:=True)
    End Sub

    Private Sub LoadChat(AfterId As Long, Replace As Boolean)
        If LoadingChat Then Return
        If String.IsNullOrWhiteSpace(CurrentPeer) Then Return
        LoadingChat = True
        Dim Peer As String = CurrentPeer
        RunInNewThread(
        Sub()
            Try
                Dim Items As JArray = ModSocial.MessageList(Peer, AfterId)
                RunInUi(
                Sub()
                    If CurrentPeer <> Peer Then Return
                    If Replace Then PanChatList.Children.Clear()
                    If Replace AndAlso (Items Is Nothing OrElse Items.Count = 0) Then
                        PanChatList.Children.Add(SocialLabel("还没有聊天记录，打个招呼吧"))
                    Else
                        For Each Token As JToken In Items
                            Dim Message As JObject = TryCast(Token, JObject)
                            PanChatList.Children.Add(BuildBubble(Message))
                            Dim Id As Long
                            If Message IsNot Nothing AndAlso Long.TryParse(Message("id")?.ToString(), Id) AndAlso Id > LastMessageId Then
                                LastMessageId = Id
                            End If
                        Next
                        ScrollChatToEnd()
                    End If
                    '打开聊天时后端已把对方消息标为已读，同步导航与侧边栏数字
                    If Replace AndAlso FrmMain IsNot Nothing Then FrmMain.RefreshMessageBadge()
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "加载聊天记录失败")
                RunInUi(
                Sub()
                    If CurrentPeer <> Peer OrElse Not Replace Then Return
                    PanChatList.Children.Clear()
                    PanChatList.Children.Add(SocialLabel(ModSocial.FriendlyError(ex)))
                End Sub)
            Finally
                LoadingChat = False
            End Try
        End Sub, "SocialChat")
    End Sub

    Private Sub ScrollChatToEnd()
        Dispatcher.InvokeAsync(
            Sub() PanChatScroll.ScrollToEnd(), DispatcherPriority.Background)
    End Sub

    Private Function BuildBubble(Item As JObject) As FrameworkElement
        If Item Is Nothing Then Return New Grid()
        Dim Mine As Boolean = Item("mine") IsNot Nothing AndAlso Item("mine").ToObject(Of Boolean)()
        Dim Content As String = If(Item("content")?.ToString(), "")
        Dim Bubble As New Border With {
            .CornerRadius = New CornerRadius(8), .Padding = New Thickness(11, 7, 11, 7),
            .MaxWidth = 340, .Margin = New Thickness(0, 3, 0, 3),
            .HorizontalAlignment = If(Mine, HorizontalAlignment.Right, HorizontalAlignment.Left),
            .Background = If(Mine, SocialBrush(Color.FromArgb(38, 19, 112, 243)),
                             SocialBrush(Color.FromArgb(14, 0, 0, 0)))}
        Dim Stack As New StackPanel
        Stack.Children.Add(New TextBlock With {
            .Text = Content, .FontSize = 12.5, .TextWrapping = TextWrapping.Wrap,
            .Foreground = SocialBrush(Color.FromRgb(&H22, &H22, &H22))})
        Stack.Children.Add(New TextBlock With {
            .Text = FormatChatTime(Item("at")?.ToString()), .FontSize = 9.5, .Opacity = 0.45,
            .Margin = New Thickness(0, 3, 0, 0),
            .HorizontalAlignment = If(Mine, HorizontalAlignment.Right, HorizontalAlignment.Left)})
        Bubble.Child = Stack
        Return Bubble
    End Function

    Private Sub BtnSend_Click() Handles BtnSend.Click
        SendCurrentMessage()
    End Sub

    Private Sub TxtMessage_PreviewKeyDown(sender As Object, e As KeyEventArgs) Handles TxtMessage.PreviewKeyDown
        If e.Key <> Key.Enter Then Return
        e.Handled = True
        SendCurrentMessage()
    End Sub

    Private Sub SendCurrentMessage()
        If Sending OrElse String.IsNullOrWhiteSpace(CurrentPeer) Then Return
        Dim Content As String = TxtMessage.Text.Trim()
        If Content = "" Then Return
        Sending = True
        Dim Peer As String = CurrentPeer
        RunInNewThread(
        Sub()
            Try
                ModSocial.SendMessage(Peer, Content)
                RunInUi(
                Sub()
                    If CurrentPeer <> Peer Then Return
                    TxtMessage.Text = ""
                End Sub)
                LoadChat(LastMessageId, False)
            Catch ex As Exception
                RunInUi(Sub() Hint(ModSocial.FriendlyError(ex), HintType.Red))
            Finally
                Sending = False
            End Try
        End Sub, "SocialSend")
    End Sub

#End Region

    ''' <summary>Unix 秒转本地时间：今天只显示时分，更早的带日期。</summary>
    Private Function FormatChatTime(Value As String) As String
        Dim Stamp As Long
        If Not Long.TryParse(Value, Stamp) OrElse Stamp <= 0 Then Return ""
        Dim Local As DateTime = DateTimeOffset.FromUnixTimeSeconds(Stamp).LocalDateTime
        Return If(Local.Date = DateTime.Today, Local.ToString("HH:mm"), Local.ToString("MM-dd HH:mm"))
    End Function

End Class
