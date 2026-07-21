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

    /// Regex pour les labels (clés et valeurs) — alphanumériques, tirets, points, underscores.
    let private labelPattern = Regex(@"^[a-zA-Z0-9][a-zA-Z0-9._\-]{0,63}$", RegexOptions.Compiled)

    /// Caractères interdits dans les commandes exécutées dans les conteneurs.
    let private dangerousChars = [| ';'; '|'; '&'; '`'; '$'; ' '; '\t'; '\n'; '\r'; '<'; '>'; '('; ')' |]

    /// Préfixes dangereux interdits dans les commandes (contournements shell Windows).
    let private dangerousPrefixes = [| "\\\\"; "//" |]

    /// Chaîtes de substitution d'environnement interdites.
    let private dangerousEnvPatterns = [| "%PATH%"; "%SYSTEMROOT%"; "%WINDIR%"; "%TEMP%"; "%TMP%" |]

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

    /// Valide qu'un octet IP est dans la plage 0-255.
    let private validateOctet (value: string) (label: string) =
        match Int32.TryParse(value) with
        | true, n when n >= 0 && n <= 255 -> ()
        | _ -> failwithf "%s contient un octet invalide: '%s'" label value

    /// Vérifie qu'un sous-réseau CIDR est valide (octets 0-255, masque 0-32).
    let validateCidr (value: string) (label: string) =
        if String.IsNullOrEmpty(value) then ()
        else
            let parts = value.Split('/')
            if parts.Length < 1 || parts.Length > 2 then
                failwithf "%s n'est pas un sous-réseau CIDR valide: '%s'" label value
            let ipParts = parts.[0].Split('.')
            if ipParts.Length <> 4 then
                failwithf "%s n'est pas une adresse IP valide: '%s'" label value
            for part in ipParts do
                validateOctet part label
            if parts.Length = 2 then
                match Int32.TryParse(parts.[1]) with
                | true, n when n >= 0 && n <= 32 -> ()
                | _ -> failwithf "%s a un masque CIDR invalide (doit être 0-32): '%s'" label value

    /// Vérifie qu'une adresse IP est valide (octets 0-255).
    let validateIp (value: string) (label: string) =
        if String.IsNullOrEmpty(value) then ()
        else
            let parts = value.Split('.')
            if parts.Length <> 4 then
                failwithf "%s n'est pas une adresse IP valide: '%s'" label value
            for part in parts do
                validateOctet part label

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
    /// Résout les symlinks avant la vérification pour éviter les contournements TOCTOU.
    let validateCniPluginPath (pluginPath: string) =
        if String.IsNullOrEmpty(pluginPath) then
            failwithf "Le chemin du plugin CNI ne peut pas être vide"
        // Résoudre le vrai chemin (suivre les symlinks junctions)
        let resolvedPath =
            try
                let fi = new FileInfo(pluginPath)
                if fi.Exists then fi.FullName
                else
                    let di = new DirectoryInfo(pluginPath)
                    if di.Exists then di.FullName
                    else Path.GetFullPath(pluginPath)
            with _ -> Path.GetFullPath(pluginPath)
        let isAllowed =
            allowedCniPluginDirs
            |> List.exists (fun dir ->
                let fullDir = Path.GetFullPath(dir)
                resolvedPath.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
        if not isAllowed then
            failwithf "Le plugin CNI '%s' n'est pas dans un répertoire autorisé" pluginPath

    /// Vérifie qu'une commande ne contient pas de caractères dangereux.
    let validateCommand (command: string array) =
        if isNull command || command.Length = 0 then
            failwithf "La commande ne peut pas être vide"
        if command.Length > 64 then
            failwithf "La commande ne peut pas contenir plus de 64 arguments"
        // Le premier argument (l'exécutable) ne doit pas contenir de slash ou de traversée
        if command.Length > 0 && not (String.IsNullOrEmpty(command.[0])) then
            if command.[0].Contains("/") || command.[0].Contains("\\") then
                failwithf "L'exécutable ne doit pas contenir de chemin: '%s'" command.[0]
        for arg in command do
            if String.IsNullOrEmpty(arg) |> not then
                if arg.Length > 1024 then
                    failwithf "Un argument de commande dépasse 1024 caractères"
                let upperArg = arg.ToUpperInvariant()
                for pattern in dangerousEnvPatterns do
                    if upperArg.Contains(pattern) then
                        failwithf "L'argument de commande contient une variable d'environnement interdite: '%s'" pattern
                for prefix in dangerousPrefixes do
                    if arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
                        failwithf "L'argument de commande contient un préfixe interdit: '%s'" arg
                for c in arg do
                    if Array.exists (fun dc -> dc = c) dangerousChars then
                        failwithf "L'argument de commande contient un caractère interdit: '%c' dans '%s'" c arg

    /// Vérifie qu'un chemin est sûr (pas de traversée via ..).
    let validatePath (path: string) (baseDir: string) (label: string) =
        if String.IsNullOrEmpty(path) then
            failwithf "%s ne peut pas être vide" label
        if path.Contains("..") then
            failwithf "%s contient une traversée de répertoire interdite: '%s'" label path
        let fullPath = Path.GetFullPath(Path.Combine(baseDir, path))
        let fullBase = Path.GetFullPath(baseDir)
        if not (fullPath.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase)) then
            failwithf "%s sort du répertoire autorisé: '%s'" label fullPath

    /// Répertoires de base autorisés pour les volumes (conteneur mutable thread-safe pour extensibilité).
    let private allowedVolumeBaseDirs =
        let dict = System.Collections.Concurrent.ConcurrentDictionary<string, byte>()
        dict.TryAdd(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Diplo"), byte 0) |> ignore
        dict.TryAdd(@"C:\ProgramData\Diplo", byte 0) |> ignore
        dict.TryAdd(Path.GetTempPath(), byte 0) |> ignore
        dict

    /// Ajouter un répertoire autorisé pour les volumes (utile pour les tests et extensions).
    let addAllowedVolumeDir (dir: string) =
        let fullDir = Path.GetFullPath(dir)
        (allowedVolumeBaseDirs : System.Collections.Concurrent.ConcurrentDictionary<string, byte>).TryAdd(fullDir, byte 0) |> ignore

    /// Valide un chemin de volume (anti-traversée + containment absolu).
    let validateVolumePath (path: string) (label: string) =
        if String.IsNullOrEmpty(path) then
            failwithf "%s ne peut pas être vide" label
        if path.Contains("..") then
            failwithf "%s contient une traversée de répertoire interdite: '%s'" label path
        if path.Contains("\0") then
            failwithf "%s contient un caractère nul: '%s'" label path
        // Résoudre le chemin complet et vérifier qu'il est dans un répertoire autorisé
        let fullPath = Path.GetFullPath(path)
        let isAllowed =
            allowedVolumeBaseDirs.Keys
            |> Seq.exists (fun dir ->
                let fullDir = Path.GetFullPath(dir)
                fullPath.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
        if not isAllowed then
            failwithf "%s n'est pas dans un répertoire autorisé: '%s'" label fullPath
