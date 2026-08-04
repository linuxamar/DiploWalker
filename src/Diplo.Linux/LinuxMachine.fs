namespace Diplo.Linux

open System
open System.Collections.Generic
open System.IO
open System.Text

/// Machine Linux émulée sur Windows : charge l'ELF, met en place la pile,
/// puis exécute le programme en traduisant les syscalls vers Windows.
type LinuxMachine(image : byte[], arguments : string[], ?memorySize : uint64) =
    let size = defaultArg memorySize (256UL <<< 20)
    let mem = VirtualMemory(size)
    let regs = Registers.create ()
    let openFiles = Dictionary<int, Stream>()
    let mutable nextFd = 3
    let mutable programBreak = 0UL
    let mutable mmapCursor = 0x0E000000UL
    let mutable halted = false
    let mutable exitStatus = 0
    let mutable stdin : Stream = Console.OpenStandardInput()
    let mutable stdout : Stream = Console.OpenStandardOutput()
    let mutable stderr : Stream = Console.OpenStandardError()

    interface ISyscallHost with
        member _.Memory = mem
        member _.Registers = regs
        member _.StandardInput = stdin
        member _.StandardOutput = stdout
        member _.StandardError = stderr
        member _.OpenFiles = openFiles
        member _.NextFd
            with get () = nextFd
            and set v = nextFd <- v
        member _.ProgramBreak
            with get () = programBreak
            and set v = programBreak <- v
        member _.MmapCursor
            with get () = mmapCursor
            and set v = mmapCursor <- v
        member _.Halted
            with get () = halted
            and set v = halted <- v
        member _.ExitStatus
            with get () = exitStatus
            and set v = exitStatus <- v

    /// Entrée standard de la machine (redirigeable pour les tests).
    member _.StandardInput
        with get () = stdin
        and set v = stdin <- v

    /// Sortie standard de la machine (redirigeable pour les tests).
    member _.StandardOutput
        with get () = stdout
        and set v = stdout <- v

    /// Sortie d'erreur de la machine (redirigeable pour les tests).
    member _.StandardError
        with get () = stderr
        and set v = stderr <- v

    member private _.SetupStack (stackTop : uint64) : uint64 =
        let mutable sp = stackTop

        let pushCString (s : string) : uint64 =
            let bytes = Encoding.UTF8.GetBytes(s + "\u0000")
            sp <- sp - uint64 bytes.Length
            mem.WriteBytes sp bytes
            sp

        let argvAddrs = Array.map pushCString arguments
        sp <- sp &&& ~~~0xFUL

        let push64v (v : uint64) =
            sp <- sp - 8UL
            mem.WriteUInt64 sp v

        push64v 0UL
        push64v 0UL
        push64v 0UL
        push64v 0UL
        for i = argvAddrs.Length - 1 downto 0 do
            push64v argvAddrs[i]
        push64v (uint64 argvAddrs.Length)
        sp

    /// Exécute le programme et retourne le code de sortie.
    member this.Run() : int =
        let image = ElfLoader.load mem image
        programBreak <- image.EndOfData

        let stackTop = size - 0x1000UL
        let rsp = this.SetupStack stackTop

        regs.RIP <- image.EntryPoint
        regs.RSP <- rsp

        Emulator.run (this :> ISyscallHost)
        exitStatus

    /// Dispose les descripteurs ouverts.
    interface IDisposable with
        member _.Dispose() =
            for kv in openFiles do
                kv.Value.Dispose()
            openFiles.Clear()
            stdin.Dispose()
            stdout.Dispose()
            stderr.Dispose()
