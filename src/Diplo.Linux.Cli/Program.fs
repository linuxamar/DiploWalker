namespace Diplo.Linux.Cli

open System
open System.IO
open Diplo.Linux
open Diplo.Volume

module Program =

    let private printUsage () =
        printfn "Utilisation : diplo-linux [options] <binaire-elf> [arguments...]"
        printfn "              diplo-linux [options] boot <image.iso> [chemin-noyau] [arguments...]"
        printfn "Exécute un binaire Linux (ELF64 x86-64) en traduisant ses syscalls vers Windows."
        printfn "Options :"
        printfn "  -t              trace les syscalls (affiche chaque appel système)."
        printfn "  -s              pas à pas : exécute une instruction à la fois."
        printfn "  -b ADRESSE      point d'arrêt (adresse hexadécimale, ex. 0x400000 ;"
        printfn "                  plusieurs adresses séparées par des virgules)."
        printfn "La commande 'boot' charge le noyau depuis une image ISO (ISO9660 ou UDF)."

    /// Analyse les options (-t, -s, -b) placées en tête de ligne de commande.
    /// Retourne (trace, pas-à-pas, points d'arrêt, arguments restants).
    let private parseOptions (argv : string[]) : bool * bool * uint64 list * string[] =
        let mutable trace = false
        let mutable step = false
        let breakpoints = ResizeArray<uint64>()
        let mutable i = 0
        let mutable error = false
        let mutable parsing = true
        while parsing && not error && i < argv.Length do
            match argv[i] with
            | "-t" ->
                trace <- true
                i <- i + 1
            | "-s" ->
                step <- true
                i <- i + 1
            | "-b" ->
                if i + 1 >= argv.Length then
                    printfn "Erreur : l'option -b nécessite une adresse."
                    error <- true
                else
                    for a in argv[i + 1].Split(',') do
                        if not error then
                            let hex =
                                if a.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                                then a.Substring(2)
                                else a
                            try
                                breakpoints.Add(Convert.ToUInt64(hex, 16))
                            with
                            | :? FormatException
                            | :? OverflowException ->
                                printfn $"Erreur : adresse de point d'arrêt invalide : {a}"
                                error <- true
                    i <- i + 2
            | arg when arg.Length > 1 && arg[0] = '-' ->
                printfn $"Erreur : option inconnue : {arg}"
                error <- true
            | _ ->
                parsing <- false

        if error then
            (false, false, List.empty, [||])
        else
            (trace, step, List.ofSeq breakpoints, argv[i..])

    /// Configure la machine selon les options puis l'exécute.
    /// Retourne le code de sortie du programme.
    let private runMachine (machine : LinuxMachine) (trace : bool) (step : bool) (breakpoints : uint64 list) : int =
        for b in breakpoints do
            machine.Breakpoints.Add b

        if trace then
            machine.SyscallHook <- Some (fun n -> printfn $"syscall {n}")

        if step then
            while machine.Step() do
                printfn $"0x{machine.Rip:X}"
        elif machine.Breakpoints.Count > 0 then
            while machine.Step() do
                ()

        if not machine.IsHalted && machine.Breakpoints.Contains machine.Rip then
            printfn $"Point d'arrêt atteint à 0x{machine.Rip:X}"

        machine.ExitStatus

    let private boot (trace : bool) (step : bool) (breakpoints : uint64 list) (isoPath : string) (kernelPath : string) (args : string[]) =
        if not (File.Exists isoPath) then
            printfn $"Erreur : fichier introuvable : {isoPath}"
            2
        else
            try
                let bytes = IsoImage.readFile isoPath kernelPath
                use machine = new LinuxMachine(bytes, args, kernel = true)
                runMachine machine trace step breakpoints
            with
            | ex ->
                printfn $"Erreur : {ex.Message}"
                1

    [<EntryPoint>]
    let main argv =
        let trace, step, breakpoints, args = parseOptions argv
        if args.Length = 0 then
            printUsage ()
            2
        elif args[0] = "boot" then
            if args.Length < 2 then
                printUsage ()
                2
            else
                let isoPath = args[1]
                let kernelPath =
                    if args.Length >= 3 then args[2] else "/boot/diplo-kernel"
                let progArgs = if args.Length >= 3 then args[3..] else [||]
                boot trace step breakpoints isoPath kernelPath progArgs
        else
            let path = args[0]
            if not (File.Exists path) then
                printfn $"Erreur : fichier introuvable : {path}"
                2
            else
                try
                    let bytes = File.ReadAllBytes path
                    use machine = new LinuxMachine(bytes, args[1..])
                    runMachine machine trace step breakpoints
                with
                | ex ->
                    printfn $"Erreur : {ex.Message}"
                    1
