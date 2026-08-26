namespace Diplo.Container.Clients

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Serilog
open Grpc.Core
open Diplo.Abstractions
open Diplo.Abstractions.Interfaces
open Diplo.Container

type ContainerdClient(runner: IProcessRunner, ?logPollIntervalMs: int, ?ctrPath: string) =

    let logPollIntervalMs = defaultArg logPollIntervalMs 500
    let ctrPath = defaultArg ctrPath "ctr"

    let runCtr (args: string list) = runner.RunWithArgs(ctrPath, args)

    let nsArgs (namespaceName: string) (subcommand: string list) =
        "--namespace" :: namespaceName :: subcommand

    let parseJson (text: string) =
        use doc = JsonDocument.Parse(text, JsonDocumentOptions(MaxDepth = 32))
        doc.RootElement.Clone()

    let validateNsId (namespaceName: string) (id: string) =
        SecurityValidation.validateId namespaceName "Le namespace"
        SecurityValidation.validateContainerId id

    let validateNs (namespaceName: string) =
        SecurityValidation.validateId namespaceName "Le namespace"

    /// Sépare un REF containerd en (repository, tag). Un ref avec digest
    /// (@sha256:...) est considéré sans tag. Le tag est le segment après le
    /// dernier ':' SITUÉ APRÈS le dernier '/' (sinon c'est le port du registre).
    let splitRef (refName: string) =
        let at = refName.IndexOf('@')
        let nameOnly = if at >= 0 then refName.Substring(0, at) else refName

        let lastSlash = nameOnly.LastIndexOf('/')
        let lastColon = nameOnly.LastIndexOf(':')

        if lastColon > lastSlash then
            (nameOnly.Substring(0, lastColon), nameOnly.Substring(lastColon + 1))
        else
            (nameOnly, "")

    // ── Cache mutualisé des requêtes ctr coûteuses ──────────────────────
    //
    // Chaque flux (suivi de logs, watch d'événements, stats) interrogeait
    // `ctr tasks list` à sa propre cadence : quelques clients suffisaient à
    // générer des centaines de processus/minute. Un TTL court mutualise un
    // seul spawn entre tous les consommateurs d'une même fenêtre.
    let sharedCache =
        System.Collections.Concurrent.ConcurrentDictionary<string, DateTime * string>()

    let cachedRun (ttlMs: int) (key: string) (fetch: unit -> string) : string =
        match sharedCache.TryGetValue(key) with
        | true, (expiresAt, value) when DateTime.UtcNow < expiresAt -> value
        | _ ->
            let value = fetch ()
            sharedCache[key] <- (DateTime.UtcNow.AddMilliseconds(float ttlMs), value)
            value

    let tasksListTtlMs = max 250 logPollIntervalMs

    let cachedTasksList (namespaceName: string) =
        cachedRun tasksListTtlMs ("tasks:" + namespaceName) (fun () ->
            runCtr (nsArgs namespaceName [ "tasks"; "list" ]))

    /// Métriques avec TTL court : les flux de stats concurrents du même
    /// conteneur partagent un seul spawn ctr au lieu d'un par flux.
    let metricsTtlMs = 750

    interface IContainerdClient with
        member _.CreateContainer
            (namespaceName, id, image, labels, env, command, args, memoryLimit, cpuShares, pidLimit, mounts)
            =
            validateNsId namespaceName id
            SecurityValidation.validateImage image
            let ctrArgs = ResizeArray()
            ctrArgs.AddRange(nsArgs namespaceName [ "container"; "create" ])

            for (k, v) in labels |> Map.toList do
                SecurityValidation.validateLabel k v
                ctrArgs.Add("--label")
                ctrArgs.Add(sprintf "%s=%s" k v)

            for (k, v) in env |> Map.toList do
                ctrArgs.Add("--env")
                ctrArgs.Add(sprintf "%s=%s" k v)

            for (src, dst, readOnly) in mounts do
                SecurityValidation.validateVolumePath src "La source du volume"

                // La spec --mount est une liste séparée par des virgules : un
                // champ contenant une virgule ou un espace permettrait d'injecter
                // des options arbitraires (ex. annuler le « ro » imposé).
                let validateMountSegment (value: string) (label: string) =
                    if String.IsNullOrEmpty value then
                        invalidArg "mounts" (sprintf "%s du montage ne peut pas être vide" label)

                    if value.Contains("..") then
                        invalidArg
                            "mounts"
                            (sprintf "%s du montage contient une traversée de répertoire interdite" label)

                    if
                        value.IndexOfAny([| ','; ' '; '\t'; '\r'; '\n' |])
                        >= 0
                    then
                        invalidArg "mounts" (sprintf "%s du montage contient un caractère interdit" label)

                validateMountSegment dst "La destination"

                // La source subit les mêmes contrôles de segment --mount :
                // une virgule ou un espace y injecterait des options
                // arbitraires au même titre que dans la destination.
                validateMountSegment src "La source"

                ctrArgs.Add("--mount")
                let options = if readOnly then "rbind,ro" else "rbind"
                ctrArgs.Add(sprintf "type=bind,src=%s,dst=%s,options=%s" src dst options)

            if memoryLimit > 0L then
                ctrArgs.Add("--memory-limit")
                ctrArgs.Add(string memoryLimit)

            if cpuShares > 0L then
                ctrArgs.Add("--cpu-shares")
                ctrArgs.Add(string cpuShares)

            if pidLimit > 0u then
                Log.Warning(
                    "Limite de processus ({PidLimit}) ignorée à la création : non prise en charge par `ctr container create` de cette version",
                    pidLimit
                )

            ctrArgs.Add(image)
            ctrArgs.Add(id)
            // Validation des commandes/args avant injection dans ctr
            if command.Length > 0 || args.Length > 0 then
                let fullCommand = Array.append command args
                SecurityValidation.validateCommand fullCommand

            for c in command do
                ctrArgs.Add(c)

            for a in args do
                ctrArgs.Add(a)

            let output = runCtr (ctrArgs |> Seq.toList)
            // L'id est généré côté service et passé à ctr : la sortie peut
            // contenir des avertissements supplémentaires, on ne l'utilise pas.
            ignore output
            id

        member _.StartContainer(namespaceName, id, detach) =
            validateNsId namespaceName id

            let args =
                if detach then
                    nsArgs namespaceName [ "task"; "start"; "--detach"; id ]
                else
                    nsArgs namespaceName [ "task"; "start"; id ]

            runCtr args |> ignore

        member _.StartContainerWithLogs(namespaceName, id, logFile) =
            validateNsId namespaceName id
            let psi = ProcessStartInfo(ctrPath)

            for a in nsArgs namespaceName [ "task"; "start"; id ] do
                psi.ArgumentList.Add(a)

            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.UseShellExecute <- false
            psi.CreateNoWindow <- true

            // Rotation simple : un journal > 50 Mo bascule en <id>.log.1 pour
            // éviter la saturation disque d'un conteneur bavard.
            if File.Exists(logFile) && (FileInfo(logFile)).Length > 50L * 1024L * 1024L then
                let old = logFile + ".1"
                File.Delete(old)
                File.Move(logFile, old)

            let proc = Process.Start(psi)

            if isNull proc then
                raise (
                    InvalidOperationException(sprintf "Impossible de démarrer le processus ctr pour le conteneur %s" id)
                )

            try
                let dir = Path.GetDirectoryName(logFile)

                if not (String.IsNullOrEmpty(dir)) then
                    Directory.CreateDirectory(dir) |> ignore

                let writer = new StreamWriter(logFile, true)
                writer.AutoFlush <- true
                let mutable disposed = false

                let append (data: string) =
                    if not (isNull data) then
                        lock writer (fun () ->
                            if not disposed then
                                writer.WriteLine(data))

                let closeWriter () =
                    lock writer (fun () ->
                        if not disposed then
                            writer.Flush()
                            writer.Dispose()
                            disposed <- true)

                proc.OutputDataReceived.AddHandler(DataReceivedEventHandler(fun _ e -> append e.Data))
                proc.ErrorDataReceived.AddHandler(DataReceivedEventHandler(fun _ e -> append e.Data))

                // Les lectures asynchrones doivent être armées AVANT
                // EnableRaisingEvents : si le process sort très vite, Exited peut
                // fire avant BeginOutputReadLine et provoquer une
                // ObjectDisposedException (échec rapporté à tort).
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()
                proc.EnableRaisingEvents <- true

                proc.Exited.AddHandler(
                    EventHandler(fun _ _ ->
                        // WaitForExit() sans délai garantit que les callbacks
                        // DataReceived déjà en file ont été traités : sinon les
                        // dernières lignes sont perdues par closeWriter.
                        try
                            proc.WaitForExit()
                        with _ ->
                            ()

                        closeWriter ()
                        proc.Dispose())
                )

                Log.Information("Conteneur {ContainerId} démarré, logs capturés dans {LogFile}", id, logFile)
            with ex ->
                // Nettoyage du processus en cas d'échec de configuration
                try
                    proc.Kill(true)
                with _ ->
                    ()

                try
                    proc.Dispose()
                with _ ->
                    ()

                Log.Error(ex, "Erreur lors du démarrage avec capture des logs du conteneur {ContainerId}", id)
                reraise ()

        member _.StopContainer(namespaceName, id, timeoutSeconds) : Task =
            validateNsId namespaceName id

            task {
                runCtr (nsArgs namespaceName [ "task"; "kill"; "--signal"; "SIGTERM"; id ])
                |> ignore

                // SIGTERM n'est qu'une demande : attendre l'arrêt RÉEL en
                // interrogeant `tasks list`, puis escalader vers SIGKILL sinon.
                let capped = if timeoutSeconds > 0 then min timeoutSeconds 300 else 10
                let deadline = DateTime.UtcNow.AddSeconds(float capped)

                let isRunning () =
                    try
                        let output = cachedTasksList namespaceName

                        output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                        |> Array.exists (fun line ->
                            let parts = line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)

                            parts.Length >= 3
                            && parts[0].Equals(id, StringComparison.OrdinalIgnoreCase)
                            && parts[2].Equals("RUNNING", StringComparison.OrdinalIgnoreCase))
                    with _ ->
                        true // vérification impossible : rester prudent, l'escalade est bornée

                while DateTime.UtcNow < deadline && isRunning () do
                    do! Task.Delay(500)

                if isRunning () then
                    Log.Warning(
                        "Le conteneur {ContainerId} tourne encore après {Timeout}s : envoi de SIGKILL",
                        id,
                        capped
                    )

                    try
                        runCtr (nsArgs namespaceName [ "task"; "kill"; "--signal"; "SIGKILL"; id ])
                        |> ignore
                    with ex ->
                        Log.Warning(ex, "SIGKILL a échoué pour le conteneur {ContainerId}", id)

                    try
                        runCtr (nsArgs namespaceName [ "task"; "delete"; "--force"; id ])
                        |> ignore
                    with ex ->
                        Log.Warning(ex, "Suppression forcée de la tâche a échoué pour {ContainerId}", id)
            }
            :> Task

        member _.DeleteContainer(namespaceName, id, force) =
            validateNsId namespaceName id

            if force then
                // Le kill est best-effort ; en revanche l'échec de la suppression
                // doit remonter : un succès mensonger ferait libérer les volumes
                // montés alors que le conteneur existe peut-être toujours.
                try
                    runCtr (nsArgs namespaceName [ "task"; "kill"; "--signal"; "SIGKILL"; id ])
                    |> ignore
                with ex ->
                    Log.Warning(ex, "Erreur lors de l'arrêt forcé du conteneur {ContainerId}", id)

                try
                    runCtr (nsArgs namespaceName [ "task"; "delete"; "--force"; id ])
                    |> ignore
                with ex ->
                    Log.Debug(ex, "Tâche déjà absente pour le conteneur {ContainerId}", id)

                runCtr (nsArgs namespaceName [ "container"; "delete"; id ]) |> ignore
            else
                runCtr (nsArgs namespaceName [ "container"; "delete"; id ]) |> ignore

        member _.PauseContainer(namespaceName, id) =
            validateNsId namespaceName id
            runCtr (nsArgs namespaceName [ "task"; "pause"; id ]) |> ignore

        member _.ResumeContainer(namespaceName, id) =
            validateNsId namespaceName id
            runCtr (nsArgs namespaceName [ "task"; "resume"; id ]) |> ignore

        member _.WaitForContainerExit(namespaceName, id, timeoutSeconds) =
            validateNsId namespaceName id

            let deadline =
                if timeoutSeconds > 0 then
                    Some(DateTime.UtcNow.AddSeconds(float timeoutSeconds))
                else
                    None

            let currentStatus () =
                try
                    let output = cachedTasksList namespaceName

                    output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                    |> Array.tryFind (fun line ->
                        let parts = line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
                        parts.Length >= 1 && parts[0].Equals(id, StringComparison.OrdinalIgnoreCase))
                    |> Option.map (fun line ->
                        let parts = line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
                        if parts.Length >= 3 then parts[2] else "unknown")
                    |> Option.defaultValue "unknown"
                with ex ->
                    Log.Warning(ex, "Erreur lors de l'attente de sortie du conteneur {ContainerId}", id)
                    "unknown"

            let rec loop () =
                let status = currentStatus ()

                match status.ToUpperInvariant() with
                // PAUSED n'est PAS un état terminal : attendre la reprise.
                | "STOPPED"
                | "DELETED"
                | "UNKNOWN" -> 0
                | _ ->
                    match deadline with
                    | Some d when DateTime.UtcNow >= d -> -1
                    | _ ->
                        Thread.Sleep(1000)
                        loop ()

            loop ()

        member _.UpdateContainer(namespaceName, id, memoryLimit, cpuShares, pidLimit) =
            validateNsId namespaceName id

            if memoryLimit > 0L || cpuShares > 0 || pidLimit > 0 then
                raise (
                    RpcException(
                        Status(
                            StatusCode.Unimplemented,
                            "La mise à jour des limites d'un conteneur n'est pas disponible avec cette version de ctr : la sous-commande `ctr task update` est absente"
                        )
                    )
                )

        member _.InspectContainer(namespaceName, id) =
            validateNsId namespaceName id
            let output = runCtr (nsArgs namespaceName [ "container"; "info"; id ])
            parseJson output

        member _.ListContainers(namespaceName, all) =
            validateNs namespaceName

            let ids =
                runCtr (nsArgs namespaceName [ "container"; "list"; "--quiet" ])
                |> fun output ->
                    output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Array.toList

            if all then
                ids
            else
                let running =
                    cachedTasksList namespaceName
                    |> fun output ->
                        output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                        |> Array.choose (fun line ->
                            let parts = line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)

                            if
                                parts.Length >= 3
                                && parts[2].Equals("RUNNING", StringComparison.OrdinalIgnoreCase)
                            then
                                Some parts[0]
                            else
                                None)
                        |> Set.ofArray

                ids |> List.filter running.Contains

        member _.GetContainerLogs(namespaceName, id, tail, follow, since) =
            validateNsId namespaceName id
            let lines = ContainerLogs.read id tail since

            if lines.Length = 0 then
                [ "Aucun journal pour ce conteneur (le conteneur doit être démarré en mode détaché pour capturer ses logs)" ]
            else
                lines |> Array.toList

        member _.GetContainerLogsStream(namespaceName, id, tail, since, ct) =
            validateNsId namespaceName id

            let isContainerRunning () =
                try
                    cachedTasksList namespaceName
                    |> fun output ->
                        output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                        |> Array.exists (fun line ->
                            let parts = line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)

                            parts.Length >= 3
                            && parts[0].Equals(id, StringComparison.OrdinalIgnoreCase)
                            && parts[2].Equals("RUNNING", StringComparison.OrdinalIgnoreCase))
                with ex ->
                    Log.Warning(ex, "Erreur lors du suivi des logs du conteneur {ContainerId}", id)
                    false

            // Canal et producteur créés PAR ÉNUMÉRATION : plusieurs
            // GetAsyncEnumerator sur le même IAsyncEnumerable ne doivent pas
            // partager un canal ni dupliquer les lignes.
            let producer (writer: System.Threading.Channels.ChannelWriter<string>) (cancel: CancellationToken) =
                async {
                    try
                        // Watermark capturé AVANT la lecture : aucun octet écrit
                        // pendant la lecture n'est ni sauté ni dupliqué ensuite.
                        let snapshot, initialOffset = ContainerLogs.readUpTo id tail since

                        for l in snapshot do
                            writer.TryWrite(l) |> ignore

                        let mutable lastOffset = initialOffset
                        let mutable running = true

                        while running && not cancel.IsCancellationRequested do
                            let lines, offset = ContainerLogs.readIncremental id lastOffset
                            lastOffset <- offset

                            for l in lines do
                                writer.TryWrite(l) |> ignore

                            if isContainerRunning () then
                                do! Async.Sleep logPollIntervalMs
                            else
                                // courte grâce au writer pour vider les derniers octets
                                do! Async.Sleep 150
                                let fin, _ = ContainerLogs.readIncremental id lastOffset

                                for l in fin do
                                    writer.TryWrite(l) |> ignore

                                running <- false

                        writer.TryComplete() |> ignore
                    with ex ->
                        writer.TryComplete(ex) |> ignore
                }

            { new IAsyncEnumerable<string> with
                member _.GetAsyncEnumerator(ct2) =
                    let cts = CancellationTokenSource.CreateLinkedTokenSource(ct, ct2)
                    let channel = System.Threading.Channels.Channel.CreateUnbounded<string>()
                    producer channel.Writer cts.Token |> Async.StartAsTask |> ignore
                    let mutable current = ""

                    { new IAsyncEnumerator<string> with
                        member _.Current = current

                        member _.MoveNextAsync() =
                            let read = channel.Reader.ReadAsync(cts.Token)

                            let task =
                                read
                                    .AsTask()
                                    .ContinueWith(fun (t: Task<string>) ->
                                        if t.IsCompletedSuccessfully then
                                            current <- t.Result
                                            true
                                        else
                                            false)

                            ValueTask<bool>(task)

                        member _.DisposeAsync() =
                            channel.Writer.TryComplete() |> ignore
                            ValueTask() } }

        member _.ExecInContainer(namespaceName, id, command) =
            validateNsId namespaceName id
            SecurityValidation.validateCommand command

            let args =
                [ yield "--namespace"
                  yield namespaceName
                  yield "tasks"
                  yield "exec"
                  yield "--exec-id"
                  yield sprintf "exec-%s" (Guid.NewGuid().ToString("N"))
                  yield id ]
                @ (command |> Array.toList)

            // L'échec est propagé : une sentinelle textuelle retournée comme
            // sortie normale rend l'erreur indistinguable d'un résultat valide.
            runCtr args

        member _.StartExec(namespaceName, id, command, stdin, stdout, stderr) =
            validateNsId namespaceName id
            SecurityValidation.validateCommand command

            let args =
                [ yield "--namespace"
                  yield namespaceName
                  yield "tasks"
                  yield "exec"
                  yield "--exec-id"
                  yield sprintf "exec-%s" (Guid.NewGuid().ToString("N"))
                  yield id ]
                @ (command |> Array.toList)

            try
                let psi = ProcessStartInfo(ctrPath)

                for a in args do
                    psi.ArgumentList.Add(a)

                psi.RedirectStandardInput <- true
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                psi.CreateNoWindow <- true
                let proc = Process.Start(psi)

                let pumpIn =
                    Task.Run(fun () ->
                        stdin.CopyTo(proc.StandardInput.BaseStream)
                        proc.StandardInput.Close())

                let pumpOut = Task.Run(fun () -> proc.StandardOutput.BaseStream.CopyTo(stdout))
                let pumpErr = Task.Run(fun () -> proc.StandardError.BaseStream.CopyTo(stderr))
                let exited = proc.WaitForExit(600_000)

                if not exited then
                    // Sans Kill, les pumps ne se terminent jamais (pipes ouverts)
                    // : attente infinie et processus ctr orphelin.
                    Log.Warning("Timeout (600s) lors de l'exécution en flux dans le conteneur {ContainerId}", id)

                    try
                        proc.Kill(true)
                    with ex ->
                        Log.Warning(ex, "Impossible de tuer le processus ctr exec pour {ContainerId}", id)

                    try
                        Task.WaitAll([| pumpOut; pumpErr; pumpIn |], 5_000) |> ignore
                    with _ ->
                        ()

                    try
                        proc.Dispose()
                    with _ ->
                        ()

                    -1
                else
                    Task.WaitAll(pumpOut, pumpErr)
                    pumpIn.Wait()
                    proc.ExitCode
            with ex ->
                Log.Error(ex, "Erreur lors de l'exécution en flux dans le conteneur {ContainerId}", id)
                -1

        member _.TaskInfo(namespaceName, id) =
            validateNsId namespaceName id

            try
                let output = cachedTasksList namespaceName

                let row =
                    output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                    |> Array.tryFind (fun line ->
                        let parts = line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
                        parts.Length >= 1 && parts[0].Equals(id, StringComparison.OrdinalIgnoreCase))

                match row with
                | None ->
                    use doc = JsonDocument.Parse("{}")
                    doc.RootElement.Clone()
                | Some line ->
                    let parts = line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)

                    // Pid émis en NOMBRE : les consommateurs lisent avec
                    // tryGetInt64 qui exige ValueKind = Number.
                    let mutable pidValue = 0L

                    if parts.Length >= 2 then
                        Int64.TryParse(parts.[1], &pidValue) |> ignore

                    let status = if parts.Length >= 3 then parts[2] else "unknown"

                    let json =
                        System.Text.Json.JsonSerializer.Serialize(
                            {| status = status
                               pid = pidValue
                               exited_at = "" |}
                        )

                    use doc = JsonDocument.Parse(json)
                    doc.RootElement.Clone()
            with ex ->
                Log.Warning(ex, "Erreur lors de la récupération des informations de tâche {ContainerId}", id)
                use doc = JsonDocument.Parse("{}")
                doc.RootElement.Clone()

        member _.PullImage(image, userArg) =
            SecurityValidation.validateImage image

            let registry =
                // Premier segment SANS tag/port : « nginx:latest » est un tag,
                // pas un hôte de registre.
                let firstSegmentRaw = image.Split('/').[0]

                let lastColon = firstSegmentRaw.LastIndexOf(':')

                let firstSegment =
                    if lastColon >= 0 then firstSegmentRaw.Substring(0, lastColon) else firstSegmentRaw

                if
                    firstSegment.Contains('.')
                    || firstSegment.Contains(':')
                    || firstSegment.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                then
                    firstSegment
                else
                    "docker.io"

            match userArg with
            | Some explicit ->
                // Identifiant EXPLICITE (option CLI --user) : repli historique
                // sur argv, seul cas restant — l'utilisateur a choisi la voie
                // directe et éphémère.
                runCtr [ "image"; "pull"; "--user"; explicit; image ] |> fun o -> o.Trim()
            | None ->
                // Identifiants STOCKÉS : passer par le helper de credentials —
                // plus aucun secret dans argv.
                match RegistryAuth.prepareHostsDir registry with
                | Some hostsDir ->
                    try
                        runCtr [ "image"; "pull"; "--hosts-dir"; hostsDir; image ]
                        |> fun o -> o.Trim()
                    finally
                        try
                            Directory.Delete(hostsDir, true)
                        with ex ->
                            Log.Debug(ex, "Nettoyage du hosts-dir temporaire impossible")
                | None ->
                    runCtr [ "image"; "pull"; image ] |> fun o -> o.Trim()

        member _.Version() =
            try
                let output = runCtr [ "version" ]
                let lines = output.Split('\n') |> Array.map (fun line -> line.Trim())

                let valueOf (prefix: string) =
                    lines
                    |> Array.tryPick (fun line ->
                        if line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
                            Some(line.Substring(prefix.Length).Trim())
                        else
                            None)

                match valueOf "Version:" with
                | Some version ->
                    let revision = defaultArg (valueOf "Revision:") ""
                    sprintf "%s (revision: %s)" version revision
                | None -> "Version inconnue"
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération de la version ctr")
                "Version inconnue"

        member _.Namespaces() =
            try
                let output = runCtr [ "namespace"; "list"; "--quiet" ]

                output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Array.toList
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération des namespaces")
                [ "Erreur lors de la récupération des namespaces" ]

        member _.CreateNamespace(name) =
            SecurityValidation.validateId name "Le namespace"
            runCtr [ "namespace"; "create"; name ] |> ignore

        member _.DeleteNamespace(name) =
            SecurityValidation.validateId name "Le namespace"
            runCtr [ "namespace"; "remove"; name ] |> ignore

        member _.RenameContainer(namespaceName, id, newName) =
            validateNsId namespaceName id
            SecurityValidation.validateName newName "Le nouveau nom"
            runCtr (nsArgs namespaceName [ "container"; "rename"; id; newName ]) |> ignore

        member _.TopContainer(namespaceName, id) =
            validateNsId namespaceName id

            try
                let output = runCtr (nsArgs namespaceName [ "task"; "ps"; id ])
                output
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération des processus du conteneur {ContainerId}", id)
                "Erreur lors de la récupération des processus"

        member _.GetContainerStats(namespaceName, id) =
            validateNsId namespaceName id

            try
                let output =
                    cachedRun metricsTtlMs (sprintf "metrics:%s:%s" namespaceName id) (fun () ->
                        runCtr (nsArgs namespaceName [ "task"; "metrics"; id ]))

                parseJson output
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération des métriques du conteneur {ContainerId}", id)
                use doc = JsonDocument.Parse("{}")
                doc.RootElement.Clone()

        member _.ListImages(namespaceName) =
            validateNs namespaceName

            try
                let output = runCtr (nsArgs namespaceName [ "image"; "list" ])

                let lines =
                    output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                // Filtre la ligne d'en-tête "REF TYPE DIGEST ..." de `ctr image list`.
                let dataLines =
                    lines
                    |> Array.filter (fun line ->
                        let parts = line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
                        not (parts.Length >= 1 && parts[0].Equals("REF", StringComparison.OrdinalIgnoreCase)))

                dataLines
                |> Array.map (fun line ->
                    let parts = line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
                    let id = if parts.Length >= 2 then parts[0] else line
                    let repository, tag = splitRef id

                    // tag est indispensable : PruneImages s'appuie dessus pour
                    // ne supprimer que les images non taguées (dangling).
                    let json =
                        System.Text.Json.JsonSerializer.Serialize(
                            {| id = id
                               repository = repository
                               ref = id
                               tag = tag |}
                        )

                    parseJson json)
                |> Array.toList
            with ex ->
                Log.Error(ex, "Erreur lors de la récupération des images")
                []

        member _.InspectImage(namespaceName, imageRef) =
            validateNs namespaceName
            SecurityValidation.validateImage imageRef

            try
                let output = runCtr (nsArgs namespaceName [ "image"; "info"; imageRef ])
                parseJson output
            with ex ->
                Log.Error(ex, "Erreur lors de l'inspection de l'image {ImageRef}", imageRef)
                use doc = JsonDocument.Parse("{}")
                doc.RootElement.Clone()

        member _.RemoveImage(namespaceName, imageRef) =
            validateNs namespaceName
            SecurityValidation.validateImage imageRef

            try
                runCtr (nsArgs namespaceName [ "image"; "remove"; imageRef ]) |> ignore
                sprintf "Image %s supprimée" imageRef
            with ex ->
                Log.Error(ex, "Erreur lors de la suppression de l'image {ImageRef}", imageRef)
                sprintf "Erreur lors de la suppression de l'image %s" imageRef

        member _.TagImage(namespaceName, source, target) =
            validateNs namespaceName
            SecurityValidation.validateImage source
            SecurityValidation.validateImage target
            runCtr (nsArgs namespaceName [ "image"; "tag"; source; target ]) |> ignore

        member _.ExportImage(namespaceName, imageRef, tarFile) =
            validateNs namespaceName
            SecurityValidation.validateImage imageRef
            runCtr (nsArgs namespaceName [ "image"; "export"; tarFile; imageRef ]) |> ignore

        member _.ImportImage(namespaceName, tarFile) =
            validateNs namespaceName
            let output = runCtr (nsArgs namespaceName [ "image"; "import"; tarFile ])

            output.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
            |> Array.toList
