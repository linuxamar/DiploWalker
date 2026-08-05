namespace Diplo.Linux.Tests

open System
open System.IO
open System.Text
open Xunit
open FsUnit.Xunit
open Diplo.Linux

module LinuxMachineTests =

    let private runWithStdout (image : byte[]) (args : string[]) : int * string =
        use machine = new LinuxMachine(image, args)
        use buffer = new MemoryStream()
        machine.StandardOutput <- buffer
        let code = machine.Run()
        let text = Encoding.UTF8.GetString(buffer.ToArray())
        code, text

    [<Fact>]
    let ``Le programme « hello » écrit sur la sortie standard et retourne 42`` () =
        let image = ElfTest.create ElfTest.helloWorldCode 0x400000UL 0x400000UL
        let code, text = runWithStdout image [||]
        code |> should equal 42
        text |> should equal "Hello, Linux\n"

    [<Fact>]
    let ``Le programme reçoit ses arguments sur la pile`` () =
        let image = ElfTest.create ElfTest.argvCode 0x400000UL 0x400000UL
        let code, text = runWithStdout image [| "prog"; "bonjour" |]
        code |> should equal 0
        text |> should equal "bonjour"

    [<Fact>]
    let ``Un fichier non ELF déclenche une erreur de validation`` () =
        let image = Array.zeroCreate 128
        image[0] <- 0x00uy
        image[1] <- 0x41uy
        use machine = new LinuxMachine(image, [||])
        (fun () -> machine.Run() |> ignore) |> should throw typeof<ArgumentException>

    let private runWithConsole (code : byte[]) : int * string =
        use machine = new LinuxMachine(ElfTest.create code 0x400000UL 0x400000UL, [||])
        use output = new MemoryStream()
        machine.ConsoleOutput <- output
        let code = machine.Run()
        let text = Encoding.UTF8.GetString(output.ToArray())
        code, text

    [<Fact>]
    let ``La console série reçoit l'écriture vers /dev/console`` () =
        let code, text = runWithConsole ElfTest.consoleWriteCode
        code |> should equal 0
        text |> should equal "Bonjour console\n"

    [<Fact>]
    let ``La console série relaie la lecture depuis /dev/console`` () =
        use machine = new LinuxMachine(ElfTest.create ElfTest.consoleEchoCode 0x400000UL 0x400000UL, [||])
        use input = new MemoryStream(Encoding.UTF8.GetBytes "Salut")
        use output = new MemoryStream()
        machine.ConsoleInput <- input
        machine.ConsoleOutput <- output
        let code = machine.Run()
        code |> should equal 0
        Encoding.UTF8.GetString(output.ToArray()) |> should equal "Salut"

    [<Fact>]
    let ``mmap anonyme retourne une adresse alignée sur une page`` () =
        let image = ElfTest.create ElfTest.mmapAnonCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``mmap, mprotect et munmap se déroulent sans erreur`` () =
        let image = ElfTest.create ElfTest.mmapLifecycleCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``mmap lit le contenu d'un fichier`` () =
        let path = Path.GetTempFileName()
        try
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes "Bonjour")
            let image = ElfTest.create (ElfTest.mmapFileCode path) 0x400000UL 0x400000UL
            let code, text = runWithStdout image [||]
            code |> should equal 0
            text |> should equal "Bonjou"
        finally
            File.Delete path

    [<Fact>]
    let ``fork : l'enfant sort avec 3 et le parent avec 7`` () =
        let image = ElfTest.create ElfTest.forkCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 3

    [<Fact>]
    let ``wait4 sans enfant terminé retourne -ECHILD`` () =
        let image = ElfTest.create ElfTest.wait4EmptyCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0

    [<Fact>]
    let ``getrandom remplit le tampon demandé`` () =
        let image = ElfTest.create ElfTest.getrandomCode 0x400000UL 0x400000UL
        let code, _ = runWithStdout image [||]
        code |> should equal 0
