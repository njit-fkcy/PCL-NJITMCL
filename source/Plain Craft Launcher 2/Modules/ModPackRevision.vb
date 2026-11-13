Imports System.IO
Imports Newtonsoft.Json.Linq

''' <summary>
''' 整合包修订(增量补丁)支持:
''' 记录本机安装的是哪个整合包的哪个 revision, 探测服务端修订,
''' 并按补丁清单在本地增删文件.
''' </summary>
Public Module ModPackRevision

    ''' <summary>标记文件相对实例目录的路径.</summary>
    Public Const MarkFile As String = "PCL\njitmcl-pack.json"

    ''' <summary>一个待更新的整合包实例.</summary>
    Public Class PackUpdateInfo
        Public ServerId As String = ""
        Public ServerName As String = ""
        Public PackId As String = ""
        Public PackName As String = ""
        Public InstanceName As String = ""
        Public InstancePath As String = ""
        Public PathIndie As String = ""
        Public LocalRevision As Integer
        Public RemoteRevision As Integer
        Public LocalSha256 As String = ""
        Public RemoteSha256 As String = ""
        ''' <summary>服务端声明的 Java 大版本, 0 表示未声明.</summary>
        Public JavaVersion As Integer
        Public Patches As JArray = New JArray()
    End Class

    ''' <summary>读取指定实例的修订标记, 没有则返回 Nothing.</summary>
    Public Function ReadMark(InstancePath As String) As JObject
        Try
            Dim path As String = IO.Path.Combine(InstancePath, MarkFile)
            If Not File.Exists(path) Then Return Nothing
            Return JObject.Parse(File.ReadAllText(path))
        Catch ex As Exception
            Logger.Warn(ex, "读取整合包修订标记失败: " & InstancePath)
            Return Nothing
        End Try
    End Function

    ''' <summary>写入修订标记.</summary>
    Public Sub WriteMark(InstancePath As String, PackId As String, ServerId As String,
                         Revision As Integer, PathIndie As String, Optional Sha256 As String = "",
                         Optional JavaVersion As Integer = 0)
        Try
            Dim dir As String = IO.Path.Combine(InstancePath, "PCL")
            Directory.CreateDirectory(dir)
            Dim obj As New JObject From {
                {"packId", PackId},
                {"serverId", ServerId},
                {"revision", Revision},
                {"sha256", If(Sha256, "").ToLowerInvariant()},
                {"pathIndie", PathIndie},
                {"installedAt", Date.Now.ToString("yyyy-MM-dd HH:mm:ss")}
            }
            '服务端声明了该整合包要求的 Java 大版本时才写入, 没有则保持自动选择
            If JavaVersion > 0 Then obj("javaVersion") = JavaVersion
            File.WriteAllText(IO.Path.Combine(dir, "njitmcl-pack.json"), obj.ToString())
        Catch ex As Exception
            Logger.Warn(ex, "写入整合包修订标记失败: " & InstancePath)
        End Try
    End Sub

    ''' <summary>扫描全部版本目录, 返回带修订标记的实例.</summary>
    Public Function ScanLocalMarks() As List(Of (InstancePath As String, InstanceName As String, Mark As JObject))
        Dim list As New List(Of (String, String, JObject))
        Try
            Dim root As String = IO.Path.Combine(McFolderSelected, "versions")
            If Not Directory.Exists(root) Then Return list
            For Each dir As String In Directory.GetDirectories(root)
                Dim mark As JObject = ReadMark(dir)
                If mark Is Nothing Then Continue For
                If String.IsNullOrWhiteSpace(mark("packId")?.ToString()) Then Continue For
                list.Add((dir, New DirectoryInfo(dir).Name, mark))
            Next
        Catch ex As Exception
            Logger.Warn(ex, "扫描整合包修订标记失败")
        End Try
        Return list
    End Function

    ''' <summary>
    ''' 对比服务端整合包 revision 与本机标记, 返回需要更新的实例.
    ''' 只有装过该整合包的机器才会出现在结果里.
    ''' </summary>
    Public Function FindUpdates(Servers As JArray) As List(Of PackUpdateInfo)
        Dim result As New List(Of PackUpdateInfo)
        If Servers Is Nothing Then Return result
        Dim locals = ScanLocalMarks()
        If locals.Count = 0 Then Return result
        For Each server As JToken In Servers
            Dim ServerObj As JObject = TryCast(server, JObject)
            If ServerObj Is Nothing Then Continue For
            Dim ServerId As String = If(ServerObj("id")?.ToString(), "")
            Dim ServerName As String = If(ServerObj("name")?.ToString(), "")
            Dim Packs As JArray = TryCast(ServerObj("modpacks"), JArray)
            If Packs Is Nothing Then Continue For
            For Each token As JToken In Packs
                Dim Pack As JObject = TryCast(token, JObject)
                If Pack Is Nothing Then Continue For
                Dim PackId As String = If(Pack("id")?.ToString(), "")
                If PackId = "" Then Continue For
                Dim RemoteRevision As Integer = If(Pack("revision")?.ToObject(Of Integer)(), 1)
                For Each local In locals
                    If If(local.Mark("packId")?.ToString(), "") <> PackId Then Continue For
                    Dim LocalServer As String = If(local.Mark("serverId")?.ToString(), "")
                    If LocalServer <> "" AndAlso LocalServer <> ServerId Then Continue For
                    Dim LocalRevision As Integer = If(local.Mark("revision")?.ToObject(Of Integer)(), 1)
                    '优先按包哈希比对: 发布工具改包后哈希一定会变, 比 revision 更可靠
                    Dim RemoteSha As String = If(Pack("sha256")?.ToString(), "").ToLowerInvariant()
                    Dim LocalSha As String = If(local.Mark("sha256")?.ToString(), "").ToLowerInvariant()
                    Dim NeedUpdate As Boolean
                    If RemoteSha <> "" AndAlso LocalSha <> "" Then
                        NeedUpdate = (RemoteSha <> LocalSha)
                    Else
                        NeedUpdate = RemoteRevision > LocalRevision
                    End If
                    If Not NeedUpdate Then Continue For
                    Dim patches As JArray = TryCast(Pack("patches"), JArray)
                    result.Add(New PackUpdateInfo With {
                        .ServerId = ServerId,
                        .ServerName = ServerName,
                        .PackId = PackId,
                        .PackName = If(Pack("name")?.ToString(), PackId),
                        .InstanceName = local.InstanceName,
                        .InstancePath = local.InstancePath,
                        .PathIndie = If(local.Mark("pathIndie")?.ToString(), local.InstancePath),
                        .LocalRevision = LocalRevision,
                        .RemoteRevision = RemoteRevision,
                        .LocalSha256 = LocalSha,
                        .RemoteSha256 = RemoteSha,
                        .JavaVersion = If(Pack("javaVersion")?.ToObject(Of Integer)(), 0),
                        .Patches = If(patches Is Nothing, New JArray(), patches)
                    })
                Next
            Next
        Next
        Return result
    End Function

    ''' <summary>本机一个能和社团整合包对上的实例, 可能没有修订标记.</summary>
    Public Class PackInstance
        Public ServerId As String = ""
        Public ServerName As String = ""
        Public PackId As String = ""
        Public PackName As String = ""
        Public InstanceName As String = ""
        Public InstancePath As String = ""
        ''' <summary>本机标记的 revision, 0 表示没有标记(旧方式安装).</summary>
        Public CurrentRevision As Integer = 0
        ''' <summary>服务端最新 revision.</summary>
        Public RemoteRevision As Integer = 1
    End Class

    ''' <summary>
    ''' 扫描本机实例, 返回能和在营服务器整合包对上的全部实例(含已登记与未登记).
    ''' 已登记的按标记里的 packId 认包, 未登记的才用实例名宽松匹配并尝试自动识别版本.
    ''' </summary>
    Public Function FindPackInstances(Servers As JArray) As List(Of PackInstance)
        Dim result As New List(Of PackInstance)
        If Servers Is Nothing Then Return result
        Try
            Dim root As String = IO.Path.Combine(McFolderSelected, "versions")
            If Not Directory.Exists(root) Then Return result
            For Each dir As String In Directory.GetDirectories(root)
                Dim Mark As JObject = ReadMark(dir)
                Dim instName As String = New DirectoryInfo(dir).Name
                For Each token As JToken In Servers
                    Dim ServerObj As JObject = TryCast(token, JObject)
                    If ServerObj Is Nothing Then Continue For
                    '已结档服务器不会再有修订, 没必要提示
                    If Equals(ServerObj("archived")?.ToObject(Of Boolean)(), True) Then Continue For
                    Dim Packs As JArray = TryCast(ServerObj("modpacks"), JArray)
                    If Packs Is Nothing OrElse Packs.Count = 0 Then Continue For
                    Dim ServerId As String = If(ServerObj("id")?.ToString(), "")
                    Dim ServerName As String = If(ServerObj("name")?.ToString(), "")

                    If Mark IsNot Nothing Then
                        If If(Mark("serverId")?.ToString(), "") <> "" AndAlso
                           If(Mark("serverId")?.ToString(), "") <> ServerId Then Continue For
                        '已登记实例: 按标记 packId 在全部包中匹配（多包服务器原只认第一个包, 会漏识别）
                        Dim Matched As JObject = Nothing
                        For Each PackToken As JToken In Packs
                            Dim Pack As JObject = TryCast(PackToken, JObject)
                            If Pack IsNot Nothing AndAlso
                               If(Mark("packId")?.ToString(), "") = If(Pack("id")?.ToString(), "") Then
                                Matched = Pack
                                Exit For
                            End If
                        Next
                        If Matched Is Nothing Then Continue For
                        Dim PackId As String = If(Matched("id")?.ToString(), "")
                        Dim RemoteJava As Integer = If(Matched("javaVersion")?.ToObject(Of Integer)(), 0)
                        Dim LocalRevision As Integer = If(Mark("revision")?.ToObject(Of Integer)(), 1)
                        '老标记缺少 Java 要求时按服务端补写, 不必等下一次整包更新
                        If RemoteJava > 0 AndAlso If(Mark("javaVersion")?.ToObject(Of Integer)(), 0) <> RemoteJava Then
                            WriteMark(dir, PackId, ServerId, LocalRevision,
                                      If(Mark("pathIndie")?.ToString(), dir),
                                      If(Mark("sha256")?.ToString(), ""), RemoteJava)
                            Logger.Info("已补写整合包 Java 要求: " & instName & " -> Java " & RemoteJava)
                        End If
                        result.Add(New PackInstance With {
                            .ServerId = ServerId,
                            .ServerName = ServerName,
                            .PackId = PackId,
                            .PackName = If(Matched("name")?.ToString(), ""),
                            .InstanceName = instName,
                            .InstancePath = dir,
                            .CurrentRevision = LocalRevision,
                            .RemoteRevision = If(Matched("revision")?.ToObject(Of Integer)(), 1)
                        })
                        Exit For
                    Else
                        If Not MatchInstance(instName, ServerId, ServerName) Then Continue For
                        '没有标记的实例: 逐个包用服务端清单自动比对本地文件, 匹配就自动补标记
                        Dim AutoPack As JObject = Nothing
                        Dim AutoRevision As Integer = 0
                        For Each PackToken As JToken In Packs
                            Dim Pack As JObject = TryCast(PackToken, JObject)
                            If Pack Is Nothing Then Continue For
                            AutoRevision = MatchLocalRevision(dir, Pack)
                            If AutoRevision > 0 Then
                                AutoPack = Pack
                                Exit For
                            End If
                        Next
                        If AutoPack Is Nothing Then AutoPack = TryCast(Packs(0), JObject) '清单比对全失败时按第一个包宽松登记
                        If AutoPack Is Nothing Then Continue For
                        Dim PackId As String = If(AutoPack("id")?.ToString(), "")
                        Dim RemoteJava As Integer = If(AutoPack("javaVersion")?.ToObject(Of Integer)(), 0)
                        Dim LocalRevision As Integer = AutoRevision
                        If AutoRevision > 0 Then
                            Dim RemoteRev As Integer = If(AutoPack("revision")?.ToObject(Of Integer)(), 1)
                            Dim RemoteSha As String = If(AutoPack("sha256")?.ToString(), "").ToLowerInvariant()
                            '只有确认是最新版才写哈希, 旧版留空以便按 revision 继续走补丁链
                            WriteMark(dir, PackId, ServerId, AutoRevision, dir,
                                      If(AutoRevision = RemoteRev, RemoteSha, ""), RemoteJava)
                            Logger.Info("自动识别整合包版本: " & instName & " -> revision " & AutoRevision)
                        End If
                        result.Add(New PackInstance With {
                            .ServerId = ServerId,
                            .ServerName = ServerName,
                            .PackId = PackId,
                            .PackName = If(AutoPack("name")?.ToString(), ""),
                            .InstanceName = instName,
                            .InstancePath = dir,
                            .CurrentRevision = LocalRevision,
                            .RemoteRevision = If(AutoPack("revision")?.ToObject(Of Integer)(), 1)
                        })
                        Exit For
                    End If
                Next
            Next
        Catch ex As Exception
            Logger.Warn(ex, "扫描整合包实例失败")
        End Try
        Return result
    End Function

    ''' <summary>
    ''' 用服务端清单自动判断本地实例属于哪个 revision.
    ''' 从最新版往前反推, 返回第一个清单文件全部匹配(存在且大小一致)的版本, 找不到返回 0.
    ''' </summary>
    Private Function MatchLocalRevision(InstancePath As String, Pack As JObject) As Integer
        Dim Manifest As JArray = TryCast(Pack("manifest"), JArray)
        If Manifest Is Nothing OrElse Manifest.Count = 0 Then Return 0
        Dim RemoteRevision As Integer = If(Pack("revision")?.ToObject(Of Integer)(), 1)
        Dim Snapshot As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase)
        For Each Item As JToken In Manifest
            Dim Rel As String = If(Item("path")?.ToString(), "").Replace("/"c, IO.Path.DirectorySeparatorChar).Trim()
            If Rel = "" Then Continue For
            Snapshot(Rel) = If(Item("size")?.ToObject(Of Long)(), 0L)
        Next
        If Snapshot.Count = 0 Then Return 0
        Dim Root As String = GuessInstanceRoot(InstancePath)
        Dim Rev As Integer = RemoteRevision
        While Rev >= 1
            If Rev < RemoteRevision Then
                Dim Patch As JObject = FindPatchFor(Pack, Rev)
                If Patch Is Nothing Then Exit While
                '撤销 Rev 到 Rev+1 的变化: 去掉这级新增的文件, 把当时删掉的文件加回来
                For Each AddToken As JToken In If(TryCast(Patch("add"), JArray), New JArray())
                    Dim Rel As String = If(AddToken("path")?.ToString(), "").Replace("/"c, IO.Path.DirectorySeparatorChar).Trim()
                    If Rel <> "" Then Snapshot.Remove(Rel)
                Next
                For Each DelToken As JToken In If(TryCast(Patch("delete"), JArray), New JArray())
                    Dim Rel As String = DelToken.ToString().Replace("/"c, IO.Path.DirectorySeparatorChar).Trim()
                    If Rel <> "" Then Snapshot(Rel) = -1
                Next
            End If
            If SnapshotMatches(Root, Snapshot) Then Return Rev
            Rev -= 1
        End While
        Return 0
    End Function

    ''' <summary>清单里的文件是否都在本地存在且大小一致, -1 表示不校验大小.</summary>
    Private Function SnapshotMatches(Root As String, Snapshot As Dictionary(Of String, Long)) As Boolean
        For Each Pair In Snapshot
            Dim Full As String = IO.Path.Combine(Root, Pair.Key)
            If Not File.Exists(Full) Then Return False
            If Pair.Value >= 0 Then
                Try
                    If New FileInfo(Full).Length <> Pair.Value Then Return False
                Catch
                    Return False
                End Try
            End If
        Next
        Return True
    End Function

    ''' <summary>实例内容根目录: 有 mods / saves / options.txt 视为版本隔离.</summary>
    Private Function GuessInstanceRoot(InstancePath As String) As String
        If Directory.Exists(IO.Path.Combine(InstancePath, "mods")) OrElse
           Directory.Exists(IO.Path.Combine(InstancePath, "saves")) OrElse
           File.Exists(IO.Path.Combine(InstancePath, "options.txt")) Then
            Return InstancePath
        End If
        Return McFolderSelected.TrimEnd("\"c)
    End Function

    ''' <summary>找到 from = FromRevision 的那条补丁.</summary>
    Private Function FindPatchFor(Pack As JObject, FromRevision As Integer) As JObject
        Dim Patches As JArray = TryCast(Pack("patches"), JArray)
        If Patches Is Nothing Then Return Nothing
        For Each Token As JToken In Patches
            Dim Patch As JObject = TryCast(Token, JObject)
            If Patch Is Nothing Then Continue For
            If If(Patch("from")?.ToObject(Of Integer)(), -1) = FromRevision Then Return Patch
        Next
        Return Nothing
    End Function

    ''' <summary>实例名与服务器名或 ID 的宽松匹配, 忽略大小写与空格.</summary>
    Private Function MatchInstance(InstanceName As String, ServerId As String, ServerName As String) As Boolean
        Dim inst As String = NormalizeName(InstanceName)
        '过短的实例名容易误匹配, 直接放弃
        If inst.Length < 4 Then Return False
        Dim name As String = NormalizeName(ServerName)
        If name.Length >= 4 AndAlso (inst.Contains(name) OrElse name.Contains(inst)) Then Return True
        Dim id As String = NormalizeName(ServerId)
        If id.Length >= 4 AndAlso inst.Contains(id) Then Return True
        Return False
    End Function

    Private Function NormalizeName(Value As String) As String
        If String.IsNullOrWhiteSpace(Value) Then Return ""
        Return Value.Replace(" ", "").Replace("　", "").ToLowerInvariant()
    End Function

    ''' <summary>
    ''' 应用一次修订: 先把新增文件下载到暂存区并校验, 再删除旧文件, 最后落盘新增文件.
    ''' 任一步失败都不会留下半成品, 标记文件也不会被改写.
    ''' 路径一律限制在实例目录内, 越界条目直接跳过.
    ''' Progress 回调(说明文字, 0 到 1 的进度)只用于展示, 抛错会被吞掉.
    ''' </summary>
    Public Function ApplyUpdate(Info As PackUpdateInfo, Optional Progress As Action(Of String, Double) = Nothing) As Integer
        Dim Report As Action(Of String, Double) =
            Sub(Message As String, Percent As Double)
                If Progress Is Nothing Then Return
                Try
                    Progress(Message, Math.Max(0, Math.Min(1, Percent)))
                Catch
                    ' 进度回调异常不影响更新
                End Try
            End Sub
        Dim revision As Integer = Info.LocalRevision
        Dim root As String = If(String.IsNullOrWhiteSpace(Info.PathIndie), Info.InstancePath, Info.PathIndie)
        Dim rootFull As String = IO.Path.GetFullPath(root)
        Dim staging As String = IO.Path.Combine(IO.Path.GetTempPath(), "njitmcl_patch_" & Guid.NewGuid().ToString("N"))
        Dim pending As New List(Of (Target As String, Temp As String))
        Dim deleted As Integer = 0
        Dim added As Integer = 0
        Try
            Directory.CreateDirectory(staging)

            '先数一遍要处理多少文件, 用于进度展示
            Dim addTotal As Integer = 0
            Dim deleteTotal As Integer = 0
            For Each token As JToken In Info.Patches
                Dim patch As JObject = TryCast(token, JObject)
                If patch Is Nothing Then Continue For
                Dim addsCount As JArray = TryCast(patch("add"), JArray)
                If addsCount IsNot Nothing Then addTotal += addsCount.Count
                Dim deletesCount As JArray = TryCast(patch("delete"), JArray)
                If deletesCount IsNot Nothing Then deleteTotal += deletesCount.Count
            Next
            Dim AddTotalSafe As Integer = Math.Max(addTotal, 1)
            Dim DeleteTotalSafe As Integer = Math.Max(deleteTotal, 1)
            Report("正在准备更新", 0)

            '第一步: 下载并校验全部新增文件, 占总进度 0 到 80%
            Dim walkRevision As Integer = revision
            Dim addDone As Integer = 0
            For Each token As JToken In Info.Patches
                Dim patch As JObject = TryCast(token, JObject)
                If patch Is Nothing Then Continue For
                If If(patch("from")?.ToObject(Of Integer)(), -1) <> walkRevision Then Continue For
                Dim adds As JArray = TryCast(patch("add"), JArray)
                If adds IsNot Nothing Then
                    For Each item As JToken In adds
                        Dim obj As JObject = TryCast(item, JObject)
                        If obj Is Nothing Then Continue For
                        Dim rel As String = If(obj("path")?.ToString(), "").Replace("/"c, IO.Path.DirectorySeparatorChar).Trim()
                        Dim expect As String = If(obj("sha256")?.ToString(), "").ToLowerInvariant()
                        Dim expectSize As Long = If(obj("size")?.ToObject(Of Long)(), 0L)
                        Dim urls As New List(Of String)
                        '优先用服务端给的镜像列表, 再补上主地址, 任意一个可用即可
                        Dim sourceList As JArray = TryCast(obj("sources"), JArray)
                        If sourceList IsNot Nothing Then
                            For Each source As JToken In sourceList
                                Dim mirror As String = If(source("url")?.ToString(), "")
                                If mirror.StartsWith("https://", StringComparison.OrdinalIgnoreCase) AndAlso
                                   Not urls.Contains(mirror) Then urls.Add(mirror)
                            Next
                        End If
                        Dim primary As String = If(obj("url")?.ToString(), "")
                        If primary.StartsWith("https://", StringComparison.OrdinalIgnoreCase) AndAlso
                           Not urls.Contains(primary) Then urls.Add(primary)
                        If rel = "" OrElse urls.Count = 0 Then Continue For
                        Dim full As String = IO.Path.GetFullPath(IO.Path.Combine(rootFull, rel))
                        '带分隔符比较，防止兄弟目录（..\Minecraft2\x）通过校验
                        If Not full.StartsWith(rootFull.TrimEnd("\"c) & "\", StringComparison.OrdinalIgnoreCase) AndAlso
                           Not String.Equals(full, rootFull.TrimEnd("\"c), StringComparison.OrdinalIgnoreCase) Then Continue For
                        Dim tmp As String = IO.Path.Combine(staging,
                            Guid.NewGuid().ToString("N") & IO.Path.GetExtension(full))
                        Dim displayName As String = IO.Path.GetFileName(full)
                        DownloadFile(urls, tmp, expectSize, displayName, Report,
                                     0.8 * addDone / AddTotalSafe, 0.8 / AddTotalSafe)
                        addDone += 1
                        Report("已下载 " & addDone & "/" & AddTotalSafe & " 个文件",
                               0.8 * addDone / AddTotalSafe)
                        If expect <> "" AndAlso Sha256Of(tmp) <> expect Then
                            Throw New Exception("补丁文件校验失败: " & rel)
                        End If
                        pending.Add((full, tmp))
                    Next
                End If
                walkRevision = If(patch("to")?.ToObject(Of Integer)(), walkRevision)
            Next

            '第二步: 删除旧文件, 占总进度 80 到 90%
            Report("正在校验并移除旧文件, 共 " & deleteTotal & " 个", 0.8)
            Dim deleteDone As Integer = 0
            Dim appliedAny As Boolean = False
            For Each token As JToken In Info.Patches
                Dim patch As JObject = TryCast(token, JObject)
                If patch Is Nothing Then Continue For
                If If(patch("from")?.ToObject(Of Integer)(), -1) <> revision Then Continue For
                appliedAny = True
                Dim deletes As JArray = TryCast(patch("delete"), JArray)
                If deletes IsNot Nothing Then
                    For Each item As JToken In deletes
                        Dim rel As String = item.ToString().Replace("/"c, IO.Path.DirectorySeparatorChar).Trim()
                        If rel = "" Then Continue For
                        Try
                            Dim full As String = IO.Path.GetFullPath(IO.Path.Combine(rootFull, rel))
                            If Not full.StartsWith(rootFull.TrimEnd("\"c) & "\", StringComparison.OrdinalIgnoreCase) Then Continue For
                            If File.Exists(full) Then
                                File.Delete(full)
                                deleted += 1
                            End If
                        Catch ex As Exception
                            Logger.Warn(ex, "删除整合包文件失败: " & rel)
                        End Try
                        deleteDone += 1
                        Report("正在移除旧文件 " & deleteDone & "/" & DeleteTotalSafe,
                               0.8 + 0.1 * deleteDone / DeleteTotalSafe)
                    Next
                End If
                revision = If(patch("to")?.ToObject(Of Integer)(), revision)
            Next

            '第三步: 落盘新增文件, 占总进度 90 到 100%
            Dim moveTotal As Integer = pending.Count
            Dim MoveTotalSafe As Integer = Math.Max(moveTotal, 1)
            For Each item In pending
                Report("正在写入文件 " & (added + 1) & "/" & MoveTotalSafe,
                       0.9 + 0.1 * added / MoveTotalSafe)
                Directory.CreateDirectory(IO.Path.GetDirectoryName(item.Target))
                If File.Exists(item.Target) Then File.Delete(item.Target)
                File.Move(item.Temp, item.Target)
                added += 1
            Next
            pending.Clear()

            '服务端只更新了哈希却没有可用补丁时, 不能假装更新成功
            If Not appliedAny Then
                '整包被重新覆盖但修订号没有前进时, 本地内容其实已是最新:
                '只同步标记里的哈希, 避免每次启动都报"没有可用的增量补丁"。
                '但哈希不同说明内容确实变了（revision 没动时）：绝不能把新哈希写到旧内容上,
                '否则下次哈希比对永久相等, 用户再也收不到更新
                If revision >= Info.RemoteRevision Then
                    If Info.LocalSha256 <> "" AndAlso Info.RemoteSha256 <> "" AndAlso
                       Info.LocalSha256 <> Info.RemoteSha256 Then
                        Throw New Exception("服务端更新了整合包内容但没有提供补丁, 请重新下载整合包")
                    End If
                    WriteMark(Info.InstancePath, Info.PackId, Info.ServerId, Info.RemoteRevision, rootFull, Info.RemoteSha256, Info.JavaVersion)
                    Report("已是最新修订, 仅同步整包哈希", 1)
                    Logger.Info("整合包 " & Info.PackId & " 无可用补丁, 本地修订 " & revision &
                                " 不小于远端 " & Info.RemoteRevision & ", 已同步标记哈希")
                    Return Info.RemoteRevision
                End If
                Throw New Exception("服务端没有提供可用的增量补丁, 请重新下载整合包")
            End If

            WriteMark(Info.InstancePath, Info.PackId, Info.ServerId, revision, rootFull, Info.RemoteSha256, Info.JavaVersion)
            Report("完成, 移除 " & deleted & " 个, 新增 " & added & " 个文件", 1)
            Logger.Info("整合包 " & Info.PackId & " 修订到 revision " & revision & ", 删除 " & deleted & ", 新增 " & added)
            Return revision
        Finally
            Try
                If Directory.Exists(staging) Then Directory.Delete(staging, True)
            Catch
                ' 清理失败忽略
            End Try
        End Try
    End Function

    ''' <summary>
    ''' 按候选地址依次下载, 单个源超时或速度过慢就换下一个源, 支持断点续传.
    ''' 全部源都失败才抛错, 避免某一个源被墙就整个修订卡死.
    ''' </summary>
    Private Sub DownloadFile(Urls As List(Of String), Target As String, ExpectedSize As Long, DisplayName As String,
                             Report As Action(Of String, Double), PercentFrom As Double, PercentSpan As Double)
        Dim LastError As Exception = Nothing
        For Index As Integer = 0 To Urls.Count - 1
            Dim Url As String = Urls(Index)
            Try
                DownloadFileSingle(Url, Target, ExpectedSize, DisplayName, Report,
                                   PercentFrom, PercentSpan, Index + 1, Urls.Count)
                Return
            Catch ex As Exception
                LastError = ex
                Logger.Warn(ex, "补丁下载失败, 尝试下一个源: " & Url)
                '保留半成品文件给下一个源断点续传；下一个源会校验 206, 不支持则整文件重下
            End Try
        Next
        Throw New Exception("补丁下载失败(已尝试 " & Urls.Count & " 个源): " &
                            If(LastError Is Nothing, "无可用地址", LastError.Message))
    End Sub

    ''' <summary>单源下载, 连接 15 秒、读写 30 秒无响应即视为失败.</summary>
    Private Sub DownloadFileSingle(Url As String, Target As String, ExpectedSize As Long, DisplayName As String,
                                   Report As Action(Of String, Double), PercentFrom As Double, PercentSpan As Double,
                                   SourceIndex As Integer, SourceCount As Integer)
        Dim Request As Net.HttpWebRequest = DirectCast(Net.WebRequest.Create(Url), Net.HttpWebRequest)
        Request.UserAgent = "PCL2/" & VersionBaseName
        Request.Timeout = 15000
        Request.ReadWriteTimeout = 30000
        Request.AllowAutoRedirect = True
        Dim StartAt As Long = 0
        If File.Exists(Target) Then
            StartAt = New FileInfo(Target).Length
            If ExpectedSize > 0 AndAlso StartAt >= ExpectedSize Then Return
            If StartAt > 0 Then Request.AddRange(StartAt)
        End If
        Using Response As Net.HttpWebResponse = DirectCast(Request.GetResponse(), Net.HttpWebResponse)
            '服务器忽略 Range 请求返回 200 时, 半成品不可作为续传基线, 改为整文件重下
            If StartAt > 0 AndAlso Response.StatusCode <> Net.HttpStatusCode.PartialContent Then
                StartAt = 0
            End If
            Dim Total As Long = ExpectedSize
            If Total <= 0 Then Total = Response.ContentLength + StartAt
            Using Input As IO.Stream = Response.GetResponseStream()
                Using Output As New IO.FileStream(Target,
                                                  If(StartAt > 0, IO.FileMode.Append, IO.FileMode.Create),
                                                  IO.FileAccess.Write)
                    Dim Buffer(65535) As Byte
                    Dim ReadTotal As Long = StartAt
                    Dim LastReport As Long = -1000
                    Do
                        Dim Count As Integer = Input.Read(Buffer, 0, Buffer.Length)
                        If Count <= 0 Then Exit Do
                        Output.Write(Buffer, 0, Count)
                        ReadTotal += Count
                        If Total > 0 Then
                            Dim NowTicks As Long = Environment.TickCount
                            If NowTicks - LastReport > 120 Then
                                LastReport = NowTicks
                                Report($"正在下载 {DisplayName}（源 {SourceIndex}/{SourceCount}）",
                                       PercentFrom + PercentSpan * Math.Min(1, ReadTotal / Total))
                            End If
                        End If
                    Loop
                    Output.Flush()
                    If Total > 0 AndAlso ReadTotal < Total Then
                        Throw New Exception("下载不完整: " & ReadTotal & "/" & Total)
                    End If
                End Using
            End Using
        End Using
    End Sub

    ''' <summary>计算文件的 SHA256, 小写十六进制.</summary>
    Private Function Sha256Of(Path As String) As String
        Using stream As IO.FileStream = IO.File.OpenRead(Path)
            Using sha As Security.Cryptography.SHA256 = Security.Cryptography.SHA256.Create()
                Return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant()
            End Using
        End Using
    End Function

End Module
