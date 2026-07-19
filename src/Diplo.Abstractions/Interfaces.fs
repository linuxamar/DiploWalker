namespace Diplo.Abstractions

open System.Text.Json

/// Interfaces des clients pour les services Diplo.
module Interfaces =

    /// Exécuteur de processus (abstraction pour le test)
    type IProcessRunner =
        abstract member Run: fileName: string * arguments: string -> string

    /// Client pour interagir avec containerd
    type IContainerdClient =
        abstract member CreateContainer: namespaceName: string * id: string * image: string * labels: Map<string, string> -> string
        abstract member StartContainer: namespaceName: string * id: string -> unit
        abstract member StopContainer: namespaceName: string * id: string * timeoutSeconds: int -> unit
        abstract member DeleteContainer: namespaceName: string * id: string * force: bool -> unit
        abstract member InspectContainer: namespaceName: string * id: string -> JsonElement
        abstract member TaskInfo: namespaceName: string * id: string -> JsonElement
        abstract member ListContainers: namespaceName: string * all: bool -> string list
        abstract member GetContainerLogs: namespaceName: string * id: string * tail: int -> string list
        abstract member ExecInContainer: namespaceName: string * id: string * command: string array -> string
        abstract member Version: unit -> string
        abstract member Namespaces: unit -> string list

    /// Driver de volumes pour la gestion du stockage
    type IVolumeDriver =
        abstract member CreateVolume: name: string * driverOpts: Map<string, string> * labels: Map<string, string> -> string * string
        abstract member RemoveVolume: id: string * force: bool -> bool
        abstract member InspectVolume: id: string -> JsonElement option
        abstract member ListVolumes: filters: Map<string, string> -> JsonElement list
        abstract member MountVolume: id: string * targetPath: string * options: string -> bool * string
        abstract member UnmountVolume: id: string * targetPath: string -> bool * string
        abstract member GetVolumeSize: id: string -> int64

    /// Plugin CNI pour la gestion réseau
    type ICniPlugin =
        abstract member Name: string
        abstract member AddNetwork: configPath: string -> Result<string, string>
        abstract member RemoveNetwork: configPath: string -> Result<unit, string>


