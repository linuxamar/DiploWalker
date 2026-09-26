namespace DiploWalker.Abstractions

open System

/// Fournisseurs de registres de conteneurs autorisés et résolution des alias
/// vers l'hôte canonique. Source unique de la politique (serveur et clients) :
/// toute image tirée ou tout compte enregistré doit passer par cette liste
/// blanche.
[<RequireQualifiedAccess>]
module RegistryProviders =

    /// Hôtes canoniques autorisés (liste blanche).
    let hosts: string list = [ "ghcr.io"; "docker.io"; "quay.io"; "mcr.microsoft.com" ]

    /// Libellé lisible des fournisseurs autorisés (messages d'erreur, CLI, GUI).
    let label: string = String.Join(", ", hosts)

    /// Alias conviviaux acceptés pour résoudre chaque fournisseur.
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

    /// Normalise une saisie brute : schéma retiré, chemin ignoré, minuscules.
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

    /// Résout une saisie (alias ou hôte) vers l'hôte canonique d'un fournisseur
    /// autorisé. Retourne None pour tout registre hors liste blanche.
    let tryResolve (input: string) : string option =
        let key = normalize input
        Map.tryFind key aliasTable

    /// Hôte de registre présumé d'une référence d'image (premier segment sans
    /// tag/port ; « docker.io » par défaut), en minuscules.
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

    /// Résout l'hôte d'une référence d'image vers le fournisseur autorisé.
    /// Retourne None pour un registre hors liste blanche.
    let tryResolveImage (image: string) : string option =
        if String.IsNullOrWhiteSpace image then None else tryResolve (hostOfImage image)
