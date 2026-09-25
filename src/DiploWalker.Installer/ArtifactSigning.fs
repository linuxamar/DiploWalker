/// VÃ©rification d'intÃ©gritÃ© des artefacts d'installation (C3).
///
/// Le manifeste des checksums (assets/artifacts.manifest) est signÃ© en
/// RSA-4096/SHA-384 hors bande (voir tools/sign-artifacts.ps1 et SECURITY.md) :
/// un build compromis ou une modification accidentelle des checksums ne peut
/// pas rÃ©gÃ©nÃ©rer une signature valide sans la clÃ© privÃ©e (jamais dans le dÃ©pÃ´t).
///
/// Les trois fichiers (manifeste, signature, clÃ© publique) sont embarquÃ©s dans
/// l'assembly comme ressources ; la clÃ© publique n'est donc dÃ©gradÃ©e qu'avec
/// une signature Authenticode du binaire lui-mÃªme (dÃ©fense en profondeur).
module DiploWalker.Installer.ArtifactSigning

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

type private AssemblyAnchor = class end

let private assembly = typeof<AssemblyAnchor>.Assembly

let [<Literal>] private manifestResourceName = "DiploWalker.Installer.assets.artifacts.manifest"
let [<Literal>] private signatureResourceName = "DiploWalker.Installer.assets.artifacts.manifest.sig"
let [<Literal>] private publicKeyResourceName = "DiploWalker.Installer.assets.DiploWalker-release.pub"

/// Lit une ressource embarquÃ©e en octets bruts (la signature couvre les octets
/// exacts du fichier, aucune normalisation de fins de ligne n'est appliquÃ©e).
let private readResource (resourceName: string) =
    use stream = assembly.GetManifestResourceStream(resourceName)

    if isNull stream then
        failwithf "Ressource embarquÃ©e introuvable : %s" resourceName

    use memory = new MemoryStream()
    stream.CopyTo(memory)
    memory.ToArray()

/// Analyse le manifeste des checksums.
///
/// Format de chaque ligne : `sha256  <fichier>  <sha256hex>`.
/// Les lignes vides et les commentaires (dÃ©butant par '#') sont ignorÃ©s.
/// Un doublon de nom de fichier ou un condensat invalide fait Ã©chouer la
/// lecture (fail-closed) plutÃ´t que de laisser un Ã©tat ambigu.
let parseManifest (content: string) : Map<string, string> =
    content.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.choose (fun line ->
        let trimmed = line.Trim()

        if trimmed.Length = 0 || trimmed.StartsWith("#") then
            None
        else
            Some trimmed)
    |> Array.fold
        (fun (map: Map<string, string>) line ->
            let parts = line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

            match parts with
            | [| "sha256"; fileName; checksum |] ->
                if fileName.Length = 0 then
                    failwithf "Fichier manquant dans le manifeste : %s" line

                if not (Regex.IsMatch(checksum, "^[0-9A-Fa-f]{64}$")) then
                    failwithf "Condensat SHA-256 invalide dans le manifeste : %s" line

                if map.ContainsKey fileName then
                    failwithf "Nom de fichier dupliquÃ© dans le manifeste : %s" fileName

                map.Add(fileName, checksum)

            | _ -> failwithf "Ligne de manifeste invalide : %s" line)
        Map.empty

/// VÃ©rifie une signature dÃ©tachÃ©e RSA-4096 (PKCS#1 v1.5, SHA-384) sur les
/// octets exacts du manifeste. Renvoie false si la clÃ©, la signature ou le
/// contenu sont invalides (aucune exception ne fuite).
let verifyDetachedSignature (publicKeyPem: string) (manifestBytes: byte[]) (signatureBytes: byte[]) =
    try
        use rsa = RSA.Create()
        rsa.ImportFromPem(publicKeyPem)
        rsa.VerifyData(manifestBytes, signatureBytes, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1)
    with :? CryptographicException ->
        false

/// Charge le manifeste embarquÃ©, vÃ©rifie sa signature avec la clÃ© publique
/// embarquÃ©e puis le parse. LÃ¨ve (fail-closed) si la signature ne correspond
/// pas â€” comportement souhaitÃ© : refuser d'installer des artefacts non vÃ©rifiÃ©s.
let loadVerifiedManifest () =
    let publicKeyPem = Encoding.UTF8.GetString(readResource publicKeyResourceName)
    let manifestBytes = readResource manifestResourceName

    let signatureBytes =
        let signatureText = Encoding.UTF8.GetString(readResource signatureResourceName).Trim()
        Convert.FromBase64String(signatureText)

    if not (verifyDetachedSignature publicKeyPem manifestBytes signatureBytes) then
        failwith
            "IntÃ©gritÃ© du manifeste des artefacts en Ã©chec : la signature RSA-4096/SHA-384 ne correspond pas."

    parseManifest (Encoding.UTF8.GetString manifestBytes)

/// Retrouve le condensat SHA-256 d'un artefact Ã  partir de son nom de fichier.
let lookupChecksum (manifest: Map<string, string>) (fileName: string) =
    Map.tryFind fileName manifest

