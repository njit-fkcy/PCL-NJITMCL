Imports System.Text.RegularExpressions
Imports System.Windows.Controls
Imports System.Windows.Media

''' <summary>
''' 启动页「社团生活」卡片内的完整面板：活动报名、社团投票与个人中心。
''' 数据来自社团主账号的设备令牌（与好友、房间同一套鉴权）。
''' </summary>
Public Class ClubLifePanel

    Public Sub New()
        InitializeComponent()
    End Sub

    Private CurrentTab As Integer = 2
    Private Data As JObject = Nothing
    Private Loading As Boolean = False
    Private Submitting As Boolean = False
    '上次成功拉取时间：切回面板时超过 60 秒就重新拉取，避免数据滞后
    Private LastLoadAt As Date = Date.MinValue

    '活动详情
    Private CurrentEvent As JObject = Nothing
    Private DetailInputs As New Dictionary(Of String, MyTextBox)()

    '投票详情
    Private CurrentPoll As JObject = Nothing
    Private CurrentPollViewer As JObject = Nothing
    Private PollChoice As String = ""

    '个人中心
    '（信息模板编辑已移至官网个人中心，启动器侧只读展示并引导去官网，相关编辑器与 PIN 解锁代码已删除）

#Region "加载"

    ''' <summary>面板第一次显示时自动加载一次。</summary>
    Private Sub PanelLoaded() Handles Me.Loaded
        If Data IsNot Nothing OrElse Loading Then Return
        RefreshAll()
    End Sub

    ''' <summary>外部刷新入口（启动页全局刷新时调用）。</summary>
    Public Sub RefreshData()
        RefreshAll()
    End Sub

    ''' <summary>
    ''' 供启动页卡片直接切到指定分栏（0 活动 / 1 投票 / 2 个人）。
    ''' </summary>
    Public Sub OpenTab(Tab As Integer)
        If Tab < 0 OrElse Tab > 2 Then Tab = 0
        CurrentTab = Tab
        ClearDetail()
        If Data Is Nothing OrElse (Date.Now - LastLoadAt).TotalSeconds > 60 Then
            RefreshAll()
        Else
            RenderTab()
        End If

    End Sub

    Private Sub BtnReload_Click(sender As Object, e As EventArgs) Handles BtnReload.Click
        RefreshAll()
    End Sub

    Private Sub RefreshAll()
        If Loading Then Return
        If Not ModSocial.IsLoggedIn() Then
            LabState.Text = "请先官网授权启动器，才可以使用社团功能"
            ShowMessage("请先点击上方「授权启动器」，完成官网授权后即可使用活动报名、投票与个人中心。")
            Return
        End If
        Loading = True
        LabState.Text = "正在读取社团生活……"
        ShowMessage("正在读取社团生活……")
        RunInNewThread(
        Sub()
            Try
                ModSocial.EnsureLogin()
                Dim Result As JObject = ModSocial.ClubBootstrap()
                RunInUi(
                Sub()
                    Loading = False
                    Data = Result
                    LastLoadAt = Date.Now
                    ClearDetail()
                    LabState.Text = "已连接社团账号：" & If(Result("displayName")?.ToString(), "")
                    RenderTab()
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "加载社团生活失败")
                Dim Message As String = ex.Message
                '连不上官网时给出可操作的提示，避免只显示底层网络错误
                If Message.Contains("连接") OrElse Message.Contains("超时") OrElse
                   Message.Contains("timed out") OrElse Message.Contains("unreachable") Then
                    Message = "连接不到社团官网。请至少登录一次社团官网（" & ModNJIT.ClubHomepage &
                              "），确认官网可访问后再试。"
                End If
                RunInUi(
                Sub()
                    Loading = False
                    '加载失败时清掉旧数据，避免授权被撤销后仍显示上一轮的个人内容
                    Data = Nothing
                    ClearDetail()
                    LabState.Text = Message
                    ShowMessage(Message & vbCrLf & "社团生活使用联机账号（社团主账号）鉴权，请先在「联机 → 我的」完成登录。")
                End Sub)
            End Try
        End Sub, "ClubLife")
    End Sub

    ''' <summary>
    ''' 操作完成后重新拉取汇总；EventId / PollId 不为空时顺带回到对应详情。
    ''' </summary>
    Private Sub ReloadData(Optional EventId As String = "", Optional PollId As String = "")
        RunInNewThread(
        Sub()
            Try
                Dim Result As JObject = ModSocial.ClubBootstrap()
                Dim EventData As JObject = Nothing
                Dim PollData As JObject = Nothing
                If EventId <> "" Then
                    Try
                        EventData = TryCast(ModSocial.ClubEventDetail(EventId)("event"), JObject)
                    Catch ex As Exception
                        Logger.Warn(ex, "刷新活动详情失败")
                    End Try
                End If
                If PollId <> "" Then
                    Try
                        PollData = ModSocial.ClubPollDetail(PollId)
                    Catch ex As Exception
                        Logger.Warn(ex, "刷新投票详情失败")
                    End Try
                End If
                RunInUi(
                Sub()
                    Data = Result
                    If EventId <> "" Then
                        CurrentEvent = EventData
                        PollChoice = ""
                    End If
                    If PollId <> "" Then
                        CurrentPoll = TryCast(PollData?("poll"), JObject)
                        CurrentPollViewer = TryCast(PollData?("viewer"), JObject)
                        PollChoice = ""
                    End If
                    RenderTab()
                End Sub)
            Catch ex As Exception
                RunInUi(Sub() Hint(ex.Message, HintType.Red))
            End Try
        End Sub, "ClubReload")
    End Sub

    Private Sub ClearDetail()
        CurrentEvent = Nothing
        CurrentPoll = Nothing
        CurrentPollViewer = Nothing
        DetailInputs.Clear()
        PollChoice = ""
    End Sub

    Private Sub ChangeTab(Tab As Integer)
        If CurrentTab = Tab Then Return
        CurrentTab = Tab
        ClearDetail()
        RenderTab()

    End Sub

    Private Sub BtnTabEvents_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnTabEvents.Click
        ChangeTab(0)
    End Sub

    Private Sub BtnTabPolls_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnTabPolls.Click
        ChangeTab(1)
    End Sub

    Private Sub BtnTabMe_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnTabMe.Click
        ChangeTab(2)
    End Sub

