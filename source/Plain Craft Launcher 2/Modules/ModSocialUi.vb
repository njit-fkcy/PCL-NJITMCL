Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

''' <summary>
''' 联机页面共用的 UI 构造函数与语义色，保证各子页面观感一致。
''' </summary>
Public Module ModSocialUi

#Region "语义色"

    ''' <summary>成功 / 进行中 / 已签到。</summary>
    Public ReadOnly SocialGreen As Color = Color.FromRgb(&H1F, &H9F, &H5A)
    ''' <summary>即将开始 / 信息提示。</summary>
    Public ReadOnly SocialBlue As Color = Color.FromRgb(&H13, &H70, &HF3)
    ''' <summary>已结束 / 已截止 / 禁用。</summary>
    Public ReadOnly SocialGray As Color = Color.FromRgb(&H8C, &H8C, &H8C)
    ''' <summary>危险 / 警告 / 未签到。</summary>
    Public ReadOnly SocialRed As Color = Color.FromRgb(&HD0, &H4B, &H3F)

    ''' <summary>语义色 → 画刷。</summary>
    Public Function SocialBrush(C As Color) As Brush
        Return New SolidColorBrush(C)
    End Function

    ''' <summary>
    ''' 取主题画刷。拉杆在构造时还没进可视树，FindResource 会找不到自己那一层，
    ''' 所以走应用级查找；再取不到就退回字面色，界面不至于崩。
    ''' </summary>
    Public Function SocialThemeBrush(Key As String, Fallback As Color) As Brush
        Try
            Dim Found As Object = Application.Current.TryFindResource(Key)
            If TypeOf Found Is Brush Then Return DirectCast(Found, Brush)
        Catch ex As Exception
            '设计器里没有 Application，忽略
        End Try
        Return New SolidColorBrush(Fallback)
    End Function

