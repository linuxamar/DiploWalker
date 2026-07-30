namespace Diplo.Gui.ViewModels

open System
open System.Collections.ObjectModel
open Avalonia.Threading
open Diplo.Core.Clients
open Diplo.Core.Output
open Diplo.Grpc.Volume

type VolumeDisplayInfo = {
    Id: string
    Nom: string
    Driver: string
    PointDeMontage: string
    Taille: string
}

type VolumeTabViewModel(outputPort: IOutputPort) as this =
    inherit ViewModelBase()

    let volumes = ObservableCollection<VolumeDisplayInfo>()

    let mutable volumeIdInput = ""
    let mutable volumeNameInput = ""
    let mutable volumeDriver = "local"
    let mutable volumeTargetPath = ""
    let mutable volumeForce = false

    member _.Volumes = volumes

    member _.VolumeIdInput with get () = volumeIdInput and set v = volumeIdInput <- v; this.OnPropertyChanged()
    member _.VolumeNameInput with get () = volumeNameInput and set v = volumeNameInput <- v; this.OnPropertyChanged()
    member _.VolumeDriver with get () = volumeDriver and set v = volumeDriver <- v; this.OnPropertyChanged()
    member _.VolumeTargetPath with get () = volumeTargetPath and set v = volumeTargetPath <- v; this.OnPropertyChanged()
    member _.VolumeForce with get () = volumeForce and set v = volumeForce <- v; this.OnPropertyChanged()

    member _.ListVolumesCommand = RelayCommand(Action(fun () -> this.ListVolumes() |> ignore))
    member _.InspectVolumeCommand = RelayCommand(Action(fun () -> this.InspectVolume() |> ignore))
    member _.CreateVolumeCommand = RelayCommand(Action(fun () -> this.CreateVolume() |> ignore))
    member _.RemoveVolumeCommand = RelayCommand(Action(fun () -> this.RemoveVolume() |> ignore))
    member _.MountVolumeCommand = RelayCommand(Action(fun () -> this.MountVolume() |> ignore))
    member _.UnmountVolumeCommand = RelayCommand(Action(fun () -> this.UnmountVolume() |> ignore))
    member _.PruneVolumesCommand = RelayCommand(Action(fun () -> this.PruneVolumes() |> ignore))

    member private this.ListVolumes() =
        task {
            try
                use client = new VolumeClient()
                let! response = client.ListAsync()
                Dispatcher.UIThread.Post(fun () ->
                    volumes.Clear()
                    for v in response.Volumes do
                        volumes.Add({
                            Id = v.Id
                            Nom = v.Name
                            Driver = v.Driver.ToString()
                            PointDeMontage = v.Mountpoint
                            Taille = if v.SizeBytes > 0L then sprintf "%d octets" v.SizeBytes else "-"
                        })
                )
                outputPort.WriteSuccess(sprintf "%d volume(s) trouvé(s)" response.Volumes.Count)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.InspectVolume() =
        task {
            try
                use client = new VolumeClient()
                let! response = client.InspectAsync(id = this.VolumeIdInput)
                outputPort.WriteLine(sprintf "ID: %s" response.Id)
                outputPort.WriteLine(sprintf "Nom: %s" response.Name)
                outputPort.WriteLine(sprintf "Driver: %s" (response.Driver.ToString()))
                outputPort.WriteLine(sprintf "Point de montage: %s" response.Mountpoint)
                outputPort.WriteLine(sprintf "État: %s" (response.State.ToString()))
                if response.SizeBytes > 0L then
                    outputPort.WriteLine(sprintf "Taille: %d octets" response.SizeBytes)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.CreateVolume() =
        task {
            try
                use client = new VolumeClient()
                let driverEnum =
                    match this.VolumeDriver.ToLowerInvariant() with
                    | "local" -> StorageDriverType.Local
                    | "nfs" -> StorageDriverType.Nfs
                    | "smb" -> StorageDriverType.Smb
                    | "azure" -> StorageDriverType.CloudAzure
                    | "aws" -> StorageDriverType.CloudAws
                    | "gcp" -> StorageDriverType.CloudGcp
                    | _ -> StorageDriverType.Local
                let! response = client.CreateAsync(name = this.VolumeNameInput, driver = driverEnum)
                outputPort.WriteSuccess(sprintf "Volume %s créé (ID: %s)" this.VolumeNameInput response.Id)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.RemoveVolume() =
        task {
            try
                use client = new VolumeClient()
                let! response = client.RemoveAsync(id = this.VolumeIdInput, force = this.VolumeForce)
                if response.Success then
                    outputPort.WriteSuccess(sprintf "Volume %s supprimé" this.VolumeIdInput)
                else
                    outputPort.WriteWarning(response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.MountVolume() =
        task {
            try
                use client = new VolumeClient()
                let! response = client.MountAsync(id = this.VolumeIdInput, targetPath = this.VolumeTargetPath)
                outputPort.WriteSuccess(sprintf "Volume %s monté sur %s - %s" this.VolumeIdInput this.VolumeTargetPath response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.UnmountVolume() =
        task {
            try
                use client = new VolumeClient()
                let! response = client.UnmountAsync(id = this.VolumeIdInput, targetPath = this.VolumeTargetPath)
                outputPort.WriteSuccess(sprintf "Volume %s démonté de %s - %s" this.VolumeIdInput this.VolumeTargetPath response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }

    member private this.PruneVolumes() =
        task {
            try
                use client = new VolumeClient()
                let! response = client.PruneVolumesAsync()
                outputPort.WriteSuccess(sprintf "Volumes nettoyés - %s" response.Message)
            with ex -> outputPort.WriteError(ex.Message)
        }
