namespace DiploWalker.Abstractions

open System.Text.Json

/// Helpers mutualisés pour l'extraction de propriétés depuis des JsonElement.
/// CONTRAT SENTINELLE : tryGetString/tryGetInt64/tryGetDouble/tryGetBool
/// retournent la valeur par défaut du type ("", 0L, 0.0, false) aussi bien
/// pour une propriété absente que pour un mismatch de type — impossible de
/// distinguer « absent » de « vide ». Pour différencier, utiliser les
/// variantes Option (tryGetElement, tryGetStringValue).
[<RequireQualifiedAccess>]
module JsonHelpers =

    /// Valeur de la propriété, ou "" si absente ou non-chaîne (sentinelle ambiguë).
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

