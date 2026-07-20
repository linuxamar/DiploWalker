namespace Diplo.Abstractions

open System
open System.IO
open System.Text.RegularExpressions

/// Validation et assainissement des entrées utilisateur pour prévenir les injections de commandes.
module SecurityValidation =

    /// Longueur maximale pour les identifiants et noms.
    let private maxLength = 128

    /// Regex pour les identifiants (IDs de conteneur, réseau, volume) — hex, tirets, underscores.
    let private idPattern = Regex(@"^[a-zA-Z0-9][a-zA-Z0-9_\-]{0,127}$", RegexOptions.Compiled)

    /// Regex pour les noms de réseau / bridge — alphanumériques, tirets, points.
    let private namePattern = Regex(@"^[a-zA-Z0-9][a-zA-Z0-9._\-]{0,63}$", RegexOptions.Compiled)

    /// Regex pour les images Docker — alphanumériques, slashes, tirets, points, deux-points, @.
    let private imagePattern = Regex(@"^[a-zA-Z0-9][a-zA-Z0-9._/\-:@]{0,511}$", RegexOptions.Compiled)

    /// Regex pour les sous-réseaux CIDR.
    let private cidrPattern = Regex(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(/\d{1,2})?$", RegexOptions.Compiled)

    /// Regex pour les adresses IP.
    let private ipPattern = Regex(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$", RegexOptions.Compiled)

    /// Regex pour les labels (clés et valeurs) — alphanumériques, tirets, points, underscores.
    let private labelPattern = Regex(@"^[a-zA-Z0-9][a-zA-Z0-9._\-]{0,63}$", RegexOptions.Compiled)

    /// Répertoires autorisés pour les plugins CNI.
    let private allowedCniPluginDirs =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "containerd", "cni", "bin")
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "containerd", "cni", "bin", "opt", "cni", "bin")
            @"C:\opt\cni\bin"
        ]

    /// Vérifie qu'une valeur correspond au motif d'identifiant.
    let validateId (value: string) (label: string) =
        if String.IsNullOrEmpty(value) then
            failwithf "%s ne peut pas être vide" label
        if value.Length > maxLength then
            failwithf "%s dépasse la longueur maximale de %d caractères" label maxLength
        if not (idPattern.IsMatch(value)) then
            failwithf "%s contient des caractères interdits: '%s'" label value

    /// Vérifie qu'un nom est valide.
    let validateName (value: string) (label: string) =
        if String.IsNullOrEmpty(value) then
            failwithf "%s ne peut pas être vide" label
        if value.Length > 64 then
            failwithf "%s dépasse la longueur maximale de 64 caractères" label
        if not (namePattern.IsMatch(value)) then
            failwithf "%s contient des caractères interdits: '%s'" label value

    /// Vérifie qu'un nom d'image Docker est valide.
    let validateImage (value: string) =
        if String.IsNullOrEmpty(value) then
            failwithf "Le nom de l'image ne peut pas être vide"
        if value.Length > 512 then
            failwithf "Le nom de l'image dépasse 512 caractères"
        if not (imagePattern.IsMatch(value)) then
            failwithf "Le nom de l'image contient des caractères interdits: '%s'" value

    /// Vérifie qu'un sous-réseau CIDR est valide.
    let validateCidr (value: string) (label: string) =
        if String.IsNullOrEmpty(value) then () // vide = auto-détection
        elif not (cidrPattern.IsMatch(value)) then
            failwithf "%s n'est pas un sous-réseau CIDR valide: '%s'" label value

    /// Vérifie qu'une adresse IP est valide.
    let validateIp (value: string) (label: string) =
        if String.IsNullOrEmpty(value) then () // vide = DHCP
        elif not (ipPattern.IsMatch(value)) then
            failwithf "%s n'est pas une adresse IP valide: '%s'" label value

    /// Vérifie qu'une clé et une valeur de label sont valides.
    let validateLabel (key: string) (value: string) =
        if not (labelPattern.IsMatch(key)) then
            failwithf "La clé du label contient des caractères interdits: '%s'" key
        if not (labelPattern.IsMatch(value)) then
            failwithf "La valeur du label contient des caractères interdits: '%s'" value

    /// Vérifie qu'un identifiant de conteneur est au format hexadécimal ou GUID.
    let validateContainerId (value: string) =
        if String.IsNullOrEmpty(value) then
            failwithf "L'identifiant du conteneur ne peut pas être vide"
        if value.Length > maxLength then
            failwithf "L'identifiant du conteneur dépasse %d caractères" maxLength
        if not (idPattern.IsMatch(value)) then
            failwithf "L'identifiant du conteneur contient des caractères interdits: '%s'" value

    /// Vérifie qu'un chemin de plugin CNI est dans les répertoires autorisés.
    let validateCniPluginPath (pluginPath: string) =
        if String.IsNullOrEmpty(pluginPath) then
            failwithf "Le chemin du plugin CNI ne peut pas être vide"
        let fullPath = Path.GetFullPath(pluginPath)
        let isAllowed =
            allowedCniPluginDirs
            |> List.exists (fun dir ->
                let fullDir = Path.GetFullPath(dir)
                fullPath.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
        if not isAllowed then
            failwithf "Le plugin CNI '%s' n'est pas dans un répertoire autorisé" pluginPath

    /// Vérifie qu'une commande ne contient pas de caractères dangereux.
    let validateCommand (command: string array) =
        if isNull command || command.Length = 0 then
            failwithf "La commande ne peut pas être vide"
        if command.Length > 64 then
            failwithf "La commande ne peut pas contenir plus de 64 arguments"
        for arg in command do
            if String.IsNullOrEmpty(arg) |> not && arg.Length > 1024 then
                failwithf "Un argument de commande dépasse 1024 caractères"
