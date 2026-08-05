namespace Diplo.Linux.Cli

open System
open System.IO
open Diplo.Linux
open Diplo.Volume

module Program =

    let private printUsage () =
        printfn "Utilisation : diplo-linux <binaire-elf> [arguments...]"
        printfn "              diplo-linux boot <image.iso> [chemin-noyau] [arguments...]"
        printfn "Exécute un binaire Linux (ELF64 x86-64) en traduisant ses syscalls vers Windows."
        printfn "La commande 'boot' charge le noyau depuis une image ISO (ISO9660 ou UDF)."

    let private boot isoPath kernelPath args =
        if not (File.Exists isoPath) then
            printfn $"Erreur : fichier introuvable : {isoPath}"
            2
        else
            try
                let bytes = IsoImage.readFile isoPath kernelPath
                use machine = new LinuxMachine(bytes, args)
                machine.Run()
            with
            | ex ->
                printfn $"Erreur : {ex.Message}"
                1

    [<EntryPoint>]
    let main argv =
        if argv.Length = 0 then
            printUsage ()
            2
        elif argv[0] = "boot" then
            if argv.Length < 2 then
                printUsage ()
                2
            else
                let isoPath = argv[1]
                let kernelPath =
                    if argv.Length >= 3 then argv[2] else "/boot/diplo-kernel"
                let args = if argv.Length >= 3 then argv[3..] else [||]
                boot isoPath kernelPath args
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
