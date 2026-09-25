namespace DiploWalker.Abstractions

open System.Text.Json

/// Options de sÃ©rialisation JSON centralisÃ©es pour tout le projet.
[<RequireQualifiedAccess>]
module DiploWalkerJson =

    let defaultOptions =
        let o = JsonSerializerOptions()
        o.WriteIndented <- true
        o

    let snakeCaseOptions =
        let o = JsonSerializerOptions()
        o.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower
        o.WriteIndented <- true
        o

    let caseInsensitiveOptions =
        let o = JsonSerializerOptions()
        o.PropertyNameCaseInsensitive <- true
        o.WriteIndented <- true
        o

    let withMaxDepth (maxDepth: int) =
        let o = JsonSerializerOptions()
        o.WriteIndented <- true
        o.MaxDepth <- maxDepth
        o

    let documentOptions = JsonDocumentOptions(MaxDepth = 64)

    let withMaxDepthDoc (maxDepth: int) =
        JsonDocumentOptions(MaxDepth = maxDepth)

