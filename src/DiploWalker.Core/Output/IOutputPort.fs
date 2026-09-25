namespace DiploWalker.Core.Output

open System.Collections.Generic

/// Abstraction de sortie pour dÃ©coupler l'affichage du mÃ©tier.
/// Le CLI Ã©crit dans la console Spectre, le GUI lie au DataGrid/TextBox.
type IOutputPort =
    abstract WriteLine: text: string -> unit
    abstract WriteError: text: string -> unit
    abstract WriteSuccess: text: string -> unit
    abstract WriteWarning: text: string -> unit
    abstract WriteTable: items: IReadOnlyList<'T> * columns: string[] * selector: ('T -> string[]) -> unit

