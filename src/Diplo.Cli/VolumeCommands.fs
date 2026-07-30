namespace Diplo.Cli.Volume

open System
open System.Threading
open System.Threading.Tasks
open Diplo.Core.Clients
open Diplo.Core.Output
open Diplo.Grpc.Volume
open Spectre.Console.Cli

// ── list ──────────────────────────────────────────────────────────
type ListVolumesSettings() =
    inherit CommandSettings()

type ListVolumesCommand(output: IOutputPort) =
    inherit AsyncCommand<ListVolumesSettings>()

    override _.ExecuteAsync(_ctx, _settings, _ct) : Task<int> =
        task {
            use client = new VolumeClient()
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

type InspectVolumeCommand(output: IOutputPort) =
    inherit AsyncCommand<InspectVolumeSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du volume est requis")
                return 1
            else
                use client = new VolumeClient()
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

type CreateVolumeCommand(output: IOutputPort) =
    inherit AsyncCommand<CreateVolumeSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Name) then
                output.WriteError("Le nom du volume est requis")
                return 1
            elif not (List.contains (settings.Driver.ToLowerInvariant()) ["local"; "nfs"; "smb"; "azure"; "aws"; "gcp"]) then
                output.WriteError(sprintf "Driver inconnu: %s" settings.Driver)
                return 1
            else
                let driverType =
                    match settings.Driver.ToLowerInvariant() with
                    | "local" -> StorageDriverType.Local
                    | "nfs" -> StorageDriverType.Nfs
                    | "smb" -> StorageDriverType.Smb
                    | "azure" -> StorageDriverType.CloudAzure
                    | "aws" -> StorageDriverType.CloudAws
                    | "gcp" -> StorageDriverType.CloudGcp
                    | _ -> failwithf "Driver %s non géré (normalement déjà validé)" settings.Driver

                use client = new VolumeClient()
                let! response = client.CreateAsync(name = settings.Name, driver = driverType)
                output.WriteSuccess(sprintf "Volume %s créé (ID: %s)" response.Name response.Id)
                return 0
        }

// ── remove ────────────────────────────────────────────────────────
type RemoveVolumeSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandOption("-f|--force")>] member val Force = false with get, set

type RemoveVolumeCommand(output: IOutputPort) =
    inherit AsyncCommand<RemoveVolumeSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du volume est requis")
                return 1
            else
                use client = new VolumeClient()
                let! response = client.RemoveAsync(settings.Id, settings.Force)
                if response.Success then
                    output.WriteSuccess(response.Message)
                else
                    output.WriteError(response.Message)
                return 0
        }

// ── mount ─────────────────────────────────────────────────────────
type MountSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandArgument(1, "<TARGET>")>] member val Target: string = null with get, set

type MountVolumeCommand(output: IOutputPort) =
    inherit AsyncCommand<MountSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du volume est requis")
                return 1
            else
                use client = new VolumeClient()
                let! response = client.MountAsync(settings.Id, settings.Target)
                output.WriteSuccess(sprintf "Volume %s monté sur %s (%s)" settings.Id settings.Target response.Mountpoint)
                return 0
        }

// ── unmount ───────────────────────────────────────────────────────
type UnmountSettings() =
    inherit CommandSettings()
    [<CommandArgument(0, "<ID>")>] member val Id: string = null with get, set
    [<CommandArgument(1, "<TARGET>")>] member val Target: string = null with get, set

type UnmountVolumeCommand(output: IOutputPort) =
    inherit AsyncCommand<UnmountSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrEmpty(settings.Id) then
                output.WriteError("L'identifiant du volume est requis")
                return 1
            else
                use client = new VolumeClient()
                let! response = client.UnmountAsync(settings.Id, settings.Target)
                output.WriteSuccess(sprintf "Volume %s démonté de %s" settings.Id settings.Target)
                return 0
        }

// ── prune ─────────────────────────────────────────────────────────
type PruneVolumesCommand(output: IOutputPort) =
    inherit AsyncCommand<CommandSettings>()

    override _.ExecuteAsync(_ctx, _settings, _ct) : Task<int> =
        task {
            use client = new VolumeClient()
            let! response = client.PruneVolumesAsync()
            if response.Count > 0 then
                output.WriteSuccess(response.Message)
                for id in response.VolumesDeleted do
                    output.WriteLine(sprintf "  - %s" id)
            else
                output.WriteWarning("Aucun volume à supprimer.")
            return 0
        }