#End Region
#Region "渲染"

    Private Sub RenderTab()
        PanBody.Children.Clear()
        BtnTabEvents.ColorType = If(CurrentTab = 0, MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        BtnTabPolls.ColorType = If(CurrentTab = 1, MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        BtnTabMe.ColorType = If(CurrentTab = 2, MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
        If Data Is Nothing Then Return
        Select Case CurrentTab
            Case 0
                If CurrentEvent IsNot Nothing Then RenderEventDetail() Else RenderEventList()
            Case 1
                If CurrentPoll IsNot Nothing Then RenderPollDetail() Else RenderPollList()
            Case 2
                RenderMe()
        End Select
    End Sub

    Private Sub ShowMessage(Text As String)
        PanBody.Children.Clear()
        PanBody.Children.Add(SocialLabel(Text))
    End Sub

    Private Function BuildBackButton(Action As Action) As FrameworkElement
        Dim Btn As New MyTextButton With {
            .Content = "← 返回列表", .FontSize = 12, .Padding = New Thickness(0),
            .HorizontalAlignment = HorizontalAlignment.Left, .Margin = New Thickness(0, 0, 0, 8)
        }
        AddHandler Btn.Click, Sub() Action()
        Return Btn
    End Function

    Private Function BuildTitleRow(Title As String, Status As String, Accent As Brush) As FrameworkElement
        Dim Head As New Grid
        Head.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Head.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Text As New TextBlock With {
            .Text = Title, .FontSize = 14, .FontWeight = FontWeights.SemiBold,
            .TextTrimming = TextTrimming.CharacterEllipsis, .VerticalAlignment = VerticalAlignment.Center
        }
        Head.Children.Add(Text)
        If Status <> "" Then
            Dim Badge As New Border With {
                .CornerRadius = New CornerRadius(3), .Padding = New Thickness(7, 2, 7, 2),
                .Margin = New Thickness(8, 0, 0, 0), .VerticalAlignment = VerticalAlignment.Center,
                .Background = New SolidColorBrush(Color.FromArgb(16, 0, 0, 0))
            }
            Badge.Child = New TextBlock With {.Text = Status, .FontSize = 10.5, .Foreground = Accent}
            Grid.SetColumn(Badge, 1)
            Head.Children.Add(Badge)
        End If
        Return Head
    End Function

    Private Function BuildMeta(Text As String) As TextBlock
        Return New TextBlock With {
            .Text = Text, .FontSize = 11.5, .Opacity = 0.6, .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 6, 0, 0)
        }
    End Function

    Private Function BuildInfoTable(Rows As List(Of KeyValuePair(Of String, String))) As FrameworkElement
        Dim Table As New Grid
        Table.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(118)})
        Table.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Dim RowIndex As Integer = 0
        For Each Row In Rows
            If Table.RowDefinitions.Count <= RowIndex Then
                Table.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
            End If
            Dim Key As New TextBlock With {
                .Text = Row.Key, .FontSize = 11.5, .Opacity = 0.5, .Margin = New Thickness(0, 0, 0, 7)}
            Grid.SetRow(Key, RowIndex)
            Grid.SetColumn(Key, 0)
            Table.Children.Add(Key)
            Dim Value As New TextBlock With {
                .Text = If(Row.Value = "", "未填写", Row.Value), .FontSize = 11.5,
                .Margin = New Thickness(0, 0, 0, 7), .TextWrapping = TextWrapping.Wrap}
            Grid.SetRow(Value, RowIndex)
            Grid.SetColumn(Value, 1)
            Table.Children.Add(Value)
            RowIndex += 1
        Next
        Return Table
    End Function

