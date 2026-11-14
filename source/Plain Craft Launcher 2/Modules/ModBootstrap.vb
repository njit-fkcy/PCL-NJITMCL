Imports System.IO
Imports System.Reflection

''' <summary>
''' 启动引导：把内嵌在 exe 里的依赖库释放到 PCL 文件夹，并从那里加载。
''' 目的：根目录只留一个 exe（加一个 .NET 强制的 .exe.config），其余文件都在 PCL\；
''' 即使只剩一个 exe，也能在下次启动时自动补齐整个 PCL\。
''' </summary>
''' <remarks>
''' 必须由 Application 的构造函数调用 —— 那时 App.xaml 的资源字典还没解析，
''' MeloongCore.Wpf 里的转换器尚未被引用，才有机会在程序集解析前装好钩子。
''' </remarks>
Public Module ModBootstrap

    ''' <summary>内嵌资源的逻辑名，同时也是落盘后的文件名。</summary>
    Private ReadOnly LibNames As String() = {
        "PCLCS.dll",
        "MeloongCore.dll",
        "MeloongCore.Wpf.dll",
        "Microsoft.Win32.Registry.dll",
        "PCL-NJITMCL.xml"}

    Public Sub Prepare()
        Dim Base As String = IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
        Dim Folder As String = IO.Path.Combine(Base, "PCL")
        Try
            Directory.CreateDirectory(Folder)
        Catch ex As Exception
            '目录都建不出来就没什么可做的了，交给后续流程报原始错误
        End Try
        EnableUnsafeHeaderParsing()
        RemoveLegacyCopies(Base)
        ExtractAll(Folder)          '先补齐，再清理：清理时要靠 PCL\ 里的文件判断哪些是旧的
        CleanObsoleteFiles(Base)
        AddHandler AppDomain.CurrentDomain.AssemblyResolve, Function(Sender, Args) ResolveAssembly(Args, Folder)
    End Sub

    ''' <summary>
    ''' 清理历次更新攒下的垃圾：更新器把被替换的文件改名成 .old 却从不删除，
    ''' 每更新一次就多一代备份。既然同名的正式文件已经能启动成功，那一份备份就没用了。
    ''' </summary>
    Private Sub CleanObsoleteFiles(Base As String)
        '更新后的 .old：正式文件已存在就删；属于已知库名的也删（那些文件现在住在 PCL\）
        Try
            Dim Known As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each Name As String In LibNames
                Known.Add(Name)
            Next
            Known.Add(IO.Path.GetFileName(Assembly.GetExecutingAssembly().Location))
            For Each Old As String In Directory.GetFiles(Base, "*.old")
                Dim Stem As String = IO.Path.GetFileName(Old).Substring(0, IO.Path.GetFileName(Old).Length - 4)
                Dim Live As String = Old.Substring(0, Old.Length - 4)
                If Known.Contains(Stem) OrElse File.Exists(Live) Then File.Delete(Old)
            Next
        Catch ex As Exception
        End Try
        '1.x 遗留的组网组件：代码里已没有任何地方启动 wg.exe
        Try
            Dim WireGuard As String = IO.Path.Combine(Base, "WireGuard")
            If Directory.Exists(WireGuard) Then Directory.Delete(WireGuard, True)
        Catch ex As Exception
        End Try
        '1.x 把 wg.exe / wireguard.exe 直接放在根目录，2.1.0 起已不再打包这两个文件；
        'PCL\ 里同名的也一并清掉，避免有人解压旧完整包时又带回来
        For Each Folder As String In {Base, IO.Path.Combine(Base, "PCL")}
            For Each Name As String In {"wg.exe", "wireguard.exe"}
                Try
                    Dim Stale As String = IO.Path.Combine(Folder, Name)
                    If File.Exists(Stale) Then File.Delete(Stale)
                Catch ex As Exception
                End Try
            Next
        Next
        Try
            Dim Nested As String = IO.Path.Combine(Base, "PCL", "WireGuard")
            If Directory.Exists(Nested) Then Directory.Delete(Nested, True)
        Catch ex As Exception
        End Try
        '根目录的 XML 文档：2.1.0 起统一放在 PCL\，根目录这份是旧的
        Try
            Dim ExeBase As String = IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location)
            Dim Stale As String = IO.Path.Combine(Base, ExeBase & ".xml")
            If File.Exists(Stale) AndAlso File.Exists(IO.Path.Combine(Base, "PCL", "PCL-NJITMCL.xml")) Then File.Delete(Stale)
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>
    ''' 等价于 app.config 里的 &lt;httpWebRequest useUnsafeHeaderParsing="true"/&gt;。
    ''' 没有配置文件时只能靠反射改内部设置，必须在任何 HTTP 请求发出前完成。
    ''' </summary>
    Private Sub EnableUnsafeHeaderParsing()
        Try
            Dim SystemAsm As Assembly = GetType(Net.Configuration.SettingsSection).Assembly
            Dim SectionType As Type = SystemAsm.GetType("System.Net.Configuration.SettingsSectionInternal")
            If SectionType Is Nothing Then Return
            Dim Instance As Object = SectionType.GetField("Section", BindingFlags.Static Or BindingFlags.NonPublic).GetValue(Nothing)
            If Instance Is Nothing Then Return
            Dim Field As Reflection.FieldInfo = SectionType.GetField("useUnsafeHeaderParsing", BindingFlags.Instance Or BindingFlags.NonPublic)
            Field?.SetValue(Instance, True)
        Catch ex As Exception
            '拿不到内部字段就退回默认行为，不因此阻塞启动
        End Try
    End Sub

    ''' <summary>旧版本把 dll 放在 exe 同级；留着会被优先探测到，导致加载到旧版本。</summary>
    Private Sub RemoveLegacyCopies(Base As String)
        For Each Name As String In LibNames
            If Not Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim Old As String = IO.Path.Combine(Base, Name)
            If Not File.Exists(Old) Then Continue For
            Try
                File.Delete(Old)
            Catch ex As Exception
                '被占用就跳过：说明已经被这份进程加载，行为一致，不必强求
            End Try
        Next
    End Sub

    ''' <summary>校验记录：版本 + 每个库的 sha256，缺一个或改一个都会触发重写。</summary>
    Private Const StampName As String = "libs.version"

    ''' <summary>缺失、被改坏或版本变了就重写。单个文件缺失只会补那一个。</summary>
    Private Sub ExtractAll(Folder As String)
        Dim Asm As Assembly = Assembly.GetExecutingAssembly()
        Dim Stamp As String = IO.Path.Combine(Folder, StampName)
        Dim Expected As String = If(Asm.GetName().Version IsNot Nothing, Asm.GetName().Version.ToString(), "")
        Dim Stored As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            If File.Exists(Stamp) Then
                Dim VerLine As String = ""
                For Each Line As String In File.ReadAllLines(Stamp)
                    Line = Line.Trim()
                    If Line.StartsWith("#") Then VerLine = Line : Continue For
                    Dim Parts As String() = Line.Split(" "c)
                    If Parts.Length = 2 Then Stored(Parts(1).Trim()) = Parts(0).Trim()
                Next
                If VerLine <> "#" & Expected Then Stored.Clear() '版本变了，全部重写一遍
            End If
        Catch ex As Exception
            Stored.Clear()
        End Try

        Dim Now As New List(Of String) From {"#" & Expected}
        For Each Name As String In LibNames
            Dim Target As String = IO.Path.Combine(Folder, Name)
            Try
                Using Stream As Stream = Asm.GetManifestResourceStream(Name)
                    If Stream Is Nothing Then Continue For '这一版没嵌进去，跳过
                    Dim Want As String = HashOf(Stream)
                    Now.Add(Want & " " & Name)
                    Dim Have As String = ""
                    If File.Exists(Target) AndAlso Stored.ContainsKey(Name) Then Have = HashOf(Target)
                    If Have = Want Then Continue For
                    Stream.Position = 0
                    Using Output As New FileStream(Target, FileMode.Create, FileAccess.Write, FileShare.Read)
                        Stream.CopyTo(Output)
                    End Using
                End Using
            Catch ex As Exception
                '单个文件失败不影响其他，真缺了会在解析时抛出更明确的错误
            End Try
        Next
        Try
            File.WriteAllLines(Stamp, Now)
        Catch ex As Exception
        End Try
    End Sub

    Private Function HashOf(Stream As Stream) As String
        Using Sha As New Security.Cryptography.SHA256Managed()
            Return BitConverter.ToString(Sha.ComputeHash(Stream)).Replace("-", "").ToLowerInvariant()
        End Using
    End Function

    Private Function HashOf(Path As String) As String
        Using Stream As New FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read)
            Return HashOf(Stream)
        End Using
    End Function

    Private Function ResolveAssembly(Args As ResolveEventArgs, Folder As String) As Assembly
        Dim SimpleName As String = New AssemblyName(Args.Name).Name
        Dim Path As String = IO.Path.Combine(Folder, SimpleName & ".dll")
        If Not File.Exists(Path) Then Return Nothing
        Try
            Return Assembly.LoadFrom(Path)
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

End Module
