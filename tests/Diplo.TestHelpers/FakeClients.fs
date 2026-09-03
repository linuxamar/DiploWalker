namespace Diplo.TestHelpers

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Diplo.Core.Clients
open Diplo.Grpc.Container
open Diplo.Grpc.Network
open Diplo.Grpc.Volume

module internal FakeClientsInternals =

    /// Énumération asynchrone vide, utilisée pour les membres streaming des fakes
    /// non exercés par les scénarios testés.
    let emptyAsyncEnumerable<'T> : IAsyncEnumerable<'T> =
        { new IAsyncEnumerable<'T> with
            member _.GetAsyncEnumerator(_ct) =
                { new IAsyncEnumerator<'T> with
                    member _.Current = Unchecked.defaultof<'T>
                    member _.MoveNextAsync() = ValueTask<bool>(false)
                    member _.DisposeAsync() = ValueTask() } }

/// Fake IContainerClient : chaque membre retourne une réponse injectable via le
/// constructeur (paramètres nommés). Les membres non fournis utilisent une
/// réponse bénigne par défaut. Des compteurs d'appels permettent de vérifier
/// que la commande a contacté le client.
type FakeContainerClient
    (
        ?delete: DeleteContainerResponse,
        ?create: CreateContainerResponse,
        ?login: LoginRegistryResponse,
        ?logout: LogoutRegistryResponse,
        ?list: ListContainersResponse,
        ?inspect: InspectContainerResponse,
        ?start: StartContainerResponse,
        ?stop: StopContainerResponse,
        ?rename: RenameContainerResponse,
        ?pull: PullImageResponse,
        ?removeImage: RemoveImageResponse,
        ?listImages: ListImagesResponse,
        ?inspectImage: InspectImageResponse,
        ?tagImage: TagImageResponse,
        ?version: GetVersionResponse,
        ?namespaces: ListNamespacesResponse,
        ?top: TopContainerResponse,
        ?stats: GetContainerStatsResponse,
        ?execOutputs: seq<ExecOutput>,
        ?logEntries: seq<ContainerLogEntry>,
        ?versionError: exn
    ) =
    let deleteR = defaultArg delete { Success = true; Message = "" }
    let createR = defaultArg create { Id = ""; Name = ""; State = ContainerState.Unknown; CreatedAt = "" }
    let loginR = defaultArg login { Success = true; Message = "" }
    let logoutR = defaultArg logout { Success = true; Message = "" }
    let listR = defaultArg list { Containers = List<ContainerInfo>() }
    let inspectR =
        defaultArg
            inspect
            { Id = ""
              Name = ""
              Image = ""
              State = ContainerState.Unknown
              CreatedAt = ""
              StartedAt = ""
              FinishedAt = ""
              Labels = Dictionary<string, string>()
              Env = Dictionary<string, string>()
              Pid = 0
              ExitCode = 0
              RestartPolicy = ""
              Ports = List<PortMapping>()
              Health = ""
              Mounts = List<string>() }
    let startR = defaultArg start { State = ContainerState.Running; Message = "" }
    let stopR = defaultArg stop { State = ContainerState.Stopped; Message = "" }
    let renameR = defaultArg rename { Success = true; Message = "" }
    let pullR = defaultArg pull { Image = ""; Message = "" }
    let removeImageR = defaultArg removeImage { Success = true; Message = "" }
    let listImagesR = defaultArg listImages { Images = List<ImageInfo>() }
    let inspectImageR =
        defaultArg
            inspectImage
            { Ref = ""
              Id = ""
              Repository = ""
              Tag = ""
              Size = 0L
              CreatedAt = ""
              Labels = Dictionary<string, string>() }
    let tagImageR = defaultArg tagImage { Source = ""; Target = ""; Message = "" }
    let versionR = defaultArg version { Version = ""; Revision = ""; GoVersion = ""; Os = ""; Arch = "" }
    let namespacesR = defaultArg namespaces { Namespaces = List<string>() }
    let topR = defaultArg top { Processes = List<ProcessInfo>() }
    let statsR =
        defaultArg
            stats
            { CpuUsage = 0.0
              MemoryUsage = 0L
              MemoryLimit = 0L
              NetworkRx = 0L
              NetworkTx = 0L
              DiskRead = 0L
              DiskWrite = 0L
              Pids = 0 }
    let execR = defaultArg execOutputs Seq.empty
    let logsR = defaultArg logEntries Seq.empty
    let versionErr = versionError

    let mutable deleteCalls = 0
    let mutable createCalls = 0
    let mutable loginCalls = 0
    let mutable logoutCalls = 0
    let mutable listCalls = 0
    let mutable inspectCalls = 0
    let mutable startCalls = 0
    let mutable stopCalls = 0
    let mutable renameCalls = 0
    let mutable pullCalls = 0
    let mutable removeImageCalls = 0
    let mutable listImagesCalls = 0
    let mutable inspectImageCalls = 0
    let mutable tagImageCalls = 0
    let mutable versionCalls = 0
    let mutable namespacesCalls = 0
    let mutable topCalls = 0
    let mutable statsCalls = 0
    let mutable execCalls = 0
    let mutable logsCalls = 0

    member _.DeleteCalls = deleteCalls
    member _.CreateCalls = createCalls
    member _.LoginCalls = loginCalls
    member _.LogoutCalls = logoutCalls
    member _.ListCalls = listCalls
    member _.InspectCalls = inspectCalls
    member _.StartCalls = startCalls
    member _.StopCalls = stopCalls
    member _.RenameCalls = renameCalls
    member _.PullCalls = pullCalls
    member _.RemoveImageCalls = removeImageCalls
    member _.ListImagesCalls = listImagesCalls
    member _.InspectImageCalls = inspectImageCalls
    member _.TagImageCalls = tagImageCalls
    member _.VersionCalls = versionCalls
    member _.NamespacesCalls = namespacesCalls
    member _.TopCalls = topCalls
    member _.StatsCalls = statsCalls
    member _.ExecCalls = execCalls
    member _.LogsCalls = logsCalls

    interface IDisposable with
        member _.Dispose() = ()

    interface IContainerClient with
        member _.CreateAsync
            (_name, _image, ?_env, ?_command, ?_args, ?_labels, ?_pidLimit, ?_memoryLimit, ?_cpuShares, ?_mounts, ?_ports, ?_ct)
            =
            createCalls <- createCalls + 1
            Task.FromResult(createR)

        member _.StartAsync(_id, ?_attach, ?_ct) =
            startCalls <- startCalls + 1
            Task.FromResult(startR)

        member _.StopAsync(_id, ?_timeoutSeconds, ?_ct) =
            stopCalls <- stopCalls + 1
            Task.FromResult(stopR)

        member _.DeleteAsync(_id, ?_force, ?_ct) =
            deleteCalls <- deleteCalls + 1
            Task.FromResult(deleteR)

        member _.InspectAsync(_id, ?_ct) =
            inspectCalls <- inspectCalls + 1
            Task.FromResult(inspectR)

        member _.ListAsync(?_all, ?_filters, ?_ct) =
            listCalls <- listCalls + 1
            Task.FromResult(listR)

        member _.GetLogsStream(_id, ?_follow, ?_tail, ?_since, ?_ct) =
            logsCalls <- logsCalls + 1
            FakeClientsInternals.emptyAsyncEnumerable

        member _.GetLogs(_id, ?_follow, ?_tail, ?_since, ?_ct) =
            logsCalls <- logsCalls + 1
            Task.FromResult(logsR)

        member _.Exec(_id, _command, ?_attachStdout, ?_attachStderr, ?_ct) =
            execCalls <- execCalls + 1
            Task.FromResult(execR)

        member _.PullImageAsync(_image, ?_user, ?_ct) =
            pullCalls <- pullCalls + 1
            Task.FromResult(pullR)

        member _.LoginRegistryAsync(_registry, _username, _password, ?_ct) =
            loginCalls <- loginCalls + 1
            Task.FromResult(loginR)

        member _.LogoutRegistryAsync(_registry, ?_ct) =
            logoutCalls <- logoutCalls + 1
            Task.FromResult(logoutR)

        member _.GetVersionAsync(?_ct) =
            versionCalls <- versionCalls + 1

            match versionErr with
            | Some ex -> Task.FromException<GetVersionResponse>(ex)
            | None -> Task.FromResult(versionR)

        member _.ListNamespacesAsync(?_ct) =
            namespacesCalls <- namespacesCalls + 1
            Task.FromResult(namespacesR)

        member _.RenameContainerAsync(_id, _newName, ?_ct) =
            renameCalls <- renameCalls + 1
            Task.FromResult(renameR)

        member _.TopContainerAsync(_id, ?_ct) =
            topCalls <- topCalls + 1
            Task.FromResult(topR)

        member _.GetContainerStatsAsync(_id, ?_ct) =
            statsCalls <- statsCalls + 1
            Task.FromResult(statsR)

        member _.ListImagesAsync(?_namespaceName, ?_ct) =
            listImagesCalls <- listImagesCalls + 1
            Task.FromResult(listImagesR)

        member _.InspectImageAsync(_ref, ?_namespaceName, ?_ct) =
            inspectImageCalls <- inspectImageCalls + 1
            Task.FromResult(inspectImageR)

        member _.RemoveImageAsync(_ref, ?_namespaceName, ?_ct) =
            removeImageCalls <- removeImageCalls + 1
            Task.FromResult(removeImageR)

        member _.TagImageAsync(_source, _target, ?_namespaceName, ?_ct) =
            tagImageCalls <- tagImageCalls + 1
            Task.FromResult(tagImageR)

