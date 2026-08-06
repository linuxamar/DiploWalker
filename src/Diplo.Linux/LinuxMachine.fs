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
    let mutable consoleInput : Stream = stdin
    let mutable consoleOutput : Stream = stdout
    let taskQueue = ResizeArray<TaskState>()
    let exitedStatuses = ResizeArray<int>()
    let mutable needsSwitch = false
    let mutable cwd = Directory.GetCurrentDirectory()
    let breakpoints = ResizeArray<uint64>()
    let mutable syscallHook : (uint64 -> unit) option = None
    let mutable imageLoaded = false

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
        member _.Console = new ConsoleStream(consoleInput, consoleOutput) :> Stream
        member _.Regions = mem.Regions
        member _.TaskQueue = taskQueue
        member _.ExitedStatuses = exitedStatuses
        member _.NeedsSwitch
            with get () = needsSwitch
            and set v = needsSwitch <- v
        member _.CurrentDirectory
            with get () = cwd
            and set v = cwd <- v
        member _.Arguments = arguments
        member _.Snapshot() : TaskState =
            { Memory = mem.Snapshot()
              Registers = regs.Clone()
              OpenFiles = Dictionary<int, Stream>(openFiles)
              NextFd = nextFd
              ProgramBreak = programBreak
              MmapCursor = mmapCursor
              Regions = List.ofSeq mem.Regions }
        member _.Restore(st : TaskState) =
            mem.Restore st.Memory
            for i = 0 to 15 do
                Registers.set64 regs i (Registers.get64 st.Registers i)
            regs.RIP <- st.Registers.RIP
            regs.RFLAGS <- st.Registers.RFLAGS
            for i = 0 to 15 do
                Registers.setXmm regs i (st.Registers.Xmm i)
            openFiles.Clear()
            for kv in st.OpenFiles do
                openFiles[kv.Key] <- kv.Value
            nextFd <- st.NextFd
            programBreak <- st.ProgramBreak
            mmapCursor <- st.MmapCursor
            mem.Regions.Clear()
            mem.Regions.AddRange st.Regions
        member this.ExecImage (bytes : byte[]) (argv : string[]) =
            let image = ElfLoader.load mem bytes
            programBreak <- image.EndOfData
            let stackTop = size - 0x1000UL
            let rsp = this.SetupStack stackTop argv
            regs.RIP <- image.EntryPoint
            regs.RSP <- rsp

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

    /// Entrée de la console série (redirigeable pour les tests).
    member _.ConsoleInput
        with get () = consoleInput
        and set v = consoleInput <- v

    /// Sortie de la console série (redirigeable pour les tests).
    member _.ConsoleOutput
        with get () = consoleOutput
        and set v = consoleOutput <- v

    /// Adresses des points d'arrêt (le pas à pas s'arrête avant d'exécuter
    /// une instruction dont l'adresse figure dans cette liste).
    member _.Breakpoints = breakpoints

    /// Pointeur d'instruction courant.
    member _.Rip
        with get () = regs.RIP

    /// Code de sortie du programme (après exécution).
    member _.ExitStatus
        with get () = exitStatus

    /// La machine est arrêtée (programme terminé).
    member _.IsHalted
        with get () = halted

    /// Rappel invoqué à chaque syscall ; il reçoit le numéro du syscall.
    member _.SyscallHook
        with get () = syscallHook
        and set v = syscallHook <- v

    /// Exécute une seule instruction ; retourne false si la machine est arrêtée
    /// (programme terminé ou point d'arrêt atteint et non encore exécuté).
    member this.Step() : bool =
        if not imageLoaded then
            this.LoadImage()
        if halted || breakpoints.Contains regs.RIP then
            false
        else
            Emulator.step (this :> ISyscallHost) syscallHook |> ignore
            not halted

    member private _.SetupStack (stackTop : uint64) (argv : string[]) : uint64 =
        let mutable sp = stackTop

        let pushCString (s : string) : uint64 =
            let bytes = Encoding.UTF8.GetBytes(s + "\u0000")
            sp <- sp - uint64 bytes.Length
            mem.WriteBytes sp bytes
            sp

        let argvAddrs = Array.map pushCString argv
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

    /// Charge l'image ELF et initialise RIP/RSP ; appelé par Run et Step.
    member private this.LoadImage () =
        let image = ElfLoader.load mem image
        programBreak <- image.EndOfData
        let stackTop = size - 0x1000UL
        let rsp = this.SetupStack stackTop arguments
        regs.RIP <- image.EntryPoint
        regs.RSP <- rsp
        imageLoaded <- true

    /// Exécute le programme et retourne le code de sortie.
    member this.Run() : int =
        this.LoadImage()
        Emulator.run (this :> ISyscallHost) syscallHook
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
