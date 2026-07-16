namespace Diplo.Abstractions

/// Interfaces des clients pour les services Diplo.
module Interfaces =

    /// Client pour interagir avec containerd
    type IContainerdClient =
        abstract member GetVersion: unit -> string
        abstract member ListNamespaces: unit -> string list

    /// Plugin CNI pour la gestion réseau
    type ICniPlugin =
        abstract member Name: string
        abstract member AddNetwork: configPath: string -> Result<string, string>
        abstract member RemoveNetwork: configPath: string -> Result<unit, string>

    /// Client de configuration centralisée
    type IConfigClient =
        abstract member GetValue: key: string -> string option
        abstract member GetSection: section: string -> Map<string, string>