#End Region
#Region "活动"

    Private Sub RenderEventList()
        Dim Events As JArray = TryCast(Data("events"), JArray)
        If Events Is Nothing OrElse Events.Count = 0 Then
            PanBody.Children.Add(SocialLabel("暂时没有社团活动，新的活动正在准备中。"))
            Return
        End If
        Dim Online As New List(Of JObject)
        Dim Others As New List(Of JObject)
        For Each Token As JToken In Events
            Dim Item As JObject = TryCast(Token, JObject)
            If Item Is Nothing Then Continue For
            Select Case If(Item("status")?.ToString(), "")
                Case "open", "upcoming"
                    Online.Add(Item)
                Case "closing"
                    Others.Add(Item)
            End Select
        Next
        For Each Item As JObject In Online
            PanBody.Children.Add(BuildEventCard(Item))
        Next
        If Others.Count > 0 Then
            PanBody.Children.Add(SocialLabel("已截止 · 等待结算"))
            For Each Item As JObject In Others
                PanBody.Children.Add(BuildEventCard(Item))
            Next
        End If
        If Online.Count = 0 AndAlso Others.Count = 0 Then
            PanBody.Children.Add(SocialLabel("暂时没有可报名的活动。"))
        End If
    End Sub

    Private Function BuildEventCard(Item As JObject) As FrameworkElement
        Dim Status As String = If(Item("status")?.ToString(), "")
        Dim Box As Border = SocialCard()
        Box.Margin = New Thickness(0, 8, 0, 0)
        Box.Cursor = Cursors.Hand
        Box.ToolTip = "点击查看活动详情"
        Dim Panel As New StackPanel
        Panel.Children.Add(BuildTitleRow(If(Item("title")?.ToString(), "社团活动"),
                                         EventStatusText(Status), EventStatusBrush(Status)))
        Dim Desc As String = If(Item("description")?.ToString(), "")
        If Desc.Length > 90 Then Desc = Desc.Substring(0, 90) & "…"
        If Desc <> "" Then
            Panel.Children.Add(New TextBlock With {
                .Text = Desc, .FontSize = 11.5, .Opacity = 0.62,
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 5, 0, 0)
            })
        End If
        Dim SignupCount As Integer = If(Item("signupCount")?.ToObject(Of Integer)(), 0)
        Dim Capacity As Integer = If(Item("capacity")?.ToObject(Of Integer)(), 0)
        Panel.Children.Add(BuildMeta("报名截止 " & FormatStamp(Item("ends_at")) &
                                     " · 已报名 " & SignupCount &
                                     If(Capacity > 0, " / " & Capacity, "") & " 人"))
        If TryCast(Item("mine"), JObject) IsNot Nothing Then
            Panel.Children.Add(New TextBlock With {
                .Text = "你已报名" & If(Item("mine")?("checked_in_at")?.Type <> JTokenType.Null, " · 已签到", " · 待签到"),
                .FontSize = 11, .Foreground = New SolidColorBrush(Color.FromRgb(&H1F, &H9F, &H5A)),
                .Margin = New Thickness(0, 5, 0, 0)
            })
        End If
        Box.Child = Panel
        Dim EventId As String = If(Item("id")?.ToString(), "")
        AddHandler Box.MouseLeftButtonUp,
            Sub(sender As Object, e As MouseButtonEventArgs)
                OpenEvent(EventId)
                e.Handled = True
            End Sub
        Return Box
    End Function

    Private Sub OpenEvent(EventId As String)
        If EventId = "" Then Return
        ShowMessage("正在读取活动详情……")
        RunInNewThread(
        Sub()
            Try
                Dim Result As JObject = ModSocial.ClubEventDetail(EventId)
                RunInUi(
                Sub()
                    CurrentEvent = TryCast(Result("event"), JObject)
                    DetailInputs.Clear()
                    RenderTab()
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    Hint(ex.Message, HintType.Red)
                    RenderEventList()
                End Sub)
            End Try
        End Sub, "ClubEvent")
    End Sub

    Private Sub RenderEventDetail()
        PanBody.Children.Clear()
        Dim EventData As JObject = CurrentEvent
        Dim Status As String = If(EventData("status")?.ToString(), "")
        Dim Profile As JObject = TryCast(EventData("profile"), JObject)
        Dim Mine As JObject = TryCast(EventData("mine"), JObject)

        PanBody.Children.Add(BuildBackButton(
            Sub()
                CurrentEvent = Nothing
                DetailInputs.Clear()
                RenderTab()
            End Sub))
        PanBody.Children.Add(BuildTitleRow(If(EventData("title")?.ToString(), "社团活动"),
                                           EventStatusText(Status), EventStatusBrush(Status)))
        Dim Desc As String = If(EventData("description")?.ToString(), "")
        If Desc <> "" Then
            PanBody.Children.Add(New TextBlock With {
                .Text = Desc, .FontSize = 12, .TextWrapping = TextWrapping.Wrap,
                .Opacity = 0.75, .Margin = New Thickness(0, 8, 0, 0)
            })
        End If
        Dim SignupCount As Integer = If(EventData("signupCount")?.ToObject(Of Integer)(), 0)
        Dim Capacity As Integer = If(EventData("capacity")?.ToObject(Of Integer)(), 0)
        PanBody.Children.Add(BuildMeta("报名时间 " & FormatStamp(EventData("starts_at")) &
                                       " ～ " & FormatStamp(EventData("ends_at"))))
        PanBody.Children.Add(BuildMeta("已报名 " & SignupCount &
                                       If(Capacity > 0, " / " & Capacity, "") & " 人" &
                                       If(Equals(EventData("checkinEnabled")?.ToObject(Of Boolean)(), True),
                                          " · 现场签到已开启", "")))

        '信息模板（内容只在官网个人中心展示与维护）
        PanBody.Children.Add(SocialDivider())
        PanBody.Children.Add(SocialSectionTitle("信息模板"))
        If Profile Is Nothing Then
            PanBody.Children.Add(SocialLabel("还没有信息模板，报名前需要先到个人中心补全。"))
        Else
            PanBody.Children.Add(SocialLabel("信息模板只在官网个人中心展示，前往官网查看与修改。"))
        End If

        '报名表
        PanBody.Children.Add(SocialDivider())
        PanBody.Children.Add(SocialSectionTitle("报名表"))
        BuildSignupTable(EventData, Profile, Mine)

        '操作
        PanBody.Children.Add(SocialDivider())
        If Mine IsNot Nothing Then
            PanBody.Children.Add(SocialLabel("你已报名" & If(Mine("checked_in_at")?.Type <> JTokenType.Null,
                                                       "，签到已完成。", "，等待现场签到。")))
            If Status = "open" AndAlso Mine("checked_in_at")?.Type = JTokenType.Null Then
                Dim BtnCancel As New MyButton With {
                    .Text = "取消报名", .Height = 30, .MinWidth = 120, .Margin = New Thickness(0, 8, 0, 0)}
                AddHandler BtnCancel.Click,
                    Sub()
                        If MyMsgBox("取消后名额会释放给其他同学，确定取消报名吗？", "取消报名", "取消报名", "再想想") <> 1 Then Return
                        SubmitCancelSignup()
                    End Sub
                PanBody.Children.Add(BtnCancel)
            End If
            Return
        End If
        If Equals(EventData("blacklisted")?.ToObject(Of Boolean)(), True) Then
            PanBody.Children.Add(SocialLabel("你已被限制参加活动：累计报名未签到次数过多，暂不能报名。"))
            Return
        End If
        If Profile Is Nothing OrElse Not Equals(Profile("complete")?.ToObject(Of Boolean)(), True) Then
            PanBody.Children.Add(SocialLabel("报名前需要先在官网补全信息模板。"))
            Dim BtnProfile As New MyButton With {
                .Text = "去官网补全信息模板", .Height = 30, .MinWidth = 132, .Margin = New Thickness(0, 8, 0, 0),
                .ColorType = MyButton.ColorState.Highlight}
            AddHandler BtnProfile.Click,
                Sub() OpenWebsite(ModNJIT.ClubHomepage & "/#club?tab=me")
            PanBody.Children.Add(BtnProfile)
            Return
        End If
        If Capacity > 0 AndAlso SignupCount >= Capacity Then
            PanBody.Children.Add(SocialLabel("报名人数已满（" & SignupCount & " / " & Capacity & "）。"))
            Return
        End If
        If Status <> "open" Then
            PanBody.Children.Add(SocialLabel(If(Status = "upcoming", "报名尚未开始。", "报名已经截止。")))
            Return
        End If
        Dim BtnSignup As New MyButton With {
            .Text = "确认报名", .Height = 32, .MinWidth = 132, .Margin = New Thickness(0, 8, 0, 0),
            .ColorType = MyButton.ColorState.Highlight}
        AddHandler BtnSignup.Click, Sub() SubmitSignup()
        PanBody.Children.Add(BtnSignup)
        PanBody.Children.Add(SocialLabel("信息模板字段会自动带入，自定义列需要你填写；报名后请按时到场签到。"))
    End Sub

    Private Sub BuildSignupTable(EventData As JObject, Profile As JObject, Mine As JObject)
        Dim Headers As JArray = TryCast(EventData("headers"), JArray)
        If Headers Is Nothing OrElse Headers.Count = 0 Then
            PanBody.Children.Add(SocialLabel("该活动没有设置报名表。"))
            Return
        End If
        DetailInputs.Clear()
        Dim Table As New Grid
        Table.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(118)})
        Table.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Dim RowIndex As Integer = 0
        For Each Token As JToken In Headers
            Dim Header As JObject = TryCast(Token, JObject)
            If Header Is Nothing Then Continue For
            If Table.RowDefinitions.Count <= RowIndex Then
                Table.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
            End If
            Dim Label As String = If(Header("label")?.ToString(), "")
            If Equals(Header("required")?.ToObject(Of Boolean)(), True) Then Label &= " *"
            Dim Key As New TextBlock With {
                .Text = Label, .FontSize = 11.5, .Opacity = 0.55, .VerticalAlignment = VerticalAlignment.Center,
                .Margin = New Thickness(0, 0, 0, 7), .TextWrapping = TextWrapping.Wrap}
            Grid.SetRow(Key, RowIndex)
            Grid.SetColumn(Key, 0)
            Table.Children.Add(Key)

            Dim TemplateKey As String = If(Header("template_key")?.ToString(), "")
            Dim HeaderId As String = If(Header("id")?.ToString(), "")
            Dim Value As FrameworkElement
            If TemplateKey <> "" Then
                Value = New TextBlock With {
                    .Text = If(If(Profile Is Nothing, "", Profile(TemplateKey)?.ToString()) = "", "未填写",
                               If(Profile Is Nothing, "", Profile(TemplateKey)?.ToString())),
                    .FontSize = 11.5, .Margin = New Thickness(0, 0, 0, 7), .VerticalAlignment = VerticalAlignment.Center,
                    .TextWrapping = TextWrapping.Wrap}
            ElseIf Mine IsNot Nothing Then
                Dim Custom As JObject = TryCast(Mine("custom"), JObject)
                Dim CustomText As String = ""
                If Custom IsNot Nothing Then CustomText = If(Custom(HeaderId)?.ToString(), "")
                Value = New TextBlock With {
                    .Text = CustomText, .FontSize = 11.5,
                    .Margin = New Thickness(0, 0, 0, 7), .VerticalAlignment = VerticalAlignment.Center,
                    .TextWrapping = TextWrapping.Wrap}
            Else
                Dim Input As New MyTextBox With {
                    .Height = 28, .Margin = New Thickness(0, 0, 0, 7), .HintText = "请填写" & Label.Replace(" *", "")}
                DetailInputs(HeaderId) = Input
                Value = Input
            End If
            Grid.SetRow(Value, RowIndex)
            Grid.SetColumn(Value, 1)
            Table.Children.Add(Value)
            RowIndex += 1
        Next
        PanBody.Children.Add(Table)
    End Sub

    Private Sub SubmitSignup()
        If Submitting Then Return
        Submitting = True
        Dim Custom As New JObject()
        For Each Pair In DetailInputs
            Custom(Pair.Key) = If(Pair.Value.Text, "").Trim()
        Next
        Dim EventId As String = If(CurrentEvent?("id")?.ToString(), "")
        RunInNewThread(
        Sub()
            Try
                ModSocial.ClubSignup(EventId, Custom)
                RunInUi(
                Sub()
                    Submitting = False
                    Hint("报名成功", HintType.Green)
                    ReloadData(EventId)
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    Submitting = False
                    Hint(ex.Message, HintType.Red)
                End Sub)
            End Try
        End Sub, "ClubSignup")
    End Sub

    Private Sub SubmitCancelSignup()
        If Submitting Then Return
        Submitting = True
        Dim EventId As String = If(CurrentEvent?("id")?.ToString(), "")
        RunInNewThread(
        Sub()
            Try
                ModSocial.ClubCancelSignup(EventId)
                RunInUi(
                Sub()
                    Submitting = False
                    Hint("已取消报名", HintType.Green)
                    ReloadData(EventId)
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    Submitting = False
                    Hint(ex.Message, HintType.Red)
                End Sub)
            End Try
        End Sub, "ClubCancelSignup")
    End Sub

    Private Function EventStatusText(Status As String) As String
        Select Case Status
            Case "open" : Return "报名中"
            Case "upcoming" : Return "即将开始"
            Case "closing" : Return "已截止"
            Case "closed" : Return "已结束"
            Case "draft" : Return "草稿"
            Case Else : Return "状态待确认"
        End Select
    End Function

    Private Function EventStatusBrush(Status As String) As Brush
        Select Case Status
            Case "open" : Return New SolidColorBrush(Color.FromRgb(&H1F, &H9F, &H5A))
            Case "upcoming" : Return New SolidColorBrush(Color.FromRgb(&H13, &H70, &HF3))
            Case "closing" : Return New SolidColorBrush(Color.FromRgb(&H8C, &H8C, &H8C))
            Case Else : Return New SolidColorBrush(Color.FromRgb(&H8C, &H8C, &H8C))
        End Select
    End Function