/// Fake INetworkClient : mêmes conventions que FakeContainerClient.
type FakeNetworkClient
    (
        ?create: CreateNetworkResponse,
        ?runCni: RunCniPluginResponse,
        ?remove: RemoveNetworkResponse,
        ?inspect: InspectNetworkResponse,
        ?list: ListNetworksResponse,
        ?connect: ConnectContainerResponse,
        ?disconnect: DisconnectContainerResponse,
        ?prune: PruneNetworksResponse,
        ?listError: exn
    ) =
    let createR =
        defaultArg create { Id = ""; Name = ""; Driver = NetworkDriver.Bridge; Subnet = ""; Gateway = ""; CreatedAt = "" }
    let runCniR = defaultArg runCni { Success = true; Ifname = ""; Ipv4Address = ""; Gateway = ""; Message = "" }
    let removeR = defaultArg remove { Success = true; Message = "" }
    let inspectR =
        defaultArg
            inspect
            { Id = ""
              Name = ""
              Driver = NetworkDriver.Bridge
              Subnet = ""
              Gateway = ""
              IpRange = ""
              Options = Dictionary<string, string>()
              Labels = Dictionary<string, string>()
              Endpoints = List<EndpointInfo>()
              CreatedAt = "" }
    let listR = defaultArg list { Networks = List<NetworkInfo>() }
    let connectR = defaultArg connect { EndpointId = ""; Ipv4Address = ""; MacAddress = ""; Message = "" }
    let disconnectR = defaultArg disconnect { Success = true; Message = "" }
    let pruneR =
        defaultArg prune { NetworksDeleted = List<string>(); Count = 0; Message = "" }
    let listErr = listError

    let mutable createCalls = 0
    let mutable runCniCalls = 0
    let mutable removeCalls = 0
    let mutable inspectCalls = 0
    let mutable listCalls = 0
    let mutable connectCalls = 0
    let mutable disconnectCalls = 0
    let mutable pruneCalls = 0

    member _.CreateCalls = createCalls
    member _.RunCniCalls = runCniCalls
    member _.RemoveCalls = removeCalls
    member _.InspectCalls = inspectCalls
    member _.ListCalls = listCalls
    member _.ConnectCalls = connectCalls
    member _.DisconnectCalls = disconnectCalls
    member _.PruneCalls = pruneCalls

    interface IDisposable with
        member _.Dispose() = ()

    interface INetworkClient with
        member _.CreateAsync
            (_name, ?_driver, ?_subnet, ?_gateway, ?_ipRange, ?_options, ?_labels, ?_cniPluginPath, ?_ct)
            =
            createCalls <- createCalls + 1
            Task.FromResult(createR)

        member _.RemoveAsync(_id, ?_force, ?_ct) =
            removeCalls <- removeCalls + 1
            Task.FromResult(removeR)

        member _.InspectAsync(_id, ?_ct) =
            inspectCalls <- inspectCalls + 1
            Task.FromResult(inspectR)

        member _.ListAsync(?_filters, ?_ct) =
            listCalls <- listCalls + 1

            match listErr with
            | Some ex -> Task.FromException<ListNetworksResponse>(ex)
            | None -> Task.FromResult(listR)

        member _.ConnectAsync(_networkId, _containerId, ?_endpointId, ?_ipv4Address, ?_options, ?_ct) =
            connectCalls <- connectCalls + 1
            Task.FromResult(connectR)

        member _.DisconnectAsync(_networkId, _containerId, ?_endpointId, ?_force, ?_ct) =
            disconnectCalls <- disconnectCalls + 1
            Task.FromResult(disconnectR)

        member _.RunCniPluginAsync(_pluginPath, _command, _containerId, _netnsPath, ?_config, ?_ct) =
            runCniCalls <- runCniCalls + 1
            Task.FromResult(runCniR)

        member _.PruneNetworksAsync(?_ct) =
            pruneCalls <- pruneCalls + 1
            Task.FromResult(pruneR)

