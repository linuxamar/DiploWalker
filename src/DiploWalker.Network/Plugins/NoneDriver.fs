namespace DiploWalker.Network.Plugins

open System
open Serilog
open DiploWalker.Grpc.Network
open DiploWalker.Abstractions

type NoneDriver() =

    let networks =
        System.Collections.Concurrent.ConcurrentDictionary<string, NetworkDriverInfo>()

    interface INetworkDriver with
        member _.DriverType = NetworkDriver.``None``

        member _.Create(name, _subnet, _gateway, _ipRange, options, labels) =
            try
                SecurityValidation.validateName name "Le nom du rÃ©seau"

                for kvp in labels do
                    SecurityValidation.validateLabel kvp.Key kvp.Value

                let id = Guid.NewGuid().ToString("N")

                let info =
                    { Id = id
                      Name = name
                      Driver = NetworkDriver.``None``
                      Subnet = ""
                      Gateway = ""
                      Options = options
                      Labels = labels
                      CreatedAt = DateTime.UtcNow.ToString("o") }

                networks.TryAdd(id, info) |> ignore
                Ok info
            with ex ->
                Log.Error(ex, "Erreur lors de la crÃ©ation du rÃ©seau None {Name}", name)
                Error "Erreur lors de la crÃ©ation du rÃ©seau"

        member _.Remove(id, _force) =
            match networks.TryRemove(id) with
            | true, _ -> Ok()
            | false, _ -> Error(sprintf "RÃ©seau None '%s' introuvable" id)

        member _.Inspect(id) =
            match networks.TryGetValue(id) with
            | true, info -> Ok info
            | false, _ -> Error(sprintf "RÃ©seau None '%s' introuvable" id)

        member _.List() = networks.Values |> Seq.toList |> Ok

        member _.Connect(networkId, containerId, _endpointId, _ipv4Address, _options) =
            // CohÃ©rence de contrat : vÃ©rifier l'existence du rÃ©seau comme les
            // autres drivers (sinon un id bidon obtiendrait un endpoint).
            match networks.TryGetValue(networkId) with
            | false, _ -> Error(sprintf "RÃ©seau None '%s' introuvable" networkId)
            | true, info ->
                let epId = Guid.NewGuid().ToString("N")

                Ok
                    { EndpointId = epId
                      ContainerId = containerId
                      Ipv4Address = ""
                      MacAddress = ""
                      Message = sprintf "Container '%s' isolÃ© (rÃ©seau None '%s')" containerId info.Name }

        member _.Disconnect(_networkId, _containerId, _endpointId, _force) = Ok()

        /// Nettoyage rÃ©el : les rÃ©seaux None n'ont pas de ressource externe,
        /// la suppression du registre est l'intÃ©gralitÃ© de l'opÃ©ration.
        member _.Prune() =
            let removed = ResizeArray<string>()

            for kvp in networks do
                match networks.TryRemove(kvp.Key) with
                | true, _ -> removed.Add(kvp.Key)
                | false, _ -> ()

            Ok(removed |> Seq.toList)

