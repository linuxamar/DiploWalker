namespace Diplo.Core.Tests

module VolumeClientTests =

    open System
    open System.Collections.Generic
    open System.Threading.Tasks
    open Xunit
    open FsUnit.Xunit
    open Grpc.Net.Client
    open Diplo.Core.Clients
    open Diplo.Core.Connection
    open Diplo.Grpc
    open Diplo.Grpc.Volume

    let private run (t: Task<'T>) = t.GetAwaiter().GetResult()

    let private assertArgumentError (f: unit -> unit) =
        (fun () -> f ()) |> should throw typeof<ArgumentException>

    let private newClient (address: string) =
        let channel = DiploChannel.forAddress address
        new VolumeClient(channel, true)

    let private offlineClient () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5002", options)
        new VolumeClient(channel, true)

    [<Fact>]
    let ``VolumeClient avec canal cree un client`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5002", options)
        use client = new VolumeClient(channel, false)
        client |> should not' (be Null)

    [<Fact>]
    let ``VolumeClient implemente IDisposable`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5002", options)
        let client = new VolumeClient(channel, false)
        (client :> IDisposable) |> should not' (be Null)
        (client :> IDisposable).Dispose()
        channel.Dispose()

    [<Fact>]
    let ``VolumeClient avec canal partage ne dispose pas le canal`` () =
        let options = GrpcChannelOptions()
        let channel = GrpcChannel.ForAddress("http://localhost:5002", options)
        let client = new VolumeClient(channel, false)
        (client :> IDisposable).Dispose()
        channel.Target |> should not' (be Null)
        channel.Dispose()

    [<Fact>]
    let ``CreateAsync cree un volume et retourne son ID`` () =
        let stub = GrpcTestHost.VolumeServiceStub()
        GrpcTestHost.withVolumeApp stub (fun address ->
            use c = newClient address
            let response = c.CreateAsync("donnees", driver = StorageDriverType.Nfs) |> run
            response.Id |> should equal "vol-donnees"
            response.Name |> should equal "donnees"
            response.Driver |> should equal StorageDriverType.Nfs
            let req = stub.LastCreate.Value
            req.Name |> should equal "donnees"
            req.Driver |> should equal StorageDriverType.Nfs)

    [<Fact>]
    let ``CreateAsync transmet les options et etiquettes du driver`` () =
        let stub = GrpcTestHost.VolumeServiceStub()
        GrpcTestHost.withVolumeApp stub (fun address ->
            use c = newClient address

            c.CreateAsync(
                "donnees",
                driver = StorageDriverType.Nfs,
                driverOpts = (dict [ "server", "10.0.0.5" ]),
                labels = (dict [ "app", "demo" ])
            )
            |> run
            |> ignore

            let req = stub.LastCreate.Value
            req.DriverOpts.["server"] |> should equal "10.0.0.5"
            req.Labels.["app"] |> should equal "demo")

    [<Fact>]
    let ``InspectAsync retourne les infos du volume`` () =
        let stub = GrpcTestHost.VolumeServiceStub()
        GrpcTestHost.withVolumeApp stub (fun address ->
            use c = newClient address
            let response = c.InspectAsync("vol-donnees") |> run
            response.Id |> should equal "vol-donnees"
            response.State |> should equal MountState.Unmounted
            stub.LastInspect.Value.Id |> should equal "vol-donnees")

    [<Fact>]
    let ``ListAsync retourne les volumes`` () =
        let stub = GrpcTestHost.VolumeServiceStub()
        GrpcTestHost.withVolumeApp stub (fun address ->
            use c = newClient address
            let response = c.ListAsync(filters = (dict [ "label", "x" ])) |> run
            response.Volumes |> should be Empty
            stub.LastList.Value.Filters.["label"] |> should equal "x")

    [<Fact>]
    let ``RemoveAsync supprime un volume`` () =
        let stub = GrpcTestHost.VolumeServiceStub()
        GrpcTestHost.withVolumeApp stub (fun address ->
            use c = newClient address
            let response = c.RemoveAsync("vol-donnees", force = true) |> run
            response.Success |> should equal true
            let req = stub.LastRemove.Value
            req.Id |> should equal "vol-donnees"
            req.Force |> should equal true)

    [<Fact>]
    let ``MountAsync monte le volume sur le chemin cible`` () =
        let stub = GrpcTestHost.VolumeServiceStub()
        GrpcTestHost.withVolumeApp stub (fun address ->
            use c = newClient address
            let response = c.MountAsync("vol-donnees", "C:\\conf") |> run
            response.State |> should equal MountState.Mounted
            let req = stub.LastMount.Value
            req.Id |> should equal "vol-donnees"
            req.TargetPath |> should equal "C:\\conf")

    [<Fact>]
    let ``UnmountAsync demonte le volume`` () =
        let stub = GrpcTestHost.VolumeServiceStub()
        GrpcTestHost.withVolumeApp stub (fun address ->
            use c = newClient address
            let response = c.UnmountAsync("vol-donnees", "C:\\conf") |> run
            response.State |> should equal MountState.Unmounted
            let req = stub.LastUnmount.Value
            req.Id |> should equal "vol-donnees"
            req.TargetPath |> should equal "C:\\conf")

    [<Fact>]
    let ``PruneVolumesAsync supprime les volumes non utilises`` () =
        let stub = GrpcTestHost.VolumeServiceStub()
        GrpcTestHost.withVolumeApp stub (fun address ->
            use c = newClient address
            let response = c.PruneVolumesAsync() |> run
            response.Count |> should equal 0
            response.Message |> should equal "0 volume(s) supprimé(s)")

    [<Fact>]
    let ``Les gardes rejettent nome et identifiants vides`` () =
        use c = offlineClient ()
        assertArgumentError (fun () -> c.CreateAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.InspectAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.RemoveAsync("") |> run |> ignore)
        assertArgumentError (fun () -> c.MountAsync("", "C:\\cible") |> run |> ignore)
        assertArgumentError (fun () -> c.MountAsync("vol", "") |> run |> ignore)
        assertArgumentError (fun () -> c.UnmountAsync("", "C:\\cible") |> run |> ignore)
        assertArgumentError (fun () -> c.UnmountAsync("vol", "") |> run |> ignore)