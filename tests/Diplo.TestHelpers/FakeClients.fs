namespace Diplo.TestHelpers

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Diplo.Core.Clients
open Diplo.Grpc.Container
open Diplo.Grpc.Network
open Diplo.Grpc.Volume

/// Fake IContainerClient : seuls les membres utilisés par les tests sont
/// implémentés ; le reste lève NotImplementedException.
type FakeContainerClient(delete: DeleteContainerResponse, create: CreateContainerResponse, login: LoginRegistryResponse)
    =

    let mutable deleteCalls = 0
    let mutable createCalls = 0
    let mutable loginCalls = 0

    member _.DeleteCalls = deleteCalls
    member _.CreateCalls = createCalls
    member _.LoginCalls = loginCalls

    interface IDisposable with
        member _.Dispose() = ()

    interface IContainerClient with
        member _.DeleteAsync(_id, ?_force, ?_ct) =
            deleteCalls <- deleteCalls + 1
            Task.FromResult(delete)

        member _.CreateAsync
            (_name, _image, ?_env, ?_command, ?_args, ?_labels, ?_pidLimit, ?_memoryLimit, ?_cpuShares, ?_mounts, ?_ct)
            =
            createCalls <- createCalls + 1
            Task.FromResult(create)

        member _.LoginRegistryAsync(_registry, _username, _password, ?_ct) =
            loginCalls <- loginCalls + 1
            Task.FromResult(login)

        member _.StartAsync(_id, ?_attach, ?_ct) = raise (NotImplementedException())
        member _.StopAsync(_id, ?_timeoutSeconds, ?_ct) = raise (NotImplementedException())
        member _.InspectAsync(_id, ?_ct) = raise (NotImplementedException())
        member _.ListAsync(?_all, ?_filters, ?_ct) = raise (NotImplementedException())
        member _.GetLogsStream(_id, ?_follow, ?_tail, ?_since, ?_ct) = raise (NotImplementedException())
        member _.GetLogs(_id, ?_follow, ?_tail, ?_since, ?_ct) = raise (NotImplementedException())
        member _.Exec(_id, _command, ?_attachStdout, ?_attachStderr, ?_ct) = raise (NotImplementedException())
        member _.PullImageAsync(_image, ?_user, ?_ct) = raise (NotImplementedException())
        member _.LogoutRegistryAsync(_registry, ?_ct) = raise (NotImplementedException())
        member _.GetVersionAsync(?_ct) = raise (NotImplementedException())
        member _.ListNamespacesAsync(?_ct) = raise (NotImplementedException())
        member _.RenameContainerAsync(_id, _newName, ?_ct) = raise (NotImplementedException())
        member _.TopContainerAsync(_id, ?_ct) = raise (NotImplementedException())
        member _.GetContainerStatsAsync(_id, ?_ct) = raise (NotImplementedException())
        member _.ListImagesAsync(?_namespaceName, ?_ct) = raise (NotImplementedException())
        member _.InspectImageAsync(_ref, ?_namespaceName, ?_ct) = raise (NotImplementedException())
        member _.RemoveImageAsync(_ref, ?_namespaceName, ?_ct) = raise (NotImplementedException())
        member _.TagImageAsync(_source, _target, ?_namespaceName, ?_ct) = raise (NotImplementedException())

/// Fake INetworkClient pour les tests.
type FakeNetworkClient(create: CreateNetworkResponse, runCni: RunCniPluginResponse) =

    let mutable createCalls = 0
    let mutable runCniCalls = 0

    member _.CreateCalls = createCalls
    member _.RunCniCalls = runCniCalls

    interface IDisposable with
        member _.Dispose() = ()

    interface INetworkClient with
        member _.CreateAsync
            (_name, ?_driver, ?_subnet, ?_gateway, ?_ipRange, ?_options, ?_labels, ?_cniPluginPath, ?_ct)
            =
            createCalls <- createCalls + 1
            Task.FromResult(create)

        member _.RunCniPluginAsync(_pluginPath, _command, _containerId, _netnsPath, ?_config, ?_ct) =
            runCniCalls <- runCniCalls + 1
            Task.FromResult(runCni)

        member _.RemoveAsync(_id, ?_force, ?_ct) = raise (NotImplementedException())
        member _.InspectAsync(_id, ?_ct) = raise (NotImplementedException())
        member _.ListAsync(?_filters, ?_ct) = raise (NotImplementedException())

        member _.ConnectAsync(_networkId, _containerId, ?_endpointId, ?_ipv4Address, ?_options, ?_ct) =
            raise (NotImplementedException())

        member _.DisconnectAsync(_networkId, _containerId, ?_endpointId, ?_force, ?_ct) =
            raise (NotImplementedException())

        member _.PruneNetworksAsync(?_ct) = raise (NotImplementedException())

/// Fake IVolumeClient pour les tests.
type FakeVolumeClient(create: CreateVolumeResponse, remove: RemoveVolumeResponse) =

    let mutable createCalls = 0
    let mutable removeCalls = 0

    member _.CreateCalls = createCalls
    member _.RemoveCalls = removeCalls

    interface IDisposable with
        member _.Dispose() = ()

    interface IVolumeClient with
        member _.CreateAsync(_name, ?_driver, ?_driverOpts, ?_labels, ?_ct) =
            createCalls <- createCalls + 1
            Task.FromResult(create)

        member _.RemoveAsync(_id, ?_force, ?_ct) =
            removeCalls <- removeCalls + 1
            Task.FromResult(remove)

        member _.InspectAsync(_id, ?_ct) = raise (NotImplementedException())
        member _.ListAsync(?_filters, ?_ct) = raise (NotImplementedException())
        member _.MountAsync(_id, _targetPath, ?_options, ?_ct) = raise (NotImplementedException())
        member _.UnmountAsync(_id, _targetPath, ?_ct) = raise (NotImplementedException())
        member _.PruneVolumesAsync(?_ct) = raise (NotImplementedException())

/// Fabrique de clients injectable retournant les fakes.
type FakeDiploClients(container: IContainerClient, network: INetworkClient, volume: IVolumeClient) =

    interface IDiploClients with
        member _.CreateContainerClient() = container
        member _.CreateNetworkClient() = network
        member _.CreateVolumeClient() = volume
