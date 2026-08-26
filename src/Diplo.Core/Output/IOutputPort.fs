namespace Diplo.Core.Output

open System.Collections.Generic

/// Abstraction de sortie pour découpler l'affichage du métier.
/// Le CLI écrit dans la console Spectre, le GUI lie au DataGrid/TextBox.
type IOutputPort =
    abstract WriteLine: text: string -> unit
    abstract WriteError: text: string -> unit
    abstract WriteSuccess: text: string -> unit
    abstract WriteWarning: text: string -> unit
    abstract WriteTable: items: IReadOnlyList<'T> * columns: string[] * selector: ('T -> string[]) -> unit
