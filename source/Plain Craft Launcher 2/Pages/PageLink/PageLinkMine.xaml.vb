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

    '授权进行中，避免连点重复申请设备码
    Private AuthRunning As Boolean = False

    Private Sub RefreshAll()
        PanContent.Children.Clear()
        If Not ModSocial.IsLoggedIn() Then
            PanContent.Children.Add(SocialLabel("尚未授权社团账号。授权后可以查看资料、添加好友与使用联机。"))
            PanContent.Children.Add(BuildActions())
            Return
        End If
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
                    PanContent.Children.Add(SocialLabel(ModSocial.FriendlyError(ex)))
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
                    .CornerRadius = New CornerRadius(8), .Padding = New Thickness(9, 2, 9, 2),
                    .Margin = New Thickness(0, 0, 6, 0),
                    .Background = New SolidColorBrush(Color.FromArgb(22, 0, 0, 0)),
                    .Child = New TextBlock With {
                        .Text = Label & " · " & If(Item("displayName")?.ToString(), ""),
                        .FontSize = 11, .Opacity = 0.75}})
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
            Dim BtnReauth As New MyButton With {
                .Text = "重新授权", .Height = 30, .MinWidth = 100, .Margin = New Thickness(8, 0, 0, 0)}
            AddHandler BtnReauth.Click, Sub() StartMineAuth(True)
            Panel.Children.Add(BtnReauth)

            Dim BtnLogout As New MyButton With {
                .Text = "退出社团账号", .Height = 30, .MinWidth = 112, .Margin = New Thickness(8, 0, 0, 0)}
            AddHandler BtnLogout.Click,
                Sub()
                    If MyMsgBox("退出后需要重新授权才能使用联机与社团生活，正在进行的房间会关闭。确定退出吗？",
                                "退出社团账号", "退出", "取消") <> 1 Then Return
                    ModSocial.Logout()
                    Hint("已退出社团账号", HintType.Green)
                    RefreshAll()
                End Sub
            Panel.Children.Add(BtnLogout)
        Else
            Dim BtnAuth As New MyButton With {
                .Text = "授权启动器", .Height = 30, .MinWidth = 112, .Margin = New Thickness(8, 0, 0, 0),
                .ColorType = MyButton.ColorState.Highlight}
            AddHandler BtnAuth.Click, Sub() StartMineAuth(False)
            Panel.Children.Add(BtnAuth)
        End If
        Return Panel
    End Function

    ''' <summary>
    ''' 与启动页同一条授权路径：正版登录态优先直授权，没有正版才申请设备码。
    ''' Reauth 为 True 时先退出当前社团账号再授权。
    ''' </summary>
    Private Sub StartMineAuth(Reauth As Boolean)
        If AuthRunning Then Return
        If Reauth Then
            If MyMsgBox("重新授权会先退出当前社团账号（正在进行的房间会关闭），再用正版身份（优先）或设备码重新授权。继续吗？",
                        "重新授权", "继续", "取消") <> 1 Then Return
            ModSocial.Logout()
        End If
        AuthRunning = True
        PanContent.Children.Clear()
        PanContent.Children.Add(SocialLabel("正在授权启动器……"))
        RunInNewThread(
        Sub()
            Try
                Dim Authorized As Boolean = ModSocial.AuthorizeLauncher(
                    "NJIT MCL 联机页",
                    Sub(UserCode As String)
                        RunInUi(
                        Sub()
                            Try
                                Clipboard.SetText(UserCode)
                            Catch
                            End Try
                            OpenWebsite(ModNJIT.ClubHomepage & "/launcher-device")
                            Hint("授权码 " & UserCode & " 已复制，请在网页登录后粘贴确认", HintType.Green)
                        End Sub)
                    End Sub)
                RunInUi(
                Sub()
                    AuthRunning = False
                    If Authorized Then
                        Hint("已授权：" & ModSocial.SocialName, HintType.Green)
                    Else
                        Hint("授权未完成，请重新点击「授权启动器」", HintType.Red)
                    End If
                    RefreshAll()
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "授权启动器失败")
                RunInUi(
                Sub()
                    AuthRunning = False
                    Hint("授权失败：" & ModSocial.FriendlyError(ex), HintType.Red)
                    RefreshAll()
                End Sub)
            End Try
        End Sub, "MineAuth")
    End Sub

End Class
