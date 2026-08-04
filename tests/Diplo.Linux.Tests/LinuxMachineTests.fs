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
