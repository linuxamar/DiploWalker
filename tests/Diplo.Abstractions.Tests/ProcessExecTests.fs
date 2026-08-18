namespace Diplo.Abstractions.Tests

module ProcessExecTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open Diplo.Abstractions

    let private tempDir () =
        let dir = Path.Combine(Path.GetTempPath(), "diplo-process-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir) |> ignore
        dir

    let private cmd (args: string list) =
        ProcessExec.runWithResult "cmd.exe" (["/c"] @ args) None None

    [<Fact>]
    let ``runWithResult capture la sortie standard`` () =
        let code, stdout, stderr = cmd [ "echo"; "hello" ]
        code |> should equal 0
        stdout.Contains("hello") |> should equal true
        stderr |> should equal ""

    [<Fact>]
    let ``runWithResult capture la sortie d'erreur sans bloquer`` () =
        let code, stdout, stderr = cmd [ "echo"; "err"; "1>&2" ]
        code |> should equal 0
        stderr.Contains("err") |> should equal true

    [<Fact>]
    let ``runWithResult retourne le code de sortie non nul`` () =
        let code, _, _ = cmd [ "exit"; "3" ]
        code |> should equal 3

    [<Fact>]
    let ``run retourne la sortie standard en cas de succes`` () =
        (ProcessExec.run "cmd.exe" [ "/c"; "echo"; "ok" ] None None).Contains("ok") |> should equal true

    [<Fact>]
    let ``run leve une exception si le processus echoue`` () =
        (fun () -> ProcessExec.run "cmd.exe" [ "/c"; "exit"; "3" ] None None |> ignore)
        |> should throw typeof<InvalidOperationException>

    [<Fact>]
    let ``runWithResult tue le processus en cas de depassement du delai`` () =
        let stopwatch = Diagnostics.Stopwatch.StartNew()
        (fun () ->
            ProcessExec.runWithResult "cmd.exe" [ "/c"; "ping"; "-n"; "5"; "127.0.0.1" ] (Some 500) None
            |> ignore)
        |> should throw typeof<TimeoutException>
        stopwatch.ElapsedMilliseconds |> should be (lessThan 10_000L)

    [<Fact>]
    let ``runWithResult transmet l'entree standard au processus`` () =
        let code, stdout, _ = ProcessExec.runWithResult "cmd.exe" [ "/c"; "more" ] None (Some "hello-stdin")
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
