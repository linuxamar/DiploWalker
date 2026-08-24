namespace Diplo.Abstractions

open System.Text.Json

/// Helpers mutualisés pour l'extraction de propriétés depuis des JsonElement.
[<RequireQualifiedAccess>]
module JsonHelpers =

    let tryGetString (el: JsonElement) (prop: string) =
        let mutable v = Unchecked.defaultof<JsonElement>

        if el.TryGetProperty(prop, &v) && v.ValueKind = JsonValueKind.String then
            v.GetString()
        else
            ""

    let tryGetInt64 (el: JsonElement) (prop: string) =
        let mutable v = Unchecked.defaultof<JsonElement>

        if el.TryGetProperty(prop, &v) && v.ValueKind = JsonValueKind.Number then
            v.GetInt64()
        else
            0L

    let tryGetDouble (el: JsonElement) (prop: string) =
        let mutable v = Unchecked.defaultof<JsonElement>

        if el.TryGetProperty(prop, &v) && v.ValueKind = JsonValueKind.Number then
            v.GetDouble()
        else
            0.0

    let tryGetBool (el: JsonElement) (prop: string) =
        let mutable v = Unchecked.defaultof<JsonElement>

        if el.TryGetProperty(prop, &v) && v.ValueKind = JsonValueKind.True then
            true
        elif el.TryGetProperty(prop, &v) && v.ValueKind = JsonValueKind.False then
            false
        else
            false

    let tryGetElement (el: JsonElement) (prop: string) =
        let mutable v = Unchecked.defaultof<JsonElement>
        if el.TryGetProperty(prop, &v) then Some v else None

    let tryGetStringValue (el: JsonElement) (prop: string) =
        let mutable v = Unchecked.defaultof<JsonElement>

        if el.TryGetProperty(prop, &v) && v.ValueKind = JsonValueKind.String then
            Some(v.GetString())
        else
            None
