Imports System.IO

''' <summary>
''' 向 Minecraft 的 servers.dat 里追加联机房间条目。
''' 只做「不存在同名地址时追加」，不改动已有条目；解析失败时保持原文件不变。
''' </summary>
Public Module ModServerList

    Private Const TagEnd As Byte = 0
    Private Const TagByte As Byte = 1
    Private Const TagShort As Byte = 2
    Private Const TagInt As Byte = 3
    Private Const TagLong As Byte = 4
    Private Const TagFloat As Byte = 5
    Private Const TagDouble As Byte = 6
    Private Const TagByteArray As Byte = 7
    Private Const TagString As Byte = 8
    Private Const TagList As Byte = 9
    Private Const TagCompound As Byte = 10
    Private Const TagIntArray As Byte = 11
    Private Const TagLongArray As Byte = 12

    ''' <summary>
    ''' 把房间写入 servers.dat；已存在同一地址时返回 False。
    ''' </summary>
    Public Function AddServer(FilePath As String, Name As String, Address As String) As Boolean
        If String.IsNullOrWhiteSpace(FilePath) OrElse String.IsNullOrWhiteSpace(Address) Then Return False
        Try
            Dim Compressed As Boolean = False
            Dim Raw As Byte() = Array.Empty(Of Byte)()
            If FileUtils.Exists(FilePath) Then
                Dim Blob As Byte() = IO.File.ReadAllBytes(FilePath)
                If Blob.Length > 2 AndAlso Blob(0) = &H1F AndAlso Blob(1) = &H8B Then
                    Compressed = True
                    Using Source As New MemoryStream(Blob)
                        Using Gzip As New IO.Compression.GZipStream(Source, IO.Compression.CompressionMode.Decompress)
                            Using Target As New MemoryStream()
                                Gzip.CopyTo(Target)
                                Raw = Target.ToArray()
                            End Using
                        End Using
                    End Using
                Else
                    Raw = Blob
                End If
            End If

            Dim Output As Byte()
            If Raw.Length = 0 Then
                Output = BuildNewFile(Name, Address)
                Compressed = True
            Else
                Output = AppendEntry(Raw, Name, Address)
            End If
            If Output Is Nothing Then Return False

            If FileUtils.Exists(FilePath) Then
                FileUtils.Copy(FilePath, FilePath & ".njitmcl-bak")
            End If
            If Compressed Then
                Using Target As New MemoryStream()
                    Using Gzip As New IO.Compression.GZipStream(Target, IO.Compression.CompressionMode.Compress, True)
                        Gzip.Write(Output, 0, Output.Length)
                    End Using
                    IO.File.WriteAllBytes(FilePath, Target.ToArray())
                End Using
            Else
                IO.File.WriteAllBytes(FilePath, Output)
            End If
            Return True
        Catch ex As Exception
            Logger.Warn(ex, "写入 servers.dat 失败")
            Return False
        End Try
    End Function

    Private Function AppendEntry(Raw As Byte(), Name As String, Address As String) As Byte()
        Dim Reader As New ByteReader(Raw)
        If Reader.ReadByte() <> TagCompound Then Return Nothing
        Reader.ReadString()
        Dim CountPosition As Integer = -1
        Dim ElementCount As Integer = 0
        Dim ContentEnd As Integer = -1
        Do
            Dim Kind As Byte = Reader.ReadByte()
            If Kind = TagEnd Then Exit Do
            Dim Key As String = Reader.ReadString()
            If Kind = TagList AndAlso Key = "servers" Then
                Reader.ReadByte() ' 元素类型
                CountPosition = Reader.Position
                ElementCount = Reader.ReadInt32()
                For i = 1 To ElementCount
                    Dim Ip As String = ReadEntryIp(Reader)
                    If Ip = Address Then Return Nothing
                Next
                ContentEnd = Reader.Position
                Exit Do
            End If
            SkipPayload(Reader, Kind)
        Loop
        If CountPosition < 0 OrElse ContentEnd < 0 Then Return Nothing

        Dim Result As New List(Of Byte)(Raw.Length + 128)
        Result.AddRange(CopyRange(Raw, 0, CountPosition))
        Result.AddRange(EncodeInt32(ElementCount + 1))
        Result.AddRange(CopyRange(Raw, CountPosition + 4, ContentEnd))
        Result.AddRange(BuildEntry(Name, Address))
        Result.AddRange(CopyRange(Raw, ContentEnd, Raw.Length))
        Return Result.ToArray()
    End Function

    ''' <summary>
    ''' 读取一个列表元素的 ip；同时把读取位置推进到该元素末尾。
    ''' NBT 的列表元素只存 payload，不带类型与名字。
    ''' </summary>
    Private Function ReadEntryIp(Reader As ByteReader) As String
        Dim Ip As String = ""
        Do
            Dim Kind As Byte = Reader.ReadByte()
            If Kind = TagEnd Then Exit Do
            Dim Key As String = Reader.ReadString()
            If Kind = TagString AndAlso Key = "ip" Then
                Ip = Reader.ReadString()
            Else
                SkipPayload(Reader, Kind)
            End If
        Loop
        Return Ip
    End Function

    Private Function BuildNewFile(Name As String, Address As String) As Byte()
        Dim Result As New List(Of Byte)
        Result.Add(TagCompound)
        Result.AddRange(EncodeInt16(0))
        Result.Add(TagList)
        Result.AddRange(EncodeString("servers"))
        Result.Add(TagCompound)
        Result.AddRange(EncodeInt32(1))
        Result.AddRange(BuildEntry(Name, Address))
        Result.Add(TagEnd)
        Return Result.ToArray()
    End Function

    Private Function BuildEntry(Name As String, Address As String) As Byte()
        Dim Result As New List(Of Byte)
        Result.Add(TagString)
        Result.AddRange(EncodeString("name"))
        Result.AddRange(EncodeString(Name))
        Result.Add(TagString)
        Result.AddRange(EncodeString("ip"))
        Result.AddRange(EncodeString(Address))
        Result.Add(TagByte)
        Result.AddRange(EncodeString("hidden"))
        Result.Add(0)
        Result.Add(TagEnd)
        Return Result.ToArray()
    End Function

    ''' <summary>跳过一个 tag 的 payload（类型与名字已由调用方读取）。</summary>
    Private Sub SkipPayload(Reader As ByteReader, Kind As Byte)
        Select Case Kind
            Case TagByte : Reader.Skip(1)
            Case TagShort : Reader.Skip(2)
            Case TagInt : Reader.Skip(4)
            Case TagLong : Reader.Skip(8)
            Case TagFloat : Reader.Skip(4)
            Case TagDouble : Reader.Skip(8)
            Case TagByteArray : Reader.Skip(Reader.ReadInt32())
            Case TagString : Reader.ReadString()
            Case TagList
                Dim Element As Byte = Reader.ReadByte()
                Dim Count As Integer = Reader.ReadInt32()
                For i = 1 To Count
                    SkipPayload(Reader, Element)
                Next
            Case TagCompound
                Do
                    Dim Child As Byte = Reader.ReadByte()
                    If Child = TagEnd Then Exit Do
                    Reader.ReadString()
                    SkipPayload(Reader, Child)
                Loop
            Case TagIntArray : Reader.Skip(Reader.ReadInt32() * 4)
            Case TagLongArray : Reader.Skip(Reader.ReadInt32() * 8)
            Case Else
                Throw New IO.InvalidDataException("未知的 NBT 类型：" & Kind)
        End Select
    End Sub

    Private Function CopyRange(Source As Byte(), Start As Integer, [End] As Integer) As Byte()
        Dim Length As Integer = [End] - Start
        If Length <= 0 Then Return Array.Empty(Of Byte)()
        Dim Result(Length - 1) As Byte
        Array.Copy(Source, Start, Result, 0, Length)
        Return Result
    End Function

    Private Function EncodeInt16(Value As Integer) As Byte()
        Return {CByte((Value >> 8) And &HFF), CByte(Value And &HFF)}
    End Function

    Private Function EncodeInt32(Value As Integer) As Byte()
        Return {CByte((Value >> 24) And &HFF), CByte((Value >> 16) And &HFF),
                CByte((Value >> 8) And &HFF), CByte(Value And &HFF)}
    End Function

    Private Function EncodeString(Value As String) As Byte()
        Dim Body As Byte() = Text.Encoding.UTF8.GetBytes(If(Value, ""))
        Dim Result As New List(Of Byte)
        Result.AddRange(EncodeInt16(Body.Length))
        Result.AddRange(Body)
        Return Result.ToArray()
    End Function

    ''' <summary>大端 NBT 读取器，带越界检查。</summary>
    Private Class ByteReader
        Private ReadOnly Data As Byte()
        Public Property Position As Integer = 0

        Public Sub New(Value As Byte())
            Data = Value
        End Sub

        Public Function ReadByte() As Byte
            If Position >= Data.Length Then Throw New IO.EndOfStreamException()
            Dim Value As Byte = Data(Position)
            Position += 1
            Return Value
        End Function

        Public Sub Skip(Count As Integer)
            If Count < 0 OrElse Position + Count > Data.Length Then Throw New IO.EndOfStreamException()
            Position += Count
        End Sub

        Public Function ReadInt32() As Integer
            If Position + 4 > Data.Length Then Throw New IO.EndOfStreamException()
            Dim Value As Long = (CLng(Data(Position)) << 24) Or (CLng(Data(Position + 1)) << 16) Or
                                (CLng(Data(Position + 2)) << 8) Or CLng(Data(Position + 3))
            If Value >= &H80000000L Then Value -= &H100000000L
            Position += 4
            Return CInt(Value)
        End Function

        Public Function ReadString() As String
            If Position + 2 > Data.Length Then Throw New IO.EndOfStreamException()
            Dim Length As Integer = (CInt(Data(Position)) << 8) Or CInt(Data(Position + 1))
            Position += 2
            If Length < 0 OrElse Position + Length > Data.Length Then Throw New IO.EndOfStreamException()
            Dim Value As String = Text.Encoding.UTF8.GetString(Data, Position, Length)
            Position += Length
            Return Value
        End Function
    End Class

End Module
