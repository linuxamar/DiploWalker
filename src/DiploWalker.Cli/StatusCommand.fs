namespace DiploWalker.Cli

open System
open System.Threading.Tasks
open Spectre.Console.Cli
open DiploWalker.Core.Clients
open DiploWalker.Core.Output

type StatusSettings() =
    inherit CommandSettings()

type StatusCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<StatusSettings>()
    new(output: IOutputPort) = StatusCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, _settings, _ct) =
        task {
            output.WriteLine("Vérification de l'état des services DiploWalker...")
            output.WriteLine("")

            use containerClient = clients.CreateContainerClient()
            use volumeClient = clients.CreateVolumeClient()
            use networkClient = clients.CreateNetworkClient()

            let! containerStatus =
                task {
                    try
                        let! v = containerClient.GetVersionAsync()

                        let info =
                            if String.IsNullOrEmpty v.Os then
                                v.Version
                            else
                                sprintf "%s (%s/%s)" v.Version v.Os v.Arch

                        return (true, info)
                    with ex ->
                        return (false, ex.Message)
                }

            let! volumeStatus =
                task {
                    try
                        let! v = volumeClient.ListAsync()
                        return (true, sprintf "%d volume(s)" v.Volumes.Count)
                    with ex ->
                        return (false, ex.Message)
                }

            let! networkStatus =
                task {
                    try
                        let! n = networkClient.ListAsync()
                        return (true, sprintf "%d réseau(x)" n.Networks.Count)
                    with ex ->
                        return (false, ex.Message)
                }

            let print name (ok, info) =
                if ok then
                    output.WriteSuccess(sprintf "  ✓ %s: %s" name info)
                else
                    output.WriteError(sprintf "  ✗ %s: %s" name info)

            print "Container" containerStatus
            print "Volume" volumeStatus
            print "Network" networkStatus

            output.WriteLine("")

            let allOk = [ containerStatus; volumeStatus; networkStatus ] |> List.forall fst

            if allOk then
                output.WriteSuccess("Tous les services sont opérationnels.")
                return 0
            else
                output.WriteWarning("Certains services ne sont pas disponibles.")
                // Un status check qui répond 0 avec des services down est
                // inutilisable pour la supervision scriptée.
                return 1
        }



