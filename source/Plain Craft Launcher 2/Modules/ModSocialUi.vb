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
