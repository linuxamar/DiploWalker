namespace Diplo.Abstractions.Tests

module SecurityValidationTests =

    open System
    open Grpc.Core
    open Xunit
    open FsUnit.Xunit
    open Diplo.Abstractions.SecurityValidation

    // ── validateCommand ──────────────────────────────────────────────

    [<Fact>]
    let ``validateCommand avec commande vide lève une exception`` () =
        (fun () -> validateCommand [||]) |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec null lève une exception`` () =
        (fun () -> validateCommand null) |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec commande simple valide passe`` () = validateCommand [| "ls" |]

    [<Fact>]
    let ``validateCommand avec arguments multiples valide passe`` () =
        validateCommand [| "echo"; "hello"; "world" |]

    [<Fact>]
    let ``validateCommand avec plus de 64 arguments lève une exception`` () =
        let args = Array.init 65 (fun i -> sprintf "arg%d" i)
        (fun () -> validateCommand args) |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec caractère point-virgule lève une exception`` () =
        (fun () -> validateCommand [| "ls;rm" |]) |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec pipe lève une exception`` () =
        (fun () -> validateCommand [| "ls|cat" |]) |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec backtick lève une exception`` () =
        (fun () -> validateCommand [| "`whoami`" |]) |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec variable PATH interdite lève une exception`` () =
        (fun () -> validateCommand [| "%PATH%" |]) |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec variable WINDIR interdite lève une exception`` () =
        (fun () -> validateCommand [| "%WINDIR%\\system32" |])
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec préfixe double slash lève une exception`` () =
        (fun () -> validateCommand [| "\\\\server\\share" |])
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec exécutable contenant slash lève une exception`` () =
        (fun () -> validateCommand [| "/bin/sh"; "arg" |])
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec argument trop long lève une exception`` () =
        let longArg = String('a', 1025)
        (fun () -> validateCommand [| longArg |]) |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec espace dans argument passe`` () =
        validateCommand [| "ls"; "-la" |]
        validateCommand [| "echo"; "bonjour monde" |]

    [<Fact>]
    let ``validateCommand avec espace suivi de caractere dangereux leve une exception`` () =
        (fun () -> validateCommand [| "echo"; "bonjour; monde" |])
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec redirection lève une exception`` () =
        (fun () -> validateCommand [| "ls>out.txt" |]) |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCommand avec parenthèse lève une exception`` () =
        (fun () -> validateCommand [| "ls(" |]) |> should throw typeof<Exception>

    // ── validateCidr ────────────────────────────────────────────────

    [<Fact>]
    let ``validateCidr avec vide ne lève pas d'exception`` () = validateCidr "" "test"

    [<Fact>]
    let ``validateCidr avec CIDR valide passe`` () = validateCidr "192.168.1.0/24" "test"

    [<Fact>]
    let ``validateCidr avec IP seule passe`` () = validateCidr "10.0.0.1" "test"

    [<Fact>]
    let ``validateCidr avec octet > 255 lève une exception`` () =
        (fun () -> validateCidr "256.0.0.1/24" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCidr avec octet négatif lève une exception`` () =
        (fun () -> validateCidr "-1.0.0.1/24" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCidr avec masque > 32 lève une exception`` () =
        (fun () -> validateCidr "10.0.0.1/33" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCidr avec masque négatif lève une exception`` () =
        (fun () -> validateCidr "10.0.0.1/-1" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCidr avec trop de parties lève une exception`` () =
        (fun () -> validateCidr "1.2.3.4/24/extra" "test")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCidr avec IP incomplète lève une exception`` () =
        (fun () -> validateCidr "192.168.1" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCidr avec masque 0 est valide`` () = validateCidr "10.0.0.0/0" "test"

    [<Fact>]
    let ``validateCidr avec masque 32 est valide`` () = validateCidr "10.0.0.1/32" "test"

    // ── validateIp ──────────────────────────────────────────────────

    [<Fact>]
    let ``validateIp avec vide ne lève pas d'exception`` () = validateIp "" "test"

    [<Fact>]
    let ``validateIp avec IP valide passe`` () = validateIp "192.168.1.1" "test"

    [<Fact>]
    let ``validateIp avec 127.0.0.1 valide passe`` () = validateIp "127.0.0.1" "test"

    [<Fact>]
    let ``validateIp avec octet > 255 lève une exception`` () =
        (fun () -> validateIp "999.999.999.999" "test")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateIp avec 3 parties lève une exception`` () =
        (fun () -> validateIp "192.168.1" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateIp avec 5 parties lève une exception`` () =
        (fun () -> validateIp "1.2.3.4.5" "test") |> should throw typeof<Exception>

    // ── validateContainerId ──────────────────────────────────────────

    [<Fact>]
    let ``validateContainerId avec vide lève une exception`` () =
        (fun () -> validateContainerId "") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateContainerId avec hex valide passe`` () = validateContainerId "abc123def456"

    [<Fact>]
    let ``validateContainerId avec GUID valide passe`` () =
        validateContainerId "a1b2c3d4-e5f6-7890-abcd-ef1234567890"

    [<Fact>]
    let ``validateContainerId avec underscore valide passe`` () =
        validateContainerId "container_test-123"

    [<Fact>]
    let ``validateContainerId avec caractères interdits lève une exception`` () =
        (fun () -> validateContainerId "container;rm -rf /")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateContainerId avec espace lève une exception`` () =
        (fun () -> validateContainerId "container with space")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateContainerId avec pipe lève une exception`` () =
        (fun () -> validateContainerId "container|cmd")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateContainerId avec backtick lève une exception`` () =
        (fun () -> validateContainerId "container`cmd`")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateContainerId trop long lève une exception`` () =
        let longId = String('a', 129)
        (fun () -> validateContainerId longId) |> should throw typeof<Exception>

    // ── validateLabel ───────────────────────────────────────────────

    [<Fact>]
    let ``validateLabel avec clé et valeur valides passe`` () = validateLabel "app" "my-app"

    [<Fact>]
    let ``validateLabel avec clé interdite lève une exception`` () =
        (fun () -> validateLabel "clé invalide" "value")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateLabel avec valeur interdite lève une exception`` () =
        (fun () -> validateLabel "key" "value;injection")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateLabel avec tirets et points passe`` () =
        validateLabel "app.kubernetes.io-name" "test-value"

    // ── validateCniCommand ──────────────────────────────────────────

    [<Fact>]
    let ``validateCniCommand avec ADD valide passe`` () = validateCniCommand "ADD"

    [<Fact>]
    let ``validateCniCommand avec DEL valide passe`` () = validateCniCommand "DEL"

    [<Fact>]
    let ``validateCniCommand avec CHECK valide passe`` () = validateCniCommand "CHECK"

    [<Fact>]
    let ``validateCniCommand avec VERSION valide passe`` () = validateCniCommand "VERSION"

    [<Fact>]
    let ``validateCniCommand avec add en minuscule passe (case insensitive)`` () = validateCniCommand "add"

    [<Fact>]
    let ``validateCniCommand avec commande inconnue lève une exception`` () =
        (fun () -> validateCniCommand "EXEC") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateCniCommand avec vide lève une exception`` () =
        (fun () -> validateCniCommand "") |> should throw typeof<Exception>

    // ── validateGrpcAddress ─────────────────────────────────────────

    [<Fact>]
    let ``validateGrpcAddress avec localhost passe`` () =
        validateGrpcAddress "http://localhost:5000"

    [<Fact>]
    let ``validateGrpcAddress avec 127.0.0.1 passe`` () =
        validateGrpcAddress "http://127.0.0.1:5000"

    [<Fact>]
    let ``validateGrpcAddress avec ::1 passe`` () = validateGrpcAddress "http://[::1]:5000"

    [<Fact>]
    let ``validateGrpcAddress avec https localhost passe`` () =
        validateGrpcAddress "https://localhost:5001"

    [<Fact>]
    let ``validateGrpcAddress avec adresse distante lève une exception`` () =
        (fun () -> validateGrpcAddress "http://192.168.1.100:5000")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateGrpcAddress avec nom de domaine lève une exception`` () =
        (fun () -> validateGrpcAddress "http://example.com:5000")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateGrpcAddress avec vide lève une exception`` () =
        (fun () -> validateGrpcAddress "") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateGrpcAddress avec schéma ftp lève une exception`` () =
        (fun () -> validateGrpcAddress "ftp://localhost:5000")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateGrpcAddress avec URL invalide lève une exception`` () =
        (fun () -> validateGrpcAddress "not-a-url") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateGrpcAddress avec adresse pipe valide passe`` () =
        validateGrpcAddress "http://pipe:/diplo-container"

    [<Fact>]
    let ``validateGrpcAddress avec adresse pipe à plusieurs segments passe`` () =
        validateGrpcAddress "http://pipe:/diplo/container"

    [<Fact>]
    let ``validateGrpcAddress avec nom de pipe vide lève une exception`` () =
        (fun () -> validateGrpcAddress "http://pipe:/")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateGrpcAddress avec nom de pipe contenant backslash lève une exception`` () =
        (fun () -> validateGrpcAddress @"http://pipe:/diplo\..\evil")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateGrpcAddress avec nom de pipe contenant traversée lève une exception`` () =
        (fun () -> validateGrpcAddress "http://pipe:/../diplo")
        |> should throw typeof<Exception>

    // ── validatePath ────────────────────────────────────────────────

    [<Fact>]
    let ``validatePath avec vide lève une exception`` () =
        (fun () -> validatePath "" "/tmp" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validatePath avec traversée .. lève une exception`` () =
        (fun () -> validatePath "../../etc/passwd" "/tmp" "test")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validatePath avec sous-dossier valide passe`` () =
        validatePath "data/file.txt" "/tmp" "test"

    [<Fact>]
    let ``validatePath qui sort du répertoire de base lève une exception`` () =
        let baseDir =
            IO.Path.Combine(IO.Path.GetTempPath(), "diplo-test-" + Guid.NewGuid().ToString("N"))

        IO.Directory.CreateDirectory(baseDir) |> ignore

        try
            (fun () -> validatePath @"C:\Windows\System32\cmd.exe" baseDir "test")
            |> should throw typeof<Exception>
        finally
            IO.Directory.Delete(baseDir, true)

    // ── validateCniPluginPath ───────────────────────────────────────

    [<Fact>]
    let ``validateCniPluginPath avec vide lève une exception`` () =
        Assert.Throws<RpcException>(fun () -> validateCniPluginPath "" |> ignore)

    [<Fact>]
    let ``validateCniPluginPath avec chemin dans répertoire autorisé retourne le chemin résolu`` () =
        let allowedDir =
            IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "containerd",
                "cni",
                "bin"
            )

        let path = IO.Path.Combine(allowedDir, "bridge.exe")
        let result = validateCniPluginPath path
        result |> should not' (be NullOrEmptyString)

    [<Fact>]
    let ``validateCniPluginPath avec chemin non autorisé lève une exception`` () =
        Assert.Throws<RpcException>(fun () -> validateCniPluginPath @"C:\malicious\path\plugin.exe" |> ignore)

    [<Fact>]
    let ``validateCniPluginPath avec opt cni bin est valide`` () =
        let path = @"C:\opt\cni\bin\bridge.exe"
        let result = validateCniPluginPath path
        result |> should not' (be NullOrEmptyString)

    // ── validateVolumePath ──────────────────────────────────────────

    [<Fact>]
    let ``validateVolumePath avec vide lève une exception`` () =
        (fun () -> validateVolumePath "" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateVolumePath avec traversée .. lève une exception`` () =
        (fun () -> validateVolumePath "../../etc/passwd" "test")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateVolumePath avec caractère nul lève une exception`` () =
        (fun () -> validateVolumePath "file\0.txt" "test")
        |> should throw typeof<Exception>

    // ── validateName ────────────────────────────────────────────────

    [<Fact>]
    let ``validateName avec vide lève une exception`` () =
        (fun () -> validateName "" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateName valide passe`` () = validateName "my-network" "test"

    [<Fact>]
    let ``validateName avec caractères interdits lève une exception`` () =
        (fun () -> validateName "réseau;injection" "test")
        |> should throw typeof<Exception>

    [<Fact>]
    let ``validateName avec points et tirets passe`` () = validateName "test.name_v1" "test"

    [<Fact>]
    let ``validateName trop long lève une exception`` () =
        let longName = String('a', 65)
        (fun () -> validateName longName "test") |> should throw typeof<Exception>

    // ── validateId ──────────────────────────────────────────────────

    [<Fact>]
    let ``validateId avec vide lève une exception`` () =
        (fun () -> validateId "" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateId valide passe`` () = validateId "abc-123_def" "test"

    [<Fact>]
    let ``validateId avec caractères interdits lève une exception`` () =
        (fun () -> validateId "id;injection" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateId avec espace lève une exception`` () =
        (fun () -> validateId "id with space" "test") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateId trop long lève une exception`` () =
        let longId = String('a', 129)
        (fun () -> validateId longId "test") |> should throw typeof<Exception>

    // ── validateImage ───────────────────────────────────────────────

    [<Fact>]
    let ``validateImage avec vide lève une exception`` () =
        (fun () -> validateImage "") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateImage avec nom simple valide passe`` () = validateImage "nginx"

    [<Fact>]
    let ``validateImage avec registry et tag valide passe`` () =
        validateImage "docker.io/library/nginx:latest"

    [<Fact>]
    let ``validateImage avec caractères interdits lève une exception`` () =
        (fun () -> validateImage "image;rm -rf /") |> should throw typeof<Exception>

    [<Fact>]
    let ``validateImage avec digest valide passe`` () = validateImage "nginx@sha256:abc123"

    [<Fact>]
    let ``validateImage trop long lève une exception`` () =
        let longImage = String('a', 513)
        (fun () -> validateImage longImage) |> should throw typeof<Exception>

    // ── validateFilePath (fichiers de configuration compose) ────────

    [<Fact>]
    let ``validateFilePath accepte un chemin absolu yaml`` () =
        validateFilePath @"C:\stack\docker-compose.yaml" "Le fichier"

    [<Fact>]
    let ``validateFilePath accepte l'extension yml`` () =
        validateFilePath @"C:\stack\compose.yml" "Le fichier"

    [<Fact>]
    let ``validateFilePath rejette un chemin relatif`` () =
        (fun () -> validateFilePath "compose.yaml" "Le fichier") |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateFilePath rejette une traversée ..`` () =
        (fun () -> validateFilePath @"C:\stack\..\secret.yaml" "Le fichier")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateFilePath rejette un chemin UNC`` () =
        (fun () -> validateFilePath @"\\srv\share\compose.yaml" "Le fichier")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateFilePath rejette un chemin réseau en slashes`` () =
        (fun () -> validateFilePath "//srv/share/compose.yml" "Le fichier")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateFilePath rejette une extension non yaml`` () =
        (fun () -> validateFilePath @"C:\stack\config.json" "Le fichier")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateFilePath rejette un caractère nul`` () =
        (fun () -> validateFilePath "C:\\stack\\a\u0000.yaml" "Le fichier")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateFilePath rejette un chemin vide`` () =
        (fun () -> validateFilePath "" "Le fichier") |> should throw typeof<RpcException>

    // ── validateContainerPath (chemins dans les commandes internes) ─

    [<Fact>]
    let ``validateContainerPath accepte un chemin POSIX simple`` () =
        validateContainerPath "/app/data.txt" "La destination"

    [<Fact>]
    let ``validateContainerPath accepte un chemin Windows simple`` () =
        validateContainerPath @"C:\data\out.log" "La destination"

    [<Fact>]
    let ``validateContainerPath rejette une traversée ..`` () =
        (fun () -> validateContainerPath "/app/../etc/passwd" "La destination")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateContainerPath rejette un point-virgule`` () =
        (fun () -> validateContainerPath "/app/a;rm" "La destination") |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateContainerPath rejette une redirection >`` () =
        (fun () -> validateContainerPath "/app/x>y" "La destination") |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateContainerPath rejette un caractère nul`` () =
        (fun () -> validateContainerPath "/app/a\u0000b" "La destination")
        |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateContainerPath rejette un chemin trop long`` () =
        let long = String('a', 1025)
        (fun () -> validateContainerPath long "La destination") |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateContainerPath rejette un chemin vide`` () =
        (fun () -> validateContainerPath "" "La destination") |> should throw typeof<RpcException>

    // ── validateNetnsPath (namespaces réseau pour plugins CNI) ──────

    [<Fact>]
    let ``validateNetnsPath accepte un chemin /proc standard`` () =
        validateNetnsPath "/proc/1234/ns/net" "Le netns"

    [<Fact>]
    let ``validateNetnsPath rejette une traversée ..`` () =
        (fun () -> validateNetnsPath "/proc/../etc" "Le netns") |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateNetnsPath rejette une substitution de commande $`` () =
        (fun () -> validateNetnsPath "/proc/$(id)/ns/net" "Le netns") |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateNetnsPath rejette un backtick`` () =
        (fun () -> validateNetnsPath "/proc/`id`/ns/net" "Le netns") |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateNetnsPath rejette un chemin vide`` () =
        (fun () -> validateNetnsPath "" "Le netns") |> should throw typeof<RpcException>

    [<Fact>]
    let ``validateNetnsPath rejette un chemin trop long`` () =
        let long = String('p', 1025)
        (fun () -> validateNetnsPath long "Le netns") |> should throw typeof<RpcException>

    // ── addAllowedVolumeDir (liste blanche dynamique des volumes) ───
    //
    // La liste est globale et additive : on utilise des répertoires temporaires
    // uniques par exécution, jamais retirés (effet résiduel inoffensif).

    [<Fact>]
    let ``addAllowedVolumeDir autorise un nouveau répertoire de base`` () =
        let unique = "diplo-voldir-" + Guid.NewGuid().ToString("N")
        let candidate = IO.Path.Combine(IO.Path.GetPathRoot(IO.Path.GetTempPath()), unique)
        let other =
            IO.Path.Combine(IO.Path.GetPathRoot(IO.Path.GetTempPath()), "diplo-other-" + Guid.NewGuid().ToString("N"))

        try
            // Avant ajout : rejeté (hors des bases par défaut).
            (fun () -> validateVolumePath (IO.Path.Combine(candidate, "sub")) "Le chemin")
            |> should throw typeof<RpcException>

            addAllowedVolumeDir candidate

            // Après ajout : accepté, y compris en sous-répertoire.
            validateVolumePath (IO.Path.Combine(candidate, "sub")) "Le chemin"
            validateVolumePath (IO.Path.Combine(candidate, "sub", "deeper")) "Le chemin"

            // Un autre répertoire reste rejeté.
            (fun () -> validateVolumePath (IO.Path.Combine(other, "x")) "Le chemin")
            |> should throw typeof<RpcException>
        finally
            try
                IO.Directory.Delete(candidate, true)
            with _ -> ()
            try
                IO.Directory.Delete(other, true)
            with _ -> ()
