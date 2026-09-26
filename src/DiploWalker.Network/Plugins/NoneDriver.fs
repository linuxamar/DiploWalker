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
                SecurityValidation.validateName name "Le nom du réseau"

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
                Log.Error(ex, "Erreur lors de la création du réseau None {Name}", name)
                Error "Erreur lors de la création du réseau"

        member _.Remove(id, _force) =
            match networks.TryRemove(id) with
            | true, _ -> Ok()
            | false, _ -> Error(sprintf "Réseau None '%s' introuvable" id)

        member _.Inspect(id) =
            match networks.TryGetValue(id) with
            | true, info -> Ok info
            | false, _ -> Error(sprintf "Réseau None '%s' introuvable" id)

        member _.List() = networks.Values |> Seq.toList |> Ok

        member _.Connect(networkId, containerId, _endpointId, _ipv4Address, _options) =
            // Cohérence de contrat : vérifier l'existence du réseau comme les
            // autres drivers (sinon un id bidon obtiendrait un endpoint).
            match networks.TryGetValue(networkId) with
            | false, _ -> Error(sprintf "Réseau None '%s' introuvable" networkId)
            | true, info ->
                let epId = Guid.NewGuid().ToString("N")

                Ok
                    { EndpointId = epId
                      ContainerId = containerId
                      Ipv4Address = ""
                      MacAddress = ""
                      Message = sprintf "Container '%s' isolé (réseau None '%s')" containerId info.Name }

        member _.Disconnect(_networkId, _containerId, _endpointId, _force) = Ok()

        /// Nettoyage réel : les réseaux None n'ont pas de ressource externe,
        /// la suppression du registre est l'intégralité de l'opération.
        member _.Prune() =
            let removed = ResizeArray<string>()

            for kvp in networks do
                match networks.TryRemove(kvp.Key) with
                | true, _ -> removed.Add(kvp.Key)
                | false, _ -> ()

            Ok(removed |> Seq.toList)