#End Region
#Region "投票"

    Private Sub RenderPollList()
        Dim Polls As JArray = TryCast(Data("polls"), JArray)
        If Polls Is Nothing OrElse Polls.Count = 0 Then
            PanBody.Children.Add(SocialLabel("暂时没有社团投票。"))
            Return
        End If
        For Each Token As JToken In Polls
            Dim Item As JObject = TryCast(Token, JObject)
            If Item Is Nothing Then Continue For
            Dim Poll As JObject = TryCast(Item("poll"), JObject)
            Dim Viewer As JObject = TryCast(Item("viewer"), JObject)
            If Poll Is Nothing Then Continue For
            PanBody.Children.Add(BuildPollCard(Poll, Viewer))
        Next
    End Sub

    Private Function BuildPollCard(Poll As JObject, Viewer As JObject) As FrameworkElement
        Dim Status As String = If(Poll("status")?.ToString(), "")
        Dim Box As Border = SocialCard()
        Box.Margin = New Thickness(0, 8, 0, 0)
        Box.Cursor = Cursors.Hand
        Box.ToolTip = "点击查看投票详情"
        Dim Panel As New StackPanel
        Panel.Children.Add(BuildTitleRow(If(Poll("title")?.ToString(), "社团投票"),
                                         PollStatusText(Status), PollStatusBrush(Status)))
        Dim Desc As String = If(Poll("description")?.ToString(), "")
        If Desc.Length > 90 Then Desc = Desc.Substring(0, 90) & "…"
        If Desc <> "" Then
            Panel.Children.Add(New TextBlock With {
                .Text = Desc, .FontSize = 11.5, .Opacity = 0.62,
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 5, 0, 0)
            })
        End If
        Dim Meta As String = "截止 " & FormatPollTime(Poll("endsAt"))
        If Equals(Viewer?("hasVoted")?.ToObject(Of Boolean)(), True) Then Meta &= " · 你已投票"
        Panel.Children.Add(BuildMeta(Meta))
        Box.Child = Panel
        Dim PollId As String = If(Poll("id")?.ToString(), "")
        AddHandler Box.MouseLeftButtonUp,
            Sub(sender As Object, e As MouseButtonEventArgs)
                OpenPoll(PollId)
                e.Handled = True
            End Sub
        Return Box
    End Function

    Private Sub OpenPoll(PollId As String)
        If PollId = "" Then Return
        ShowMessage("正在读取投票详情……")
        RunInNewThread(
        Sub()
            Try
                Dim Result As JObject = ModSocial.ClubPollDetail(PollId)
                RunInUi(
                Sub()
                    CurrentPoll = TryCast(Result("poll"), JObject)
                    CurrentPollViewer = TryCast(Result("viewer"), JObject)
                    PollChoice = ""
                    RenderTab()
                End Sub)
            Catch ex As Exception
                RunInUi(
                Sub()
                    Hint(ex.Message, HintType.Red)
                    RenderPollList()
                End Sub)
            End Try
        End Sub, "ClubPoll")
    End Sub

    Private Sub RenderPollDetail()
        PanBody.Children.Clear()
        Dim Poll As JObject = CurrentPoll
        Dim Viewer As JObject = CurrentPollViewer
        Dim Status As String = If(Poll("status")?.ToString(), "")
        Dim HasVoted As Boolean = Equals(Viewer?("hasVoted")?.ToObject(Of Boolean)(), True)

        PanBody.Children.Add(BuildBackButton(
            Sub()
                CurrentPoll = Nothing
                CurrentPollViewer = Nothing
                PollChoice = ""
                RenderTab()
            End Sub))
        PanBody.Children.Add(BuildTitleRow(If(Poll("title")?.ToString(), "社团投票"),
                                           PollStatusText(Status), PollStatusBrush(Status)))
        Dim Desc As String = If(Poll("description")?.ToString(), "")
        If Desc <> "" Then
            PanBody.Children.Add(New TextBlock With {
                .Text = Desc, .FontSize = 12, .TextWrapping = TextWrapping.Wrap,
                .Opacity = 0.75, .Margin = New Thickness(0, 8, 0, 0)
            })
        End If
        PanBody.Children.Add(BuildMeta("时间 " & FormatPollTime(Poll("startsAt")) &
                                       " ～ " & FormatPollTime(Poll("endsAt")) &
                                       " · 共 " & If(Poll("totalVotes")?.ToObject(Of Integer)(), 0) & " 票"))

        PanBody.Children.Add(SocialDivider())
        PanBody.Children.Add(SocialSectionTitle("选项"))
        Dim Options As JArray = TryCast(Poll("options"), JArray)
        Dim SelectedOption As String = If(Viewer?("selectedOptionId")?.ToString(), "")
        If Options IsNot Nothing Then
            For Each Token As JToken In Options
                Dim OptionItem As JObject = TryCast(Token, JObject)
                If OptionItem Is Nothing Then Continue For
                Dim OptionId As String = If(OptionItem("id")?.ToString(), "")
                Dim Text As String = If(OptionItem("text")?.ToString(), "选项")
                Dim Votes As JToken = OptionItem("votes")
                Dim Label As String = Text
                If Votes IsNot Nothing AndAlso Votes.Type <> JTokenType.Null Then
                    Label &= "（" & Votes.ToString() & " 票）"
                End If
                Dim Btn As New MyButton With {
                    .Text = Label, .Height = 34, .Margin = New Thickness(0, 6, 0, 0),
                    .ColorType = If(OptionId = If(PollChoice <> "", PollChoice, SelectedOption),
                                    MyButton.ColorState.Highlight, MyButton.ColorState.Normal)
                }
                Btn.IsEnabled = Status = "open" AndAlso Not HasVoted
                Dim LocalOption As String = OptionId
                AddHandler Btn.Click,
                    Sub()
                        PollChoice = LocalOption
                        RenderPollDetail()
                    End Sub
                PanBody.Children.Add(Btn)
            Next
        End If

        PanBody.Children.Add(SocialDivider())
        If HasVoted Then
            PanBody.Children.Add(SocialLabel("你已投票，投出后不可修改。"))
            Return
        End If
        If Status = "scheduled" Then
            PanBody.Children.Add(SocialLabel("投票尚未开始。"))
            Return
        End If
        If Status = "ended" Then
            PanBody.Children.Add(SocialLabel("投票已结束。"))
            Return
        End If
        Dim BtnVote As New MyButton With {
            .Text = "确认投票", .Height = 32, .MinWidth = 132, .Margin = New Thickness(0, 8, 0, 0),
            .ColorType = MyButton.ColorState.Highlight}
        AddHandler BtnVote.Click, Sub() SubmitVote()
        PanBody.Children.Add(BtnVote)
        PanBody.Children.Add(SocialLabel("每个账号限投一次，投出后不可修改。"))
    End Sub

    Private Sub SubmitVote()
        If PollChoice = "" Then
            Hint("请先选择一个选项", HintType.Blue)
            Return
        End If
        Dim Poll As JObject = CurrentPoll
        Dim PollId As String = If(Poll?("id")?.ToString(), "")
        Dim Version As Integer = If(Poll?("version")?.ToObject(Of Integer)(), 0)
        If MyMsgBox("投票投出后不可修改，确定选择「" & OptionText(PollChoice) & "」吗？",
                    "确认投票", "投票", "取消") <> 1 Then Return
        Dim Choice As String = PollChoice
        RunInNewThread(
        Sub()
            Try
                ModSocial.ClubVote(PollId, Choice, Version)
                RunInUi(
                Sub()
                    Hint("投票成功", HintType.Green)
                    ReloadData(PollId:=PollId)
                End Sub)
            Catch ex As Exception
                RunInUi(Sub() Hint(ex.Message, HintType.Red))
            End Try
        End Sub, "ClubVote")
    End Sub

    Private Function OptionText(OptionId As String) As String
        Dim Options As JArray = TryCast(CurrentPoll?("options"), JArray)
        If Options IsNot Nothing Then
            For Each Token As JToken In Options
                Dim Item As JObject = TryCast(Token, JObject)
                If Item IsNot Nothing AndAlso Equals(Item("id")?.ToString(), OptionId) Then
                    Return If(Item("text")?.ToString(), "选项")
                End If
            Next
        End If
        Return "选项"
    End Function

    Private Function PollStatusText(Status As String) As String
        Select Case Status
            Case "open" : Return "进行中"
            Case "scheduled" : Return "即将开始"
            Case "ended" : Return "已结束"
            Case "draft" : Return "草稿"
            Case Else : Return "状态待确认"
        End Select
    End Function

    Private Function PollStatusBrush(Status As String) As Brush
        Select Case Status
            Case "open" : Return New SolidColorBrush(Color.FromRgb(&H1F, &H9F, &H5A))
            Case "scheduled" : Return New SolidColorBrush(Color.FromRgb(&H13, &H70, &HF3))
            Case Else : Return New SolidColorBrush(Color.FromRgb(&H8C, &H8C, &H8C))
        End Select
    End Function

