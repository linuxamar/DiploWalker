namespace Diplo.Linux

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading

/// Flux combinant une entrée et une sortie (console série).
type ConsoleStream (input : Stream, output : Stream) =
    inherit Stream()

    override _.CanRead = input.CanRead
    override _.CanWrite = output.CanWrite
    override _.CanSeek = false
    override _.Length = raise (NotSupportedException "ConsoleStream non seekable")
    override _.Position
        with get () = raise (NotSupportedException "ConsoleStream non seekable")
        and set _ = raise (NotSupportedException "ConsoleStream non seekable")
    override _.Flush() = output.Flush()
    override _.Seek(_, _) = raise (NotSupportedException "ConsoleStream non seekable")
    override _.SetLength(_) = raise (NotSupportedException "ConsoleStream non seekable")
    override _.Read(data, offset, count) = input.Read(data, offset, count)
    override _.Write(data, offset, count) = output.Write(data, offset, count)

/// Copie de l'état d'une tâche (fork).
type TaskState =
    { Memory : byte[]
      Registers : Registers
      OpenFiles : Dictionary<int, Stream>
      NextFd : int
      ProgramBreak : uint64
      MmapCursor : uint64
      Regions : MemoryRegion list }

/// Flux en lecture seule représentant un répertoire (getdents).
type DirectoryStream (path : string) =
    inherit Stream()

    override _.CanRead = false
    override _.CanWrite = false
    override _.CanSeek = false
    override _.Length = raise (NotSupportedException "DirectoryStream non seekable")
    override _.Position
        with get () = raise (NotSupportedException "DirectoryStream non seekable")
        and set _ = raise (NotSupportedException "DirectoryStream non seekable")
    override _.Flush() = ()
    override _.Seek(_, _) = raise (NotSupportedException "DirectoryStream non seekable")
    override _.SetLength(_) = raise (NotSupportedException "DirectoryStream non seekable")
    override _.Read(_, _, _) = raise (NotSupportedException "Lecture impossible sur un répertoire")
    override _.Write(_, _, _) = raise (NotSupportedException "Écriture impossible sur un répertoire")

    /// Chemin du répertoire représenté par ce flux (getdents).
    member _.Path = path

/// Hôte exposé au traducteur de syscalls.
type ISyscallHost =
    abstract Memory : VirtualMemory
    abstract Registers : Registers
    abstract StandardInput : Stream
    abstract StandardOutput : Stream
    abstract StandardError : Stream
    abstract Console : Stream
    abstract OpenFiles : Dictionary<int, Stream>
    abstract NextFd : int with get, set
    abstract ProgramBreak : uint64 with get, set
    abstract MmapCursor : uint64 with get, set
    abstract Halted : bool with get, set
    abstract ExitStatus : int with get, set
    abstract Regions : ResizeArray<MemoryRegion>
    abstract TaskQueue : ResizeArray<TaskState>
    abstract NeedsSwitch : bool with get, set
    abstract CurrentDirectory : string with get, set
    abstract ExitedStatuses : ResizeArray<int>
    abstract Snapshot : unit -> TaskState
    abstract Restore : TaskState -> unit
    abstract ExecImage : byte[] -> string[] -> unit

