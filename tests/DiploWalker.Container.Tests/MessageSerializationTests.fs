namespace DiploWalker.Container.Tests

module MessageSerializationTests =

    open System.IO
    open Xunit
    open FsUnit.Xunit
    open ProtoBuf
    open DiploWalker.Grpc.Container

    let roundTrip (value: CreateContainerRequest) : CreateContainerRequest =
        use stream = new MemoryStream()
        Serializer.Serialize(stream, value)
        stream.Position <- 0L
        Serializer.Deserialize<CreateContainerRequest>(stream)

    let freshCreateContainerRequest () : CreateContainerRequest =
        { Name = ""
          Image = ""
          Env = System.Collections.Generic.Dictionary<string, string>()
          Command = System.Collections.Generic.List<string>()
          Args = System.Collections.Generic.List<string>()
          Labels = System.Collections.Generic.Dictionary<string, string>()
          PidLimit = 0
          MemoryLimit = 0L
          CpuShares = 0
          Mounts = System.Collections.Generic.List<ContainerMount>()
          RestartPolicy = ""
          RestartMaxCount = 0
          Ports = System.Collections.Generic.List<PortMapping>()
          HealthCheck = Unchecked.defaultof<HealthCheckConfig> }

    [<Fact>]
    let ``CreateContainerRequest round-trip préserve les collections renseignées`` () =
        let request = freshCreateContainerRequest ()
        request.Name <- "demo"
        request.Image <- "nginx:latest"
        request.Env.["KEY"] <- "value"
        request.Command.Add("echo")
        request.Labels.["env"] <- "prod"

        let restored = roundTrip request
        restored.Name |> should equal "demo"
        restored.Image |> should equal "nginx:latest"
        restored.Env.["KEY"] |> should equal "value"
        restored.Command.Contains("echo") |> should equal true
        restored.Labels.["env"] |> should equal "prod"

    [<Fact>]
    let ``CreateContainerRequest EnsureCollections initialise les collections nulles`` () =
        let request = freshCreateContainerRequest ()
        let restored = roundTrip request
        isNull restored.Env |> should equal false
        isNull restored.Command |> should equal false
        isNull restored.Args |> should equal false
        isNull restored.Labels |> should equal false
        isNull restored.Mounts |> should equal false
        isNull restored.Ports |> should equal false

    let roundTripRaw<'T> (value: 'T) : 'T =
        use stream = new MemoryStream()
        Serializer.Serialize(stream, value)
        stream.Position <- 0L
        Serializer.Deserialize<'T>(stream)

    [<Fact>]
    let ``ListContainersResponse EnsureCollections initialise la liste`` () =
        let response: ListContainersResponse =
            { Containers = System.Collections.Generic.List<ContainerInfo>() }
        let restored = roundTripRaw<ListContainersResponse> response
        isNull restored.Containers |> should equal false

    [<Fact>]
    let ``InspectContainerResponse EnsureCollections initialise toutes les collections`` () =
        let response: InspectContainerResponse =
            { Id = ""
              Name = ""
              Image = ""
              State = ContainerState.Unknown
              CreatedAt = ""
              StartedAt = ""
              FinishedAt = ""
              Labels = System.Collections.Generic.Dictionary<string, string>()
              Env = System.Collections.Generic.Dictionary<string, string>()
              Pid = 0
              ExitCode = 0
              RestartPolicy = ""
              Ports = System.Collections.Generic.List<PortMapping>()
              Health = ""
              Mounts = System.Collections.Generic.List<string>() }
        let restored = roundTripRaw<InspectContainerResponse> response
        isNull restored.Labels |> should equal false
        isNull restored.Env |> should equal false
        isNull restored.Ports |> should equal false
        isNull restored.Mounts |> should equal false

    [<Fact>]
    let ``ContainerInfo EnsureCollections initialise les labels`` () =
        let response: ContainerInfo =
            { Id = ""
              Name = ""
              Image = ""
              State = ContainerState.Unknown
              CreatedAt = ""
              Labels = System.Collections.Generic.Dictionary<string, string>() }
        let restored = roundTripRaw<ContainerInfo> response
        isNull restored.Labels |> should equal false

    [<Fact>]
    let ``PruneContainersResponse EnsureCollections initialise la liste`` () =
        let response: PruneContainersResponse =
            { Deleted = System.Collections.Generic.List<string>() }
        let restored = roundTripRaw<PruneContainersResponse> response
        isNull restored.Deleted |> should equal false

    [<Fact>]
    let ``TopContainerResponse EnsureCollections initialise la liste`` () =
        let response: TopContainerResponse =
            { Processes = System.Collections.Generic.List<ProcessInfo>() }
        let restored = roundTripRaw<TopContainerResponse> response
        isNull restored.Processes |> should equal false

    [<Fact>]
    let ``ListNamespacesResponse EnsureCollections initialise la liste`` () =
        let response: ListNamespacesResponse =
            { Namespaces = System.Collections.Generic.List<string>() }
        let restored = roundTripRaw<ListNamespacesResponse> response
        isNull restored.Namespaces |> should equal false

    [<Fact>]
    let ``ListImagesResponse EnsureCollections initialise la liste`` () =
        let response: ListImagesResponse =
            { Images = System.Collections.Generic.List<ImageInfo>() }
        let restored = roundTripRaw<ListImagesResponse> response
        isNull restored.Images |> should equal false

    [<Fact>]
    let ``ImportImageResponse EnsureCollections initialise la liste`` () =
        let response: ImportImageResponse =
            { ImageRefs = System.Collections.Generic.List<string>()
              Message = "" }
        let restored = roundTripRaw<ImportImageResponse> response
        isNull restored.ImageRefs |> should equal false

    [<Fact>]
    let ``ExecInContainerRequest EnsureCollections initialise la commande`` () =
        let request: ExecInContainerRequest =
            { Id = ""
              Command = System.Collections.Generic.List<string>()
              AttachStdin = false
              AttachStdout = false
              AttachStderr = false }
        let restored = roundTripRaw<ExecInContainerRequest> request
        isNull restored.Command |> should equal false

    [<Fact>]
    let ``ExecMessage EnsureCollections initialise la commande`` () =
        let message: ExecMessage =
            { Id = ""
              Command = System.Collections.Generic.List<string>()
              Data = [||]
              Eof = false }
        let restored = roundTripRaw<ExecMessage> message
        isNull restored.Command |> should equal false

