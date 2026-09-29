namespace DiploWalker.Container.Tests

module RegistryAuthTests =

    open System
    open System.IO
    open System.Text.Json
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Abstractions
    open DiploWalker.Container

    let shouldContain (substring: string) (text: string) = Assert.Contains(substring, text)

    let shouldNotContain (substring: string) (text: string) = Assert.DoesNotContain(substring, text)

    /// Exécutable témoin du PATH, présent sur les deux plateformes. Sous Windows
    /// l'extension est obligatoire : `tryFindOnPath` teste `File.Exists` sur le
    /// chemin composé, et .NET n'applique pas la résolution PATHEXT du shell —
    /// `cmd` seul ne correspond à aucun fichier.
    let private pathWitness = if OperatingSystem.IsWindows() then "cmd.exe" else "pwsh"

    /// Crée un fichier d'état temporaire isolé pour chaque test.
    let private withStateFile (test: string -> unit) =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-registry-tests-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory dir |> ignore
        let path = Path.Combine(dir, "registry-auth.json")

        try
            test path
        finally
            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``add persist l'identifiant et le chiffre (pas de mot de passe en clair)`` () =
        withStateFile (fun path ->
            RegistryAuth.add path "myregistry.azurecr.io" "user" "secret"
            let json = File.ReadAllText path
            json |> shouldContain "myregistry.azurecr.io"
            json |> shouldContain "user"
            json |> shouldNotContain "secret")

    [<Fact>]
    let ``tryGetUserArg retourne user:password apres add`` () =
        withStateFile (fun path ->
            RegistryAuth.add path "myregistry.azurecr.io" "user" "secret"

            RegistryAuth.tryGetUserArg path "myregistry.azurecr.io"
            |> should equal (Some "user:secret"))

    [<Fact>]
    let ``remove supprime l'identifiant`` () =
        withStateFile (fun path ->
            RegistryAuth.add path "myregistry.azurecr.io" "user" "secret"
            RegistryAuth.remove path "myregistry.azurecr.io"
            RegistryAuth.tryGetUserArg path "myregistry.azurecr.io" |> should equal None)

    [<Fact>]
    let ``load retourne un etat vide si le fichier n'existe pas`` () =
        withStateFile (fun path -> RegistryAuth.load path |> should be Empty)

    [<Fact>]
    let ``add remplace l'identifiant d'un meme registre`` () =
        withStateFile (fun path ->
            RegistryAuth.add path "reg.example.com" "user1" "pass1"
            RegistryAuth.add path "reg.example.com" "user2" "pass2"

            RegistryAuth.tryGetUserArg path "reg.example.com"
            |> should equal (Some "user2:pass2"))

    // ── normalizeRegistryHost ────────────────────────────────────

    [<Theory>]
    [<InlineData("")>]
    [<InlineData("   ")>]
    let ``normalizeRegistryHost retourne vide pour une entree vide ou blanche`` (input: string) =
        RegistryAuth.normalizeRegistryHost input |> should equal ""

    [<Fact>]
    let ``normalizeRegistryHost mappe docker.io sur son endpoint canonique`` () =
        RegistryAuth.normalizeRegistryHost "docker.io"
        |> should equal "https://registry-1.docker.io"

    [<Fact>]
    let ``normalizeRegistryHost mappe docker.io avec scheme et slash final`` () =
        RegistryAuth.normalizeRegistryHost "https://docker.io/"
        |> should equal "https://registry-1.docker.io"

    [<Fact>]
    let ``normalizeRegistryHost prefixe https pour un hote simple`` () =
        RegistryAuth.normalizeRegistryHost "myregistry.azurecr.io"
        |> should equal "https://myregistry.azurecr.io"

    [<Fact>]
    let ``normalizeRegistryHost retire scheme chemin et slash final`` () =
        RegistryAuth.normalizeRegistryHost "  https://REG.example.com/v2/  "
        |> should equal "https://REG.example.com"

    [<Fact>]
    let ``normalizeRegistryHost accepte un hote avec port`` () =
        RegistryAuth.normalizeRegistryHost "registry.local:5000"
        |> should equal "https://registry.local:5000"

    [<Fact>]
    let ``normalizeRegistryHost rejette un hote sans point ni port`` () =
        RegistryAuth.normalizeRegistryHost "localhost" |> should equal ""

    [<Fact>]
    let ``normalizeRegistryHost accepte le scheme http`` () =
        RegistryAuth.normalizeRegistryHost "http://reg.example.com"
        |> should equal "https://reg.example.com"

    [<Fact>]
    let ``tryFindOnPath trouve un executable present dans le PATH`` () =
        // Le lanceur Unix du helper execute pwsh : il doit etre trouvable.
        RegistryAuth.tryFindOnPath pathWitness
        |> Option.isSome
        |> should equal true

    [<Fact>]
    let ``tryFindOnPath ignore un nom absent du PATH`` () =
        RegistryAuth.tryFindOnPath "diplo-outil-inexistant-9f2c" |> should equal None

    [<Fact>]
    let ``tryFindOnPath ignore les entrees vides du PATH`` () =
        // Une entree vide vaut "." pour un shell POSIX ; on l'ecarte plutot,
        // resoudre le helper depuis le repertoire courant serait une fuite.
        // Le nom cherche doit rester trouvable malgre les entrees vides.
        //
        // Les entrees vides sont produites avec le separateur de la plateforme
        // (« ; » sous Windows) : un « :: » ne creerait aucune entree vide sous
        // Windows, mais agglutinerait « ::C:\WINDOWS\system32 » en un seul
        // repertoire inexistant.
        let saved = Environment.GetEnvironmentVariable "PATH"
        let sep = string Path.PathSeparator
        let withBlanks = sep + sep + (if isNull saved then "" else saved) + sep + sep

        try
            Environment.SetEnvironmentVariable("PATH", withBlanks)

            RegistryAuth.tryFindOnPath pathWitness
            |> Option.isSome
            |> should equal true
        finally
            Environment.SetEnvironmentVariable("PATH", saved)

    // ── prepareHostsDir / ensureHelper ───────────────────────────

    [<Fact>]
    let ``prepareHostsDir retourne None pour un registre non exploitable`` () =
        RegistryAuth.prepareHostsDir "localvolume" |> should equal None

    [<Fact>]
    let ``prepareHostsDir genere hosts.toml deleguant au helper`` () =
        match RegistryAuth.prepareHostsDir "myregistry.azurecr.io" with
        | None -> failwith "prepareHostsDir aurait dû réussir"
        | Some root ->
            try
                let tomlPath = Path.Combine(root, "myregistry.azurecr.io", "hosts.toml")
                File.Exists tomlPath |> should be True
                let toml = File.ReadAllText tomlPath
                toml |> shouldContain "[host.\"https://myregistry.azurecr.io\"]"
                toml |> shouldContain "capabilities = [\"pull\", \"resolve\"]"
                toml |> shouldContain "auth = '"
                // Lanceur de la plateforme : .cmd sous Windows, script sh ailleurs.
                toml
                |> shouldContain
                    (if OperatingSystem.IsWindows() then
                         "diplo-cred-helper.cmd"
                     else
                         "diplo-cred-helper")
            finally
                try
                    Directory.Delete(root, true)
                with _ ->
                    ()

    [<Fact>]
    let ``prepareHostsDir utilise l'endpoint canonique de docker.io comme repertoire`` () =
        match RegistryAuth.prepareHostsDir "docker.io" with
        | None -> failwith "prepareHostsDir aurait dû réussir"
        | Some root ->
            try
                Directory.Exists(Path.Combine(root, "registry-1.docker.io"))
                |> should be True
            finally
                try
                    Directory.Delete(root, true)
                with _ ->
                    ()

    [<Fact>]
    let ``ensureHelper est idempotent (meme contenu apres double appel)`` () =
        let cmdPath1 = RegistryAuth.ensureHelper ()
        let ps1Path = Path.Combine(Path.GetDirectoryName cmdPath1, "diplo-cred-helper.ps1")
        let content1 = File.ReadAllText ps1Path

        let cmdPath2 = RegistryAuth.ensureHelper ()

        cmdPath2 |> should equal cmdPath1
        File.Exists cmdPath2 |> should be True
        File.ReadAllText ps1Path |> should equal content1

    // ── Script du helper (protocole docker-credential) ──────────
    //
    // Le vrai script lit le fichier d'état et la clé de chiffrement indiqués
    // par l'environnement : les tests exécutent le script d'un helper jetable
    // produit par `RegistryAuth.writeHelperTo` et pointé sur un état temporaire,
    // jamais sur le vrai fichier.
    //
    // Le même script PowerShell sert sous les deux plateformes (DPAPI sous
    // Windows, AES-GCM ailleurs) : nécessite PowerShell 7 (`pwsh`) dans le PATH
    // sous Unix.
    //
    // Le script est invoqué DIRECTEMENT, et non par le lanceur. Celui-ci est un
    // wrapper de trois lignes (`cmd.exe` → `powershell -ExecutionPolicy Bypass
    // -File %~dp0\...`) : l'exécuter depuis les tests produisait une chaîne
    // comportementale — dépôt d'un script en répertoire temporaire, lancement
    // via cmd.exe, contournement de la stratégie d'exécution, déchiffrement
    // puis restitution d'un identifiant — que les moteurs heuristiques des
    // antivirus (Avira) signalent comme un vol d'identifiants, à raison. Ce qui
    // mérite réellement d'être couvert est préservé : protocole stdin, DPAPI /
    // AES-GCM, découverte des chemins par le script. Le lanceur reste couvert
    // par `ensureHelper` et `prepareHostsDir`, qui vérifient son contenu sans
    // l'exécuter ; son seul rôle est de poser les deux variables ci-dessous
    // puis d'appeler le script.
    //
    // `-ExecutionPolicy` est donc volontairement absent : c'est la stratégie
    // d'exécution de la machine qui s'applique. Le test échouerait sur une
    // machine en `Restricted` ou `AllSigned`, ce qu'aucune machine de
    // développement ou de CI ne définit par défaut (`RemoteSigned` est la
    // valeur par défaut de Windows).
    /// `Environment.SetEnvironmentVariable` est global au processus : le verrou
    /// sérialise le couple set / Process.Start / restore. Sans lui, deux tests
    /// visant deux états différents pourraient lire l'un le chemin posé par
    /// l'autre.
    let private helperEnvLock = obj ()

    /// Exécute le script du helper en lui envoyant l'URL du serveur sur stdin
    /// (protocole docker-credential) et lit sa sortie standard. `env` reproduit
    /// ce que le lanceur poserait ; les entrées vides sont ignorées, comme le
    /// fait `writeHelperTo`.
    let private runHelperScript (ps1Path: string) (env: (string * string) list) (stdinLine: string option) =
        let exe = if OperatingSystem.IsWindows() then "powershell" else "pwsh"

        let vars =
            env
            |> List.filter (fun (_, value) -> not (String.IsNullOrEmpty value))
            |> List.map (fun (name, value) -> name, value)

        lock helperEnvLock (fun () ->
            let saved = vars |> List.map (fun (name, _) -> name, Environment.GetEnvironmentVariable name)

            try
                for name, value in vars do
                    Environment.SetEnvironmentVariable(name, value)

                ProcessExec.runWithResult exe [ "-NoProfile"; "-File"; ps1Path ] (Some 30000) stdinLine None
            finally
                for name, previous in saved do
                    Environment.SetEnvironmentVariable(name, previous))

    /// Helper jetable pointé sur un état temporaire : reproduit ce que le
    /// lanceur poserait pour un helper dédié, dont `writeHelperTo` reçoit les
    /// deux chemins explicites.
    let private runTempHelperScript (root: string) (statePath: string) (stdinLine: string option) =
        let keyPath = RegistryAuth.keyFile ()
        let launcher = RegistryAuth.writeHelperTo (Path.Combine(root, "helper")) statePath keyPath
        let ps1Path = Path.Combine(Path.GetDirectoryName launcher, "diplo-cred-helper.ps1")
        runHelperScript ps1Path [ "DIPLO_REGISTRY_AUTH_STATE", statePath; "DIPLO_REGISTRY_KEY_FILE", keyPath ] stdinLine

    /// Prépare la structure <racine>\Diplo\registry-auth.json attendue par le helper.
    let private withHelperState (test: string -> string -> unit) =
        let root =
            Path.Combine(Path.GetTempPath(), "diplo-helper-tests-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(Path.Combine(root, "Diplo")) |> ignore
        let statePath = Path.Combine(root, "Diplo", "registry-auth.json")

        try
            test root statePath
        finally
            try
                Directory.Delete(root, true)
            with _ ->
                ()

    [<Fact>]
    let ``le helper repond Username et Secret pour le registre demande`` () =
        withHelperState (fun root statePath ->
            // Guillemets et backslash dans l'utilisateur : vérifie l'échappement JSON.
            RegistryAuth.add statePath "myregistry.azurecr.io" "us\"er\\x" "s3cret!"

            let (code, stdout, _) = runTempHelperScript root statePath (Some "https://myregistry.azurecr.io/v1/")

            code |> should equal 0

            let json = JsonSerializer.Deserialize<JsonElement>(json = stdout)

            json.GetProperty("Username").GetString()
            |> should equal "us\"er\\x"

            json.GetProperty("Secret").GetString() |> should equal "s3cret!")

    [<Fact>]
    let ``le helper echoue sans sortie pour un registre inconnu`` () =
        withHelperState (fun root statePath ->
            RegistryAuth.add statePath "known.example.com" "user" "pass"

            let (code, stdout, _) = runTempHelperScript root statePath (Some "https://nothere.tld/v1/")

            Assert.NotEqual(0, code)
            stdout.Trim() |> should equal "")

    [<Fact>]
    let ``le helper repond pour docker.io via son endpoint canonique`` () =
        withHelperState (fun root statePath ->
            RegistryAuth.add statePath "docker.io" "hubuser" "hubpass"

            let (code, stdout, _) = runTempHelperScript root statePath (Some "https://registry-1.docker.io/v1/")

            code |> should equal 0

            let json = JsonSerializer.Deserialize<JsonElement>(json = stdout)

            json.GetProperty("Username").GetString() |> should equal "hubuser"
            json.GetProperty("Secret").GetString() |> should equal "hubpass")

    /// Helper partagé, comme installé par `ensureHelper` : ni le lanceur ni
    /// l'environnement ne transportent de chemin, le script découvre son fichier
    /// d'état et sa clé de chiffrement par rapport à son propre emplacement
    /// (`<racine>/cred-helper/..`).
    [<Fact>]
    let ``le helper partage retrouve son etat et sa cle sans chemin fourni`` () =
        withHelperState (fun root statePath ->
            // Le helper partage vit dans <root>/cred-helper : son etat et sa
            // cle sont donc directement sous <root> (le script les decouvre
            // un niveau au-dessus), et non dans <root>/Diplo.
            let sharedState = Path.Combine(root, "registry-auth.json")

            // La cle AES-GCM n'existe que hors Windows : sous Windows le mot de
            // passe est scelle par DPAPI et le script ignore entierement
            // DIPLO_REGISTRY_KEY_FILE (cf. helperScript). La copier ici
            // echouerait avec un FileNotFoundException sous Windows.
            if not (OperatingSystem.IsWindows()) then
                File.Copy(RegistryAuth.keyFile (), Path.Combine(root, "registry-key.bin"), true)

            RegistryAuth.add sharedState "shared.example.com" "shareduser" "sharedpass"

            let launcher = RegistryAuth.writeHelperTo (Path.Combine(root, "cred-helper")) "" ""
            let ps1Path = Path.Combine(Path.GetDirectoryName launcher, "diplo-cred-helper.ps1")
            let (code, stdout, _) = runHelperScript ps1Path [] (Some "https://shared.example.com/v1/")

            code |> should equal 0

            let json = JsonSerializer.Deserialize<JsonElement>(json = stdout)

            json.GetProperty("Username").GetString() |> should equal "shareduser"
            json.GetProperty("Secret").GetString() |> should equal "sharedpass")