/// Fake IVolumeClient : mêmes conventions que FakeContainerClient.
type FakeVolumeClient
    (
        ?create: CreateVolumeResponse,
        ?remove: RemoveVolumeResponse,
        ?inspect: InspectVolumeResponse,
        ?list: ListVolumesResponse,
        ?mount: MountVolumeResponse,
        ?unmount: UnmountVolumeResponse,
        ?prune: PruneVolumesResponse,
        ?listError: exn
    ) =
    let createR =
        defaultArg create { Id = ""; Name = ""; Driver = StorageDriverType.Local; Mountpoint = ""; CreatedAt = "" }
    let removeR = defaultArg remove { Success = true; Message = "" }
    let inspectR =
        defaultArg
            inspect
            { Id = ""
              Name = ""
              Driver = StorageDriverType.Local
              Mountpoint = ""
              State = MountState.Unmounted
              Labels = Dictionary<string, string>()
              DriverOpts = Dictionary<string, string>()
              SizeBytes = 0L
              CreatedAt = "" }
    let listR = defaultArg list { Volumes = List<VolumeInfo>() }
    let mountR = defaultArg mount { State = MountState.Mounted; Mountpoint = ""; Message = "" }
    let unmountR = defaultArg unmount { State = MountState.Unmounted; Message = "" }
    let pruneR = defaultArg prune { VolumesDeleted = List<string>(); Count = 0; Message = "" }
    let listErr = listError

    let mutable createCalls = 0
    let mutable removeCalls = 0
    let mutable inspectCalls = 0
    let mutable listCalls = 0
    let mutable mountCalls = 0
    let mutable unmountCalls = 0
    let mutable pruneCalls = 0

    member _.CreateCalls = createCalls
    member _.RemoveCalls = removeCalls
    member _.InspectCalls = inspectCalls
    member _.ListCalls = listCalls
    member _.MountCalls = mountCalls
    member _.UnmountCalls = unmountCalls
    member _.PruneCalls = pruneCalls

    interface IDisposable with
        member _.Dispose() = ()

    interface IVolumeClient with
        member _.CreateAsync(_name, ?_driver, ?_driverOpts, ?_labels, ?_ct) =
            createCalls <- createCalls + 1
            Task.FromResult(createR)

        member _.RemoveAsync(_id, ?_force, ?_ct) =
            removeCalls <- removeCalls + 1
            Task.FromResult(removeR)

        member _.InspectAsync(_id, ?_ct) =
            inspectCalls <- inspectCalls + 1
            Task.FromResult(inspectR)

        member _.ListAsync(?_filters, ?_ct) =
            listCalls <- listCalls + 1

            match listErr with
            | Some ex -> Task.FromException<ListVolumesResponse>(ex)
            | None -> Task.FromResult(listR)

        member _.MountAsync(_id, _targetPath, ?_options, ?_ct) =
            mountCalls <- mountCalls + 1
            Task.FromResult(mountR)

        member _.UnmountAsync(_id, _targetPath, ?_ct) =
            unmountCalls <- unmountCalls + 1
            Task.FromResult(unmountR)

        member _.PruneVolumesAsync(?_ct) =
            pruneCalls <- pruneCalls + 1
            Task.FromResult(pruneR)

/// Fabrique de clients injectable retournant les fakes.
type FakeDiploClients(container: IContainerClient, network: INetworkClient, volume: IVolumeClient) =

    interface IDiploClients with
        member _.CreateContainerClient() = container
        member _.CreateNetworkClient() = network
        member _.CreateVolumeClient() = volume