#End Region
#Region "个人中心"

    Private Sub RenderMe()
        Dim Profile As JObject = TryCast(Data("profile"), JObject)
        Dim Reputation As JObject = TryCast(Data("reputation"), JObject)
        Dim ActivityData As JObject = TryCast(Data("activity"), JObject)
        Dim Players As JArray = TryCast(Data("players"), JArray)
        Dim MyEvents As JArray = TryCast(Data("myEvents"), JArray)

        '账号
        Dim AccountCard As Border = SocialCard()
        AccountCard.Margin = New Thickness(0, 8, 0, 0)
        Dim AccountPanel As New StackPanel With {.Orientation = Orientation.Horizontal}
        Dim AccountId As String = If(Data("accountId")?.ToString(), "")
        Dim Avatar As FrameworkElement = SocialAvatar("/api/launcher/avatar/" & AccountId, 56,
                                                     If(Data("displayName")?.ToString(), ""))
        Avatar.Margin = New Thickness(0, 0, 14, 0)
        AccountPanel.Children.Add(Avatar)
        Dim AccountInfo As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        AccountInfo.Children.Add(New TextBlock With {
            .Text = If(Data("displayName")?.ToString(), "社团账号"), .FontSize = 17,
            .FontWeight = FontWeights.SemiBold})
        AccountInfo.Children.Add(SocialLabel("社团主账号 · 活动报名与投票都记在这个账号上"))
        AccountPanel.Children.Add(AccountInfo)
        AccountCard.Child = AccountPanel
        PanBody.Children.Add(AccountCard)

        '信誉
        Dim RepCard As Border = SocialCard()
        Dim RepPanel As New StackPanel
        RepPanel.Children.Add(SocialSectionTitle("信誉"))
        RepPanel.Children.Add(BuildMeta("参加活动 " & If(Reputation?("joined")?.ToObject(Of Integer)(), 0) &
                                        " · 成功签到 " & If(Reputation?("attended")?.ToObject(Of Integer)(), 0) &
                                        " · 未签到 " & If(Reputation?("missed")?.ToObject(Of Integer)(), 0)))
        If Equals(Reputation?("blacklisted")?.ToObject(Of Boolean)(), True) Then
            RepPanel.Children.Add(New TextBlock With {
                .Text = "你目前被限制报名活动：累计报名未签到次数过多。",
                .FontSize = 11.5, .Foreground = New SolidColorBrush(Color.FromRgb(&HD0, &H4B, &H3F)),
                .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 6, 0, 0)})
        End If
        RepCard.Child = RepPanel
        PanBody.Children.Add(RepCard)

        '游玩记录
        Dim PlayCard As Border = SocialCard()
        Dim PlayPanel As New StackPanel
        PlayPanel.Children.Add(SocialSectionTitle("游玩记录"))
        PlayPanel.Children.Add(BuildMeta("累计游玩 " & FormatDuration(If(ActivityData?("seconds")?.ToObject(Of Long)(), 0L)) &
                                         " · 最常游玩 " & If(ActivityData?("favorite")?("name")?.ToString(), "—") &
                                         " · 最近上线 " & FormatStamp(ActivityData?("lastSeen"))))
        Dim Servers As JArray = TryCast(ActivityData?("servers"), JArray)
        If Servers IsNot Nothing AndAlso Servers.Count > 0 Then
            For Each Token As JToken In Servers
                Dim Item As JObject = TryCast(Token, JObject)
                If Item Is Nothing Then Continue For
                Dim Line As New Grid With {.Margin = New Thickness(0, 6, 0, 0)}
                Line.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
                Line.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
                Line.Children.Add(New TextBlock With {
                    .Text = If(Item("name")?.ToString(), "服务器"), .FontSize = 11.5, .Opacity = 0.7})
                Dim TimeText As New TextBlock With {
                    .Text = FormatDuration(If(Item("seconds")?.ToObject(Of Long)(), 0L)),
                    .FontSize = 11.5, .Opacity = 0.7}
                Grid.SetColumn(TimeText, 1)
                Line.Children.Add(TimeText)
                PlayPanel.Children.Add(Line)
            Next
        Else
            PlayPanel.Children.Add(SocialLabel("还没有绑定 MC 角色，绑定后这里会显示累计游玩时长。"))
        End If
        PlayCard.Child = PlayPanel
        PanBody.Children.Add(PlayCard)

        '角色
        Dim RoleCard As Border = SocialCard()
        Dim RolePanel As New StackPanel
        RolePanel.Children.Add(SocialSectionTitle("MC 角色"))
        If Players Is Nothing OrElse Players.Count = 0 Then
            RolePanel.Children.Add(SocialLabel("还没有绑定 MC 角色，可在社团网站个人中心绑定。"))
        Else
            For Each Token As JToken In Players
                Dim Item As JObject = TryCast(Token, JObject)
                If Item Is Nothing Then Continue For
                Dim Provider As String = If(Item("provider")?.ToString(), "")
                RolePanel.Children.Add(BuildMeta(If(Provider = "mua", "MUA 角色", "正版角色") & "：" &
                                                 If(Item("name")?.ToString(), "")))
            Next
        End If
        RoleCard.Child = RolePanel
        PanBody.Children.Add(RoleCard)

        '活动记录
        Dim HistoryCard As Border = SocialCard()
        Dim HistoryPanel As New StackPanel
        HistoryPanel.Children.Add(SocialSectionTitle("活动记录"))
        If MyEvents Is Nothing OrElse MyEvents.Count = 0 Then
            HistoryPanel.Children.Add(SocialLabel("还没有报名过活动。"))
        Else
            For Each Token As JToken In MyEvents
                Dim Item As JObject = TryCast(Token, JObject)
                If Item Is Nothing Then Continue For
                Dim State As String = If(Equals(Item("checkedIn")?.ToObject(Of Boolean)(), True), "已签到",
                                         If(Equals(Item("missed")?.ToObject(Of Boolean)(), True), "未签到", "待签到"))
                HistoryPanel.Children.Add(BuildMeta(FormatStamp(Item("starts_at")) & " · " &
                                                    If(Item("title")?.ToString(), "社团活动") & " · " & State))
            Next
        End If
        HistoryCard.Child = HistoryPanel
        PanBody.Children.Add(HistoryCard)
    End Sub

    '（信息模板查看与编辑已移至官网个人中心：原 BuildProfileView / UnlockProfile /
    '  BuildProfileEditor / SendEmailCode / SubmitProfile 均为死代码，已删除）

