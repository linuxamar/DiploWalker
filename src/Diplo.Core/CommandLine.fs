namespace Diplo.Core

open System.Text

/// Découpage d'une ligne de commande en arguments en respectant les
/// guillemets doubles (ex. `cmd /c "echo bonjour le monde"`). Partagé entre
/// la CLI et l'interface graphique pour `exec` sur un conteneur.
module CommandLine =

    let split (line: string) : string list =
        let tokens = ResizeArray<string>()
        let current = StringBuilder()
        let mutable inQuotes = false
        for c in line do
            match c with
            | '"' -> inQuotes <- not inQuotes
            | ' ' when not inQuotes ->
                if current.Length > 0 then
                    tokens.Add(current.ToString())
                    current.Clear() |> ignore
            | c -> current.Append(c) |> ignore
        if current.Length > 0 then
            tokens.Add(current.ToString())
        tokens |> List.ofSeq

    let join (args: string seq) : string =
        System.String.Join(" ", args)
