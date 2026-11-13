Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

''' <summary>
''' 联机页面共用的少量 UI 构造函数，保证三个子页面观感一致。
''' </summary>
Public Module ModSocialUi

    ''' <summary>圆角头像；没有图片地址时显示昵称首字母。</summary>
    Public Function SocialAvatar(RelativePath As String, Size As Integer, Optional Name As String = "") As FrameworkElement
        Dim Host As New Grid With {.Width = Size, .Height = Size}
        Host.Clip = New RectangleGeometry(New Rect(0, 0, Size, Size), 6, 6)
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
            .Text = Text, .FontSize = 14, .FontWeight = FontWeights.SemiBold,
            .Margin = New Thickness(0, 0, 0, 2)}
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
            .CornerRadius = New CornerRadius(6), .Padding = New Thickness(14, 12, 14, 12),
            .Margin = New Thickness(0, 8, 0, 0),
            .BorderThickness = New Thickness(1),
            .BorderBrush = New SolidColorBrush(Color.FromArgb(24, 0, 0, 0)),
            .Background = New SolidColorBrush(Color.FromArgb(8, 0, 0, 0))}
    End Function

    ''' <summary>一行分隔线。</summary>
    Public Function SocialDivider() As Border
        Return New Border With {
            .Height = 1, .Margin = New Thickness(0, 14, 0, 0),
            .Background = New SolidColorBrush(Color.FromArgb(20, 0, 0, 0))}
    End Function

End Module
