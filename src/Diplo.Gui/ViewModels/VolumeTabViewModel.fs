namespace Diplo.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Threading.Tasks
open Avalonia.Platform.Storage
open Avalonia.Threading
open Diplo.Core.Clients
open Diplo.Grpc
open Diplo.Core.Output
open Diplo.Grpc.Volume
open Diplo.Disk
open Diplo.Gui.Services

type VolumeDisplayInfo =
    { Id: string
      Nom: string
      Driver: string
      PointDeMontage: string
      Taille: string }

type VolumeTabViewModel(outputPort: IOutputPort, ?volumeClientFactory: unit -> IVolumeClient) as this =
    inherit ViewModelBase()

    let volumes = ObservableCollection<VolumeDisplayInfo>()

    let volumeClient =
        let factory = defaultArg volumeClientFactory (fun () -> new VolumeClient() :> IVolumeClient)
        factory ()

    let mutable volumeIdInput = ""
    let mutable volumeNameInput = ""
    let mutable volumeDriver = "local"
    let mutable volumeTargetPath = ""
    let mutable volumeForce = false
    let mutable imageSourceDir = ""
    let mutable imageDestPath = ""
    let mutable imageFormat = "raw"
    let mutable storageProvider: IStorageProvider = null

    let listVolumesCmd = RelayCommand(Action(fun () -> this.ListVolumes() |> ignore))

    let inspectVolumeCmd =
        RelayCommand(Action(fun () -> this.InspectVolume() |> ignore))

    let createVolumeCmd = RelayCommand(Action(fun () -> this.CreateVolume() |> ignore))
    let removeVolumeCmd = RelayCommand(Action(fun () -> this.RemoveVolume() |> ignore))
    let mountVolumeCmd = RelayCommand(Action(fun () -> this.MountVolume() |> ignore))

    let unmountVolumeCmd =
        RelayCommand(Action(fun () -> this.UnmountVolume() |> ignore))

    let pruneVolumesCmd = RelayCommand(Action(fun () -> this.PruneVolumes() |> ignore))
    let createImageCmd = RelayCommand(Action(fun () -> this.CreateImage() |> ignore))
    let browseSourceCmd = RelayCommand(Action(fun () -> this.BrowseSource() |> ignore))
    let browseDestCmd = RelayCommand(Action(fun () -> this.BrowseDest() |> ignore))

    member _.Volumes = volumes

    member _.VolumeIdInput
        with get () = volumeIdInput
        and set v =
            volumeIdInput <- v
            this.OnPropertyChanged()

    member _.VolumeNameInput
        with get () = volumeNameInput
        and set v =
            volumeNameInput <- v
            this.OnPropertyChanged()

    member _.VolumeDriver
        with get () = volumeDriver
        and set v =
            volumeDriver <- v
            this.OnPropertyChanged()

    member _.VolumeTargetPath
        with get () = volumeTargetPath
        and set v =
            volumeTargetPath <- v
            this.OnPropertyChanged()

    member _.VolumeForce
        with get () = volumeForce
        and set v =
            volumeForce <- v
            this.OnPropertyChanged()

    member _.ImageSourceDir
        with get () = imageSourceDir
        and set v =
            imageSourceDir <- v
            this.OnPropertyChanged()

    member _.ImageDestPath
        with get () = imageDestPath
        and set v =
            imageDestPath <- v
            this.OnPropertyChanged()

    member _.ImageFormat
        with get () = imageFormat
        and set v =
            imageFormat <- v
            this.OnPropertyChanged()

    member _.SetStorageProvider(sp: IStorageProvider) = storageProvider <- sp

    member _.ListVolumesCommand = listVolumesCmd
    member _.InspectVolumeCommand = inspectVolumeCmd
    member _.CreateVolumeCommand = createVolumeCmd
    member _.RemoveVolumeCommand = removeVolumeCmd
    member _.MountVolumeCommand = mountVolumeCmd
    member _.UnmountVolumeCommand = unmountVolumeCmd
    member _.PruneVolumesCommand = pruneVolumesCmd
    member _.CreateImageCommand = createImageCmd
    member _.BrowseSourceCommand = browseSourceCmd
    member _.BrowseDestCommand = browseDestCmd

    member private this.BrowseSource() =
        // Cmd.run : exceptions attendues et signalées — le fire-and-forget
        // rendait l'échec du picker totalement muet.
        Cmd.run outputPort (fun () ->
            task {
                if isNull storageProvider then
                    outputPort.WriteWarning("Fournisseur de stockage non disponible")
                else
                    let! result =
                        storageProvider.OpenFolderPickerAsync(
                            FolderPickerOpenOptions(Title = "Sélectionner le répertoire source", AllowMultiple = false)
                        )

                    if result.Count > 0 then
                        this.ImageSourceDir <- result.[0].Path.LocalPath
            })

    member private this.BrowseDest() =
        Cmd.run outputPort (fun () ->
            task {
                if isNull storageProvider then
                    outputPort.WriteWarning("Fournisseur de stockage non disponible")
                else
                    let! result =
                        storageProvider.OpenFilePickerAsync(
                            FilePickerOpenOptions(
                                Title = "Enregistrer l'image disque sous",
                                AllowMultiple = false,
                                FileTypeFilter =
                                    [ FilePickerFileType("VHD", Patterns = [| "*.vhd" |])
                                      FilePickerFileType("VHDX", Patterns = [| "*.vhdx" |])
                                      FilePickerFileType("VMDK", Patterns = [| "*.vmdk" |])
                                      FilePickerFileType("VDI", Patterns = [| "*.vdi" |])
                                      FilePickerFileType("Raw", Patterns = [| "*.img"; "*.raw" |])
                                      FilePickerFileType("Tous", Patterns = [| "*.*" |]) ]
                            )
                        )

                    if result.Count > 0 then
                        this.ImageDestPath <- result.[0].Path.LocalPath
            })

    member private this.ListVolumes() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = volumeClient.ListAsync()

                UiThread.Post(fun () ->
                    volumes.Clear()

                    for v in response.Volumes do
                        volumes.Add(
                            { Id = v.Id
                              Nom = v.Name
                              Driver = v.Driver.ToString()
                              PointDeMontage = v.Mountpoint
                              Taille =
                                if v.SizeBytes > 0L then
                                    sprintf "%d octets" v.SizeBytes
                                else
                                    "-" }
                        ))

                outputPort.WriteSuccess(sprintf "%d volume(s) trouvé(s)" response.Volumes.Count)
            })

    member private this.InspectVolume() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = volumeClient.InspectAsync(id = this.VolumeIdInput)
                outputPort.WriteLine(sprintf "ID: %s" response.Id)
                outputPort.WriteLine(sprintf "Nom: %s" response.Name)
                outputPort.WriteLine(sprintf "Driver: %s" (response.Driver.ToString()))
                outputPort.WriteLine(sprintf "Point de montage: %s" response.Mountpoint)
                outputPort.WriteLine(sprintf "État: %s" (response.State.ToString()))

                if response.SizeBytes > 0L then
                    outputPort.WriteLine(sprintf "Taille: %d octets" response.SizeBytes)
            })

    member private this.CreateVolume() =
        Cmd.run outputPort (fun () ->
            task {
                let driverEnum = DriverMappings.parseVolumeDriver this.VolumeDriver
                let! response = volumeClient.CreateAsync(name = this.VolumeNameInput, driver = driverEnum)
                outputPort.WriteSuccess(sprintf "Volume %s créé (ID: %s)" this.VolumeNameInput response.Id)
            })

    member private this.RemoveVolume() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = volumeClient.RemoveAsync(id = this.VolumeIdInput, force = this.VolumeForce)

                if response.Success then
                    outputPort.WriteSuccess(sprintf "Volume %s supprimé" this.VolumeIdInput)
                else
                    outputPort.WriteWarning(response.Message)
            })

    member private this.MountVolume() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = volumeClient.MountAsync(id = this.VolumeIdInput, targetPath = this.VolumeTargetPath)

                outputPort.WriteSuccess(
                    sprintf "Volume %s monté sur %s - %s" this.VolumeIdInput this.VolumeTargetPath response.Message
                )
            })

    member private this.UnmountVolume() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = volumeClient.UnmountAsync(id = this.VolumeIdInput, targetPath = this.VolumeTargetPath)

                outputPort.WriteSuccess(
                    sprintf "Volume %s démonté de %s - %s" this.VolumeIdInput this.VolumeTargetPath response.Message
                )
            })

    member private this.PruneVolumes() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = volumeClient.PruneVolumesAsync()
                outputPort.WriteSuccess(sprintf "Volumes nettoyés - %s" response.Message)
            })

    member private this.CreateImage() =
        Cmd.run outputPort (fun () ->
            task {
                if String.IsNullOrEmpty this.ImageSourceDir then
                    outputPort.WriteError("Le répertoire source est requis")
                elif not (System.IO.Directory.Exists this.ImageSourceDir) then
                    outputPort.WriteError(sprintf "Le répertoire source n'existe pas : '%s'" this.ImageSourceDir)
                elif String.IsNullOrEmpty this.ImageDestPath then
                    outputPort.WriteError("Le chemin de destination est requis")
                else
                    let formatOpt =
                        match this.ImageFormat.ToLowerInvariant() with
                        | "vhd" -> Some DiskFormat.Vhd
                        | "vhdx" -> Some DiskFormat.Vhdx
                        | "vmdk" -> Some DiskFormat.Vmdk
                        | "vdi" -> Some DiskFormat.Vdi
                        | "raw"
                        | "" -> Some DiskFormat.Raw
                        | other ->
                            outputPort.WriteError(
                                sprintf "Format inconnu : '%s' (utilisez vhd, vhdx, vmdk, vdi ou raw)" other
                            )

                            None

                    match formatOpt with
                    | None -> ()
                    | Some format ->
                        outputPort.WriteLine(
                            sprintf
                                "Création de l'image '%s' au format %s…"
                                this.ImageDestPath
                                (DiskFormat.toString format)
                        )

                        // Travail lourd (parcours récursif, écriture de Go) :
                        // déporté hors du thread UI sinon l'interface gèle.
                        let! result = System.Threading.Tasks.Task.Run(fun () ->
                            FsImage.create this.ImageSourceDir this.ImageDestPath format
                            |> Result.defaultWith failwith)

                        let size = System.IO.FileInfo(result).Length
                        outputPort.WriteSuccess(sprintf "Image créée : %s (%d Mo)" result (size / 1024L / 1024L))
            })

    interface IDisposable with
        member _.Dispose() = (volumeClient :> IDisposable).Dispose()
