namespace DiploWalker.Core.Mounts

open System

/// Analyse les spÃ©cifications de montage au format `src=...,dst=...[;ro]`.
/// Les montages sont sÃ©parÃ©s par un retour Ã  la ligne, un point-virgule,
/// ou (pour <c>parseArray</c>) par chaque Ã©lÃ©ment du tableau.
module MountParser =

    /// Analyse une chaÃ®ne pouvant contenir plusieurs montages
    /// (sÃ©parÃ©s par un retour Ã  la ligne ou un point-virgule).
    /// Une spÃ©cification invalide (src ou dst manquant) est ignorÃ©e.
    let parse (text: string) : (string * string * bool) list =
        if isNull text then
            []
        else
            text.Split([| '\r'; '\n'; ';' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.choose (fun line ->
                // Trim : "src=C:\data, dst=/app" sans trim perdrait dst.
                let parts =
                    line.Split([| ',' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map (fun p -> p.Trim())

                let valueOf (prefix: string) =
                    parts
                    |> Array.tryFind (fun p -> p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    |> Option.map (fun p -> p.Substring(prefix.Length).Trim())

                match valueOf "src=", valueOf "dst=" with
                | Some src, Some dst when src <> "" && dst <> "" ->
                    let readOnly =
                        parts
                        |> Array.exists (fun p -> p.Equals("ro", StringComparison.OrdinalIgnoreCase))

                    Some(src, dst, readOnly)
                | _ -> None)
            |> Array.toList

    /// Analyse une liste de spÃ©cifications (une par Ã©lÃ©ment), comme
    /// l'option de ligne de commande rÃ©pÃ©table.
    let parseArray (values: string[]) : (string * string * bool) list =
        values |> Array.collect (fun v -> parse v |> List.toArray) |> Array.toList

