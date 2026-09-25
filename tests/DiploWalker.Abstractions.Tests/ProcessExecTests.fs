namespace DiploWalker.Abstractions.Tests

module ProcessExecTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Abstractions

    let private tempDir () =
        let dir =
            Path.Combine(Path.GetTempPath(), "diplo-process-tests", Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory(dir) |> ignore
        dir

    /// Interpreteur de commandes de la plateforme : `cmd.exe /c` sous Windows,
    /// `sh -c` ailleurs. Les tests restent identiques sur les deux systemes.
    let private shellExe =
        if OperatingSystem.IsWindows() then
            "cmd.exe"
        else
            "sh"

    let private shellCFlag =
        if OperatingSystem.IsWindows() then
            "/c"
        else
            "-c"

    /// Execute une ligne de commande via l'interpreteur de la plateforme.
    let private shell (commandLine: string) =
        ProcessExec.runWithResult shellExe [ shellCFlag; commandLine ] None None None

    /// Commande qui dure plus longtemps que le delai de 500 ms des tests de timeout.
    let private slowCommand =
        if OperatingSystem.IsWindows() then
            "ping -n 6 127.0.0.1 > nul"
        else
            "sleep 5"

    /// Commande qui recopie l'entree standard sur la sortie standard.
    let private echoStdinCommand =
        if OperatingSystem.IsWindows() then
            "more"
        else
            "cat"

    [<Fact>]
    let ``runWithResult capture la sortie standard`` () =
        let code, stdout, stderr = shell "echo hello"
        code |> should equal 0
        stdout.Contains("hello") |> should equal true
        stderr |> should equal ""

    [<Fact>]
    let ``runWithResult capture la sortie d'erreur sans bloquer`` () =
        let code, stdout, stderr = shell "echo err 1>&2"
        code |> should equal 0
        stderr.Contains("err") |> should equal true

    [<Fact>]
    let ``runWithResult retourne le code de sortie non nul`` () =
        let code, _, _ = shell "exit 3"
        code |> should equal 3

    [<Fact>]
    let ``run retourne la sortie standard en cas de succes`` () =
        (ProcessExec.run shellExe [ shellCFlag; "echo ok" ] None None None).Contains("ok")
        |> should equal true

    [<Fact>]
    let ``run leve une exception si le processus echoue`` () =
        (fun () -> ProcessExec.run shellExe [ shellCFlag; "exit 3" ] None None None |> ignore)
        |> should throw typeof<InvalidOperationException>

    [<Fact>]
    let ``runWithResult tue le processus en cas de depassement du delai`` () =
        let stopwatch = Diagnostics.Stopwatch.StartNew()

        (fun () ->
            ProcessExec.runWithResult shellExe [ shellCFlag; slowCommand ] (Some 500) None None
            |> ignore)
        |> should throw typeof<TimeoutException>

        stopwatch.ElapsedMilliseconds |> should be (lessThan 10_000L)

    [<Fact>]
    let ``runWithResult transmet l'entree standard au processus`` () =
        let code, stdout, _ =
            ProcessExec.runWithResult shellExe [ shellCFlag; echoStdinCommand ] None (Some "hello-stdin") None

        code |> should equal 0
        stdout.Contains("hello-stdin") |> should equal true

    [<Fact>]
    let ``atomicWrite ecrit le contenu et cree le repertoire parent`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "sub", "state.json")
            AtomicFile.write path "{\"a\":1}"
            File.ReadAllText(path) |> should equal "{\"a\":1}"
            Directory.Exists(Path.Combine(dir, "sub")) |> should equal true
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``atomicWrite remplace le contenu existant`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "state.json")
            AtomicFile.write path "premier"
            AtomicFile.write path "second"
            File.ReadAllText(path) |> should equal "second"
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``atomicWrite ne laisse aucun fichier temporaire`` () =
        let dir = tempDir ()

        try
            let path = Path.Combine(dir, "state.json")
            AtomicFile.write path "contenu"
            Directory.GetFiles(dir, "*.tmp") |> should be Empty
        finally
            Directory.Delete(dir, true)

    [<Fact>]
    let ``runUnit retourne unit sans erreur si le processus reussit`` () =
        let result = ProcessExec.runUnit shellExe [ shellCFlag; "echo ok" ] None None None
        result |> should equal ()

    [<Fact>]
    let ``runUnit leve une exception si le processus echoue`` () =
        (fun () -> ProcessExec.runUnit shellExe [ shellCFlag; "exit 1" ] None None None)
        |> should throw typeof<InvalidOperationException>

    [<Fact>]
    let ``runUnit leve une exception en cas de timeout`` () =
        (fun () -> ProcessExec.runUnit shellExe [ shellCFlag; slowCommand ] (Some 500) None None)
        |> should throw typeof<TimeoutException>

    [<Fact>]
    let ``MountTimeoutMs vaut 30000`` () =
        ProcessExec.MountTimeoutMs |> should equal 30_000

