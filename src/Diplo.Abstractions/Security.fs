namespace Diplo.Abstractions

open System
open System.IO
open System.Text.RegularExpressions
open Grpc.Core

/// Validation et assainissement des entrées utilisateur pour prévenir les injections de commandes.
module SecurityValidation =

    /// Longueur maximale pour les identifiants et noms.
    let private maxLength = 128

    /// Regex pour les identifiants (IDs de conteneur, réseau, volume) — hex, tirets, underscores.
    let private idPattern =
        Regex(@"^[a-zA-Z0-9][a-zA-Z0-9_\-]{0,127}$", RegexOptions.Compiled)

    /// Regex pour les noms de réseau / bridge — alphanumériques, tirets, points.
    let private namePattern =
        Regex(@"^[a-zA-Z0-9][a-zA-Z0-9._\-]{0,63}$", RegexOptions.Compiled)

    /// Regex pour les images Docker — alphanumériques, slashes, tirets, points, deux-points, @.
    let private imagePattern =
        Regex(@"^[a-zA-Z0-9][a-zA-Z0-9._/\-:@]{0,511}$", RegexOptions.Compiled)

    /// Regex pour les labels (clés et valeurs) — alphanumériques, tirets, points, underscores.
    let private labelPattern =
        Regex(@"^[a-zA-Z0-9][a-zA-Z0-9._\-]{0,63}$", RegexOptions.Compiled)

    /// Caractères interdits dans les commandes exécutées dans les conteneurs.
    let private dangerousChars =
        [| ';'; '|'; '&'; '`'; '$'; '\t'; '\n'; '\r'; '<'; '>'; '('; ')' |]

    /// Préfixes dangereux interdits dans les commandes (contournements shell Windows).
    let private dangerousPrefixes = [| "\\\\"; "//" |]

    /// Chaîtes de substitution d'environnement interdites.
    let private dangerousEnvPatterns =
        [| "%PATH%"; "%SYSTEMROOT%"; "%WINDIR%"; "%TEMP%"; "%TMP%" |]

    /// Répertoires autorisés pour les plugins CNI.
    let private allowedCniPluginDirs =
        [ Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "containerd", "cni", "bin")
          Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
              "containerd",
              "cni",
              "bin",
              "opt",
              "cni",
              "bin"
          )
          @"C:\opt\cni\bin" ]

    /// Vérifie qu'une valeur correspond au motif d'identifiant.
    let validateId (value: string) (label: string) =
        if String.IsNullOrWhiteSpace(value) then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s ne peut pas être vide" label)))

        if value.Length > maxLength then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s dépasse la longueur maximale de %d caractères" label maxLength
                    )
                )
            )

        if not (idPattern.IsMatch(value)) then
            raise (
                RpcException(
                    Status(StatusCode.InvalidArgument, sprintf "%s contient des caractères interdits: '%s'" label value)
                )
            )

    /// Vérifie qu'un nom est valide.
    let validateName (value: string) (label: string) =
        if String.IsNullOrWhiteSpace(value) then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s ne peut pas être vide" label)))

        if value.Length > 64 then
            raise (
                RpcException(
                    Status(StatusCode.InvalidArgument, sprintf "%s dépasse la longueur maximale de 64 caractères" label)
                )
            )

        if not (namePattern.IsMatch(value)) then
            raise (
                RpcException(
                    Status(StatusCode.InvalidArgument, sprintf "%s contient des caractères interdits: '%s'" label value)
                )
            )

    /// Vérifie qu'un nom d'image Docker est valide.
    let validateImage (value: string) =
        if String.IsNullOrWhiteSpace(value) then
            raise (RpcException(Status(StatusCode.InvalidArgument, "Le nom de l'image ne peut pas être vide")))

        if value.Length > 512 then
            raise (RpcException(Status(StatusCode.InvalidArgument, "Le nom de l'image dépasse 512 caractères")))

        if not (imagePattern.IsMatch(value)) then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "Le nom de l'image contient des caractères interdits: '%s'" value
                    )
                )
            )

    /// Valide qu'un octet IP est dans la plage 0-255.
    let private validateOctet (value: string) (label: string) =
        match Int32.TryParse(value) with
        | true, n when n >= 0 && n <= 255 -> ()
        | _ ->
            raise (
                RpcException(
                    Status(StatusCode.InvalidArgument, sprintf "%s contient un octet invalide: '%s'" label value)
                )
            )

    /// Vérifie qu'un sous-réseau CIDR est valide (octets 0-255, masque 0-32).
    let validateCidr (value: string) (label: string) =
        if String.IsNullOrWhiteSpace(value) then
            ()
        else
            let parts = value.Split('/')

            if parts.Length < 1 || parts.Length > 2 then
                raise (
                    RpcException(
                        Status(
                            StatusCode.InvalidArgument,
                            sprintf "%s n'est pas un sous-réseau CIDR valide: '%s'" label value
                        )
                    )
                )

            let ipParts = parts.[0].Split('.')

            if ipParts.Length <> 4 then
                raise (
                    RpcException(
                        Status(
                            StatusCode.InvalidArgument,
                            sprintf "%s n'est pas une adresse IP valide: '%s'" label value
                        )
                    )
                )

            for part in ipParts do
                validateOctet part label

            if parts.Length = 2 then
                match Int32.TryParse(parts.[1]) with
                | true, n when n >= 0 && n <= 32 -> ()
                | _ ->
                    raise (
                        RpcException(
                            Status(
                                StatusCode.InvalidArgument,
                                sprintf "%s a un masque CIDR invalide (doit être 0-32): '%s'" label value
                            )
                        )
                    )

    /// Vérifie qu'une adresse IP est valide (octets 0-255).
    let validateIp (value: string) (label: string) =
        if String.IsNullOrWhiteSpace(value) then
            ()
        else
            let parts = value.Split('.')

            if parts.Length <> 4 then
                raise (
                    RpcException(
                        Status(
                            StatusCode.InvalidArgument,
                            sprintf "%s n'est pas une adresse IP valide: '%s'" label value
                        )
                    )
                )

            for part in parts do
                validateOctet part label

    /// Vérifie qu'une clé et une valeur de label sont valides.
    let validateLabel (key: string) (value: string) =
        if not (labelPattern.IsMatch(key)) then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "La clé du label contient des caractères interdits: '%s'" key
                    )
                )
            )

        if not (labelPattern.IsMatch(value)) then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "La valeur du label contient des caractères interdits: '%s'" value
                    )
                )
            )

    /// Vérifie qu'un identifiant de conteneur est au format hexadécimal ou GUID.
    let validateContainerId (value: string) =
        if String.IsNullOrWhiteSpace(value) then
            raise (RpcException(Status(StatusCode.InvalidArgument, "L'identifiant du conteneur ne peut pas être vide")))

        if value.Length > maxLength then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "L'identifiant du conteneur dépasse %d caractères" maxLength
                    )
                )
            )

        if not (idPattern.IsMatch(value)) then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "L'identifiant du conteneur contient des caractères interdits: '%s'" value
                    )
                )
            )

    /// Vérifie qu'un chemin de plugin CNI est dans les répertoires autorisés.
    /// Résout les symlinks et retourne le chemin résolu pour éliminer la fenêtre TOCTOU.
    let validateCniPluginPath (pluginPath: string) : string =
        if String.IsNullOrWhiteSpace(pluginPath) then
            raise (RpcException(Status(StatusCode.InvalidArgument, "Le chemin du plugin CNI ne peut pas être vide")))

        let resolvedPath =
            try
                let fi = new FileInfo(pluginPath)

                if fi.Exists then
                    fi.FullName
                else
                    let di = new DirectoryInfo(pluginPath)

                    if di.Exists then
                        di.FullName
                    else
                        Path.GetFullPath(pluginPath)
            with _ ->
                Path.GetFullPath(pluginPath)

        let isAllowed =
            allowedCniPluginDirs
            |> List.exists (fun dir ->
                let fullDir = Path.GetFullPath(dir)

                let fullDirWithSep =
                    if fullDir.EndsWith(Path.DirectorySeparatorChar) then
                        fullDir
                    else
                        fullDir + string Path.DirectorySeparatorChar

                resolvedPath.StartsWith(fullDirWithSep, StringComparison.OrdinalIgnoreCase))

        if not isAllowed then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "Le plugin CNI '%s' n'est pas dans un répertoire autorisé" pluginPath
                    )
                )
            )

        resolvedPath

    /// Commandes CNI autorisées (spécification CNI v1.0).
    let private allowedCniCommands = set [ "ADD"; "DEL"; "CHECK"; "VERSION" ]

    /// Vérifie qu'une commande CNI fait partie de l'allowlist.
    let validateCniCommand (command: string) =
        if String.IsNullOrWhiteSpace(command) then
            raise (RpcException(Status(StatusCode.InvalidArgument, "La commande CNI ne peut pas être vide")))

        let normalized = command.Trim().ToUpperInvariant()

        if not (allowedCniCommands.Contains(normalized)) then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf
                            "La commande CNI '%s' n'est pas autorisée. Commandes autorisées: %s"
                            command
                            (allowedCniCommands |> Set.toSeq |> String.concat ", ")
                    )
                )
            )

    /// Vérifie qu'une commande ne contient pas de caractères dangereux.
    let validateCommand (command: string array) =
        if isNull command || command.Length = 0 then
            raise (RpcException(Status(StatusCode.InvalidArgument, "La commande ne peut pas être vide")))

        if command.Length > 64 then
            raise (
                RpcException(
                    Status(StatusCode.InvalidArgument, "La commande ne peut pas contenir plus de 64 arguments")
                )
            )

        if command.Length > 0 && not (String.IsNullOrWhiteSpace(command.[0])) then
            if command.[0].Contains("/") || command.[0].Contains("\\") then
                raise (
                    RpcException(
                        Status(
                            StatusCode.InvalidArgument,
                            sprintf "L'exécutable ne doit pas contenir de chemin: '%s'" command.[0]
                        )
                    )
                )

        for arg in command do
            if String.IsNullOrWhiteSpace(arg) |> not then
                if arg.Length > 1024 then
                    raise (
                        RpcException(
                            Status(StatusCode.InvalidArgument, "Un argument de commande dépasse 1024 caractères")
                        )
                    )

                let upperArg = arg.ToUpperInvariant()

                for pattern in dangerousEnvPatterns do
                    if upperArg.Contains(pattern) then
                        raise (
                            RpcException(
                                Status(
                                    StatusCode.InvalidArgument,
                                    sprintf
                                        "L'argument de commande contient une variable d'environnement interdite: '%s'"
                                        pattern
                                )
                            )
                        )

                for prefix in dangerousPrefixes do
                    if arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
                        raise (
                            RpcException(
                                Status(
                                    StatusCode.InvalidArgument,
                                    sprintf "L'argument de commande contient un préfixe interdit: '%s'" arg
                                )
                            )
                        )

                for c in arg do
                    if Array.exists (fun dc -> dc = c) dangerousChars then
                        raise (
                            RpcException(
                                Status(
                                    StatusCode.InvalidArgument,
                                    sprintf
                                        "L'argument de commande contient un caractère interdit: '%c' dans '%s'"
                                        c
                                        arg
                                )
                            )
                        )

    /// Vérifie qu'un chemin de fichier cible dans un conteneur est sûr.
    /// S'utilise quand le chemin est intégré dans une commande interne (ex. redirection
    /// shell de WriteFile) : seules les parties issues de l'utilisateur sont contrôlées.
    let validateContainerPath (path: string) (label: string) =
        if String.IsNullOrWhiteSpace(path) then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s ne peut pas être vide" label)))

        if path.Length > 1024 then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s dépasse 1024 caractères" label)))

        if path.Contains("..") then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s contient une traversée de répertoire interdite: '%s'" label path
                    )
                )
            )

        if path.Contains("\0") then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s contient un caractère nul" label)))

        for c in path do
            if Array.exists (fun dc -> dc = c) dangerousChars then
                raise (
                    RpcException(
                        Status(
                            StatusCode.InvalidArgument,
                            sprintf "%s contient un caractère interdit: '%c' dans '%s'" label c path
                        )
                    )
                )

    /// Vérifie qu'un chemin est sûr (pas de traversée via ..).
    /// Résout les symlinks pour empêcher les contournements via liens symboliques.
    let validatePath (path: string) (baseDir: string) (label: string) =
        if String.IsNullOrWhiteSpace(path) then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s ne peut pas être vide" label)))

        if path.Contains("..") then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s contient une traversée de répertoire interdite: '%s'" label path
                    )
                )
            )

        let resolvedPath =
            try
                let combined = Path.Combine(baseDir, path)
                let fi = new FileInfo(combined)

                if fi.Exists then
                    fi.FullName
                else
                    let di = new DirectoryInfo(combined)

                    if di.Exists then
                        di.FullName
                    else
                        Path.GetFullPath(combined)
            with _ ->
                Path.GetFullPath(Path.Combine(baseDir, path))

        let resolvedBase =
            try
                let di = new DirectoryInfo(baseDir)
                if di.Exists then di.FullName else Path.GetFullPath(baseDir)
            with _ ->
                Path.GetFullPath(baseDir)

        let resolvedBaseWithSep =
            if resolvedBase.EndsWith(Path.DirectorySeparatorChar) then
                resolvedBase
            else
                resolvedBase + string Path.DirectorySeparatorChar

        if not (resolvedPath.StartsWith(resolvedBaseWithSep, StringComparison.OrdinalIgnoreCase)) then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s sort du répertoire autorisé: '%s'" label resolvedPath
                    )
                )
            )

    /// Hosts autorisés pour les connexions gRPC (localhost uniquement).
    let private allowedGrpcHosts = set [ "localhost"; "127.0.0.1"; "::1" ]

    /// Vérifie qu'une adresse gRPC pointe vers localhost uniquement.
    /// Empêche la fuite de tokens d'authentification sur le réseau.
    /// Les adresses par named pipe ("http://pipe:/<nom>") sont acceptées : elles
    /// désignent un canal local, on ne fait donc qu'en valider le nom de tube.
    let validateGrpcAddress (address: string) =
        if String.IsNullOrWhiteSpace(address) then
            raise (RpcException(Status(StatusCode.InvalidArgument, "L'adresse gRPC ne peut pas être vide")))

        match System.Uri.TryCreate(address, System.UriKind.Absolute) with
        | true, uri ->
            if not (uri.Scheme = "http" || uri.Scheme = "https") then
                raise (
                    RpcException(
                        Status(
                            StatusCode.InvalidArgument,
                            sprintf "Le schéma '%s' n'est pas supporté. Utilisez http:// ou https://" uri.Scheme
                        )
                    )
                )

            if String.Equals(uri.Host, "pipe", System.StringComparison.OrdinalIgnoreCase) then
                // Le Uri de .NET normalise les backslashes du chemin en slashes :
                // on extrait le nom du pipe de la chaîne brute pour ne rien manquer.
                let idx = address.IndexOf("pipe:", System.StringComparison.OrdinalIgnoreCase)
                let name = address.Substring(idx + 5).TrimStart('/')

                if
                    String.IsNullOrWhiteSpace(name)
                    || name.Contains("\\")
                    || name.Contains("..")
                    || name.Contains("\0")
                then
                    raise (
                        RpcException(
                            Status(
                                StatusCode.InvalidArgument,
                                sprintf
                                    "Le nom du pipe '%s' est invalide. Utilisez une adresse de la forme http://pipe:/<nom>"
                                    name
                            )
                        )
                    )
            else
                let host = uri.Host.Trim('[', ']')

                if not (allowedGrpcHosts.Contains(host)) then
                    raise (
                        RpcException(
                            Status(
                                StatusCode.InvalidArgument,
                                sprintf
                                    "L'adresse gRPC '%s' n'est pas autorisée. Seul l'hôte local est accepté (localhost, 127.0.0.1, ::1)"
                                    address
                            )
                        )
                    )
        | false, _ ->
            raise (
                RpcException(
                    Status(StatusCode.InvalidArgument, sprintf "L'adresse gRPC '%s' n'est pas une URL valide" address)
                )
            )

    /// Extensions autorisées pour les fichiers de configuration (compose, etc.).
    let private allowedYamlExtensions = set [ ".yaml"; ".yml" ]

    /// Valide le chemin d'un fichier de configuration (anti-traversée + extension + caractères nuls).
    let validateFilePath (path: string) (label: string) =
        if String.IsNullOrWhiteSpace(path) then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s ne peut pas être vide" label)))

        if path.Contains("..") then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s contient une traversée de répertoire interdite: '%s'" label path
                    )
                )
            )

        if path.Contains("\0") then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s contient un caractère nul" label)))

        let ext = Path.GetExtension(path)

        if not (allowedYamlExtensions.Contains(ext)) then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s doit avoir une extension .yaml ou .yml, reçu: '%s'" label ext
                    )
                )
            )

    /// Répertoires de base autorisés pour les volumes (immutable snapshot pattern).
    let private defaultAllowedVolumeDirs =
        [ Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Diplo")
          @"C:\ProgramData\Diplo"
          Path.GetTempPath() ]
        |> List.map Path.GetFullPath

    /// Cellule de ref contenant l'ensemble immuable des répertoires autorisés (thread-safe via copy-on-write).
    let private allowedVolumeBaseDirsRef = ref (defaultAllowedVolumeDirs |> Set.ofList)

    /// Ajouter un répertoire autorisé pour les volumes (utile pour les tests et extensions).
    /// Utilise un pattern copy-on-write avec ImmutableHashSet.
    let addAllowedVolumeDir (dir: string) =
        let fullDir = Path.GetFullPath(dir)
        lock allowedVolumeBaseDirsRef (fun () -> allowedVolumeBaseDirsRef := Set.add fullDir !allowedVolumeBaseDirsRef)

    /// Valide un chemin de volume (anti-traversée + containment absolu).
    let validateVolumePath (path: string) (label: string) =
        if String.IsNullOrWhiteSpace(path) then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s ne peut pas être vide" label)))

        if path.Contains("..") then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s contient une traversée de répertoire interdite: '%s'" label path
                    )
                )
            )

        if path.Contains("\0") then
            raise (
                RpcException(
                    Status(StatusCode.InvalidArgument, sprintf "%s contient un caractère nul: '%s'" label path)
                )
            )

        let fullPath = Path.GetFullPath(path)
        let snapshot = !allowedVolumeBaseDirsRef

        let isAllowed =
            snapshot
            |> Set.exists (fun fullDir ->
                let fullDirWithSep =
                    if fullDir.EndsWith(Path.DirectorySeparatorChar) then
                        fullDir
                    else
                        fullDir + string Path.DirectorySeparatorChar

                fullPath.StartsWith(fullDirWithSep, StringComparison.OrdinalIgnoreCase))

        if not isAllowed then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s n'est pas dans un répertoire autorisé: '%s'" label fullPath
                    )
                )
            )

    /// Valide un chemin de namespace Linux (netns) pour les plugins CNI.
    /// Doit être un chemin absolu sans traversée ni caractères dangereux.
    let validateNetnsPath (path: string) (label: string) =
        if String.IsNullOrWhiteSpace(path) then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s ne peut pas être vide" label)))

        if path.Length > 1024 then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s dépasse 1024 caractères" label)))

        if path.Contains("..") then
            raise (
                RpcException(
                    Status(
                        StatusCode.InvalidArgument,
                        sprintf "%s contient une traversée de répertoire interdite: '%s'" label path
                    )
                )
            )

        if path.Contains("\0") then
            raise (RpcException(Status(StatusCode.InvalidArgument, sprintf "%s contient un caractère nul" label)))

        for c in path do
            if Array.exists (fun dc -> dc = c) dangerousChars then
                raise (
                    RpcException(
                        Status(
                            StatusCode.InvalidArgument,
                            sprintf "%s contient un caractère interdit: '%c' dans '%s'" label c path
                        )
                    )
                )
