namespace DiploWalker.Abstractions

open System

/// Fournisseurs de registres de conteneurs autorisÃ©s et rÃ©solution des alias
/// vers l'hÃ´te canonique. Source unique de la politique (serveur et clients) :
/// toute image tirÃ©e ou tout compte enregistrÃ© doit passer par cette liste
/// blanche.
[<RequireQualifiedAccess>]
module RegistryProviders =

    /// HÃ´tes canoniques autorisÃ©s (liste blanche).
    let hosts: string list = [ "ghcr.io"; "docker.io"; "quay.io"; "mcr.microsoft.com" ]

    /// LibellÃ© lisible des fournisseurs autorisÃ©s (messages d'erreur, CLI, GUI).
    let label: string = String.Join(", ", hosts)

    /// Alias conviviaux acceptÃ©s pour rÃ©soudre chaque fournisseur.
    let aliases: string list = [ "ghcr"; "dockerhub"; "docker"; "hub"; "quay"; "mcr" ]

    let private aliasTable: Map<string, string> =
        [ "ghcr", "ghcr.io"
          "ghcr.io", "ghcr.io"
          "dockerhub", "docker.io"
          "docker", "docker.io"
          "hub", "docker.io"
          "docker.io", "docker.io"
          "registry-1.docker.io", "docker.io"
          "quay", "quay.io"
          "quay.io", "quay.io"
          "mcr", "mcr.microsoft.com"
          "mcr.microsoft.com", "mcr.microsoft.com" ]
        |> Map.ofList

    /// Normalise une saisie brute : schÃ©ma retirÃ©, chemin ignorÃ©, minuscules.
    let private normalize (input: string) : string =
        if String.IsNullOrWhiteSpace input then
            ""
        else
            let trimmed = input.Trim().TrimEnd('/')

            let noScheme =
                if trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) then
                    trimmed.Substring(7)
                elif trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then
                    trimmed.Substring(8)
                else
                    trimmed

            noScheme.Split('/').[0].ToLowerInvariant()

    /// RÃ©sout une saisie (alias ou hÃ´te) vers l'hÃ´te canonique d'un fournisseur
    /// autorisÃ©. Retourne None pour tout registre hors liste blanche.
    let tryResolve (input: string) : string option =
        let key = normalize input
        Map.tryFind key aliasTable

    /// HÃ´te de registre prÃ©sumÃ© d'une rÃ©fÃ©rence d'image (premier segment sans
    /// tag/port ; Â« docker.io Â» par dÃ©faut), en minuscules.
    let hostOfImage (image: string) : string =
        let firstSegmentRaw =
            if String.IsNullOrWhiteSpace image then "" else image.Trim().Split('/').[0]

        let lastColon = firstSegmentRaw.LastIndexOf(':')

        let firstSegment =
            if lastColon >= 0 then firstSegmentRaw.Substring(0, lastColon) else firstSegmentRaw

        if
            firstSegment.Contains('.')
            || firstSegment.Contains(':')
            || firstSegment.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        then
            firstSegment.ToLowerInvariant()
        else
            "docker.io"

    /// RÃ©sout l'hÃ´te d'une rÃ©fÃ©rence d'image vers le fournisseur autorisÃ©.
    /// Retourne None pour un registre hors liste blanche.
    let tryResolveImage (image: string) : string option =
        if String.IsNullOrWhiteSpace image then None else tryResolve (hostOfImage image)
