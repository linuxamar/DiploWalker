namespace DiploWalker.Core

open System.Text

/// Découpage d'une ligne de commande en arguments en respectant les
/// guillemets doubles (ex. `cmd /c "echo bonjour le monde"`). Partagé entre
/// la CLI et l'interface graphique pour `exec` sur un conteneur.
module CommandLine =

    /// Découpe une ligne en arguments. Règles :
    /// - un guillemet ouvre/ferme une section entre guillemets (les espaces y
    ///   sont préservés) ;
    /// - `""` à l'intérieur d'une section représente un guillemet littéral
    ///   (aller-retour avec `join`) ;
    /// - des guillemets non équilibrés lèvent `ArgumentException`.
    let split (line: string) : string list =
        if isNull line then
            []
        else
            let tokens = ResizeArray<string>()
            let current = StringBuilder()
            let mutable inQuotes = false
            let mutable i = 0

            while i < line.Length do
                match line[i] with
                | '"' when inQuotes && i + 1 < line.Length && line[i + 1] = '"' ->
                    // Guillemet échappé : `""` devient `"` (voir `join`).
                    current.Append('"') |> ignore
                    i <- i + 2
                | '"' ->
                    inQuotes <- not inQuotes
                    i <- i + 1
                | ' ' when not inQuotes ->
                    if current.Length > 0 then
                        tokens.Add(current.ToString())
                        current.Clear() |> ignore

                    i <- i + 1
                | c ->
                    current.Append(c) |> ignore
                    i <- i + 1

            if inQuotes then
                raise (System.ArgumentException("Guillemets non équilibrés dans la commande", "line"))

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

