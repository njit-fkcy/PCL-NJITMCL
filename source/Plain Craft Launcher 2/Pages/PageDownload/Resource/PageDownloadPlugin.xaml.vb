Imports System.Windows.Controls

''' <summary>
''' 插件市场页：列表由官网中控提供，插件以独立进程运行，不进入启动器本体。
''' </summary>
Public Class PageDownloadPlugin
    Implements IRefreshable

    Private Shared HasLoaded As Boolean = False

    Private Sub PageEnterHandler() Handles Me.PageEnter
        If HasLoaded Then Return
        HasLoaded = True
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

        Dim Installed As Boolean = ModPlugin.IsInstalled(Plugin)
        Dim Action As New MyButton With {
            .Text = If(Installed, "打开", "安装"),
            .Padding = New Thickness(13, 0, 13, 0),
            .Height = 28,
            .MinWidth = 76,
            .ColorType = If(Installed, MyButton.ColorState.Normal, MyButton.ColorState.Highlight),
            .HorizontalAlignment = HorizontalAlignment.Right,
            .VerticalAlignment = VerticalAlignment.Center}
        AddHandler Action.Click, Sub() HandleAction(Plugin, Action)
        Grid.SetColumn(Action, 1)
        Row.Children.Add(Action)
        Return Row
    End Function

    Private Sub HandleAction(Plugin As NjitPlugin, Action As MyButton)
        If ModPlugin.IsInstalled(Plugin) Then
            Try
                ModPlugin.Launch(Plugin)
                Hint("已启动插件：" & Plugin.Name, HintType.Green)
            Catch ex As Exception
                Hint("启动插件失败：" & ex.Message, HintType.Red)
            End Try
            Return
        End If
        If MyMsgBox($"确定安装插件「{Plugin.Name}」吗？{vbCrLf}插件由第三方提供并以独立进程运行，启动器不对其行为负责。",
                    "安装插件", "安装", "取消") = 2 Then Return
        Action.IsEnabled = False
        Action.Text = "安装中……"
        RunInNewThread(
        Sub()
            Try
                ModPlugin.Install(Plugin)
                RunInUi(
                Sub()
                    Action.Text = "打开"
                    Action.ColorType = MyButton.ColorState.Normal
                    Action.IsEnabled = True
                    Hint("插件已安装：" & Plugin.Name, HintType.Green)
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "安装插件失败：" & Plugin.Id)
                RunInUi(
                Sub()
                    Action.Text = "安装"
                    Action.IsEnabled = True
                    Hint("安装插件失败：" & ex.Message, HintType.Red)
                End Sub)
            End Try
        End Sub, "PluginInstall")
    End Sub

End Class
