namespace DiploWalker.Cli.Catalog

open System
open System.Threading
open System.Threading.Tasks
open DiploWalker.Cli
open DiploWalker.Core
open DiploWalker.Core.Clients
open DiploWalker.Core.Output
open Spectre.Console.Cli

/// Résout le chemin du fichier catalogue : l'option --catalog prime sur le
/// chemin par défaut (DIPLO_CONFIG_HOME ou répertoire courant).
module private Paths =
    let catalogPath (settingsPath: string) =
        if String.IsNullOrWhiteSpace settingsPath then
            ImageCatalog.catalogPath ()
        else
            settingsPath

// ── list ──────────────────────────────────────────────────────────
type CatalogListSettings() =
    inherit CommandSettings()

    [<CommandOption("--catalog")>]
    member val CatalogPath: string = null with get, set

type CatalogListCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<CatalogListSettings>()
    new(output: IOutputPort) = CatalogListCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            let path = Paths.catalogPath settings.CatalogPath
            let entries = ImageCatalog.load path

            if List.isEmpty entries then
                output.WriteWarning("Catalogue vide.")
            else
                output.WriteTable(
                    ResizeArray(entries),
                    [| "Référence"; "Note"; "Ajouté"; "Mis à jour" |],
                    fun e ->
                        [| e.Ref
                           defaultArg e.Note ""
                           e.AddedAt
                           e.UpdatedAt |]
                )

            return 0
        }

// ── add ───────────────────────────────────────────────────────────
type CatalogAddSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<REF>")>]
    member val Ref: string = null with get, set

    [<CommandOption("--note")>]
    member val Note: string = null with get, set

    [<CommandOption("--no-pull")>]
    member val NoPull = false with get, set

    [<CommandOption("--catalog")>]
    member val CatalogPath: string = null with get, set

type CatalogAddCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<CatalogAddSettings>()
    new(output: IOutputPort) = CatalogAddCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace settings.Ref then
                output.WriteError("La référence de l'image est requise")
                return 1
            else
                let path = Paths.catalogPath settings.CatalogPath

                if not settings.NoPull then
                    use client = clients.CreateContainerClient()
                    let! response = client.PullImageAsync(image = settings.Ref, ct = ct)
                    output.WriteSuccess(sprintf "Image %s téléchargée - %s" settings.Ref response.Message)

                let note =
                    if String.IsNullOrWhiteSpace settings.Note then
                        None
                    else
                        Some settings.Note

                if ImageCatalog.add path settings.Ref note then
                    output.WriteSuccess(sprintf "Image %s ajoutée au catalogue" settings.Ref)
                else
                    output.WriteWarning(sprintf "L'image %s est déjà au catalogue" settings.Ref)

                return 0
        }

// ── update ────────────────────────────────────────────────────────
type CatalogUpdateSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<REF>")>]
    member val Ref: string = null with get, set

    [<CommandOption("--target")>]
    member val Target: string = null with get, set

    [<CommandOption("--note")>]
    member val Note: string = null with get, set

    [<CommandOption("--catalog")>]
    member val CatalogPath: string = null with get, set

type CatalogUpdateCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<CatalogUpdateSettings>()
    new(output: IOutputPort) = CatalogUpdateCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace settings.Ref then
                output.WriteError("La référence de l'image est requise")
                return 1
            else
                let path = Paths.catalogPath settings.CatalogPath
                let hasTarget = not (String.IsNullOrWhiteSpace settings.Target)

                if hasTarget then
                    use client = clients.CreateContainerClient()
                    let! response = client.TagImageAsync(source = settings.Ref, target = settings.Target, ct = ct)
                    output.WriteSuccess(sprintf "Image %s étiquetée en %s - %s" settings.Ref settings.Target response.Message)

                let newRef =
                    if hasTarget then
                        Some settings.Target
                    else
                        None

                let note =
                    if String.IsNullOrWhiteSpace settings.Note then
                        None
                    else
                        Some settings.Note

                if ImageCatalog.update path settings.Ref newRef note then
                    output.WriteSuccess(sprintf "Entrée %s mise à jour dans le catalogue" settings.Ref)
                    return 0
                else
                    output.WriteError(sprintf "L'image %s n'est pas au catalogue" settings.Ref)
                    return 1
        }

// ── delete ────────────────────────────────────────────────────────
type CatalogDeleteSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<REF>")>]
    member val Ref: string = null with get, set

    [<CommandOption("--no-docker")>]
    member val NoDocker = false with get, set

    [<CommandOption("--catalog")>]
    member val CatalogPath: string = null with get, set

type CatalogDeleteCommand(output: IOutputPort, clients: IDiploClients) =
    inherit AsyncCommand<CatalogDeleteSettings>()
    new(output: IOutputPort) = CatalogDeleteCommand(output, DiploWalkerClients())

    override _.ExecuteAsync(_ctx, settings, ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace settings.Ref then
                output.WriteError("La référence de l'image est requise")
                return 1
            else
                let path = Paths.catalogPath settings.CatalogPath

                if not (ImageCatalog.load path |> List.exists (fun e -> e.Ref = settings.Ref)) then
                    output.WriteWarning(sprintf "L'image %s n'est pas au catalogue" settings.Ref)
                    return 0
                elif settings.NoDocker then
                    ImageCatalog.remove path settings.Ref |> ignore
                    output.WriteSuccess(sprintf "Image %s retirée du catalogue" settings.Ref)
                    return 0
                else
                    use client = clients.CreateContainerClient()
                    let! response = client.RemoveImageAsync(ref = settings.Ref, ct = ct)

                    if not response.Success then
                        output.WriteError(response.Message)
                        return 1
                    else
                        ImageCatalog.remove path settings.Ref |> ignore
                        output.WriteSuccess(sprintf "Image %s supprimée et retirée du catalogue" settings.Ref)
                        return 0
        }

