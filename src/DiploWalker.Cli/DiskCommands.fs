namespace DiploWalker.Cli.Disk

open System
open System.Threading.Tasks
open DiploWalker.Core.Output
open DiploWalker.Disk
open Spectre.Console.Cli

// â”€â”€ create-image â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
type CreateImageSettings() =
    inherit CommandSettings()

    [<CommandArgument(0, "<SOURCE>")>]
    member val Source: string = null with get, set

    [<CommandArgument(1, "<DEST>")>]
    member val Dest: string = null with get, set

    [<CommandOption("--format")>]
    member val Format: string = "raw" with get, set

type CreateImageCommand(output: IOutputPort) =
    inherit AsyncCommand<CreateImageSettings>()

    override _.ExecuteAsync(_ctx, settings, _ct) : Task<int> =
        task {
            if String.IsNullOrWhiteSpace settings.Source then
                output.WriteError("Le rÃ©pertoire source est requis")
                return 1
            elif not (IO.Directory.Exists settings.Source) then
                output.WriteError(sprintf "Le rÃ©pertoire source n'existe pas : '%s'" settings.Source)
                return 1
            elif String.IsNullOrWhiteSpace settings.Dest then
                output.WriteError("Le chemin de destination est requis")
                return 1
            else
                let formatOpt =
                    match settings.Format.ToLowerInvariant() with
                    | "vhd" -> Some DiskFormat.Vhd
                    | "vhdx" -> Some DiskFormat.Vhdx
                    | "vmdk" -> Some DiskFormat.Vmdk
                    | "vdi" -> Some DiskFormat.Vdi
                    | "raw"
                    | "" -> Some DiskFormat.Raw
                    | other ->
                        output.WriteError(sprintf "Format inconnu : '%s' (utilisez vhd, vhdx, vmdk, vdi ou raw)" other)
                        None

                match formatOpt with
                | None -> return 1
                | Some format ->

                    try
                        output.WriteLine(
                            sprintf "CrÃ©ation de l'image '%s' au format %sâ€¦" settings.Dest (DiskFormat.toString format)
                        )

                        let result = FsImage.create settings.Source settings.Dest format |> Result.defaultWith failwith
                        let size = IO.FileInfo(result).Length
                        output.WriteSuccess(sprintf "Image crÃ©Ã©e : %s (%d Mo)" result (size / 1024L / 1024L))
                        return 0
                    with ex ->
                        output.WriteError(sprintf "Ã‰chec de la crÃ©ation : %s" ex.Message)
                        return 1
        }

