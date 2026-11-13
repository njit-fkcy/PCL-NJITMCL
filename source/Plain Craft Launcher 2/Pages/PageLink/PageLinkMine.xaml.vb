Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

''' <summary>
''' 「联机 → 我的」：头像、昵称与账号信息。
''' </summary>
Public Class PageLinkMine
    Implements IRefreshable

    Private Sub PageEnterHandler() Handles Me.PageEnter
        RefreshAll()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        RefreshAll()
    End Sub

    Private Sub RefreshAll()
        PanContent.Children.Clear()
        PanContent.Children.Add(SocialLabel("正在读取账号资料……"))
        RunInNewThread(
        Sub()
            Try
                Dim Result As JObject = ModSocial.SocialProfile()
                Dim Account As JObject = TryCast(Result("account"), JObject)
                RunInUi(Sub() Render(Account))
            Catch ex As Exception
                Logger.Warn(ex, "加载联机账号资料失败")
                RunInUi(
                Sub()
                    PanContent.Children.Clear()
                    PanContent.Children.Add(SocialLabel(ex.Message))
                    PanContent.Children.Add(BuildActions())
                End Sub)
            End Try
        End Sub, "SocialProfile")
    End Sub

    Private Sub Render(Account As JObject)
        PanContent.Children.Clear()
        If Account Is Nothing Then
            PanContent.Children.Add(SocialLabel("中控没有返回账号资料"))
            PanContent.Children.Add(BuildActions())
            Return
        End If

        Dim Name As String = If(Account("displayName")?.ToString(), "未命名")
        Dim AccountId As String = If(Account("accountId")?.ToString(), "")
        Dim AvatarPath As String = If(Account("avatarUrl")?.ToString(), "")
        Dim AvatarSource As String = If(Account("avatarSource")?.ToString(), "")

        '头部：大头像 + 昵称 + 身份
        Dim Head As New StackPanel With {.Orientation = Orientation.Horizontal}
        Dim Avatar As FrameworkElement = SocialAvatar(AvatarPath, 96, Name)
        Avatar.Margin = New Thickness(0, 0, 20, 0)
        Head.Children.Add(Avatar)

        Dim Info As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Info.Children.Add(New TextBlock With {
            .Text = Name, .FontSize = 22, .FontWeight = FontWeights.SemiBold})
        Info.Children.Add(New TextBlock With {
            .Text = "联机名与网站昵称一致", .FontSize = 11, .Opacity = 0.55,
            .Margin = New Thickness(0, 4, 0, 0)})

        Dim Identities As JArray = TryCast(Account("identities"), JArray)
        Dim Tags As New StackPanel With {
            .Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 10, 0, 0)}
        If Identities IsNot Nothing Then
            For Each Item As JToken In Identities
                Dim Provider As String = If(Item("provider")?.ToString(), "")
                Dim Label As String = If(Provider = "mojang", "正版角色",
                                      If(Provider = "mua", "MUA 角色", Provider))
                Tags.Children.Add(New Border With {
                    .CornerRadius = New CornerRadius(3), .Padding = New Thickness(8, 2, 8, 2),
                    .Margin = New Thickness(0, 0, 6, 0),
                    .Background = New SolidColorBrush(Color.FromArgb(22, 0, 0, 0)),
                    .Child = New TextBlock With {
                        .Text = Label & " · " & If(Item("displayName")?.ToString(), ""),
                        .FontSize = 11, .Opacity = 0.7}})
            Next
        End If
        If Tags.Children.Count = 0 Then
            Tags.Children.Add(New TextBlock With {
                .Text = "还没有绑定身份", .FontSize = 11, .Opacity = 0.5})
        End If
        Info.Children.Add(Tags)
        Head.Children.Add(Info)
        PanContent.Children.Add(Head)

        PanContent.Children.Add(SocialDivider())

        '资料明细
        Dim SourceLabel As String = If(AvatarSource = "mua", "MUA 头像",
                                      If(AvatarSource = "mojang", "正版头像", "自动（正版优先）"))
        Dim Table As New Grid With {.Margin = New Thickness(0, 10, 0, 0)}
        Table.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(132)})
        Table.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        AddRow(Table, 0, "联机名", Name)
        AddRow(Table, 1, "账号 ID", If(AccountId = "", "—", AccountId))
        AddRow(Table, 2, "头像来源", SourceLabel)
        AddRow(Table, 3, "修改头像", "在网站个人中心选择 MUA 或正版头像")
        PanContent.Children.Add(Table)

        PanContent.Children.Add(BuildActions())

        PanContent.Children.Add(SocialLabel("头像与昵称都跟随社团网站账号；在网站个人中心改完，回到这里点刷新即可。"))
    End Sub

    Private Sub AddRow(Grid As Grid, Row As Integer, Label As String, Value As String)
        If Grid.RowDefinitions.Count <= Row Then
            Grid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        End If
        Dim Key As New TextBlock With {
            .Text = Label, .FontSize = 12, .Opacity = 0.5, .Margin = New Thickness(0, 0, 0, 8)}
        Grid.SetRow(Key, Row)
        Grid.SetColumn(Key, 0)
        Grid.Children.Add(Key)
        Dim Text As New TextBlock With {
            .Text = Value, .FontSize = 12, .Margin = New Thickness(0, 0, 0, 8),
            .TextWrapping = TextWrapping.Wrap}
        Grid.SetRow(Text, Row)
        Grid.SetColumn(Text, 1)
        Grid.Children.Add(Text)
    End Sub

    Private Function BuildActions() As FrameworkElement
        Dim Panel As New StackPanel With {
            .Orientation = Orientation.Horizontal, .Margin = New Thickness(0, 18, 0, 0)}
        Dim BtnRefresh As New MyButton With {
            .Text = "刷新", .Height = 30, .MinWidth = 84}
        AddHandler BtnRefresh.Click, Sub() RefreshAll()
        Dim BtnAvatar As New MyButton With {
            .Text = "去网站修改头像", .Height = 30, .MinWidth = 132, .Margin = New Thickness(8, 0, 0, 0),
            .ColorType = MyButton.ColorState.Highlight}
        AddHandler BtnAvatar.Click,
            Sub() OpenWebsite(ModNJIT.ClubHomepage & "/#club?tab=me")
        Panel.Children.Add(BtnRefresh)
        Panel.Children.Add(BtnAvatar)
        If ModSocial.IsLoggedIn() Then
            Dim BtnLogout As New MyButton With {
                .Text = "退出联机账号", .Height = 30, .MinWidth = 112, .Margin = New Thickness(8, 0, 0, 0)}
            AddHandler BtnLogout.Click,
                Sub()
                    If MyMsgBox("退出后需要重新用正版或 MUA 账号登录才能使用联机，确定退出吗？",
                                "退出联机账号", "退出", "取消") <> 1 Then Return
                    ModSocial.Logout()
                    Hint("已退出联机账号", HintType.Green)
                    RefreshAll()
                End Sub
            Panel.Children.Add(BtnLogout)
        Else
            Dim BtnAuth As New MyButton With {
                .Text = "去授权启动器", .Height = 30, .MinWidth = 112, .Margin = New Thickness(8, 0, 0, 0),
                .ColorType = MyButton.ColorState.Highlight}
            AddHandler BtnAuth.Click,
                Sub()
                    FrmMain.PageChange(FormMain.PageType.Launch)
                    Hint("请在「社团生活」卡片点击「授权启动器」", HintType.Blue)
                End Sub
            Panel.Children.Add(BtnAuth)
        End If
        Return Panel
    End Function

End Class
