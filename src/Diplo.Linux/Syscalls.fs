namespace Diplo.Linux

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Threading

/// Hôte exposé au traducteur de syscalls.
type ISyscallHost =
    abstract Memory : VirtualMemory
    abstract Registers : Registers
    abstract StandardInput : Stream
    abstract StandardOutput : Stream
    abstract StandardError : Stream
    abstract OpenFiles : Dictionary<int, Stream>
    abstract NextFd : int with get, set
    abstract ProgramBreak : uint64 with get, set
    abstract MmapCursor : uint64 with get, set
    abstract Halted : bool with get, set
    abstract ExitStatus : int with get, set

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
        if path = "" then
            errno -2
        else
            let access = fileAccess flags
            let create = flags &&& 0x40UL <> 0UL   // O_CREAT
            let trunc = flags &&& 0x200UL <> 0UL   // O_TRUNC
            let append = flags &&& 0x400UL <> 0UL  // O_APPEND
            let mode =
                if trunc then FileMode.Create
                elif create then FileMode.OpenOrCreate
                else FileMode.Open
            let fs = new FileStream(path, mode, access)
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
            let size = stream.Length
            let now = uint64 (DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            let m = host.Memory
            m.WriteUInt64 (buf + 0UL) 0UL                 // st_dev
            m.WriteUInt64 (buf + 8UL) 0UL                 // st_ino
            m.WriteUInt64 (buf + 16UL) 1UL                // st_nlink
            m.WriteUInt32 (buf + 24UL) 0x81A4u            // st_mode 0100644
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
            let path = Directory.GetCurrentDirectory()
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

    let private sysMmap (host : ISyscallHost) (addr : uint64) (length : uint64)
                        (prot : uint64) (flags : uint64) (fd : uint64) (offset : uint64) : uint64 =
        let dest =
            if addr <> 0UL then addr
            else
                let result = host.MmapCursor
                host.MmapCursor <- host.MmapCursor + length
                result
        if fd <> 0xFFFFFFFFFFFFFFFFUL then   // MAP_ANONYMOUS
            match host.OpenFiles.TryGetValue(int fd) with
            | true, s ->
                let oldPos = s.Position
                s.Position <- int64 offset
                let data = Array.zeroCreate (int length)
                let n = s.Read(data, 0, data.Length)
                if n > 0 then
                    host.Memory.WriteBytes dest data[0 .. n - 1]
                s.Position <- oldPos
            | false, _ -> ()
        dest

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
        host.Halted <- true
        host.ExitStatus <- int status
        host.ExitStatus |> uint64

    let private sysExitGroup (host : ISyscallHost) (status : uint64) : uint64 =
        host.Halted <- true
        host.ExitStatus <- int status
        host.ExitStatus |> uint64

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
            | 5UL -> sysFstat host a1 a2
            | 8UL -> sysLseek host a1 a2 a3
            | 9UL -> sysMmap host a1 a2 a3 a4 a5 a6
            | 10UL -> 0UL
            | 11UL -> 0UL
            | 12UL -> sysBrk host a1
            | 19UL -> sysReadv host a1 a2 a3
            | 20UL -> sysWritev host a1 a2 a3
            | 21UL -> sysAccess host a1
            | 22UL -> sysPipe host a1
            | 32UL -> sysDup host a1
            | 33UL -> sysDup2 host a1 a2
            | 35UL -> sysNanosleep host a1 a2
            | 39UL -> sysGetpid ()
            | 60UL -> sysExit host a1
            | 63UL -> sysUname host a1
            | 79UL -> sysGetcwd host a1 a2
            | 96UL -> sysGettimeofday host a1
            | 102UL -> sysGetuid ()
            | 104UL -> sysGetgid ()
            | 107UL -> sysGeteuid ()
            | 108UL -> sysGetegid ()
            | 110UL -> sysGetppid ()
            | 228UL -> sysClockGettime host a1 a2
            | 231UL -> sysExitGroup host a1
            | 257UL -> sysOpenat host a1 a2 a3
            | 292UL -> sysDup3 host a1 a2 a3
            | 293UL -> sysPipe2 host a1 a2
            | _ -> errno enosys

        r.RAX <- result
