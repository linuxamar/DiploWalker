namespace Diplo.Abstractions

open System.Text.Json

/// Interfaces des clients pour les services Diplo.
module Interfaces =

    /// Client pour interagir avec containerd
    type IContainerdClient =
        abstract member GetVersion: unit -> string
        abstract member ListNamespaces: unit -> string list
        abstract member CreateContainer: namespaceName: string * id: string * image: string * labels: Map<string, string> -> string
        abstract member StartContainer: namespaceName: string * id: string -> unit
        abstract member StopContainer: namespaceName: string * id: string * timeoutSeconds: int -> unit
        abstract member DeleteContainer: namespaceName: string * id: string * force: bool -> unit
        abstract member InspectContainer: namespaceName: string * id: string -> JsonElement
        abstract member TaskInfo: namespaceName: string * id: string -> JsonElement
        abstract member ListContainers: namespaceName: string * all: bool -> string list
        abstract member GetContainerLogs: namespaceName: string * id: string * tail: int -> string list
        abstract member ExecInContainer: namespaceName: string * id: string * command: string array -> string
        abstract member Version: unit -> JsonElement
        abstract member Namespaces: unit -> string list

    /// Plugin CNI pour la gestion réseau
    type ICniPlugin =
        abstract member Name: string
        abstract member AddNetwork: configPath: string -> Result<string, string>
        abstract member RemoveNetwork: configPath: string -> Result<unit, string>

    /// Client de configuration centralisée
    type IConfigClient =
        abstract member GetValue: key: string -> string option
        abstract member GetSection: section: string -> Map<string, string>
