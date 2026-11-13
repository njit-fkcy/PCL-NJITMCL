Imports System.Text.RegularExpressions
Imports Newtonsoft.Json.Linq

''' <summary>
''' 插件市场的一个插件条目。
''' </summary>
Public Class NjitPlugin
    Public Id As String = ""
    Public Name As String = ""
    Public Version As String = ""
    Public Entry As String = ""
    Public MinLauncherVersion As String = ""
    Public Description As String = ""
    Public Author As String = ""
    Public Homepage As String = ""
    Public DownloadUrl As String = ""
    ''' <summary>多源下载地址，按中控给的顺序尝试（校园内网优先）。</summary>
    Public DownloadUrls As New List(Of String)
    Public FileName As String = ""
    Public Size As Long = 0
End Class

''' <summary>
''' NJIT MCL 插件市场：列表来自官网中控，下载走启动器原生下载引擎。
''' 插件安装在 PCL\Plugins\&lt;id&gt;\&lt;版本&gt;\，与启动器本体文件完全隔离。
''' </summary>
Public Module ModPlugin

    Private ReadOnly IdPattern As New Regex("^[a-z0-9][a-z0-9-]{1,39}$")
    Private ReadOnly DownloadPattern As New Regex("^https://github\.com/[^/]+/[^/]+/releases/download/")

    ''' <summary>
    ''' 插件安装根目录，位于启动器可执行文件同级的 Plugins 文件夹。
    ''' </summary>
    Public ReadOnly Property PluginsFolder As String
        Get
            Return IO.Path.GetDirectoryName(PathExe) & "\Plugins\"
        End Get
    End Property

    ''' <summary>
    ''' 从官网中控获取插件列表。
    ''' </summary>
    Public Function FetchList() As List(Of NjitPlugin)
        Dim Raw As String = NetRequestByClient(ModNJIT.ClubHomepage & "/api/plugins", RequireJson:=True, Timeout:=15000)
        Dim Root As JObject = Raw.DeserializeJson()
        If Root Is Nothing Then Throw New Exception("官网返回的插件列表为空")
        If Root("success") IsNot Nothing AndAlso Not Root("success").ToObject(Of Boolean)() Then
            Throw New Exception(If(Root("message")?.ToString(), "插件列表暂时不可用"))
        End If
        Dim Result As New List(Of NjitPlugin)
        Dim Items = TryCast(Root("plugins"), JArray)
        If Items Is Nothing Then Return Result
        For Each Token As JToken In Items
            Dim Obj = TryCast(Token, JObject)
            If Obj Is Nothing Then Continue For
            Dim Item As New NjitPlugin With {
                .Id = If(Obj("id")?.ToString(), "").Trim().ToLowerInvariant(),
                .Name = If(Obj("name")?.ToString(), "").Trim(),
                .Version = If(Obj("version")?.ToString(), "").Trim(),
                .Entry = If(Obj("entry")?.ToString(), "").Trim().Replace("\"c, "/"c),
                .MinLauncherVersion = If(Obj("minLauncherVersion")?.ToString(), "").Trim(),
                .Description = If(Obj("description")?.ToString(), "").Trim(),
                .Author = If(Obj("author")?.ToString(), "").Trim(),
                .Homepage = If(Obj("homepage")?.ToString(), "").Trim(),
                .DownloadUrl = If(Obj("downloadUrl")?.ToString(), "").Trim(),
                .FileName = If(Obj("fileName")?.ToString(), "").Trim()
            }
            Long.TryParse(If(Obj("size")?.ToString(), "0"), Item.Size)
            ' 客户端再校验一次，避免信任被篡改的列表
            If Not IdPattern.IsMatch(Item.Id) Then Continue For
            If Item.Name = "" OrElse Item.Version = "" OrElse Item.Description = "" Then Continue For
            If Not Item.Entry.ToLowerInvariant().EndsWith(".exe") OrElse Item.Entry.Contains("..") Then Continue For
            If Not DownloadPattern.IsMatch(Item.DownloadUrl) Then Continue For
            If Item.Size <= 0 OrElse Item.Size > 200L * 1024 * 1024 Then Continue For
            If Not MeetsMinimum(Item.MinLauncherVersion) Then Continue For
            ' 多源：中控给了 downloadUrls 就用它，否则回退到单一 GitHub 直链
            Dim urls = TryCast(Obj("downloadUrls"), JArray)
            If urls IsNot Nothing Then
                For Each urlToken As JToken In urls
                    Dim url = If(urlToken?.ToString(), "").Trim()
                    If url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) AndAlso
                       Not Item.DownloadUrls.Contains(url) Then Item.DownloadUrls.Add(url)
                Next
            End If
            If Item.DownloadUrls.Count = 0 Then Item.DownloadUrls.Add(Item.DownloadUrl)
            Result.Add(Item)
        Next
        Return Result
    End Function

    ''' <summary>
    ''' 判断当前启动器版本是否满足插件要求。
    ''' </summary>
    Public Function MeetsMinimum(Required As String) As Boolean
        If String.IsNullOrWhiteSpace(Required) Then Return True
        Dim Need As Integer() = VersionTuple(Required)
        Dim Have As Integer() = VersionTuple(VersionBaseName)
        For i = 0 To 3
            If Have(i) > Need(i) Then Return True
            If Have(i) < Need(i) Then Return False
        Next
        Return True
    End Function

    Private Function VersionTuple(Value As String) As Integer()
        Dim Result(3) As Integer
        Dim Matches = Regex.Matches(If(Value, ""), "\d+")
        For i = 0 To Math.Min(3, Matches.Count - 1)
            Integer.TryParse(Matches(i).Value, Result(i))
        Next
        Return Result
    End Function

    ''' <summary>
    ''' 插件的安装目录，以 \ 结尾。
    ''' </summary>
    Public Function GetInstallFolder(Plugin As NjitPlugin) As String
        Return PluginsFolder & Plugin.Id & "\" & SafeFolderName(Plugin.Version) & "\"
    End Function

    ''' <summary>
    ''' 获取插件入口程序的完整路径。
    ''' </summary>
    Public Function GetEntryPath(Plugin As NjitPlugin) As String
        Return GetInstallFolder(Plugin) & Plugin.Entry.Replace("/"c, IO.Path.DirectorySeparatorChar)
    End Function

    Public Function IsInstalled(Plugin As NjitPlugin) As Boolean
        Return FileUtils.Exists(GetEntryPath(Plugin))
    End Function

    ''' <summary>
    ''' 本地已安装的最高版本号；没有安装时返回空字符串。
    ''' </summary>
    Public Function GetInstalledVersion(Plugin As NjitPlugin) As String
        Dim Root As String = PluginsFolder & Plugin.Id & "\"
        If Not DirectoryUtils.Exists(Root) Then Return ""
        Dim Folders As String() = {}
        Try
            Folders = IO.Directory.GetDirectories(Root)
        Catch ex As Exception
            Logger.Warn(ex, "读取插件安装目录失败：" & Root)
            Return ""
        End Try
        Dim Best As String = ""
        For Each Folder As String In Folders
            Dim Version As String = IO.Path.GetFileName(Folder)
            Dim JsonPath As String = IO.Path.Combine(Folder, "plugin.json")
            If FileUtils.Exists(JsonPath) Then
                Try
                    Dim Obj As JObject = JObject.Parse(FileUtils.ReadAsString(JsonPath))
                    Dim Value As String = If(Obj("version")?.ToString(), "").Trim()
                    If Value <> "" Then Version = Value
                Catch
                    'plugin.json 损坏时退回目录名
                End Try
            End If
            If Version <> "" AndAlso CompareVersion(Version, Best) > 0 Then Best = Version
        Next
        Return Best
    End Function

    ''' <summary>
    ''' 比较两个版本号：Left 较新返回 1，较旧返回 -1，相同返回 0。
    ''' </summary>
    Public Function CompareVersion(Left As String, Right As String) As Integer
        Dim A As Integer() = VersionTuple(Left)
        Dim B As Integer() = VersionTuple(Right)
        For i = 0 To 3
            If A(i) > B(i) Then Return 1
            If A(i) < B(i) Then Return -1
        Next
        Return 0
    End Function

    ''' <summary>
    ''' 已安装但低于市场上架版本时返回 True。
    ''' </summary>
    Public Function NeedsUpdate(Plugin As NjitPlugin) As Boolean
        Dim InstalledVersion As String = GetInstalledVersion(Plugin)
        Return InstalledVersion <> "" AndAlso CompareVersion(Plugin.Version, InstalledVersion) > 0
    End Function

    ''' <summary>
    ''' 删除该插件的全部版本目录；插件正在运行时文件被占用，删除后会检测残留并报错。
    ''' </summary>
    Public Sub Uninstall(Plugin As NjitPlugin)
        Dim EntryPath As String = GetEntryPath(Plugin)
        '先确认入口程序未被占用，避免删到一半留下残缺目录
        If FileUtils.Exists(EntryPath) Then
            Try
                Using Stream As New IO.FileStream(EntryPath, IO.FileMode.Open, IO.FileAccess.ReadWrite, IO.FileShare.None)
                End Using
            Catch ex As IO.IOException
                Throw New Exception("插件正在运行，请先关闭插件后再卸载")
            End Try
        End If
        Dim Root As String = PluginsFolder & Plugin.Id & "\"
        DirectoryUtils.Delete(Root)
        If DirectoryUtils.Exists(Root) Then Throw New Exception("插件文件正被占用，请先关闭插件后再卸载")
    End Sub

    ''' <summary>
    ''' 启动已安装的插件，返回是否成功唤起。
    ''' </summary>
    Public Function Launch(Plugin As NjitPlugin) As Boolean
        Dim EntryPath As String = GetEntryPath(Plugin)
        If Not FileUtils.Exists(EntryPath) Then Return False
        Process.Start(New ProcessStartInfo With {
            .FileName = EntryPath,
            .WorkingDirectory = GetInstallFolder(Plugin),
            .UseShellExecute = True})
        Return True
    End Function

    ''' <summary>
    ''' 下载并安装插件；需要在后台线程调用，失败时抛出异常。
    ''' </summary>
    Public Sub Install(Plugin As NjitPlugin)
        DirectoryUtils.Create(PathTemp & "PluginInstall\")
        Dim ZipFile As String = PathTemp & "PluginInstall\" & Plugin.Id & ".zip"
        Dim TempRoot As String = PathTemp & "PluginInstall\" & Plugin.Id & "\"
        Dim Tasks As New List(Of NetFile) From {New NetFile(Plugin.DownloadUrls, ZipFile)}
        Dim Downloader As New LoaderDownload("下载插件 " & Plugin.Name, Tasks)
        Dim Installer As New LoaderTask(Of Integer, Integer)("安装插件 " & Plugin.Name,
            Sub(Task) InstallCore(Plugin, ZipFile, TempRoot, Task))
        Dim Combo As New LoaderCombo(Of String)("安装插件 " & Plugin.Name,
            New List(Of LoaderBase) From {Downloader, Installer})
        Combo.Start()
        LoaderTaskbarAdd(Combo)
        RunInUi(Sub() FrmMain?.BtnExtraDownload.ShowRefresh())
        Combo.WaitForExit()
        If Combo.State <> LoadState.Finished OrElse Combo.Error IsNot Nothing Then
            Throw New Exception(If(Combo.Error?.Message, "下载或解压插件失败"))
        End If
    End Sub

    Private Sub InstallCore(Plugin As NjitPlugin, ZipPath As String, TempRoot As String, Task As LoaderTask(Of Integer, Integer))
        Task.Progress = 0.05
        DirectoryUtils.Delete(TempRoot)
        FileUtils.ExtractToDirectory(ZipPath, TempRoot, Task.CreateCancellationToken(),
            Task.CreateSyncProgressProvider(FromPercentage:=0.05, ToPercentage:=0.6))
        ' 兼容 zip 内多套一层文件夹的情况
        Dim SourceRoot As String = TempRoot
        If Not FileUtils.Exists(TempRoot & "plugin.json") Then
            Dim SubFolders = IO.Directory.GetDirectories(TempRoot)
            If SubFolders.Length = 1 AndAlso FileUtils.Exists(SubFolders(0) & "\plugin.json") Then
                SourceRoot = SubFolders(0) & "\"
            End If
        End If
        If Not FileUtils.Exists(SourceRoot & "plugin.json") Then
            Throw New Exception("插件包内缺少 plugin.json")
        End If
        Dim EntryFile As String = SourceRoot & Plugin.Entry.Replace("/"c, IO.Path.DirectorySeparatorChar)
        If Not FileUtils.Exists(EntryFile) Then
            Throw New Exception("插件包内缺少入口程序：" & Plugin.Entry)
        End If
        Task.Progress = 0.65
        Dim Target As String = GetInstallFolder(Plugin)
        DirectoryUtils.Delete(Target)
        DirectoryUtils.Create(Target)
        DirectoryUtils.Copy(SourceRoot, Target)
        Task.Progress = 0.95
        If Not FileUtils.Exists(GetEntryPath(Plugin)) Then
            Throw New Exception("插件安装后缺少入口程序")
        End If
        DirectoryUtils.Delete(TempRoot)
        FileUtils.Delete(ZipPath)
        Task.Progress = 1
    End Sub

    Private Function SafeFolderName(Value As String) As String
        Dim Result As String = Regex.Replace(If(Value, ""), "[^0-9A-Za-z._-]", "_")
        If Result = "" Then Result = "unknown"
        Return Result
    End Function

End Module