#End Region
#Region "工具"

    ''' <summary>秒级时间戳 → 本地时间。</summary>
    Private Function FormatStamp(Token As JToken) As String
        Dim Seconds As Long = 0L
        If Token IsNot Nothing AndAlso Token.Type <> JTokenType.Null Then
            Long.TryParse(Token.ToString(), Seconds)
        End If
        If Seconds <= 0 Then Return "时间待确认"
        Try
            Return DateTimeOffset.FromUnixTimeSeconds(Seconds).ToLocalTime().ToString("MM-dd HH:mm")
        Catch ex As Exception
            Return "时间待确认"
        End Try
    End Function

    ''' <summary>ISO 时间（投票接口）→ 本地时间。</summary>
    Private Function FormatPollTime(Token As JToken) As String
        Dim Raw As String = If(Token Is Nothing OrElse Token.Type = JTokenType.Null, "", Token.ToString())
        If Raw = "" Then Return "时间待确认"
        Try
            Return DateTimeOffset.Parse(Raw).ToLocalTime().ToString("MM-dd HH:mm")
        Catch ex As Exception
            Return "时间待确认"
        End Try
    End Function

    Private Function FormatDuration(Seconds As Long) As String
        If Seconds <= 0 Then Return "暂无记录"
        Dim Hours As Long = Seconds \ 3600
        Dim Minutes As Long = (Seconds Mod 3600) \ 60
        If Hours > 0 Then Return Hours & " 小时" & If(Minutes > 0, " " & Minutes & " 分钟", "")
        If Minutes > 0 Then Return Minutes & " 分钟"
        Return "不足 1 分钟"
    End Function

#End Region

End Class
