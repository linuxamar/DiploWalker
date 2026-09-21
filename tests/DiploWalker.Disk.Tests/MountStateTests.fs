namespace DiploWalker.Disk.Tests

module MountStateTests =

    open System
    open System.IO
    open Xunit
    open FsUnit.Xunit
    open DiploWalker.Disk

    let private run (f: string -> unit) =
        let root = TestImage.createTempDir ()

        try
            f root
        finally
            TestImage.cleanupDir root

    let private sampleEntry source hostPath dest readOnly : MountState.MountEntry =
        { Source = source
          HostPath = hostPath
          Destination = dest
          ReadOnly = readOnly }

    // â”€â”€ load : cas limites â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``load retourne Map.empty si le fichier n'existe pas`` () =
        let path = Path.Combine(Path.GetTempPath(), "diplo-absent-" + Guid.NewGuid().ToString("N"))
        MountState.load path |> should equal Map.empty<string, MountState.MountEntry list>

    [<Fact>]
    let ``load retourne Map.empty si le fichier est vide`` () =
        run (fun root ->
            let path = Path.Combine(root, "empty.json")
            File.WriteAllText(path, "")
            MountState.load path |> should equal Map.empty<string, MountState.MountEntry list>)

    [<Fact>]
    let ``load retourne Map.empty si le fichier contient null`` () =
        run (fun root ->
            let path = Path.Combine(root, "null.json")
            File.WriteAllText(path, "null")
            MountState.load path |> should equal Map.empty<string, MountState.MountEntry list>)

    [<Fact>]
    let ``load retourne Map.empty si le JSON est corrompu`` () =
        run (fun root ->
            let path = Path.Combine(root, "corrupt.json")
            File.WriteAllText(path, "{ ce n'est pas du json }}")
            MountState.load path |> should equal Map.empty<string, MountState.MountEntry list>)

    [<Fact>]
    let ``load retourne Map.empty si le fichier contient uniquement des espaces`` () =
        run (fun root ->
            let path = Path.Combine(root, "spaces.json")
            File.WriteAllText(path, "   \n  \t  ")
            MountState.load path |> should equal Map.empty<string, MountState.MountEntry list>)

    // â”€â”€ load + save : aller-retour â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``save puis load conserve les entrees`` () =
        run (fun root ->
            let path = Path.Combine(root, "state.json")
            let entry = sampleEntry "C:\\img" "C:\\stg" "C:\\dst" false
            MountState.save path [ "c1", [ entry ] ]
            let loaded = MountState.load path
            loaded.Count |> should equal 1
            loaded.["c1"].Head.Source |> should equal "C:\\img")

    [<Fact>]
    let ``save ecrase l'ancien contenu`` () =
        run (fun root ->
            let path = Path.Combine(root, "state.json")
            MountState.save path [ "old", [ sampleEntry "a" "b" "c" false ] ]
            MountState.save path [ "new", [ sampleEntry "x" "y" "z" true ] ]
            let loaded = MountState.load path
            loaded.Count |> should equal 1
            loaded.ContainsKey("old") |> should equal false
            loaded.["new"].Head.ReadOnly |> should equal true)

    [<Fact>]
    let ``save avec plusieurs conteneurs`` () =
        run (fun root ->
            let path = Path.Combine(root, "state.json")
            let e1 = sampleEntry "s1" "h1" "d1" false
            let e2 = sampleEntry "s2" "h2" "d2" true
            let e3 = sampleEntry "s3" "h3" "d3" false
            MountState.save path [ "a", [ e1 ]; "b", [ e2; e3 ] ]
            let loaded = MountState.load path
            loaded.Count |> should equal 2
            loaded.["b"].Length |> should equal 2)

    [<Fact>]
    let ``save cree le repertoire parent si inexistant`` () =
        run (fun root ->
            let path = Path.Combine(root, "sub", "dir", "state.json")
            MountState.save path [ "c", [ sampleEntry "s" "h" "d" false ] ]
            File.Exists path |> should equal true)

    // â”€â”€ stateFile â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [<Fact>]
    let ``stateFile retourne un chemin dans ProgramData`` () =
        let path = MountState.stateFile ()
        path.Contains("Diplo") |> should equal true
        path.EndsWith("mounted-state.json") |> should equal true

