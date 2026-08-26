namespace Diplo.Core

open System.Text

/// Découpage d'une ligne de commande en arguments en respectant les
/// guillemets doubles (ex. `cmd /c "echo bonjour le monde"`). Partagé entre
/// la CLI et l'interface graphique pour `exec` sur un conteneur.
module CommandLine =

    let split (line: string) : string list =
        if isNull line then
            []
        else
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

    /// Réassemble les arguments en une ligne ré-interprétable par split :
    /// les jetons contenant espace ou guillemet sont ré-entourés de guillemets.
    let join (args: string seq) : string =
        args
        |> Seq.map (fun a ->
            if isNull a then
                ""
            elif a.Contains(' ') || a.Contains('"') || a = "" then
                "\"" + a.Replace("\"", "\"\"") + "\""
            else
                a)
        |> String.concat " "
