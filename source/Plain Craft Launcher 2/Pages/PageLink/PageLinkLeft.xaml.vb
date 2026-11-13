''' <summary>
''' 「联机」顶级页面的左侧栏：我的 / 服务器 / 我的房间 / 好友。
''' </summary>
Public Class PageLinkLeft

#Region "页面切换"

    ''' <summary>
    ''' 当前页面的编号。
    ''' </summary>
    Public PageID As FormMain.PageSubType = FormMain.PageSubType.LinkMine

    Public Sub New()
        InitializeComponent()
        PageID = FormMain.PageSubType.LinkMine
    End Sub

    ''' <summary>
    ''' 勾选事件改变页面。
    ''' </summary>
    Private Sub PageCheck(sender As FrameworkElement, e As RouteEventArgs) Handles ItemMine.Check, ItemServer.Check, ItemRoom.Check, ItemFriend.Check
        If sender.Tag IsNot Nothing Then PageChange(Val(sender.Tag))
    End Sub

    Public Function PageGet(Optional ID As FormMain.PageSubType = -1) As MyPageRight
        If ID = -1 Then ID = PageID
        Select Case ID
            Case FormMain.PageSubType.LinkMine, FormMain.PageSubType.LinkMain
                If FrmLinkMine Is Nothing Then FrmLinkMine = New PageLinkMine
                Return FrmLinkMine
            Case FormMain.PageSubType.LinkServer
                If FrmLinkServer Is Nothing Then FrmLinkServer = New PageLinkServer
                Return FrmLinkServer
            Case FormMain.PageSubType.LinkRoom
                If FrmLinkRoom Is Nothing Then FrmLinkRoom = New PageLinkRoom
                Return FrmLinkRoom
            Case FormMain.PageSubType.LinkFriend
                If FrmLinkFriend Is Nothing Then FrmLinkFriend = New PageLinkFriend
                Return FrmLinkFriend
            Case Else
                Throw New Exception("未知的联机子页面种类：" & ID)
        End Select
    End Function

    ''' <summary>
    ''' 切换现有页面。
    ''' </summary>
    Public Sub PageChange(ID As FormMain.PageSubType)
        If PageID = ID Then Return
        AniControlEnabled += 1
        Try
            PageChangeRun(PageGet(ID))
            PageID = ID
        Catch ex As Exception
            Logger.Error(ex, $"切换联机分页面失败（ID {ID}）")
        Finally
            AniControlEnabled -= 1
        End Try
    End Sub

    Private Shared Sub PageChangeRun(Target As MyPageRight)
        AniStop("FrmMain PageChangeRight")
        If Target.Parent IsNot Nothing Then Target.SetValue(ContentPresenter.ContentProperty, Nothing)
        FrmMain.PageRight = Target
        CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnExit()
        AniStart({
                         AaCode(Sub()
                                    CType(FrmMain.PanMainRight.Child, MyPageRight).PageOnForceExit()
                                    FrmMain.PanMainRight.Child = FrmMain.PageRight
                                    FrmMain.PageRight.Opacity = 0
                                End Sub, 130),
                         AaCode(Sub()
                                    FrmMain.PageRight.Opacity = 1
                                    FrmMain.PageRight.PageOnEnter()
                                End Sub, 30, True)
                     }, "PageLeft PageChange")
    End Sub

#End Region

End Class
