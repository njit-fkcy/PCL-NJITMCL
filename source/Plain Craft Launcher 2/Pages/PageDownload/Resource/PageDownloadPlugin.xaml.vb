Imports System.Windows
Imports System.Windows.Controls

''' <summary>
''' 插件市场页：列表由官网中控提供，插件以独立进程运行，不进入启动器本体。
''' </summary>
Public Class PageDownloadPlugin
    Implements IRefreshable

    '每次进入都重新拉取，保证「更新」判定与线上版本一致
    Private Sub PageEnterHandler() Handles Me.PageEnter
        Reload()
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        Reload()
    End Sub

    Private Sub Reload()
        LabStatus.Text = "正在获取插件列表……"
        PanPlugins.Children.Clear()
        RunInNewThread(
        Sub()
            Try
                Dim Plugins As List(Of NjitPlugin) = ModPlugin.FetchList()
                RunInUi(Sub() RenderList(Plugins))
            Catch ex As Exception
                Logger.Warn(ex, "获取插件列表失败")
                RunInUi(Sub() LabStatus.Text = "获取插件列表失败：" & ex.Message)
            End Try
        End Sub, "PluginList")
    End Sub

    Private Sub RenderList(Plugins As List(Of NjitPlugin))
        PanPlugins.Children.Clear()
        If Plugins.Count = 0 Then
            LabStatus.Text = "暂时还没有上架的插件。"
            Return
        End If
        LabStatus.Text = $"共 {Plugins.Count} 个插件"
        For Each Plugin As NjitPlugin In Plugins
            PanPlugins.Children.Add(BuildRow(Plugin))
        Next
    End Sub

    Private Function BuildRow(Plugin As NjitPlugin) As FrameworkElement
        Dim Row As New Grid With {.Height = 52, .Margin = New Thickness(0, 0, 0, 3)}
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim InfoPanel As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(4, 0, 12, 0)}
        Dim Title As New TextBlock With {
            .Text = $"{Plugin.Name}  {Plugin.Version}",
            .FontSize = 14,
            .TextTrimming = TextTrimming.CharacterEllipsis}
        Dim Desc As New TextBlock With {
            .Text = Plugin.Description & If(String.IsNullOrWhiteSpace(Plugin.Author), "", " ｜ " & Plugin.Author),
            .FontSize = 12,
            .Opacity = 0.6,
            .Margin = New Thickness(0, 2, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis}
        InfoPanel.Children.Add(Title)
        InfoPanel.Children.Add(Desc)
        Grid.SetColumn(InfoPanel, 0)
        Row.Children.Add(InfoPanel)

        Dim LocalVersion As String = ModPlugin.GetInstalledVersion(Plugin)
        Dim HasUpdate As Boolean = LocalVersion <> "" AndAlso ModPlugin.NeedsUpdate(Plugin)
        Dim Action As New MyButton With {
            .Text = If(HasUpdate, "更新", If(LocalVersion <> "", "打开", "安装")),
            .Padding = New Thickness(13, 0, 13, 0),
            .Height = 28,
            .MinWidth = 76,
            .ColorType = If(LocalVersion = "" OrElse HasUpdate, MyButton.ColorState.Highlight, MyButton.ColorState.Normal),
            .VerticalAlignment = VerticalAlignment.Center}
        Dim UninstallButton As New MyButton With {
            .Text = "卸载",
            .Padding = New Thickness(13, 0, 13, 0),
            .Height = 28,
            .MinWidth = 76,
            .Margin = New Thickness(8, 0, 0, 0),
            .ColorType = MyButton.ColorState.Normal,
            .VerticalAlignment = VerticalAlignment.Center,
            .Visibility = If(LocalVersion <> "", Visibility.Visible, Visibility.Collapsed)}
        AddHandler Action.Click, Sub() HandleAction(Plugin, Action, UninstallButton)
        AddHandler UninstallButton.Click, Sub() HandleUninstall(Plugin, Action, UninstallButton)
        Dim Actions As New StackPanel With {
            .Orientation = Orientation.Horizontal,
            .HorizontalAlignment = HorizontalAlignment.Right,
            .VerticalAlignment = VerticalAlignment.Center}
        Actions.Children.Add(UninstallButton)
        Actions.Children.Add(Action)
        Grid.SetColumn(Actions, 1)
        Row.Children.Add(Actions)
        Return Row
    End Function

    Private Sub HandleAction(Plugin As NjitPlugin, Action As MyButton, UninstallButton As MyButton)
        Dim LocalVersion As String = ModPlugin.GetInstalledVersion(Plugin)
        If LocalVersion <> "" AndAlso Not ModPlugin.NeedsUpdate(Plugin) Then
            Try
                If Not ModPlugin.Launch(Plugin) Then
                    Hint("启动插件失败：安装目录中找不到入口程序，请尝试重新安装", HintType.Red)
                    Return
                End If
                Hint("已启动插件：" & Plugin.Name, HintType.Green)
            Catch ex As Exception
                Hint("启动插件失败：" & ex.Message, HintType.Red)
            End Try
            Return
        End If
        Dim IsUpdate As Boolean = LocalVersion <> ""
        Dim Question As String = If(IsUpdate,
            $"确定将插件「{Plugin.Name}」更新到 {Plugin.Version} 吗？",
            $"确定安装插件「{Plugin.Name}」吗？")
        If MyMsgBox(Question & vbCrLf & "插件由第三方提供并以独立进程运行，启动器不对其行为负责。",
                    If(IsUpdate, "更新插件", "安装插件"), If(IsUpdate, "更新", "安装"), "取消") = 2 Then Return
        Action.IsEnabled = False
        UninstallButton.IsEnabled = False
        Action.Text = If(IsUpdate, "更新中……", "安装中……")
        RunInNewThread(
        Sub()
            Try
                ModPlugin.Install(Plugin)
                RunInUi(
                Sub()
                    Action.Text = "打开"
                    Action.ColorType = MyButton.ColorState.Normal
                    Action.IsEnabled = True
                    UninstallButton.IsEnabled = True
                    UninstallButton.Visibility = Visibility.Visible
                    Hint("插件已" & If(IsUpdate, "更新", "安装") & "：" & Plugin.Name, HintType.Green)
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "安装插件失败：" & Plugin.Id)
                RunInUi(
                Sub()
                    Action.Text = If(IsUpdate, "更新", "安装")
                    Action.IsEnabled = True
                    UninstallButton.IsEnabled = True
                    Hint(If(IsUpdate, "更新", "安装") & "插件失败：" & ex.Message, HintType.Red)
                End Sub)
            End Try
        End Sub, "PluginInstall")
    End Sub

    Private Sub HandleUninstall(Plugin As NjitPlugin, Action As MyButton, UninstallButton As MyButton)
        If MyMsgBox($"确定卸载插件「{Plugin.Name}」吗？{vbCrLf}将删除已安装的全部版本及插件数据，无法恢复。",
                    "卸载插件", "卸载", "取消") = 2 Then Return
        Action.IsEnabled = False
        UninstallButton.IsEnabled = False
        UninstallButton.Text = "卸载中……"
        RunInNewThread(
        Sub()
            Try
                ModPlugin.Uninstall(Plugin)
                RunInUi(
                Sub()
                    Action.Text = "安装"
                    Action.ColorType = MyButton.ColorState.Highlight
                    Action.IsEnabled = True
                    UninstallButton.Text = "卸载"
                    UninstallButton.IsEnabled = True
                    UninstallButton.Visibility = Visibility.Collapsed
                    Hint("插件已卸载：" & Plugin.Name, HintType.Green)
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "卸载插件失败：" & Plugin.Id)
                RunInUi(
                Sub()
                    Action.IsEnabled = True
                    UninstallButton.Text = "卸载"
                    UninstallButton.IsEnabled = True
                    Hint("卸载插件失败：" & ex.Message, HintType.Red)
                End Sub)
            End Try
        End Sub, "PluginUninstall")
    End Sub

End Class
