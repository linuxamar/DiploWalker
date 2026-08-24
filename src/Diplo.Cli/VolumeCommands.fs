namespace Diplo.Cli.Volume

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Diplo.Core.Clients
open Diplo.Grpc
open Diplo.Core.Output
open Diplo.Grpc.Volume
open Spectre.Console.Cli

// ── list ──────────────────────────────────────────────────────────
type ListVolumesSettings() =
    inherit CommandSettings()

type ListVolumesCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<ListVolumesSettings>()
    new(output: IOutputPort) = ListVolumesCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, _settings, _ct) : Task<int> =
        task {
            use client = clients.CreateVolumeClient()
            let! response = client.ListAsync(ct = CancellationToken.None)

            if response.Volumes.Count = 0 then
                output.WriteWarning("Aucun volume trouvé.")
            else
                output.WriteTable(
                    response.Volumes,
                    [| "ID"; "Nom"; "Driver"; "Point de montage"; "État" |],
                    fun v ->
                        [| v.Id
                           v.Name
                           v.Driver.ToString()
                           v.Mountpoint
                           v.State.ToString() |])
            return 0
        }

// ── inspect ───────────────────────────────────────────────────────
type InspectVolumeSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set

type InspectVolumeCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<InspectVolumeSettings>()
    new(output: IOutputPort) = InspectVolumeCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError("L'identifiant du volume est requis")
                return 1
            else
                use client = clients.CreateVolumeClient()
                let! response = client.InspectAsync(settings.Id)

                output.WriteSuccess(sprintf "Volume %s" response.Name)
                output.WriteLine(sprintf "  ID        : %s" response.Id)
                output.WriteLine(sprintf "  Driver    : %s" (response.Driver.ToString()))
                output.WriteLine(sprintf "  Montage   : %s" response.Mountpoint)
                output.WriteLine(sprintf "  État      : %s" (response.State.ToString()))
                output.WriteLine(sprintf "  Taille    : %d octets" response.SizeBytes)
                output.WriteLine(sprintf "  Créé      : %s" response.CreatedAt)
                return 0
        }

// ── create ────────────────────────────────────────────────────────
type CreateVolumeSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<NAME>")>] member val Name: string = null with get, set
    [<CommandOption("--driver")>] member val Driver: string = "local" with get, set
    [<CommandOption("--path")>] member val Path: string = null with get, set
    [<CommandOption("--server")>] member val Server: string = null with get, set
    [<CommandOption("--share")>] member val Share: string = null with get, set
    [<CommandOption("--username")>] member val Username: string = null with get, set
    [<CommandOption("--password")>] member val Password: string = null with get, set
    [<CommandOption("--export")>] member val Export: string = null with get, set
    [<CommandOption("--opt")>] member val Opts: string[] = [||] with get, set

type CreateVolumeCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<CreateVolumeSettings>()
    new(output: IOutputPort) = CreateVolumeCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Name) then
                output.WriteError("Le nom du volume est requis")
                return 1
            elif not (DriverMappings.isValidVolumeDriver settings.Driver) then
                output.WriteError(sprintf "Driver inconnu: %s" settings.Driver)
                return 1
            elif settings.Driver.ToLowerInvariant() = "smb" && (String.IsNullOrWhiteSpace settings.Server || String.IsNullOrWhiteSpace settings.Share) then
                output.WriteError("Le driver SMB requiert les options --server et --share")
                return 1
            elif settings.Driver.ToLowerInvariant() = "nfs" && (String.IsNullOrWhiteSpace settings.Server || String.IsNullOrWhiteSpace settings.Export) then
                output.WriteError("Le driver NFS requiert les options --server et --export")
                return 1
            else
                let driverType =
                    DriverMappings.parseVolumeDriver settings.Driver

                let driverOpts = System.Collections.Generic.Dictionary<string, string>()
                let addOpt (key: string) (value: string) =
                    if not (String.IsNullOrWhiteSpace value) then
                        driverOpts.[key] <- value
                addOpt "path" settings.Path
                addOpt "server" settings.Server
                addOpt "share" settings.Share
                addOpt "username" settings.Username
                addOpt "password" settings.Password
                addOpt "export" settings.Export
                for pair in settings.Opts do
                    match pair.Split('=', 2) with
                    | [| k; v |] when not (String.IsNullOrWhiteSpace k) -> driverOpts.[k] <- v
                    | _ -> ()

                use client = clients.CreateVolumeClient()
                let! response =
                    client.CreateAsync(
                        name = settings.Name,
                        driver = driverType,
                        ?driverOpts = (if driverOpts.Count > 0 then Some(driverOpts :> IDictionary<string, string>) else None))
                output.WriteSuccess(sprintf "Volume %s créé (ID: %s)" response.Name response.Id)
                return 0
        }

// ── remove ────────────────────────────────────────────────────────
type RemoveVolumeSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandOption("-f|--force")>] member val Force = false with get, set

type RemoveVolumeCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<RemoveVolumeSettings>()
    new(output: IOutputPort) = RemoveVolumeCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError("L'identifiant du volume est requis")
                return 1
            else
                use client = clients.CreateVolumeClient()
                let! response = client.RemoveAsync(settings.Id, settings.Force)
                if response.Success then
                    output.WriteSuccess(response.Message)
                    return 0
                else
                    output.WriteError(response.Message)
                    return 1
        }

// ── mount ─────────────────────────────────────────────────────────
type MountSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandArgument(1, "<TARGET>")>] member val Target: string = null with get, set

type MountVolumeCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<MountSettings>()
    new(output: IOutputPort) = MountVolumeCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError("L'identifiant du volume est requis")
                return 1
            else
                use client = clients.CreateVolumeClient()
                let! response = client.MountAsync(settings.Id, settings.Target)
                output.WriteSuccess(sprintf "Volume %s monté sur %s (%s)" settings.Id settings.Target response.Mountpoint)
                return 0
        }

// ── unmount ───────────────────────────────────────────────────────
type UnmountSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandArgument(1, "<TARGET>")>] member val Target: string = null with get, set

type UnmountVolumeCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<UnmountSettings>()
    new(output: IOutputPort) = UnmountVolumeCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace(settings.Id) then
                output.WriteError("L'identifiant du volume est requis")
                return 1
            else
                use client = clients.CreateVolumeClient()
                let! response = client.UnmountAsync(settings.Id, settings.Target)
                output.WriteSuccess(sprintf "Volume %s démonté de %s" settings.Id settings.Target)
                return 0
        }

// ── prune ─────────────────────────────────────────────────────────
type PruneVolumesSettings() =
    inherit CommandSettings()

type PruneVolumesCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<PruneVolumesSettings>()
    new(output: IOutputPort) = PruneVolumesCommand(output, DiploClients())

    override _.ExecuteAsync(_ctx, _settings, _ct) : Task<int> =
        task {
            use client = clients.CreateVolumeClient()
            let! response = client.PruneVolumesAsync()
            if response.Count > 0 then
                output.WriteSuccess(response.Message)
                for id in response.VolumesDeleted do
                    output.WriteLine(sprintf "  - %s" id)
            else
                output.WriteWarning("Aucun volume à supprimer.")
            return 0
        }
