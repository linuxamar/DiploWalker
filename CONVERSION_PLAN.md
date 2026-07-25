# Plan de Conversion : Diplo.Grpc de C# vers F#

## Résumé

Ce plan converting le projet **Diplo.Grpc** (le seul projet C# de la solution) en F# pur, en utilisant **protobuf-net** + **protobuf-net.Grpc** (code-first, sans fichiers .proto ni Grpc.Tools).

**Approche** : Éliminer complètement les fichiers `.proto` et la dépendance à Grpc.Tools (qui ne génère que du C#). Remplacer par des types F# records avec `[<ProtoContract>]` et des interfaces de service avec `[<ServiceContract>]`.

---

## Packages NuGet

| Projet | Nouveau Package | Version | Remplace |
|--------|----------------|---------|----------|
| Diplo.Grpc | `protobuf-net` | 3.2.47 | `Google.Protobuf` |
| Diplo.Grpc | `protobuf-net.Grpc` | 1.2.2 | `Grpc.Tools`, `Grpc.AspNetCore` |
| Diplo.Container/Volume/Network | `protobuf-net.Grpc.AspNetCore` | 1.2.2 | `Grpc.AspNetCore` |
| Diplo.Core | `protobuf-net.Grpc` | 1.2.2 | — |
| Diplo.Core | `Grpc.Net.Client` | 2.80.0 | (déjà présent) |
| Diplo.Abstractions | `protobuf-net.Grpc.AspNetCore` | 1.2.2 | `Grpc.AspNetCore` |
| Tests (Container/Volume/Network/Core) | `protobuf-net.Grpc` | 1.2.2 | `Grpc.Core.Testing` |

**Supprimé de Diplo.Grpc** : `Google.Protobuf`, `Grpc.Tools`, `Grpc.AspNetCore`

---

## Étape 1 : Créer les Types Messages F# (3 fichiers)

Chaque fichier remplace un fichier `.proto` par des records F#.

### 1a. `ContainerMessages.fs` — remplace `container.proto`

```fsharp
namespace Diplo.Grpc.Container

open System
open ProtoBuf

// ═══════════════════════════════════════════════
// Enums
// ═══════════════════════════════════════════════

[<ProtoContract>]
type ContainerState =
    | [<ProtoEnum>] Unknown = 0
    | [<ProtoEnum>] Created = 1
    | [<ProtoEnum>] Running = 2
    | [<ProtoEnum>] Paused = 3
    | [<ProtoEnum>] Stopped = 4
    | [<ProtoEnum>] Failed = 5

// ═══════════════════════════════════════════════
// CreateContainer
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type CreateContainerRequest =
    { [<ProtoMember(1)>] Name : string
      [<ProtoMember(2)>] Image : string
      [<ProtoMember(3)>] Env : System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(4)>] Command : System.Collections.Generic.List<string>
      [<ProtoMember(5)>] Args : System.Collections.Generic.List<string>
      [<ProtoMember(6)>] Labels : System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(7)>] PidLimit : int
      [<ProtoMember(8)>] MemoryLimit : int64
      [<ProtoMember(9)>] CpuShares : int }

[<ProtoContract; CLIMutable>]
type CreateContainerResponse =
    { [<ProtoMember(1)>] Id : string
      [<ProtoMember(2)>] Name : string
      [<ProtoMember(3)>] State : ContainerState
      [<ProtoMember(4)>] CreatedAt : string }

// ═══════════════════════════════════════════════
// StartContainer
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type StartContainerRequest =
    { [<ProtoMember(1)>] Id : string }

[<ProtoContract; CLIMutable>]
type StartContainerResponse =
    { [<ProtoMember(1)>] State : ContainerState
      [<ProtoMember(2)>] Message : string }

// ═══════════════════════════════════════════════
// StopContainer
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type StopContainerRequest =
    { [<ProtoMember(1)>] Id : string
      [<ProtoMember(2)>] TimeoutSeconds : int }

[<ProtoContract; CLIMutable>]
type StopContainerResponse =
    { [<ProtoMember(1)>] State : ContainerState
      [<ProtoMember(2)>] Message : string }

// ═══════════════════════════════════════════════
// DeleteContainer
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type DeleteContainerRequest =
    { [<ProtoMember(1)>] Id : string
      [<ProtoMember(2)>] Force : bool }

[<ProtoContract; CLIMutable>]
type DeleteContainerResponse =
    { [<ProtoMember(1)>] Success : bool
      [<ProtoMember(2)>] Message : string }

// ═══════════════════════════════════════════════
// InspectContainer
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type InspectContainerRequest =
    { [<ProtoMember(1)>] Id : string }

[<ProtoContract; CLIMutable>]
type InspectContainerResponse =
    { [<ProtoMember(1)>] Id : string
      [<ProtoMember(2)>] Name : string
      [<ProtoMember(3)>] Image : string
      [<ProtoMember(4)>] State : ContainerState
      [<ProtoMember(5)>] CreatedAt : string
      [<ProtoMember(6)>] StartedAt : string
      [<ProtoMember(7)>] FinishedAt : string
      [<ProtoMember(8)>] Labels : System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(9)>] Env : System.Collections.Generic.Dictionary<string, string>
      [<ProtoMember(10)>] Pid : int
      [<ProtoMember(11)>] ExitCode : int }

// ═══════════════════════════════════════════════
// ListContainers
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type ListContainersRequest =
    { [<ProtoMember(1)>] All : bool
      [<ProtoMember(2)>] Filters : System.Collections.Generic.Dictionary<string, string> }

[<ProtoContract; CLIMutable>]
type ContainerInfo =
    { [<ProtoMember(1)>] Id : string
      [<ProtoMember(2)>] Name : string
      [<ProtoMember(3)>] Image : string
      [<ProtoMember(4)>] State : ContainerState
      [<ProtoMember(5)>] CreatedAt : string
      [<ProtoMember(6)>] Labels : System.Collections.Generic.Dictionary<string, string> }

[<ProtoContract; CLIMutable>]
type ListContainersResponse =
    { [<ProtoMember(1)>] Containers : System.Collections.Generic.List<ContainerInfo> }

// ═══════════════════════════════════════════════
// GetContainerLogs (server streaming → IAsyncEnumerable)
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type GetContainerLogsRequest =
    { [<ProtoMember(1)>] Id : string
      [<ProtoMember(2)>] Follow : bool
      [<ProtoMember(3)>] Tail : int
      [<ProtoMember(4)>] Since : string }

[<ProtoContract; CLIMutable>]
type ContainerLogEntry =
    { [<ProtoMember(1)>] Timestamp : string
      [<ProtoMember(2)>] Stream : string
      [<ProtoMember(3)>] Log : string }

// ═══════════════════════════════════════════════
// ExecInContainer (server streaming → IAsyncEnumerable)
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type ExecInContainerRequest =
    { [<ProtoMember(1)>] Id : string
      [<ProtoMember(2)>] Command : System.Collections.Generic.List<string>
      [<ProtoMember(3)>] AttachStdin : bool
      [<ProtoMember(4)>] AttachStdout : bool
      [<ProtoMember(5)>] AttachStderr : bool }

[<ProtoContract; CLIMutable>]
type ExecOutput =
    { [<ProtoMember(1)>] Stream : string
      [<ProtoMember(2)>] Data : byte[] }

// ═══════════════════════════════════════════════
// PullImage
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type PullImageRequest =
    { [<ProtoMember(1)>] Image : string }

[<ProtoContract; CLIMutable>]
type PullImageResponse =
    { [<ProtoMember(1)>] Image : string
      [<ProtoMember(2)>] Message : string }

// ═══════════════════════════════════════════════
// GetVersion
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type GetVersionRequest = { [<ProtoMember(1)>] Placeholder : bool }

[<ProtoContract; CLIMutable>]
type GetVersionResponse =
    { [<ProtoMember(1)>] Version : string
      [<ProtoMember(2)>] Revision : string
      [<ProtoMember(3)>] GoVersion : string
      [<ProtoMember(4)>] Os : string
      [<ProtoMember(5)>] Arch : string }

// ═══════════════════════════════════════════════
// ListNamespaces
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type ListNamespacesRequest = { [<ProtoMember(1)>] Placeholder : bool }

[<ProtoContract; CLIMutable>]
type ListNamespacesResponse =
    { [<ProtoMember(1)>] Namespaces : System.Collections.Generic.List<string> }

// ═══════════════════════════════════════════════
// RenameContainer
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type RenameContainerRequest =
    { [<ProtoMember(1)>] Id : string
      [<ProtoMember(2)>] NewName : string }

[<ProtoContract; CLIMutable>]
type RenameContainerResponse =
    { [<ProtoMember(1)>] Success : bool
      [<ProtoMember(2)>] Message : string }

// ═══════════════════════════════════════════════
// TopContainer
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type TopContainerRequest =
    { [<ProtoMember(1)>] Id : string }

[<ProtoContract; CLIMutable>]
type ProcessInfo =
    { [<ProtoMember(1)>] Pid : int64
      [<ProtoMember(2)>] User : string
      [<ProtoMember(3)>] Command : string
      [<ProtoMember(4)>] CpuPercent : float
      [<ProtoMember(5)>] MemPercent : float
      [<ProtoMember(6)>] Rss : int64 }

[<ProtoContract; CLIMutable>]
type TopContainerResponse =
    { [<ProtoMember(1)>] Processes : System.Collections.Generic.List<ProcessInfo> }

// ═══════════════════════════════════════════════
// GetContainerStats
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type GetContainerStatsRequest =
    { [<ProtoMember(1)>] Id : string }

[<ProtoContract; CLIMutable>]
type GetContainerStatsResponse =
    { [<ProtoMember(1)>] CpuUsage : float
      [<ProtoMember(2)>] MemoryUsage : int64
      [<ProtoMember(3)>] MemoryLimit : int64
      [<ProtoMember(4)>] NetworkRx : int64
      [<ProtoMember(5)>] NetworkTx : int64
      [<ProtoMember(6)>] DiskRead : int64
      [<ProtoMember(7)>] DiskWrite : int64
      [<ProtoMember(8)>] Pids : int }

// ═══════════════════════════════════════════════
// ListImages
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type ListImagesRequest =
    { [<ProtoMember(1)>] NamespaceName : string }

[<ProtoContract; CLIMutable>]
type ImageInfo =
    { [<ProtoMember(1)>] Ref : string
      [<ProtoMember(2)>] Id : string
      [<ProtoMember(3)>] Repository : string
      [<ProtoMember(4)>] Tag : string
      [<ProtoMember(5)>] Size : int64
      [<ProtoMember(6)>] CreatedAt : string }

[<ProtoContract; CLIMutable>]
type ListImagesResponse =
    { [<ProtoMember(1)>] Images : System.Collections.Generic.List<ImageInfo> }

// ═══════════════════════════════════════════════
// InspectImage
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type InspectImageRequest =
    { [<ProtoMember(1)>] Ref : string
      [<ProtoMember(2)>] NamespaceName : string }

[<ProtoContract; CLIMutable>]
type InspectImageResponse =
    { [<ProtoMember(1)>] Ref : string
      [<ProtoMember(2)>] Id : string
      [<ProtoMember(3)>] Repository : string
      [<ProtoMember(4)>] Tag : string
      [<ProtoMember(5)>] Size : int64
      [<ProtoMember(6)>] CreatedAt : string
      [<ProtoMember(7)>] Labels : System.Collections.Generic.Dictionary<string, string> }

// ═══════════════════════════════════════════════
// RemoveImage
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type RemoveImageRequest =
    { [<ProtoMember(1)>] Ref : string
      [<ProtoMember(2)>] NamespaceName : string }

[<ProtoContract; CLIMutable>]
type RemoveImageResponse =
    { [<ProtoMember(1)>] Success : bool
      [<ProtoMember(2)>] Message : string }

// ═══════════════════════════════════════════════
// TagImage
// ═══════════════════════════════════════════════

[<ProtoContract; CLIMutable>]
type TagImageRequest =
    { [<ProtoMember(1)>] Source : string
      [<ProtoMember(2)>] Target : string
      [<ProtoMember(3)>] NamespaceName : string }

[<ProtoContract; CLIMutable>]
type TagImageResponse =
    { [<ProtoMember(1)>] Source : string
      [<ProtoMember(2)>] Target : string
      [<ProtoMember(3)>] Message : string }
```

### 1b. `VolumeMessages.fs` — remplace `volume.proto`

Même pattern avec les enums `StorageDriverType`, `MountState` et les 7 paires Request/Response.

### 1c. `NetworkMessages.fs` — remplace `network.proto`

Même pattern avec les enums `NetworkDriver`, `EndpointState` et les 8 paires Request/Response + types auxiliaires (`EndpointInfo`, `CniConfiguration`).

---

## Étape 2 : Créer les Interfaces de Service F# (3 fichiers)

### 2a. `IContainerService.fs`

```fsharp
namespace Diplo.Grpc.Container

open System
open System.Collections.Generic
open System.Threading.Tasks
open ProtoBuf.Grpc

[<ServiceContract>]
type IContainerService =
    abstract CreateContainer : CreateContainerRequest * CallContext -> Task<CreateContainerResponse>
    abstract StartContainer : StartContainerRequest * CallContext -> Task<StartContainerResponse>
    abstract StopContainer : StopContainerRequest * CallContext -> Task<StopContainerResponse>
    abstract DeleteContainer : DeleteContainerRequest * CallContext -> Task<DeleteContainerResponse>
    abstract InspectContainer : InspectContainerRequest * CallContext -> Task<InspectContainerResponse>
    abstract ListContainers : ListContainersRequest * CallContext -> Task<ListContainersResponse>
    abstract GetContainerLogs : GetContainerLogsRequest * CallContext -> IAsyncEnumerable<ContainerLogEntry>
    abstract ExecInContainer : ExecInContainerRequest * CallContext -> IAsyncEnumerable<ExecOutput>
    abstract PullImage : PullImageRequest * CallContext -> Task<PullImageResponse>
    abstract GetVersion : GetVersionRequest * CallContext -> Task<GetVersionResponse>
    abstract ListNamespaces : ListNamespacesRequest * CallContext -> Task<ListNamespacesResponse>
    abstract RenameContainer : RenameContainerRequest * CallContext -> Task<RenameContainerResponse>
    abstract TopContainer : TopContainerRequest * CallContext -> Task<TopContainerResponse>
    abstract GetContainerStats : GetContainerStatsRequest * CallContext -> Task<GetContainerStatsResponse>
    abstract ListImages : ListImagesRequest * CallContext -> Task<ListImagesResponse>
    abstract InspectImage : InspectImageRequest * CallContext -> Task<InspectImageResponse>
    abstract RemoveImage : RemoveImageRequest * CallContext -> Task<RemoveImageResponse>
    abstract TagImage : TagImageRequest * CallContext -> Task<TagImageResponse>
```

### 2b. `IVolumeService.fs` — 7 méthodes (sans streaming)

### 2c. `INetworkService.fs` — 8 méthodes (sans streaming, RunCniPlugin est unary)

---

## Étape 3 : Modifier `Diplo.Grpc.fsproj` (remplace `.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="ContainerMessages.fs" />
    <Compile Include="VolumeMessages.fs" />
    <Compile Include="NetworkMessages.fs" />
    <Compile Include="IContainerService.fs" />
    <Compile Include="IVolumeService.fs" />
    <Compile Include="INetworkService.fs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="protobuf-net" Version="3.2.47" />
    <PackageReference Include="protobuf-net.Grpc" Version="1.2.2" />
  </ItemGroup>
</Project>
```

**Ordre de compilation** : Messages d'abord, puis Interfaces (dépendent des types messages).

---

## Étape 4 : Modifier les Implémentations de Service

### 4a. `ContainerServiceImpl.fs`

Changements :
- **Supprimer** : `inherit ContainerService.ContainerServiceBase()`
- **Ajouter** : `interface IContainerService with`
- **Changer** : `override` → `member` pour chaque méthode
- **Changer** : `RpcException(Status(...))` → `failwith "message"` ou garder RpcException avec le bon using
- **Changer** : Streaming avec `responseStream.WriteAsync()` → `IAsyncEnumerable` avec `yield`
- **Changer** : `Google.Protobuf.ByteString.CopyFromUtf8(result)` → `System.Text.Encoding.UTF8.GetBytes(result)`
- **Changer** : `ContainerState.Running` → `ContainerState.Running` (enum F# au lieu de C#, même nom)

**Avant (streaming)** :
```fsharp
override _.GetContainerLogs(request, responseStream, context) =
    task {
        ...
        for line in logs do
            let entry = ContainerLogEntry(Timestamp = ..., Stream = ..., Log = line)
            do! responseStream.WriteAsync(entry)
        return ()
    }
```

**Après (IAsyncEnumerable)** :
```fsharp
interface IContainerService with
    member _.GetContainerLogs(request, _context) =
        let logs = client.GetContainerLogs(...)
        logs |> Seq.map (fun line ->
            { Timestamp = DateTime.UtcNow.ToString("o")
              Stream = "stdout"
              Log = line })
        |> AsyncSeq.ofSeq
        :> IAsyncEnumerable<ContainerLogEntry>
```

### 4b. `VolumeServiceImpl.fs` — même pattern

### 4c. `NetworkServiceImpl.fs` — même pattern

---

## Étape 5 : Modifier les Clients Diplo.Core

### 5a. `ContainerClient.fs`

**Avant** :
```fsharp
let client = ContainerService.ContainerServiceClient(channel)
let! response = client.CreateContainerAsync(request, cancellationToken = ct)
```

**Après** :
```fsharp
let client = channel.CreateGrpcService<IContainerService>()
let! response = client.CreateContainer(request, CallContext(ct))
```

**Changements clés** :
- `ContainerService.ContainerServiceClient(channel)` → `channel.CreateGrpcService<IContainerService>()`
- `client.XxxAsync(request, cancellationToken = ct)` → `client.Xxx(request, CallContext(ct))`
- `CallContext` depuis `ProtoBuf.Grpc`
- Streaming : `client.GetContainerLogs(request).ResponseStream` → `client.GetContainerLogs(request, CallContext(ct))` retourne `IAsyncEnumerable<T>`

### 5b. `VolumeClient.fs` — même pattern

### 5c. `NetworkClient.fs` — même pattern

### 5d. `DiploChannel.fs` — pas de changement significatif (GrpcChannel reste identique)

### 5e. `Diplo.Core.fsproj` — packages changent

```xml
<PackageReference Include="protobuf-net.Grpc" Version="1.2.2" />
<PackageReference Include="Grpc.Net.Client" Version="2.80.0" />
<!-- SUPPRIMÉ : ProjectReference Diplo.Grpc.csproj -->
<!-- AJOUTÉ : -->
<ProjectReference Include="..\Diplo.Grpc\Diplo.Grpc.fsproj" />
<ProjectReference Include="..\Diplo.Abstractions\Diplo.Abstractions.fsproj" />
```

---

## Étape 6 : Modifier les Program.fs des Services

### 6a. `Container/Program.fs`

**Avant** :
```fsharp
builder.Services.AddGrpc() |> ignore
builder.Services.AddSingleton<ContainerServiceImpl>() |> ignore
fun app -> app.MapGrpcService<ContainerServiceImpl>() |> ignore
```

**Après** :
```fsharp
open ProtoBuf.Grpc.Server

builder.Services.AddCodeFirstGrpc() |> ignore
builder.Services.AddSingleton<ContainerServiceImpl>() |> ignore
fun app -> app.MapGrpcService<ContainerServiceImpl>() |> ignore
```

`AddCodeFirstGrpc()` remplace `AddGrpc()` pour protobuf-net.Grpc.

### 6b. `Volume/Program.fs` — même pattern

### 6c. `Network/Program.fs` — même pattern

**ATTENTION** : `NetworkDriver` enum change de `Diplo.Grpc.Network.NetworkDriver` (enum C# généré) à `Diplo.Grpc.Network.NetworkDriver` (enum F# défini). Même namespace, même nom — les valeurs changent de syntaxe :
- `NetworkDriver.Bridge` → `NetworkDriver.Bridge` (identique)
- `NetworkDriver.CustomCni` → `NetworkDriver.CustomCni` (identique)
- `NetworkDriver.``None``` → `NetworkDriver.``None``` (identique, backtick syntax)
- `NetworkDriver.``Pod``` → `NetworkDriver.``Pod``` (identique)

---

## Étape 7 : Modifier les fsproj de Dépendance

| Projet | Changement |
|--------|-----------|
| `Diplo.Container.fsproj` | `Grpc.AspNetCore` → `protobuf-net.Grpc.AspNetCore 1.2.2` + `FSharp.Control.Tasks` |
| `Diplo.Volume.fsproj` | Idem |
| `Diplo.Network.fsproj` | Idem |
| `Diplo.Core.fsproj` | `Grpc.Net.Client` + nouveau projet reference Diplo.Grpc.fsproj |
| `Diplo.Abstractions.fsproj` | `Grpc.AspNetCore` → `protobuf-net.Grpc.AspNetCore 1.2.2` |
| `Diplo.Cli.fsproj` | Pas de changement direct (ref Diplo.Core) |
| `Diplo.Gui.fsproj` | Pas de changement direct (ref Diplo.Core) |
| `Diplo.Contracts.fsproj` | Garder `Google.Protobuf` si encore utilisé, sinon supprimer |

---

## Étape 8 : Modifier les Tests

### Tests d'Implémentation (Container/Volume/Network)

**Changements** :
- `Grpc.Core.Testing.TestServerCallContext.Create(...)` → plus nécessaire, passer `CallContext()` directement
- `MockServerStreamWriter<'T>()` → plus nécessaire, le streaming utilise `IAsyncEnumerable`
- Proto types : même noms, mais ce sont des records F# au lieu de classes C#
- `req.Command.Add("echo")` → `req.Command.Add("echo")` (même syntaxe, `System.Collections.Generic.List<string>`)
- `ContainerState.Running` → `ContainerState.Running` (même nom, enum F#)
- `Google.Protobuf.ByteString.CopyFromUtf8(...)` → `System.Text.Encoding.UTF8.GetBytes(...)`
- `writer.Items.[0].Data.ToStringUtf8()` → `System.Text.Encoding.UTF8.GetString(output.Data)`

### Tests Clients (Core)

**Changements** :
- `ContainerService.ContainerServiceClient(channel)` → `channel.CreateGrpcService<IContainerService>()`
- Les mock servers doivent implémenter l'interface F# `IContainerService`

---

## Étape 9 : Fichiers à Supprimer

- `src/Diplo.Grpc/Diplo.Grpc.csproj`
- `src/Diplo.Grpc/Protos/container.proto`
- `src/Diplo.Grpc/Protos/volume.proto`
- `src/Diplo.Grpc/Protos/network.proto`

---

## Étape 10 : Fichiers à Créer

- `src/Diplo.Grpc/Diplo.Grpc.fsproj`
- `src/Diplo.Grpc/ContainerMessages.fs`
- `src/Diplo.Grpc/VolumeMessages.fs`
- `src/Diplo.Grpc/NetworkMessages.fs`
- `src/Diplo.Grpc/IContainerService.fs`
- `src/Diplo.Grpc/IVolumeService.fs`
- `src/Diplo.Grpc/INetworkService.fs`

---

## Étape 11 : Fichiers à Modifier (18 fichiers)

1. `src/Diplo.Container/Services/ContainerServiceImpl.fs`
2. `src/Diplo.Container/Program.fs`
3. `src/Diplo.Container/Diplo.Container.fsproj`
4. `src/Diplo.Volume/Services/VolumeServiceImpl.fs`
5. `src/Diplo.Volume/Program.fs`
6. `src/Diplo.Volume/Diplo.Volume.fsproj`
7. `src/Diplo.Network/Services/NetworkServiceImpl.fs`
8. `src/Diplo.Network/Plugins/INetworkDriver.fs`
9. `src/Diplo.Network/Program.fs`
10. `src/Diplo.Network/Diplo.Network.fsproj`
11. `src/Diplo.Core/Clients/ContainerClient.fs`
12. `src/Diplo.Core/Clients/VolumeClient.fs`
13. `src/Diplo.Core/Clients/NetworkClient.fs`
14. `src/Diplo.Core/Diplo.Core.fsproj`
15. `src/Diplo.Abstractions/Diplo.Abstractions.fsproj`
16. Tests Container/Volume/Network/Core (4 fichiers de tests d'implémentation)

---

## Risques et Mitigations

| Risque | Impact | Mitigation |
|--------|--------|-----------|
| protobuf-net wire format ≠ Google.Protobuf | Faible (système fermé client+server Diplo) | Test d'intégration end-to-end |
| F# streaming (pas de yield return async) | Moyen | Utiliser `seq {}` + `AsyncSeq.ofSeq` ou `Channel<T>` |
| CLIMutable records et protobuf-net | Faible | Test de sérialisation/désérialisation unitaire |
| Changement de syntaxe enum (C# → F#) | Faible | Même noms, vérifier les valeurs |
| `RpcException` dans les interfaces F# | Faible | Garder `Grpc.Core` pour les exceptions, ou utiliser `failwith` |

---

## Ordre d'Exécution Recommandé

1. Créer les types messages (étape 1)
2. Créer les interfaces de service (étape 2)
3. Modifier Diplo.Grpc.fsproj (étape 3)
4. Compiler Diplo.Grpc seul pour valider
5. Modifier les implémentations de service (étape 4)
6. Modifier les clients Core (étape 5)
7. Modifier les Program.fs (étape 6)
8. Modifier les fsproj (étape 7)
9. Compiler toute la solution
10. Modifier les tests (étape 8)
11. Exécuter tous les tests
12. Supprimer les anciens fichiers (étape 9)
