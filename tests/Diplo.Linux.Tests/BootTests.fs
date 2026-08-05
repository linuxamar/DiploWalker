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