#End Region

    ''' <summary>圆角头像；没有图片地址时显示昵称首字母。</summary>
    Public Function SocialAvatar(RelativePath As String, Size As Integer, Optional Name As String = "") As FrameworkElement
        Dim Host As New Grid With {.Width = Size, .Height = Size}
        Host.Clip = New RectangleGeometry(New Rect(0, 0, Size, Size), 8, 8)
        Host.Children.Add(New Border With {
            .Background = New SolidColorBrush(Color.FromArgb(18, 0, 0, 0))})
        Dim Initial As String = If(String.IsNullOrWhiteSpace(Name), "", Name.Trim().Substring(0, 1).ToUpper())
        If Initial <> "" Then
            Host.Children.Add(New TextBlock With {
                .Text = Initial, .FontSize = Math.Max(11, Size * 0.4), .FontWeight = FontWeights.SemiBold,
                .Opacity = 0.45, .HorizontalAlignment = HorizontalAlignment.Center,
                .VerticalAlignment = VerticalAlignment.Center})
        End If
        Dim Url As String = ModSocial.AvatarUrl(RelativePath)
        If Url <> "" Then
            Host.Children.Add(New MyImage With {
                .Width = Size, .Height = Size, .Stretch = Stretch.UniformToFill, .Source = Url})
        End If
        Return Host
    End Function

    ''' <summary>分区标题。</summary>
    Public Function SocialSectionTitle(Text As String) As TextBlock
        Return New TextBlock With {
            .Text = Text, .FontSize = 13.5, .FontWeight = FontWeights.SemiBold,
            .Margin = New Thickness(0, 0, 0, 4)}
    End Function

    ''' <summary>灰色小字说明。</summary>
    Public Function SocialLabel(Text As String) As TextBlock
        Return New TextBlock With {
            .Text = Text, .FontSize = 11, .Opacity = 0.55, .Margin = New Thickness(0, 6, 0, 4),
            .TextWrapping = TextWrapping.Wrap}
    End Function

    ''' <summary>
    ''' 设置行标题：与分区标题同字号、同字重，但去掉了下边距 ——
    ''' 它要和下面那行状态文字叠成两行，和「进服校验」卡片左侧完全一致。
    ''' </summary>
    Public Function SocialRowTitle(Text As String) As TextBlock
        Return New TextBlock With {
            .Text = Text, .FontSize = 13.5, .FontWeight = FontWeights.SemiBold,
            .VerticalAlignment = VerticalAlignment.Center}
    End Function

    ''' <summary>
    ''' 设置行的状态行：11 号灰字、与标题间距 2。
    ''' 卡片里的「已接管（PID …）」和拉杆下的「已关闭：仅校园内网入口」都用它，
    ''' 这样两处的第二行是同一种画风，不会一个 0.55 一个 0.6、一个带 6 的边距一个带 2。
    ''' </summary>
    Public Function SocialRowState(Text As String) As TextBlock
        Return New TextBlock With {
            .Text = Text, .FontSize = 11, .Opacity = 0.55,
            .Margin = New Thickness(0, 2, 0, 0), .TextWrapping = TextWrapping.Wrap,
            .VerticalAlignment = VerticalAlignment.Center}
    End Function

    ''' <summary>
    ''' 一行可复制的地址：左侧标签（校内 / 公网 / 本机）+ 等宽地址 + 备注 + 复制按钮。
    '''
    ''' 为什么要一行一个：一个房间可能同时注册了校内和公网两个入口，挤成一行字符串时
    ''' 只能显示其中一个（旧实现公网优先，校内那个就永远看不见），而且整行是纯文本，
    ''' 想复制还得自己选中。现在每个入口单独一行、各自带按钮，点地址本身也能复制。
    ''' </summary>
    Public Function SocialAddressRow(Label As String, Address As String, Accent As Color,
                                     Optional Note As String = "") As FrameworkElement
        Dim Row As New Grid With {.Margin = New Thickness(0, 3, 0, 0)}
        '打标记：外层卡片靠点击空白处展开/收起，点到这一行时不能顺手把卡片收起来
        Row.Tag = CopyRegionTag
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Row.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim TagText As New TextBlock With {
            .Text = Label, .FontSize = 11, .Foreground = SocialBrush(Accent),
            .Margin = New Thickness(0, 0, 8, 0), .MinWidth = 24,
            .VerticalAlignment = VerticalAlignment.Center}
        Grid.SetColumn(TagText, 0)
        Row.Children.Add(TagText)

        Dim Value As New TextBlock With {
            .Text = Address, .FontSize = 11.5, .FontFamily = New FontFamily("Consolas"),
            .VerticalAlignment = VerticalAlignment.Center, .Cursor = Cursors.Hand,
            .ToolTip = "点击复制"}
        AddHandler Value.MouseLeftButtonUp, Sub() SocialCopy(Address, Label & "地址")
        Grid.SetColumn(Value, 1)
        Row.Children.Add(Value)

        If Note <> "" Then
            Dim NoteText As New TextBlock With {
                .Text = Note, .FontSize = 10.5, .Opacity = 0.5, .Margin = New Thickness(8, 0, 0, 0),
                .VerticalAlignment = VerticalAlignment.Center}
            Grid.SetColumn(NoteText, 2)
            Row.Children.Add(NoteText)
        End If

        Dim Btn As New MyButton With {
            .Text = "复制", .Height = 22, .MinWidth = 44, .Margin = New Thickness(8, 0, 0, 0),
            .VerticalAlignment = VerticalAlignment.Center}
        AddHandler Btn.Click, Sub() SocialCopy(Address, Label & "地址")
        Grid.SetColumn(Btn, 3)
        Row.Children.Add(Btn)
        Return Row
    End Function

    ''' <summary>复制到剪贴板并给出反馈；空地址不动作。</summary>
    Public Sub SocialCopy(Value As String, What As String)
        If String.IsNullOrWhiteSpace(Value) Then Return
        Try
            Clipboard.SetText(Value)
            Hint(What & "已复制：" & Value, HintType.Green)
        Catch ex As Exception
            Hint("复制失败：剪贴板被其他程序占用", HintType.Red)
        End Try
    End Sub

    ''' <summary>
    ''' 可复制区域的标记。外层卡片用「点卡片展开」这种整块点击时，
    ''' 要跳过带这个标记的子树，否则点一下地址会连带把卡片收起。
    ''' </summary>
    Public Const CopyRegionTag As String = "SocialCopyRegion"

    ''' <summary>浅色圆角卡片。</summary>
    Public Function SocialCard() As Border
        Return New Border With {
            .CornerRadius = New CornerRadius(8), .Padding = New Thickness(16, 12, 16, 12),
            .Margin = New Thickness(0, 8, 0, 0),
            .BorderThickness = New Thickness(1),
            .BorderBrush = New SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
            .Background = New SolidColorBrush(Color.FromArgb(8, 0, 0, 0))}
    End Function

    ''' <summary>一行分隔线。</summary>
    Public Function SocialDivider() As Border
        Return New Border With {
            .Height = 1, .Margin = New Thickness(0, 16, 0, 0),
            .Background = New SolidColorBrush(Color.FromArgb(20, 0, 0, 0))}
    End Function

    ''' <summary>
    ''' 状态徽章（胶囊形）：用语义色浅底 + 同色文字，让「报名中 / 已截止 / 进行中」一眼可辨。
    ''' </summary>
    Public Function SocialBadge(Text As String, Accent As Color) As Border
        Return New Border With {
            .CornerRadius = New CornerRadius(9), .Padding = New Thickness(9, 2, 9, 2),
            .Margin = New Thickness(8, 0, 0, 0), .VerticalAlignment = VerticalAlignment.Center,
            .Background = New SolidColorBrush(Color.FromArgb(26, Accent.R, Accent.G, Accent.B)),
            .Child = New TextBlock With {
                .Text = Text, .FontSize = 10.5, .Foreground = New SolidColorBrush(Accent)}}
    End Function

End Module

''' <summary>
''' 联机页统一的拉杆开关：左边标题 + 状态行，右边 44×22 轨道与 16 号白色滑块。
''' 「开启公网房间」与「进服校验」共用同一套实现，页面里不要再各写一份。
''' </summary>
Public Class SocialSwitch
    Inherits Grid

    ''' <summary>轨道：整根 44×22 圆角条，点它才切换。</summary>
    Public ReadOnly Property Track As Border
    ''' <summary>滑块。</summary>
    Public ReadOnly Property Knob As Border
    ''' <summary>标题下面那行灰字。</summary>
    Public ReadOnly Property StateText As TextBlock

    Private _On As Boolean

    Public Sub New(Title As String, InitialState As String)
        Margin = New Thickness(0, 12, 0, 0)
        ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim Left As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Left.Children.Add(ModSocialUi.SocialRowTitle(Title))
        StateText = ModSocialUi.SocialRowState(InitialState)
        Left.Children.Add(StateText)
        Children.Add(Left)

        Track = New Border With {
            .Width = 44, .Height = 22, .CornerRadius = New CornerRadius(11),
            .VerticalAlignment = VerticalAlignment.Center, .Cursor = Cursors.Hand}
        Knob = New Border With {
            .Width = 16, .Height = 16, .CornerRadius = New CornerRadius(8),
            .Background = New SolidColorBrush(Colors.White),
            .HorizontalAlignment = HorizontalAlignment.Left, .Margin = New Thickness(3, 0, 0, 0)}
        Track.Child = Knob
        SetColumn(Track, 1)
        Children.Add(Track)

        ApplyVisual()
    End Sub

    ''' <summary>开关状态；赋值同时刷新轨道色与滑块位置。</summary>
    Public Property IsOn As Boolean
        Get
            Return _On
        End Get
        Set(Value As Boolean)
            _On = Value
            ApplyVisual()
        End Set
    End Property

    ''' <summary>接管期间置灰，避免连点。</summary>
    Public Sub SetInteractive(Value As Boolean)
        Track.IsEnabled = Value
        Track.Opacity = If(Value, 1.0, 0.45)
    End Sub

    Private Sub ApplyVisual()
        If Track Is Nothing Then Return
        Track.Background = ModSocialUi.SocialThemeBrush(
            If(_On, "ColorBrush3", "ColorBrush7"),
            If(_On, Color.FromRgb(&H13, &H70, &HF3), Color.FromRgb(&HE0, &HEA, &HFD)))
        Knob.HorizontalAlignment = If(_On, HorizontalAlignment.Right, HorizontalAlignment.Left)
        Knob.Margin = If(_On, New Thickness(0, 0, 3, 0), New Thickness(3, 0, 0, 0))
    End Sub

End Class
