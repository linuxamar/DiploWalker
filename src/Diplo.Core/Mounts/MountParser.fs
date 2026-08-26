namespace Diplo.Core.Mounts

open System

/// Analyse les spécifications de montage au format `src=...,dst=...[;ro]`.
/// Les montages sont séparés par un retour à la ligne, un point-virgule,
/// ou (pour <c>parseArray</c>) par chaque élément du tableau.
module MountParser =

    /// Analyse une chaîne pouvant contenir plusieurs montages
    /// (séparés par un retour à la ligne ou un point-virgule).
    /// Une spécification invalide (src ou dst manquant) est ignorée.
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

    /// Analyse une liste de spécifications (une par élément), comme
    /// l'option de ligne de commande répétable.
    let parseArray (values: string[]) : (string * string * bool) list =
        values |> Array.collect (fun v -> parse v |> List.toArray) |> Array.toList
