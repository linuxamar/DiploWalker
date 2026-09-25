namespace DiploWalker.Abstractions

open System.Text.Json

/// Helpers mutualisÃ©s pour l'extraction de propriÃ©tÃ©s depuis des JsonElement.
/// CONTRAT SENTINELLE : tryGetString/tryGetInt64/tryGetDouble/tryGetBool
/// retournent la valeur par dÃ©faut du type ("", 0L, 0.0, false) aussi bien
/// pour une propriÃ©tÃ© absente que pour un mismatch de type â€” impossible de
/// distinguer Â« absent Â» de Â« vide Â». Pour diffÃ©rencier, utiliser les
/// variantes Option (tryGetElement, tryGetStringValue).
[<RequireQualifiedAccess>]
module JsonHelpers =

    /// Valeur de la propriÃ©tÃ©, ou "" si absente ou non-chaÃ®ne (sentinelle ambiguÃ«).
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

