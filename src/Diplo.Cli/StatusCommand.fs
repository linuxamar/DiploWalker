namespace Diplo.Cli

open System.Threading.Tasks
open Spectre.Console.Cli
open Diplo.Core.Clients
open Diplo.Core.Output

type StatusCommand(output: IOutputPort) =
    inherit AsyncCommand<CommandSettings>()

    override _.ExecuteAsync(_ctx, _settings, _ct) =
        task {
            output.WriteLine("Vérification de l'état des services Diplo...")
            output.WriteLine("")

            use containerClient = new ContainerClient()
            use volumeClient = new VolumeClient()
            use networkClient = new NetworkClient()

            let! containerStatus =
                task {
                    try
                        let! v = containerClient.GetVersionAsync()
                        return (true, sprintf "v%s (%s/%s)" v.Version v.Os v.Arch)
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

            let allOk =
                [ containerStatus; volumeStatus; networkStatus ]
                |> List.forall fst

            if allOk then
                output.WriteSuccess("Tous les services sont opérationnels.")
            else
                output.WriteWarning("Certains services ne sont pas disponibles.")

            return 0
        }
