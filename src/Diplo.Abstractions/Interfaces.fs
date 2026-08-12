namespace Diplo.Abstractions

open System.Text.Json

/// Interfaces des clients pour les services Diplo.
module Interfaces =

    /// Exécuteur de processus (abstraction pour le test)
    type IProcessRunner =
        abstract member RunWithArgs: fileName: string * args: string list -> string

    /// Client pour interagir avec containerd
    type IContainerdClient =
        abstract member CreateContainer: namespaceName: string * id: string * image: string * labels: Map<string, string> * env: Map<string, string> * command: string array * args: string array * memoryLimit: int64 * cpuShares: int64 * pidLimit: uint32 * mounts: (string * string * bool) list -> string
        abstract member StartContainer: namespaceName: string * id: string * detach: bool -> unit
        /// Démarre le conteneur en attaché et capture sa sortie standard dans
        /// le fichier de logs donné (processus en arrière-plan).
        abstract member StartContainerWithLogs: namespaceName: string * id: string * logFile: string -> unit
        abstract member StopContainer: namespaceName: string * id: string * timeoutSeconds: int -> System.Threading.Tasks.Task
        abstract member DeleteContainer: namespaceName: string * id: string * force: bool -> unit
        abstract member PauseContainer: namespaceName: string * id: string -> unit
        abstract member ResumeContainer: namespaceName: string * id: string -> unit
        /// Attend la sortie du conteneur et retourne son code de sortie
        /// (0 si sorti, -1 en cas de timeout, 0 si déjà arrêté).
        abstract member WaitForContainerExit: namespaceName: string * id: string * timeoutSeconds: int -> int
        abstract member UpdateContainer: namespaceName: string * id: string * memoryLimit: int64 * cpuShares: int * pidLimit: int -> unit
        abstract member InspectContainer: namespaceName: string * id: string -> JsonElement
        abstract member TaskInfo: namespaceName: string * id: string -> JsonElement
        abstract member ListContainers: namespaceName: string * all: bool -> string list
        abstract member GetContainerLogs: namespaceName: string * id: string * tail: int * follow: bool * since: string -> string list
        /// Suit les journaux d'un conteneur en continu : émet l'instantané
        /// (tail/since) puis les nouvelles lignes au fil de leur écriture,
        /// jusqu'à la sortie du conteneur ou l'annulation.
        abstract member GetContainerLogsStream: namespaceName: string * id: string * tail: int * since: string * ct: System.Threading.CancellationToken -> System.Collections.Generic.IAsyncEnumerable<string>
        abstract member ExecInContainer: namespaceName: string * id: string * command: string array -> string
        abstract member PullImage: image: string * userArg: string option -> string
        abstract member Version: unit -> string
        abstract member Namespaces: unit -> string list
        abstract member CreateNamespace: name: string -> unit
        abstract member DeleteNamespace: name: string -> unit
        abstract member RenameContainer: namespaceName: string * id: string * newName: string -> unit
        abstract member TopContainer: namespaceName: string * id: string -> string
        abstract member GetContainerStats: namespaceName: string * id: string -> JsonElement
        abstract member ListImages: namespaceName: string -> JsonElement list
        abstract member InspectImage: namespaceName: string * ref: string -> JsonElement
        abstract member RemoveImage: namespaceName: string * ref: string -> string
        abstract member TagImage: namespaceName: string * source: string * target: string -> unit
        abstract member ExportImage: namespaceName: string * imageRef: string * tarFile: string -> unit
        abstract member ImportImage: namespaceName: string * tarFile: string -> string list
        /// Exécute une commande dans le conteneur avec redirection des flux
        /// (entrée, sortie, erreur) et retourne le code de sortie.
        abstract member StartExec: namespaceName: string * id: string * command: string array * stdin: System.IO.Stream * stdout: System.IO.Stream * stderr: System.IO.Stream -> int

    /// Driver de volumes pour la gestion du stockage
    type IVolumeDriver =
        abstract member CreateVolume: name: string * driverOpts: Map<string, string> * labels: Map<string, string> -> string * string
        abstract member RemoveVolume: id: string * force: bool -> bool
        abstract member InspectVolume: id: string -> JsonElement option
        abstract member ListVolumes: filters: Map<string, string> -> JsonElement list
        abstract member MountVolume: id: string * targetPath: string * options: string -> bool * string
        abstract member UnmountVolume: id: string * targetPath: string -> bool * string
        abstract member GetVolumeSize: id: string -> int64
        abstract member PruneVolumes: unit -> string list