/// Traducteur de syscalls Linux vers les API Windows.
module Syscalls =

    let private enosys = -38

    let private errno (code : int) : uint64 = uint64 code

    /// Lit une chaîne terminée par zéro depuis la mémoire guest.
    let readCString (mem : VirtualMemory) (addr : uint64) : string =
        let sb = StringBuilder()
        let mutable a = addr
        let mutable done' = false
        while not done' do
            let b = mem.ReadByte a
            if b = 0uy then
                done' <- true
            else
                sb.Append(char b) |> ignore
                a <- a + 1UL
        sb.ToString()

    /// Canal mémoire minimal partagé entre deux descripteurs (pipe).
    type private MemoryPipe () =
        inherit Stream()

        let buffer = new MemoryStream()
        let sync = obj()
        let mutable readPos = 0L

        override _.CanRead = true
        override _.CanSeek = false
        override _.CanWrite = true
        override _.Length = buffer.Length
        override _.Position
            with get () = readPos
            and set _ = ()
        override _.Flush() = ()
        override _.Seek(_, _) = invalidOp "MemoryPipe non seekable"
        override _.SetLength(_) = invalidOp "MemoryPipe non settable"
        override _.Read(data, offset, count) =
            lock sync (fun () ->
                let available = buffer.Length - readPos
                let n = int (min (int64 count) available)
                if n <= 0 then 0
                else
                    buffer.Position <- readPos
                    let read = buffer.Read(data, offset, n)
                    readPos <- readPos + int64 read
                    read)
        override _.Write(data, offset, count) =
            lock sync (fun () ->
                buffer.Position <- buffer.Length
                buffer.Write(data, offset, count))

    let private streamFor (host : ISyscallHost) (fd : uint64) : Stream =
        if fd = 0UL then host.StandardInput
        elif fd = 1UL then host.StandardOutput
        elif fd = 2UL then host.StandardError
        else
            match host.OpenFiles.TryGetValue(int fd) with
            | true, s -> s
            | false, _ -> failwith $"Descriptor {fd} inconnu"

    let private readStream (host : ISyscallHost) (fd : uint64) : Stream =
        streamFor host fd

    let private writeStream (host : ISyscallHost) (fd : uint64) : Stream =
        streamFor host fd

    let private sysWrite (host : ISyscallHost) (fd : uint64) (buf : uint64) (count : uint64) : uint64 =
        try
            let data = host.Memory.ReadBytes buf (int count)
            let stream = writeStream host fd
            if not stream.CanWrite then errno -9
            else
                stream.Write(data, 0, data.Length)
                stream.Flush()
                count
        with
        | _ -> errno enosys

    let private sysRead (host : ISyscallHost) (fd : uint64) (buf : uint64) (count : uint64) : uint64 =
        try
            let stream = readStream host fd
            if not stream.CanRead then errno -9
            else
                let data = Array.zeroCreate (int count)
                let n = stream.Read(data, 0, data.Length)
                if n > 0 then
                    host.Memory.WriteBytes buf data[0 .. n - 1]
                uint64 n
        with
        | _ -> errno enosys

    let private sysWritev (host : ISyscallHost) (fd : uint64) (iov : uint64) (iovcnt : uint64) : uint64 =
        try
            let stream = writeStream host fd
            if not stream.CanWrite then errno -9
            else
                let mutable total = 0L
                for i in 0 .. int iovcnt - 1 do
                    let baseAddr = host.Memory.ReadUInt64 (iov + uint64 (i * 16))
                    let len = int (host.Memory.ReadUInt64 (iov + uint64 (i * 16) + 8UL))
                    stream.Write(host.Memory.ReadBytes baseAddr len, 0, len)
                    total <- total + int64 len
                stream.Flush()
                uint64 total
        with
        | _ -> errno enosys

    let private sysReadv (host : ISyscallHost) (fd : uint64) (iov : uint64) (iovcnt : uint64) : uint64 =
        try
            let stream = readStream host fd
            if not stream.CanRead then errno -9
            else
                let mutable total = 0L
                for i in 0 .. int iovcnt - 1 do
                    let baseAddr = host.Memory.ReadUInt64 (iov + uint64 (i * 16))
                    let len = int (host.Memory.ReadUInt64 (iov + uint64 (i * 16) + 8UL))
                    let data = Array.zeroCreate len
                    let n = stream.Read(data, 0, len)
                    if n > 0 then
                        host.Memory.WriteBytes baseAddr data[0 .. n - 1]
                    total <- total + int64 n
                    if n < len then ()
                uint64 total
        with
        | _ -> errno enosys

    let private sysPipe (host : ISyscallHost) (buf : uint64) : uint64 =
        try
            let pipe = new MemoryPipe()
            let r = host.NextFd
            host.NextFd <- host.NextFd + 1
            let w = host.NextFd
            host.NextFd <- host.NextFd + 1
            host.OpenFiles[r] <- pipe
            host.OpenFiles[w] <- pipe
            host.Memory.WriteUInt32 buf (uint32 r)
            host.Memory.WriteUInt32 (buf + 4UL) (uint32 w)
            0UL
        with
        | _ -> errno enosys

    let private sysPipe2 (host : ISyscallHost) (buf : uint64) (_flags : uint64) : uint64 =
        sysPipe host buf

    let private sysDup (host : ISyscallHost) (fd : uint64) : uint64 =
        try
            let s = streamFor host fd
            let newFd = host.NextFd
            host.NextFd <- host.NextFd + 1
            host.OpenFiles[newFd] <- s
            uint64 newFd
        with
        | _ -> errno -9

    let private sysDup2 (host : ISyscallHost) (oldfd : uint64) (newfd : uint64) : uint64 =
        if oldfd = newfd then newfd
        else
            try
                let s = streamFor host oldfd
                host.OpenFiles.Remove(int newfd) |> ignore
                host.OpenFiles[int newfd] <- s
                newfd
            with
            | _ -> errno -9

    let private sysDup3 (host : ISyscallHost) (oldfd : uint64) (newfd : uint64) (_flags : uint64) : uint64 =
        if oldfd = newfd then errno -22
        else sysDup2 host oldfd newfd

    let private sysNanosleep (host : ISyscallHost) (req : uint64) (rem : uint64) : uint64 =
        try
            let sec = int64 (host.Memory.ReadUInt64 req)
            let nanos = int64 (host.Memory.ReadUInt64 (req + 8UL))
            let ms = sec * 1000L + nanos / 1_000_000L
            if ms > 0L then Thread.Sleep(int ms)
            if rem <> 0UL then
                host.Memory.WriteUInt64 rem 0UL
                host.Memory.WriteUInt64 (rem + 8UL) 0UL
            0UL
        with
        | _ -> errno -22

    let private sysGettimeofday (host : ISyscallHost) (tv : uint64) : uint64 =
        let now = DateTimeOffset.UtcNow
        let sec = now.ToUnixTimeSeconds()
        let usec = (now.Ticks % TimeSpan.TicksPerSecond) / 10L
        host.Memory.WriteUInt64 tv (uint64 sec)
        host.Memory.WriteUInt64 (tv + 8UL) (uint64 usec)
        0UL

    let private fileAccess (flags : uint64) : FileAccess =
        let access = flags &&& 0x3UL
        if access = 0UL then FileAccess.Read
        elif access = 1UL then FileAccess.Write
        else FileAccess.ReadWrite

    let private sysOpenPath (host : ISyscallHost) (path : string) (flags : uint64) : uint64 =
        let consoleDevices =
            [ "/dev/console"; "/dev/tty"; "/dev/ttyS0"; "/dev/ttyS1" ]
        if List.contains path consoleDevices then
            let fd = host.NextFd
            host.NextFd <- host.NextFd + 1
            host.OpenFiles[fd] <- host.Console
            uint64 fd
        elif path = "" then
            errno -2
        else
            let fullPath =
                if Path.IsPathRooted path then path
                else Path.Combine(host.CurrentDirectory, path)
            if Directory.Exists fullPath then
                let fd = host.NextFd
                host.NextFd <- host.NextFd + 1
                host.OpenFiles[fd] <- new DirectoryStream(fullPath)
                uint64 fd
            else
                let access = fileAccess flags
                let create = flags &&& 0x40UL <> 0UL   // O_CREAT
                let trunc = flags &&& 0x200UL <> 0UL   // O_TRUNC
                let append = flags &&& 0x400UL <> 0UL  // O_APPEND
                let mode =
                    if trunc then FileMode.Create
                    elif create then FileMode.OpenOrCreate
                    else FileMode.Open
                let fs = new FileStream(fullPath, mode, access)
                if append then
                    fs.Seek(0L, SeekOrigin.End) |> ignore
                let fd = host.NextFd
                host.NextFd <- host.NextFd + 1
                host.OpenFiles[fd] <- fs
                uint64 fd

    let private sysOpen (host : ISyscallHost) (path : uint64) (flags : uint64) : uint64 =
        try
            sysOpenPath host (readCString host.Memory path) flags
        with
        | :? FileNotFoundException -> errno -2
        | :? DirectoryNotFoundException -> errno -2
        | _ -> errno enosys

    let private sysOpenat (host : ISyscallHost) (dirfd : uint64) (path : uint64) (flags : uint64) : uint64 =
        try
            if dirfd = 0xFFFFFFFFFFFFFF9CUL then   // AT_FDCWD
                sysOpenPath host (readCString host.Memory path) flags
            else
                errno -9
        with
        | :? FileNotFoundException -> errno -2
        | :? DirectoryNotFoundException -> errno -2
        | _ -> errno enosys

    let private sysLseek (host : ISyscallHost) (fd : uint64) (offset : uint64) (whence : uint64) : uint64 =
        try
            match host.OpenFiles.TryGetValue(int fd) with
            | true, s ->
                let origin =
                    match whence with
                    | 0UL -> SeekOrigin.Begin
                    | 1UL -> SeekOrigin.Current
                    | 2UL -> SeekOrigin.End
                    | _ -> invalidOp "whence inconnu"
                uint64 (s.Seek(int64 offset, origin))
            | false, _ -> errno -9
        with
        | _ -> errno enosys

    let private sysFstat (host : ISyscallHost) (fd : uint64) (buf : uint64) : uint64 =
        try
            let stream =
                if fd = 0UL then host.StandardInput
                elif fd = 1UL then host.StandardOutput
                elif fd = 2UL then host.StandardError
                else
                    match host.OpenFiles.TryGetValue(int fd) with
                    | true, s -> s
                    | false, _ -> failwith $"Descriptor {fd} inconnu"
            let isConsole = stream :? ConsoleStream
            let size =
                if isConsole then 0L
                else stream.Length
            let mode =
                if isConsole then 0x2190u   // character device
                else 0x81A4u
            let now = uint64 (DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            let m = host.Memory
            m.WriteUInt64 (buf + 0UL) 0UL                 // st_dev
            m.WriteUInt64 (buf + 8UL) 0UL                 // st_ino
            m.WriteUInt64 (buf + 16UL) 1UL                // st_nlink
            m.WriteUInt32 (buf + 24UL) mode               // st_mode
            m.WriteUInt32 (buf + 28UL) 0u                 // st_uid
            m.WriteUInt32 (buf + 32UL) 0u                 // st_gid
            m.WriteUInt32 (buf + 36UL) 0u                 // __pad0
            m.WriteUInt64 (buf + 40UL) 0UL                // st_rdev
            m.WriteUInt64 (buf + 48UL) (uint64 size)      // st_size
            m.WriteUInt64 (buf + 56UL) 4096UL             // st_blksize
            m.WriteUInt64 (buf + 64UL) ((uint64 size + 511UL) / 512UL)  // st_blocks
            m.WriteUInt64 (buf + 72UL) now                // st_atime
            m.WriteUInt64 (buf + 80UL) 0UL
            m.WriteUInt64 (buf + 88UL) now                // st_mtime
            m.WriteUInt64 (buf + 96UL) 0UL
            m.WriteUInt64 (buf + 104UL) now               // st_ctime
            m.WriteUInt64 (buf + 112UL) 0UL
            m.WriteUInt64 (buf + 120UL) 0UL
            m.WriteUInt64 (buf + 128UL) 0UL
            m.WriteUInt64 (buf + 136UL) 0UL
            0UL
        with
        | _ -> errno -9

    let private sysAccess (host : ISyscallHost) (path : uint64) : uint64 =
        let p = readCString host.Memory path
        if File.Exists p || Directory.Exists p then 0UL else errno -2

    let private sysGetcwd (host : ISyscallHost) (buf : uint64) (size : uint64) : uint64 =
        try
            let path = host.CurrentDirectory
            let bytes = Array.append (Encoding.UTF8.GetBytes path) [| 0uy |]
            if uint64 bytes.Length > size then
                errno -68
            else
                host.Memory.WriteBytes buf bytes
                buf
        with
        | _ -> errno -2

    let private sysUname (host : ISyscallHost) (buf : uint64) : uint64 =
        let fields = [ "Linux"; "diplo"; "6.6.0"; "#1"; "x86_64"; "" ]
        fields
        |> List.iteri (fun i f ->
            let bytes = Encoding.UTF8.GetBytes f
            host.Memory.WriteBytes (buf + uint64 (i * 65)) bytes)
        0UL

    let private sysClockGettime (host : ISyscallHost) (clockid : uint64) (buf : uint64) : uint64 =
        if clockid > 1UL then
            errno -22
        else
            let now = DateTimeOffset.UtcNow
            let seconds : int64 =
                if clockid = 1UL then Environment.TickCount64 / 1000L
                else now.ToUnixTimeSeconds()
            let nanos : int64 =
                if clockid = 1UL then (Environment.TickCount64 % 1000L) * 1_000_000L
                else now.Ticks % TimeSpan.TicksPerSecond * 100L
            host.Memory.WriteUInt64 buf (uint64 seconds)
            host.Memory.WriteUInt64 (buf + 8UL) (uint64 nanos)
            0UL

    let private sysClose (host : ISyscallHost) (fd : uint64) : uint64 =
        try
            match host.OpenFiles.TryGetValue(int fd) with
            | true, s ->
                host.OpenFiles.Remove(int fd) |> ignore
                match s with
                | :? FileStream as fs -> fs.Dispose()
                | _ -> ()
                0UL
            | false, _ -> errno -9
        with
        | _ -> errno enosys

    let private alignPage (length : uint64) : uint64 =
        (length + 0xFFFUL) &&& ~~~0xFFFUL

    let private align8 (length : uint64) : uint64 =
        (length + 7UL) &&& ~~~7UL

    let private mapShared = 0x1UL
    let private mapPrivate = 0x2UL
    let private mapFixed = 0x10UL
    let private mapAnonymous = 0x20UL
    let private mremapMayMove = 0x1UL
    let private mremapFixed = 0x2UL

    let private sysMmap (host : ISyscallHost) (addr : uint64) (length : uint64)
                        (prot : uint64) (flags : uint64) (fd : uint64) (offset : uint64) : uint64 =
        let pageLen = alignPage length
        let anonymous = flags &&& mapAnonymous <> 0UL || fd = 0xFFFFFFFFFFFFFFFFUL
        let dest =
            if flags &&& mapFixed <> 0UL then addr
            elif addr <> 0UL then addr
            else
                let result = host.MmapCursor
                host.MmapCursor <- host.MmapCursor + pageLen
                result
        if dest + pageLen > host.Memory.Size then
            errno -12   // ENOMEM
        else
            host.Memory.MapRegion dest pageLen prot
            if not anonymous then
                match host.OpenFiles.TryGetValue(int fd) with
                | true, s ->
                    let oldPos = s.Position
                    s.Position <- int64 offset
                    let data = Array.zeroCreate (int pageLen)
                    let n = s.Read(data, 0, data.Length)
                    if n > 0 then
                        host.Memory.WriteBytes dest data[0 .. n - 1]
                    s.Position <- oldPos
                | false, _ -> ()
            dest

    let private sysMunmap (host : ISyscallHost) (addr : uint64) (length : uint64) : uint64 =
        let pageLen = alignPage length
        host.Memory.UnmapRange addr pageLen
        host.Memory.Zero addr pageLen
        0UL

    let private sysMprotect (host : ISyscallHost) (addr : uint64) (length : uint64) (prot : uint64) : uint64 =
        let pageLen = alignPage length
        host.Memory.ProtectRange addr pageLen prot
        0UL

    let private sysBrk (host : ISyscallHost) (addr : uint64) : uint64 =
        if addr = 0UL then
            host.ProgramBreak
        elif addr > host.ProgramBreak then
            host.Memory.Zero host.ProgramBreak (addr - host.ProgramBreak)
            host.ProgramBreak <- addr
            addr
        else
            host.ProgramBreak

    let private sysGetpid () : uint64 = 1UL

    let private sysGetppid () : uint64 = 0UL

    let private sysGetuid () : uint64 = 0UL

    let private sysGetgid () : uint64 = 0UL

    let private sysGeteuid () : uint64 = 0UL

    let private sysGetegid () : uint64 = 0UL

    let private sysExit (host : ISyscallHost) (status : uint64) : uint64 =
        if host.TaskQueue.Count > 0 then
            host.ExitedStatuses.Add(int status)
            host.NeedsSwitch <- true
            uint64 status
        else
            host.Halted <- true
            host.ExitStatus <- int status
            host.ExitStatus |> uint64

    let private sysExitGroup (host : ISyscallHost) (status : uint64) : uint64 =
        sysExit host status

    let private sysFork (host : ISyscallHost) : uint64 =
        let snapshot = host.Snapshot()
        snapshot.Registers.RAX <- 1UL   // l'enfant reprend avec pid 1
        host.TaskQueue.Add snapshot
        0UL   // le parent reçoit le pid de l'enfant

    let private sysExecve (host : ISyscallHost) (pathPtr : uint64) (argvPtr : uint64) (_envp : uint64) : uint64 =
        let path = readCString host.Memory pathPtr
        if not (File.Exists path) then
            errno -2
        else
            let argv = ResizeArray<string>()
            let mutable i = 0
            let mutable stop = false
            while not stop do
                let ptr = host.Memory.ReadUInt64 (argvPtr + uint64 (i * 8))
                if ptr = 0UL then
                    stop <- true
                else
                    argv.Add(readCString host.Memory ptr)
                    i <- i + 1
            host.ExecImage (File.ReadAllBytes path) (argv |> Seq.toArray)
            0UL

    let private sysWait4 (host : ISyscallHost) (_pid : uint64) (statusPtr : uint64) (_options : uint64) : uint64 =
        if host.ExitedStatuses.Count > 0 then
            let status = host.ExitedStatuses.[0]
            host.ExitedStatuses.RemoveAt 0
            if statusPtr <> 0UL then
                host.Memory.WriteUInt32 statusPtr (uint32 (status <<< 8))
            1UL
        else
            errno -10   // ECHILD

    let private sysChdir (host : ISyscallHost) (pathPtr : uint64) : uint64 =
        let path = readCString host.Memory pathPtr
        if Directory.Exists path then
            host.CurrentDirectory <- path
            0UL
        else
            errno -2

    let private sysStat (host : ISyscallHost) (pathPtr : uint64) (buf : uint64) : uint64 =
        let path = readCString host.Memory pathPtr
        let m = host.Memory
        let fullPath =
            if Path.IsPathRooted path then path
            else Path.Combine(host.CurrentDirectory, path)
        if Directory.Exists fullPath then
            let info = DirectoryInfo(fullPath)
            let now = uint64 (DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            m.WriteUInt64 (buf + 0UL) 0UL
            m.WriteUInt64 (buf + 8UL) 0UL
            m.WriteUInt64 (buf + 16UL) 1UL
            m.WriteUInt32 (buf + 24UL) 0x41EDu   // répertoire
            m.WriteUInt32 (buf + 28UL) 0u
            m.WriteUInt32 (buf + 32UL) 0u
            m.WriteUInt32 (buf + 36UL) 0u
            m.WriteUInt64 (buf + 40UL) 0UL
            m.WriteUInt64 (buf + 48UL) 0UL
            m.WriteUInt64 (buf + 56UL) 4096UL
            m.WriteUInt64 (buf + 64UL) 0UL
            m.WriteUInt64 (buf + 72UL) now
            m.WriteUInt64 (buf + 80UL) 0UL
            m.WriteUInt64 (buf + 88UL) now
            m.WriteUInt64 (buf + 96UL) 0UL
            m.WriteUInt64 (buf + 104UL) now
            m.WriteUInt64 (buf + 112UL) 0UL
            m.WriteUInt64 (buf + 120UL) 0UL
            m.WriteUInt64 (buf + 128UL) 0UL
            m.WriteUInt64 (buf + 136UL) 0UL
            0UL
        elif File.Exists fullPath then
            let info = FileInfo(fullPath)
            let now = uint64 (DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            m.WriteUInt64 (buf + 0UL) 0UL
            m.WriteUInt64 (buf + 8UL) 0UL
            m.WriteUInt64 (buf + 16UL) 1UL
            m.WriteUInt32 (buf + 24UL) 0x81A4u   // fichier
            m.WriteUInt32 (buf + 28UL) 0u
            m.WriteUInt32 (buf + 32UL) 0u
            m.WriteUInt32 (buf + 36UL) 0u
            m.WriteUInt64 (buf + 40UL) 0UL
            m.WriteUInt64 (buf + 48UL) (uint64 info.Length)
            m.WriteUInt64 (buf + 56UL) 4096UL
            m.WriteUInt64 (buf + 64UL) ((uint64 info.Length + 511UL) / 512UL)
            m.WriteUInt64 (buf + 72UL) now
            m.WriteUInt64 (buf + 80UL) 0UL
            m.WriteUInt64 (buf + 88UL) now
            m.WriteUInt64 (buf + 96UL) 0UL
            m.WriteUInt64 (buf + 104UL) now
            m.WriteUInt64 (buf + 112UL) 0UL
            m.WriteUInt64 (buf + 120UL) 0UL
            m.WriteUInt64 (buf + 128UL) 0UL
            m.WriteUInt64 (buf + 136UL) 0UL
            0UL
        else
            errno -2

    let private sysRename (host : ISyscallHost) (oldPtr : uint64) (newPtr : uint64) : uint64 =
        let oldPath = readCString host.Memory oldPtr
        let newPath = readCString host.Memory newPtr
        if File.Exists oldPath then
            File.Move(oldPath, newPath)
            0UL
        elif Directory.Exists oldPath then
            Directory.Move(oldPath, newPath)
            0UL
        else
            errno -2

    let private sysMkdir (host : ISyscallHost) (pathPtr : uint64) (_mode : uint64) : uint64 =
        let path = readCString host.Memory pathPtr
        if Directory.Exists path || File.Exists path then
            errno -17   // EEXIST
        else
            Directory.CreateDirectory path |> ignore
            0UL

    let private sysRmdir (host : ISyscallHost) (pathPtr : uint64) : uint64 =
        let path = readCString host.Memory pathPtr
        if Directory.Exists path then
            Directory.Delete path
            0UL
        else
            errno -2

    let private sysUnlink (host : ISyscallHost) (pathPtr : uint64) : uint64 =
        let path = readCString host.Memory pathPtr
        if File.Exists path then
            File.Delete path
            0UL
        else
            errno -2

    let private sysGetdents64 (host : ISyscallHost) (fd : uint64) (dirp : uint64) (_count : uint64) : uint64 =
        let stream = streamFor host fd
        match stream with
        | :? DirectoryStream as ds ->
            let entries = DirectoryInfo(ds.Path).GetFileSystemInfos()
            let mutable off = 0UL
            let mutable total = 0UL
            for e in entries do
                let name = e.Name
                let nameBytes = Encoding.UTF8.GetBytes name
                let reclen = uint16 (align8 (24UL + uint64 nameBytes.Length + 1UL))
                let ino =
                    nameBytes
                    |> Array.fold (fun acc b -> (acc * 131UL) + uint64 b) 1UL
                host.Memory.WriteUInt64 (dirp + total) ino
                host.Memory.WriteUInt64 (dirp + total + 8UL) off
                host.Memory.WriteUInt16 (dirp + total + 16UL) reclen
                host.Memory.WriteByte (dirp + total + 18UL) (if e.Attributes.HasFlag FileAttributes.Directory then 4uy else 8uy)
                host.Memory.WriteByte (dirp + total + 19UL) 0uy
                host.Memory.WriteBytes (dirp + total + 20UL) (Array.append nameBytes [| 0uy |])
                total <- total + uint64 reclen
                off <- off + uint64 reclen
            total
        | _ -> errno -9

    let private sysGetrandom (host : ISyscallHost) (buf : uint64) (buflen : uint64) (_flags : uint64) : uint64 =
        let data = Array.zeroCreate<byte> (int buflen)
        RandomNumberGenerator.Fill data
        host.Memory.WriteBytes buf data
        buflen

    let private sysIoctl (_host : ISyscallHost) (_fd : uint64) (_request : uint64) (_arg : uint64) : uint64 =
        errno -25

    let private sysFcntl (host : ISyscallHost) (fd : uint64) (cmd : uint64) (arg : uint64) : uint64 =
        match cmd with
        | 0UL ->   // F_DUPFD : plus petit descripteur libre supérieur ou égal à arg
            try
                let s = streamFor host fd
                let mutable newFd = int arg
                while host.OpenFiles.ContainsKey newFd do
                    newFd <- newFd + 1
                host.OpenFiles.[newFd] <- s
                uint64 newFd
            with _ -> errno -9
        | 1UL -> 0UL   // F_GETFD
        | 2UL -> 0UL   // F_SETFD
        | 3UL -> 0UL   // F_GETFL
        | 4UL -> 0UL   // F_SETFL
        | _ -> errno -22

    let private sysGetrlimit (host : ISyscallHost) (resource : uint64) (rlim : uint64) : uint64 =
        match resource with
        | 7UL ->   // RLIMIT_NOFILE
            host.Memory.WriteUInt64 rlim 4096UL
            host.Memory.WriteUInt64 (rlim + 8UL) 4096UL
            0UL
        | 3UL ->   // RLIMIT_STACK
            let stack = 8UL * 1024UL * 1024UL
            host.Memory.WriteUInt64 rlim stack
            host.Memory.WriteUInt64 (rlim + 8UL) stack
            0UL
        | 9UL ->   // RLIMIT_AS
            host.Memory.WriteUInt64 rlim UInt64.MaxValue
            host.Memory.WriteUInt64 (rlim + 8UL) UInt64.MaxValue
            0UL
        | _ -> errno -22

    let private sysSetTidAddress (_host : ISyscallHost) (_addr : uint64) : uint64 =
        0UL

    let private sysFsync (host : ISyscallHost) (fd : uint64) : uint64 =
        try
            let s = streamFor host fd
            s.Flush ()
            0UL
        with _ -> errno -9

    let private sysFtruncate (host : ISyscallHost) (fd : uint64) (length : uint64) : uint64 =
        try
            let s = streamFor host fd
            if s.CanWrite then
                s.SetLength (int64 length)
                0UL
            else
                errno -9
        with _ -> errno -9

    let private sysFchdir (host : ISyscallHost) (fd : uint64) : uint64 =
        try
            let s = streamFor host fd
            match s with
            | :? DirectoryStream as d ->
                host.CurrentDirectory <- d.Path
                0UL
            | _ -> errno -9
        with _ -> errno -9

    let private sysCreat (host : ISyscallHost) (pathPtr : uint64) (_mode : uint64) : uint64 =
        sysOpenPath host (readCString host.Memory pathPtr) 0x241UL

    let private sysSetrlimit (_host : ISyscallHost) (_resource : uint64) (_rlim : uint64) : uint64 =
        0UL

    let private sysGettid () : uint64 =
        1UL

    let private sysTime (host : ISyscallHost) (tloc : uint64) : uint64 =
        let now = DateTimeOffset.UtcNow.ToUnixTimeSeconds ()
        if tloc <> 0UL then
            host.Memory.WriteUInt64 tloc (uint64 now)
        uint64 now

    // ---------------------------------------------------------------------------
    // Syscalls complémentaires
    // ---------------------------------------------------------------------------

    let private atFdcwd = 0xFFFFFFFFFFFFFF9CUL
    let mutable private currentUmask = 0o22u

    let private sysNoop (_host : ISyscallHost) : uint64 =
        0UL

    // -- Mémoire ----------------------------------------------------------------

    let private sysMsync (_host : ISyscallHost) (_addr : uint64) (_length : uint64) (_flags : uint64) : uint64 =
        0UL

    let private sysMadvise (_host : ISyscallHost) (_addr : uint64) (_length : uint64) (_advice : uint64) : uint64 =
        0UL

    let private sysMincore (host : ISyscallHost) (addr : uint64) (length : uint64) (vec : uint64) : uint64 =
        try
            let pages = alignPage length / 4096UL
            if vec <> 0UL then host.Memory.Zero vec pages
            0UL
        with _ -> errno enosys

    let private sysMremap (host : ISyscallHost) (oldAddr : uint64) (oldSize : uint64) (newSize : uint64) (flags : uint64) (newAddr : uint64) : uint64 =
        let oldLen = alignPage oldSize
        let newLen = alignPage newSize
        let dest =
            if flags &&& mremapFixed <> 0UL then newAddr
            else
                let result = host.MmapCursor
                host.MmapCursor <- host.MmapCursor + newLen
                result
        if dest + newLen > host.Memory.Size then
            errno -12   // ENOMEM
        else
            let copyLen = min oldLen newLen
            if copyLen > 0UL then
                let data = host.Memory.ReadBytes oldAddr (int copyLen)
                host.Memory.WriteBytes dest data
            host.Memory.UnmapRange oldAddr oldLen
            host.Memory.MapRegion dest newLen 0x3UL
            dest

    let private sysFutex (_host : ISyscallHost) (_uaddr : uint64) (op : uint64) (_val : uint64) (_timeout : uint64) (_uaddr2 : uint64) (_val3 : uint64) : uint64 =
        if op &&& 0x7FUL = 1UL then 1UL else 0UL

    // -- Minuterie et temps -----------------------------------------------------

    let private sysGetitimer (host : ISyscallHost) (_which : uint64) (value : uint64) : uint64 =
        if value <> 0UL then host.Memory.Zero value 16UL
        0UL

    let private sysClockGetres (host : ISyscallHost) (clockid : uint64) (res : uint64) : uint64 =
        if clockid > 1UL then errno -22
        else
            if res <> 0UL then
                host.Memory.WriteUInt64 res 0UL
                host.Memory.WriteUInt64 (res + 8UL) 1UL
            0UL

    let private sysClockNanosleep (host : ISyscallHost) (_clockid : uint64) (_flags : uint64) (req : uint64) (rem : uint64) : uint64 =
        sysNanosleep host req rem

    let private sysTimes (host : ISyscallHost) (buf : uint64) : uint64 =
        try
            if buf <> 0UL then
                for i in 0 .. 3 do
                    host.Memory.WriteUInt64 (buf + uint64 i * 8UL) 0UL
            uint64 Environment.TickCount64
        with _ -> errno enosys

    // -- Processus et droits ----------------------------------------------------

    let private sysGetresuid (host : ISyscallHost) (ruid : uint64) (euid : uint64) (suid : uint64) : uint64 =
        if ruid <> 0UL then host.Memory.WriteUInt32 ruid 0u
        if euid <> 0UL then host.Memory.WriteUInt32 euid 0u
        if suid <> 0UL then host.Memory.WriteUInt32 suid 0u
        0UL

    let private sysGetresgid (host : ISyscallHost) (rgid : uint64) (egid : uint64) (sgid : uint64) : uint64 =
        if rgid <> 0UL then host.Memory.WriteUInt32 rgid 0u
        if egid <> 0UL then host.Memory.WriteUInt32 egid 0u
        if sgid <> 0UL then host.Memory.WriteUInt32 sgid 0u
        0UL

    let private sysGetcpu (host : ISyscallHost) (cpuPtr : uint64) (nodePtr : uint64) (_tcache : uint64) : uint64 =
        try
            if cpuPtr <> 0UL then host.Memory.WriteUInt32 cpuPtr 0u
            if nodePtr <> 0UL then host.Memory.WriteUInt32 nodePtr 0u
            0UL
        with _ -> errno enosys

    let private sysUmask (mask : uint64) : uint64 =
        let old = currentUmask
        currentUmask <- uint32 (mask &&& 0xFFFFUL)
        uint64 old

    let private sysPrlimit64 (host : ISyscallHost) (_pid : uint64) (resource : uint64) (_newLimit : uint64) (oldLimit : uint64) : uint64 =
        if oldLimit <> 0UL then
            match resource with
            | 7UL ->
                host.Memory.WriteUInt64 oldLimit 4096UL
                host.Memory.WriteUInt64 (oldLimit + 8UL) 4096UL
            | 3UL ->
                let stack = 8UL <<< 20
                host.Memory.WriteUInt64 oldLimit stack
                host.Memory.WriteUInt64 (oldLimit + 8UL) stack
            | 9UL ->
                host.Memory.WriteUInt64 oldLimit System.UInt64.MaxValue
                host.Memory.WriteUInt64 (oldLimit + 8UL) System.UInt64.MaxValue
            | _ -> ()
        0UL

    let private sysSchedGetaffinity (host : ISyscallHost) (_pid : uint64) (len : uint64) (mask : uint64) : uint64 =
        try
            if mask <> 0UL then
                let n = int (min len 8UL)
                host.Memory.WriteBytes mask (Array.create n 0xFFuy)
            len
        with _ -> errno enosys

    let private sysGetrusage (host : ISyscallHost) (_who : uint64) (usage : uint64) : uint64 =
        try
            if usage <> 0UL then host.Memory.Zero usage 144UL
            0UL
        with _ -> errno enosys

    let private sysSysinfo (host : ISyscallHost) (buf : uint64) : uint64 =
        try
            let total = 256UL <<< 20
            host.Memory.WriteUInt64 (buf + 0UL) (uint64 (Environment.TickCount64 / 1000L))
            host.Memory.WriteUInt64 (buf + 8UL) 0UL
            host.Memory.WriteUInt64 (buf + 16UL) 0UL
            host.Memory.WriteUInt64 (buf + 24UL) 0UL
            host.Memory.WriteUInt64 (buf + 32UL) total
            host.Memory.WriteUInt64 (buf + 40UL) total
            host.Memory.WriteUInt64 (buf + 48UL) 0UL
            host.Memory.WriteUInt64 (buf + 56UL) 0UL
            host.Memory.WriteUInt64 (buf + 64UL) 0UL
            host.Memory.WriteUInt64 (buf + 72UL) 0UL
            host.Memory.WriteUInt16 (buf + 80UL) 1us
            host.Memory.WriteUInt16 (buf + 82UL) 0us
            host.Memory.WriteUInt32 (buf + 84UL) 0u
            host.Memory.WriteUInt32 (buf + 88UL) 0u
            host.Memory.WriteUInt32 (buf + 92UL) 1u
            0UL
        with _ -> errno enosys

    // -- Système de fichiers ----------------------------------------------------

    let private writeStatfs (mem : VirtualMemory) (buf : uint64) : unit =
        mem.WriteUInt64 (buf + 0UL) 0xEF53UL
        mem.WriteUInt64 (buf + 8UL) 4096UL
        mem.WriteUInt64 (buf + 16UL) 1048576UL
        mem.WriteUInt64 (buf + 24UL) 524288UL
        mem.WriteUInt64 (buf + 32UL) 524288UL
        mem.WriteUInt64 (buf + 40UL) 1000UL
        mem.WriteUInt64 (buf + 48UL) 900UL
        mem.WriteUInt32 (buf + 56UL) 0u
        mem.WriteUInt32 (buf + 60UL) 0u
        mem.WriteUInt64 (buf + 64UL) 255UL
        mem.WriteUInt64 (buf + 72UL) 4096UL
        mem.WriteUInt64 (buf + 80UL) 0UL

    let private sysStatfs (host : ISyscallHost) (pathPtr : uint64) (buf : uint64) : uint64 =
        try
            let path = readCString host.Memory pathPtr
            let fullPath = if path.StartsWith "/" then path else Path.Combine (host.CurrentDirectory, path)
            if not (File.Exists fullPath || Directory.Exists fullPath) then errno -2
            else
                writeStatfs host.Memory buf
                0UL
        with _ -> errno -2

    let private sysFstatfs (host : ISyscallHost) (fd : uint64) (buf : uint64) : uint64 =
        try
            streamFor host fd |> ignore
            writeStatfs host.Memory buf
            0UL
        with _ -> errno -9

    let private sysTruncate (host : ISyscallHost) (pathPtr : uint64) (length : uint64) : uint64 =
        try
            let path = readCString host.Memory pathPtr
            let fullPath = if path.StartsWith "/" then path else Path.Combine (host.CurrentDirectory, path)
            use s = new FileStream (fullPath, FileMode.Open, FileAccess.Write)
            s.SetLength (int64 length)
            0UL
        with _ -> errno -2

    let private sysFdatasync (host : ISyscallHost) (fd : uint64) : uint64 =
        try
            (streamFor host fd).Flush ()
            0UL
        with _ -> errno -9

    let private sysGetdents (host : ISyscallHost) (fd : uint64) (dirp : uint64) (count : uint64) : uint64 =
        try
            match streamFor host fd with
            | :? DirectoryStream as ds ->
                let entries = DirectoryInfo(ds.Path).GetFileSystemInfos ()
                let mutable total = 0UL
                let mutable off = 0UL
                for info in entries do
                    let name = info.Name
                    let nameBytes = Encoding.UTF8.GetBytes name
                    let reclen = uint16 (align8 (18UL + uint64 nameBytes.Length + 1UL))
                    if uint64 total + uint64 reclen > count then ()
                    else
                        let ino = nameBytes |> Array.fold (fun acc b -> acc * 131UL + uint64 b) 1UL
                        host.Memory.WriteUInt64 (dirp + total) ino
                        host.Memory.WriteUInt64 (dirp + total + 8UL) off
                        host.Memory.WriteUInt16 (dirp + total + 16UL) reclen
                        host.Memory.WriteBytes (dirp + total + 18UL) (Array.append nameBytes [| 0uy |])
                        total <- total + uint64 reclen
                        off <- off + uint64 reclen
                total
            | _ -> errno -9
        with _ -> errno -9

    let private sysSymlink (host : ISyscallHost) (targetPtr : uint64) (linkPtr : uint64) : uint64 =
        try
            let target = readCString host.Memory targetPtr
            let link = readCString host.Memory linkPtr
            let fullLink = if link.StartsWith "/" then link else Path.Combine (host.CurrentDirectory, link)
            if File.Exists fullLink || Directory.Exists fullLink then errno -17
            else
                File.CreateSymbolicLink (fullLink, target) |> ignore
                0UL
        with
        | :? UnauthorizedAccessException -> errno -1
        | _ -> errno -2

    let private sysReadlink (host : ISyscallHost) (pathPtr : uint64) (buf : uint64) (bufsiz : uint64) : uint64 =
        try
            let path = readCString host.Memory pathPtr
            let fullPath = if path.StartsWith "/" then path else Path.Combine (host.CurrentDirectory, path)
            let target = FileInfo(fullPath).LinkTarget
            if isNull target then errno -22
            else
                let bytes = Encoding.UTF8.GetBytes target
                let n = int (min bufsiz (uint64 bytes.Length))
                host.Memory.WriteBytes buf bytes.[0 .. n - 1]
                uint64 n
        with _ -> errno -2

    // -- Entrées / sorties ------------------------------------------------------

    let private sysPread (host : ISyscallHost) (fd : uint64) (buf : uint64) (count : uint64) (offset : uint64) : uint64 =
        try
            let s = streamFor host fd
            if not s.CanRead then errno -9
            else
                let saved = if s.CanSeek then s.Position else 0L
                if s.CanSeek then s.Seek (int64 offset, SeekOrigin.Begin) |> ignore
                let data = Array.zeroCreate<byte> (int count)
                let n = s.Read (data, 0, int count)
                if s.CanSeek then s.Position <- saved
                host.Memory.WriteBytes buf data.[0 .. n - 1]
                uint64 n
        with _ -> errno enosys

    let private sysPwrite (host : ISyscallHost) (fd : uint64) (buf : uint64) (count : uint64) (offset : uint64) : uint64 =
        try
            let s = streamFor host fd
            if not s.CanWrite then errno -9
            else
                let data = host.Memory.ReadBytes buf (int count)
                let saved = if s.CanSeek then s.Position else 0L
                if s.CanSeek then s.Seek (int64 offset, SeekOrigin.Begin) |> ignore
                s.Write (data, 0, data.Length)
                s.Flush ()
                if s.CanSeek then s.Position <- saved
                uint64 count
        with _ -> errno enosys

    let private sysSendfile (host : ISyscallHost) (outFd : uint64) (inFd : uint64) (offsetPtr : uint64) (count : uint64) : uint64 =
        try
            let src = streamFor host inFd
            let dst = streamFor host outFd
            let saved = if src.CanSeek then src.Position else 0L
            if offsetPtr <> 0UL && src.CanSeek then
                src.Seek (int64 (host.Memory.ReadUInt64 offsetPtr), SeekOrigin.Begin) |> ignore
            let buffer = Array.zeroCreate<byte> 8192
            let mutable total = 0UL
            let mutable finished = false
            while not finished do
                let remaining = count - total
                if remaining <= 0UL then finished <- true
                else
                    let toRead = int (min remaining (uint64 buffer.Length))
                    let n = src.Read (buffer, 0, toRead)
                    if n <= 0 then finished <- true
                    else
                        dst.Write (buffer, 0, n)
                        total <- total + uint64 n
            dst.Flush ()
            if offsetPtr <> 0UL && src.CanSeek then
                host.Memory.WriteUInt64 offsetPtr (uint64 src.Position)
            if src.CanSeek then src.Position <- saved
            total
        with _ -> errno enosys

    // -- poll / select ----------------------------------------------------------

    let private sysPoll (host : ISyscallHost) (fds : uint64) (nfds : uint64) (_timeout : uint64) : uint64 =
        try
            let mutable ready = 0UL
            for i in 0 .. int nfds - 1 do
                let entry = fds + (uint64 i) * 8UL
                let fd = int (host.Memory.ReadUInt32 entry)
                let events = host.Memory.ReadUInt16 (entry + 4UL)
                let revents =
                    try
                        let s = streamFor host (uint64 fd)
                        if events = 0us then 0us else events
                    with _ -> 0x20us
                host.Memory.WriteUInt16 (entry + 6UL) revents
                if revents <> 0us then ready <- ready + 1UL
            ready
        with _ -> errno enosys

    let private fdSetContains (set : byte[]) (fd : int) : bool =
        let index = fd / 8
        let bit = fd % 8
        index < set.Length && (set.[index] &&& (1uy <<< bit)) <> 0uy

    let private sysSelect (host : ISyscallHost) (nfds : uint64) (readfds : uint64) (writefds : uint64) (exceptfds : uint64) (_timeout : uint64) : uint64 =
        try
            let rd = if readfds = 0UL then Array.zeroCreate 16 else host.Memory.ReadBytes readfds 16
            let wr = if writefds = 0UL then Array.zeroCreate 16 else host.Memory.ReadBytes writefds 16
            let ex = if exceptfds = 0UL then Array.zeroCreate 16 else host.Memory.ReadBytes exceptfds 16
            let clearBit (set : byte[]) (fd : int) =
                set.[fd / 8] <- set.[fd / 8] &&& ~~~(1uy <<< (fd % 8))
            let mutable count = 0UL
            for fd in 0 .. int nfds - 1 do
                let ok =
                    try
                        streamFor host (uint64 fd) |> ignore
                        true
                    with _ -> false
                if ok then
                    if readfds <> 0UL && fdSetContains rd fd then count <- count + 1UL
                    if writefds <> 0UL && fdSetContains wr fd then count <- count + 1UL
                    if exceptfds <> 0UL then clearBit ex fd
                else
                    if readfds <> 0UL then clearBit rd fd
                    if writefds <> 0UL then clearBit wr fd
                    if exceptfds <> 0UL then clearBit ex fd
            if readfds <> 0UL then host.Memory.WriteBytes readfds rd
            if writefds <> 0UL then host.Memory.WriteBytes writefds wr
            if exceptfds <> 0UL then host.Memory.Zero exceptfds 16UL
            count
        with _ -> errno enosys

    // -- Wrappers AT_FDCWD ------------------------------------------------------

    let private sysNewfstatat (host : ISyscallHost) (dirfd : uint64) (path : uint64) (buf : uint64) (_flags : uint64) : uint64 =
        if dirfd = atFdcwd then sysStat host path buf else errno -9

    let private sysMkdirat (host : ISyscallHost) (dirfd : uint64) (path : uint64) (mode : uint64) : uint64 =
        if dirfd = atFdcwd then sysMkdir host path mode else errno -9

    let private sysUnlinkat (host : ISyscallHost) (dirfd : uint64) (path : uint64) (_flags : uint64) : uint64 =
        if dirfd = atFdcwd then sysUnlink host path else errno -9

    let private sysRenameat (host : ISyscallHost) (olddirfd : uint64) (oldpath : uint64) (newdirfd : uint64) (newpath : uint64) : uint64 =
        if olddirfd = atFdcwd && newdirfd = atFdcwd then sysRename host oldpath newpath else errno -9

    let private sysSymlinkat (host : ISyscallHost) (target : uint64) (_newdirfd : uint64) (link : uint64) : uint64 =
        sysSymlink host target link

    let private sysReadlinkat (host : ISyscallHost) (_dirfd : uint64) (path : uint64) (buf : uint64) (bufsiz : uint64) : uint64 =
        sysReadlink host path buf bufsiz

    let private sysFchmodat (host : ISyscallHost) (dirfd : uint64) (_path : uint64) (_mode : uint64) : uint64 =
        if dirfd = atFdcwd then 0UL else errno -9

    let private sysFaccessat (host : ISyscallHost) (dirfd : uint64) (path : uint64) (_mode : uint64) : uint64 =
        if dirfd = atFdcwd then sysAccess host path else errno -9

    let private sysFchownat (host : ISyscallHost) (dirfd : uint64) (_path : uint64) (_uid : uint64) (_gid : uint64) (_flags : uint64) : uint64 =
        if dirfd = atFdcwd then 0UL else errno -9

    let private sysPselect6 (host : ISyscallHost) (nfds : uint64) (readfds : uint64) (writefds : uint64) (exceptfds : uint64) : uint64 =
        sysSelect host nfds readfds writefds exceptfds 0UL

    let private sysPpoll (host : ISyscallHost) (fds : uint64) (nfds : uint64) : uint64 =
        sysPoll host fds nfds 0UL

    /// Exécute le syscall désigné par RAX et écrit le résultat dans RAX.
    let dispatch (host : ISyscallHost) : unit =
        let r = host.Registers
        let num = r.RAX
        let a1 = Registers.get64 r 7   // rdi
        let a2 = Registers.get64 r 6   // rsi
        let a3 = Registers.get64 r 2   // rdx
        let a4 = Registers.get64 r 10  // r10
        let a5 = Registers.get64 r 8   // r8
        let a6 = Registers.get64 r 9   // r9

        let result : uint64 =
            match num with
            | 0UL -> sysRead host a1 a2 a3
            | 1UL -> sysWrite host a1 a2 a3
            | 2UL -> sysOpen host a1 a2
            | 3UL -> sysClose host a1
            | 4UL -> sysStat host a1 a2
            | 5UL -> sysFstat host a1 a2
            | 6UL -> sysStat host a1 a2
            | 8UL -> sysLseek host a1 a2 a3
            | 9UL -> sysMmap host a1 a2 a3 a4 a5 a6
            | 10UL -> sysMprotect host a1 a2 a3
            | 11UL -> sysMunmap host a1 a2
            | 12UL -> sysBrk host a1
            | 19UL -> sysReadv host a1 a2 a3
            | 20UL -> sysWritev host a1 a2 a3
            | 21UL -> sysAccess host a1
            | 22UL -> sysPipe host a1
            | 32UL -> sysDup host a1
            | 33UL -> sysDup2 host a1 a2
            | 35UL -> sysNanosleep host a1 a2
            | 39UL -> sysGetpid ()
            | 57UL -> sysFork host
            | 59UL -> sysExecve host a1 a2 a3
            | 60UL -> sysExit host a1
            | 61UL -> sysWait4 host a1 a2 a3
            | 63UL -> sysUname host a1
            | 79UL -> sysGetcwd host a1 a2
            | 80UL -> sysChdir host a1
            | 82UL -> sysRename host a1 a2
            | 83UL -> sysMkdir host a1 a2
            | 84UL -> sysRmdir host a1
            | 87UL -> sysUnlink host a1
            | 96UL -> sysGettimeofday host a1
            | 16UL -> sysIoctl host a1 a2 a3
            | 72UL -> sysFcntl host a1 a2 a3
            | 97UL -> sysGetrlimit host a1 a2
            | 218UL -> sysSetTidAddress host a1
            | 74UL -> sysFsync host a1
            | 77UL -> sysFtruncate host a1 a2
            | 81UL -> sysFchdir host a1
            | 85UL -> sysCreat host a1 a2
            | 160UL -> sysSetrlimit host a1 a2
            | 186UL -> sysGettid ()
            | 201UL -> sysTime host a1
            | 102UL -> sysGetuid ()
            | 104UL -> sysGetgid ()
            | 107UL -> sysGeteuid ()
            | 108UL -> sysGetegid ()
            | 110UL -> sysGetppid ()
            | 217UL -> sysGetdents64 host a1 a2 a3
            | 228UL -> sysClockGettime host a1 a2
            | 231UL -> sysExitGroup host a1
            | 257UL -> sysOpenat host a1 a2 a3
            | 292UL -> sysDup3 host a1 a2 a3
            | 293UL -> sysPipe2 host a1 a2
            | 318UL -> sysGetrandom host a1 a2 a3
            | 7UL -> sysPoll host a1 a2 a3
            | 17UL -> sysPread host a1 a2 a3 a4
            | 18UL -> sysPwrite host a1 a2 a3 a4
            | 23UL -> sysSelect host a1 a2 a3 a4 a5
            | 25UL -> sysMremap host a1 a2 a3 a4 a5
            | 26UL -> sysMsync host a1 a2 a3
            | 27UL -> sysMincore host a1 a2 a3
            | 28UL -> sysMadvise host a1 a2 a3
            | 36UL -> sysGetitimer host a1 a2
            | 40UL -> sysSendfile host a1 a2 a3 a4
            | 75UL -> sysFdatasync host a1
            | 76UL -> sysTruncate host a1 a2
            | 78UL -> sysGetdents host a1 a2 a3
            | 88UL -> sysSymlink host a1 a2
            | 89UL -> sysReadlink host a1 a2 a3
            | 95UL -> sysUmask a1
            | 98UL -> sysGetrusage host a1 a2
            | 99UL -> sysSysinfo host a1
            | 100UL -> sysTimes host a1
            | 118UL -> sysGetresuid host a1 a2 a3
            | 120UL -> sysGetresgid host a1 a2 a3
            | 137UL -> sysStatfs host a1 a2
            | 138UL -> sysFstatfs host a1 a2
            | 202UL -> sysFutex host a1 a2 a3 a4 a5 a6
            | 204UL -> sysSchedGetaffinity host a1 a2 a3
            | 229UL -> sysClockGetres host a1 a2
            | 230UL -> sysClockNanosleep host a1 a2 a3 a4
            | 258UL -> sysMkdirat host a1 a2 a3
            | 260UL -> sysFchownat host a1 a2 a3 a4 a5
            | 262UL -> sysNewfstatat host a1 a2 a3 a4
            | 263UL -> sysUnlinkat host a1 a2 a3
            | 264UL -> sysRenameat host a1 a2 a3 a4
            | 266UL -> sysSymlinkat host a1 a2 a3
            | 267UL -> sysReadlinkat host a1 a2 a3 a4
            | 268UL -> sysFchmodat host a1 a2 a3
            | 269UL -> sysFaccessat host a1 a2 a3
            | 270UL -> sysPselect6 host a1 a2 a3 a4
            | 271UL -> sysPpoll host a1 a2
            | 302UL -> sysPrlimit64 host a1 a2 a3 a4
            | 309UL -> sysGetcpu host a1 a2 a3
            // No-op documentés (retournent 0)
            | 13UL | 14UL | 15UL | 24UL | 37UL | 38UL | 62UL | 73UL -> sysNoop host
            | 103UL | 105UL | 106UL | 109UL | 111UL | 112UL | 113UL | 114UL -> sysNoop host
            | 115UL | 116UL | 117UL | 119UL | 121UL | 122UL | 123UL | 124UL -> sysNoop host
            | 127UL | 130UL | 131UL | 132UL | 135UL | 140UL | 141UL -> sysNoop host
            | 142UL | 143UL | 144UL | 145UL | 146UL | 147UL | 148UL -> sysNoop host
            | 149UL | 150UL | 151UL | 152UL | 153UL | 157UL | 158UL | 159UL -> sysNoop host
            | 162UL | 164UL | 187UL | 200UL | 203UL | 216UL | 221UL | 227UL -> sysNoop host
            | 234UL | 235UL | 251UL | 252UL | 261UL | 273UL | 274UL | 280UL -> sysNoop host
            | 314UL | 315UL | 324UL | 325UL -> sysNoop host
            // Stubs -EPERM (opérations privilégiées interdites)
            | 155UL | 161UL | 163UL | 165UL | 166UL | 167UL | 168UL | 169UL -> errno -1
            | 170UL | 171UL | 172UL | 173UL | 175UL | 176UL | 272UL | 308UL -> errno -1
            // Stubs -ENOSYS (non implémentés dans cet émulateur)
            | 29UL | 30UL | 31UL | 34UL | 41UL | 42UL | 43UL | 44UL | 45UL -> errno enosys
            | 46UL | 47UL | 48UL | 49UL | 50UL | 51UL | 52UL | 53UL | 54UL | 55UL -> errno enosys
            | 56UL | 58UL | 64UL | 65UL | 66UL | 67UL | 68UL | 69UL | 70UL | 71UL -> errno enosys
            | 86UL | 101UL | 125UL | 126UL | 128UL | 129UL | 133UL | 134UL -> errno enosys
            | 136UL | 139UL | 154UL | 156UL | 174UL | 177UL | 178UL | 179UL -> errno enosys
            | 180UL | 181UL | 182UL | 183UL | 184UL | 185UL | 188UL | 189UL | 190UL | 191UL -> errno enosys
            | 192UL | 193UL | 194UL | 195UL | 196UL | 197UL | 198UL | 199UL -> errno enosys
            | 205UL | 206UL | 207UL | 208UL | 209UL | 210UL | 211UL | 212UL | 213UL -> errno enosys
            | 214UL | 215UL | 219UL | 220UL | 222UL | 223UL | 224UL | 225UL | 226UL -> errno enosys
            | 232UL | 233UL | 236UL | 237UL | 238UL | 239UL | 240UL | 241UL -> errno enosys
            | 242UL | 243UL | 244UL | 245UL | 246UL | 247UL | 248UL | 249UL | 250UL -> errno enosys
            | 253UL | 254UL | 255UL | 256UL | 265UL | 275UL | 276UL | 277UL | 278UL | 279UL -> errno enosys
            | 281UL | 282UL | 283UL | 284UL | 285UL | 286UL | 287UL | 288UL | 289UL -> errno enosys
            | 290UL | 291UL | 294UL | 295UL | 296UL | 297UL | 298UL | 299UL -> errno enosys
            | 300UL | 301UL | 303UL | 304UL | 305UL | 306UL | 307UL | 310UL | 311UL -> errno enosys
            | 312UL | 313UL | 316UL | 317UL | 319UL | 320UL | 321UL | 322UL | 323UL -> errno enosys
            | 326UL | 327UL | 328UL | 329UL | 330UL | 331UL | 332UL | 333UL | 334UL | 335UL | 336UL -> errno enosys
            | 424UL | 425UL | 426UL | 427UL | 428UL | 429UL | 430UL | 431UL | 432UL | 433UL -> errno enosys
            | 434UL | 435UL | 436UL | 437UL | 438UL | 439UL | 440UL | 441UL | 442UL | 443UL -> errno enosys
            | 444UL | 445UL | 446UL | 447UL | 448UL | 449UL | 450UL | 451UL | 452UL | 453UL -> errno enosys
            | 454UL | 455UL | 456UL | 457UL | 458UL | 459UL | 460UL | 461UL | 462UL | 463UL -> errno enosys
            | 464UL | 465UL | 466UL | 467UL | 468UL | 469UL | 470UL | 471UL -> errno enosys
            | _ -> errno enosys

        r.RAX <- result
