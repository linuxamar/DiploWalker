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

/// Région mémoire allouée (mmap/mprotect).
type MemoryRegion =
    { Start : uint64
      Length : uint64
      Prot : uint64 }

/// Copie de l'état d'une tâche (fork).
type TaskState =
    { Memory : byte[]
      Registers : Registers
      OpenFiles : Dictionary<int, Stream>
      NextFd : int
      ProgramBreak : uint64
      MmapCursor : uint64 }

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
            | true, _ ->
                host.OpenFiles.Remove(int fd) |> ignore
                0UL
            | false, _ -> errno -9
        with
        | _ -> errno enosys

    let private alignPage (length : uint64) : uint64 =
        (length + 0xFFFUL) &&& ~~~0xFFFUL

    let private align8 (length : uint64) : uint64 =
        (length + 7UL) &&& ~~~7UL

    let private addRegion (host : ISyscallHost) (start : uint64) (length : uint64) (prot : uint64) =
        let remaining =
            host.Regions
            |> Seq.filter (fun r -> not (start + length > r.Start && start < r.Start + r.Length))
            |> Seq.toArray
        host.Regions.Clear()
        host.Regions.AddRange remaining
        host.Regions.Add({ Start = start; Length = length; Prot = prot })

    let private sysMmap (host : ISyscallHost) (addr : uint64) (length : uint64)
                        (prot : uint64) (flags : uint64) (fd : uint64) (offset : uint64) : uint64 =
        let pageLen = alignPage length
        let dest =
            if addr <> 0UL then addr
            elif flags &&& 0x10UL <> 0UL then addr   // MAP_FIXED
            else
                let result = host.MmapCursor
                host.MmapCursor <- host.MmapCursor + pageLen
                result
        if dest + pageLen > host.Memory.Size then
            errno -12   // ENOMEM
        else
            addRegion host dest pageLen prot
            if fd <> 0xFFFFFFFFFFFFFFFFUL then   // MAP_ANONYMOUS
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
        host.Memory.Zero addr pageLen
        host.Regions
        |> Seq.filter (fun r -> not (addr + pageLen > r.Start && addr < r.Start + r.Length))
        |> Seq.toArray
        |> Array.iter (fun r -> host.Regions.Remove r |> ignore)
        0UL

    let private sysMprotect (host : ISyscallHost) (addr : uint64) (length : uint64) (prot : uint64) : uint64 =
        let pageLen = alignPage length
        let updated =
            host.Regions
            |> Seq.map (fun r ->
                if addr + pageLen > r.Start && addr < r.Start + r.Length then
                    { r with Prot = prot }
                else r)
            |> Seq.toArray
        host.Regions.Clear()
        host.Regions.AddRange updated
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
            | _ -> errno enosys

        r.RAX <- result
