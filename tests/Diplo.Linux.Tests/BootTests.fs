namespace Diplo.Linux.Tests

open System.IO
open System.Text
open Xunit
open FsUnit.Xunit
open Diplo.Linux

module BootTests =

    let private runKernel (messages : string list) : int * string =
        let image = BootImage.createKernel messages
        use machine = new LinuxMachine(image, [||])
        use buffer = new MemoryStream()
        machine.StandardOutput <- buffer
        let code = machine.Run()
        let text = Encoding.UTF8.GetString(buffer.ToArray())
        code, text

    [<Fact>]
    let ``Le noyau écrit sa bannière de démarrage puis s'arrête avec le code 0`` () =
        let code, text = runKernel [ "Linux diplo boot\n" ]
        code |> should equal 0
        text |> should equal "Linux diplo boot\n"

    [<Fact>]
    let ``Le noyau écrit plusieurs messages dans l'ordre de la séquence de boot`` () =
        let code, text = runKernel [ "Booting kernel\n"; "Starting init\n"; "System ready\n" ]
        code |> should equal 0
        text |> should equal "Booting kernel\nStarting init\nSystem ready\n"

    [<Fact>]
    let ``Le point d'entrée du noyau est l'adresse conventionnelle de 1 Mo`` () =
        BootImage.KernelEntryPoint |> should equal 0x100000UL

    [<Fact>]
    let ``Le noyau gère un message vide sans erreur`` () =
        let code, text = runKernel []
        code |> should equal 0
        text |> should equal ""

    [<Fact>]
    let ``Le noyau écrit ses messages sur le port série COM1 puis s'arrête`` () =
        let a = ElfTest.Asm()
        a.RawBytes [| 0x66uy; 0xBAuy; 0xF8uy; 0x03uy |]  // mov dx, 0x3F8
        a.RawBytes [| 0xB0uy; 0x4Buy |]                  // mov al, 'K'
        a.RawBytes [| 0xEEuy |]                          // out dx, al
        a.RawBytes [| 0xF4uy |]                          // hlt
        let image = ElfTest.create (a.Build()) BootImage.KernelEntryPoint BootImage.KernelEntryPoint
        use machine = new LinuxMachine(image, [||], kernel = true)
        use buffer = new MemoryStream()
        machine.StandardOutput <- buffer
        let code = machine.Run()
        code |> should equal 0
        Encoding.UTF8.GetString(buffer.ToArray()) |> should equal "K"

    [<Fact>]
    let ``Le noyau lit l'état du port série COM1 et obtient le registre LSR prêt`` () =
        let a = ElfTest.Asm()
        a.RawBytes [| 0x66uy; 0xBAuy; 0xFduy; 0x03uy |]  // mov dx, 0x3FD (LSR)
        a.RawBytes [| 0xECuy |]                          // in al, dx
        a.RawBytes [| 0x66uy; 0xBAuy; 0xF8uy; 0x03uy |]  // mov dx, 0x3F8 (THR)
        a.RawBytes [| 0xEEuy |]                          // out dx, al
        a.RawBytes [| 0xF4uy |]                          // hlt
        let image = ElfTest.create (a.Build()) BootImage.KernelEntryPoint BootImage.KernelEntryPoint
        use machine = new LinuxMachine(image, [||], kernel = true)
        use buffer = new MemoryStream()
        machine.StandardOutput <- buffer
        let code = machine.Run()
        code |> should equal 0
        buffer.ToArray() |> should equal [| 0x60uy |]
