namespace Diplo.Linux.Cli

open System
open System.IO
open Diplo.Linux

module Program =

    let private printUsage () =
        printfn "Utilisation : diplo-linux <binaire-elf> [arguments...]"
        printfn "Exécute un binaire Linux (ELF64 x86-64) en traduisant ses syscalls vers Windows."

    [<EntryPoint>]
    let main argv =
        if argv.Length = 0 then
            printUsage ()
            2
        else
            let path = argv[0]
            if not (File.Exists path) then
                printfn $"Erreur : fichier introuvable : {path}"
                2
            else
                try
                    let bytes = File.ReadAllBytes path
                    use machine = new LinuxMachine(bytes, argv[1..])
                    machine.Run()
                with
                | ex ->
                    printfn $"Erreur : {ex.Message}"
                    1
